using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DreamLuau.Backend;
using DreamLuau.Binding;
using NuLua;
using NuLua.Luau;

namespace DreamLuau.NuLua;

sealed class NuLuaVm : IDreamVm
{
    readonly LuauState rootState;
    readonly LuauState state;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly ThreadLocal<int> callbackDepth = new(() => 0);
    readonly DreamObjectRegistry objects = new();
    readonly ConcurrentQueue<Exception> callbackErrors = new();
    readonly Dictionary<Type, DreamTypeBinding> bindingsByType = [];
    readonly Dictionary<string, DreamTypeBinding> bindingsByName = new(StringComparer.Ordinal);
    readonly Dictionary<Type, LuaTable> metatables = [];
    readonly Dictionary<(Type Type, string Name), LuaFunction> methodFunctions = [];
    readonly List<LuaFunction> ownedFunctions = [];
    readonly List<LuauBuffer> ownedBuffers = [];
    DreamValueConverter converter = new();
    bool disposed;

    public NuLuaVm(LuauState rootState, LuauState state)
    {
        this.rootState = rootState;
        this.state = state;
    }

    internal DreamValueConverter Converter => converter;

    public async ValueTask<IReadOnlyList<IDreamValue>> ExecuteAsync(
        DreamScript script,
        IReadOnlyList<object?> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(arguments);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        LuauState? coroutine = null;
        try
        {
            ThrowIfDisposed();
            coroutine = (LuauState)state.CreateThread();
            state.Pop(1);
            coroutine.LoadString(script.Source.AsSpan(), script.Name.AsSpan());
            for (var i = 0; i < arguments.Count; i++)
                PushObject(coroutine, arguments[i]);
            await ((ILuaState)coroutine).ResumeAsync(arguments.Count, cancellationToken).ConfigureAwait(false);
            ThrowCallbackError();
            return ReadResults(coroutine);
        }
        catch (Exception exception)
        {
            var callbackFailures = DrainCallbackErrors();
            if (callbackFailures.Count > 0)
                throw new AggregateException("NuLua callback failed during script execution.", [.. callbackFailures, exception]);
            throw;
        }
        finally
        {
            coroutine?.Dispose();
            gate.Release();
        }
    }

    public IDreamValue GetGlobal(string name) => RunSynchronous(() =>
    {
        ThrowIfDisposed();
        return WrapValue(state[name]);
    });

    public void SetGlobal(string name, object? value) => RunSynchronous(() =>
    {
        ThrowIfDisposed();
        PushObject(state, value);
        state.SetGlobal(name);
    });

    public async ValueTask<IReadOnlyList<IDreamValue>> CallGlobalAsync(
        string name,
        IReadOnlyList<object?> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var functionValue = state[name];
            if (functionValue.Type != LuaValueType.Function)
                throw new InvalidOperationException($"Global '{name}' is not a function.");
            using var function = functionValue.Read<LuaFunction>();
            var values = await InvokeFunctionLockedAsync(function, arguments, cancellationToken).ConfigureAwait(false);
            ThrowCallbackError();
            return values;
        }
        catch (Exception exception)
        {
            var callbackFailures = DrainCallbackErrors();
            if (callbackFailures.Count > 0)
                throw new AggregateException("NuLua callback failed during function call.", [.. callbackFailures, exception]);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public void RegisterType(DreamTypeBinding binding) => RunSynchronous(() =>
    {
        ArgumentNullException.ThrowIfNull(binding);
        ThrowIfDisposed();
        if (bindingsByName.ContainsKey(binding.Name) || bindingsByType.ContainsKey(binding.TargetType))
            throw new InvalidOperationException($"Binding '{binding.Name}' or type '{binding.TargetType}' already exists in this context.");
        ValidateBinding(binding);
        converter = binding.Converter;
        bindingsByName.Add(binding.Name, binding);
        bindingsByType.Add(binding.TargetType, binding);

        var metatable = state.CreateTable();
        metatables.Add(binding.TargetType, metatable);
        metatable["__index"] = OwnFunction(CreateFunction((lua, args) => Index(binding, lua, args)));
        metatable["__newindex"] = OwnFunction(CreateFunction((lua, args) => NewIndex(binding, lua, args)));
        metatable["__gc"] = OwnFunction(CreateFunction((_, args) => FinalizeObject(args)));
        metatable["__metatable"] = "protected";

        foreach (var group in binding.Methods.GroupBy(item => item.Method.Name, StringComparer.Ordinal))
        {
            var methods = group.ToArray();
            var function = methods.Any(item => IsAsyncReturnType(item.Method.ReturnType))
                ? CreateAsyncFunction((lua, args, cancellationToken) =>
                {
                    var instance = ResolveTarget(args[0]);
                    return InvokeMethodsAsync(methods, instance, lua, args, 1, cancellationToken);
                })
                : CreateFunction((lua, args) =>
                {
                    var instance = ResolveTarget(args[0]);
                    return InvokeMethods(binding, methods, instance, lua, args, argumentOffset: 1);
                });
            methodFunctions.Add((binding.TargetType, group.Key), OwnFunction(function));
        }

        var classTable = state.CreateTable();
        if (binding.Constructors.Count > 0 || binding.HasDefaultValueTypeConstructor)
        {
            classTable["new"] = OwnFunction(CreateFunction((lua, args) => Construct(binding, lua, args)));
            using var classMetatable = state.CreateTable();
            classMetatable["__call"] = OwnFunction(CreateFunction((lua, args) => Construct(binding, lua, args, 1)));
            state.PushValue(classTable.Reference);
            state.SetMetatable(-1, classMetatable);
            state.Pop(1);
        }
        foreach (var group in binding.StaticMethods.GroupBy(item => item.Method.Name, StringComparer.Ordinal))
        {
            var methods = group.ToArray();
            classTable[group.Key] = OwnFunction(methods.Any(item => IsAsyncReturnType(item.Method.ReturnType))
                ? CreateAsyncFunction((lua, args, cancellationToken) =>
                    InvokeMethodsAsync(methods, null, lua, args, 0, cancellationToken))
                : CreateFunction((lua, args) => InvokeMethods(binding, methods, null, lua, args, 0)));
        }
        state.PushValue(classTable.Reference);
        state.SetGlobal(binding.Name);
        classTable.Dispose();
    });

    internal IDreamValue WrapValue(LuaValue value)
    {
        return value.Type switch
        {
            LuaValueType.Table => new NuLuaDreamTable(this, value.Read<LuaTable>()),
            LuaValueType.Function => new NuLuaDreamFunction(this, value.Read<LuaFunction>()),
            LuaValueType.UserData => WrapUserdata(value),
            _ => new NuLuaDreamValue(this, value),
        };
    }

    internal IDreamUserdata WrapUserdata(LuaValue value)
    {
        var userData = value.Read<LuaUserData>();
        var handle = userData.Read<long>();
        userData.Dispose();
        return new NuLuaDreamUserdata(this, handle);
    }

    internal object ResolveUserdata(long handle) => objects.GetTarget(new DreamObjectHandle(handle));

    internal ValueTask<IReadOnlyList<IDreamValue>> InvokeAsync(
        LuaFunction function,
        IReadOnlyList<object?> arguments,
        CancellationToken cancellationToken) => RunAsynchronous(
            () => InvokeFunctionLockedAsync(function, arguments, cancellationToken), cancellationToken);

    async ValueTask<IReadOnlyList<IDreamValue>> InvokeFunctionLockedAsync(
        LuaFunction function,
        IReadOnlyList<object?> arguments,
        CancellationToken cancellationToken)
    {
        var coroutine = (LuauState)state.CreateThread();
        state.Pop(1);
        try
        {
            coroutine.PushValue(function.Reference);
            for (var i = 0; i < arguments.Count; i++)
                PushObject(coroutine, arguments[i]);
            await ((ILuaState)coroutine).ResumeAsync(arguments.Count, cancellationToken).ConfigureAwait(false);
            ThrowCallbackError();
            return ReadResults(coroutine);
        }
        finally
        {
            coroutine.Dispose();
        }
    }

    internal IDreamValue ReadTableField(LuaTable table, string key) => RunSynchronous(() =>
    {
        ThrowIfDisposed();
        return WrapValue(table[key.AsSpan()]);
    });

    internal void WriteTableField(LuaTable table, string key, object? value) => RunSynchronous(() =>
    {
        ThrowIfDisposed();
        state.PushValue(table.Reference);
        state.PushString(key.AsSpan());
        PushObject(state, value);
        state.SetTable(-3);
        state.Pop(1);
    });

    async ValueTask<T> RunAsynchronous<T>(Func<ValueTask<T>> action, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    T RunSynchronous<T>(Func<T> action)
    {
        var nested = callbackDepth.Value > 0;
        if (!nested)
        {
            gate.Wait();
        }
        try
        {
            ThrowIfDisposed();
            return action();
        }
        finally
        {
            if (!nested)
            {
                gate.Release();
            }
        }
    }

    void RunSynchronous(Action action) => RunSynchronous(() =>
    {
        action();
        return true;
    });

    void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    IReadOnlyList<IDreamValue> ReadResults(LuauState coroutine)
    {
        var count = coroutine.GetTop();
        if (count > 0)
            coroutine.XMove(state, count);
        var values = new IDreamValue[count];
        for (var i = 0; i < count; i++)
            values[i] = WrapValue(state.ToLuaValue(i + 1));
        state.SetTop(0);
        return values;
    }

    LuaFunction CreateFunction(LuaFunc<LuauState> callback) => rootState.CreateFunction((lua, args) =>
    {
        callbackDepth.Value++;
        try
        {
            return callback(lua, args);
        }
        catch (Exception exception)
        {
            callbackErrors.Enqueue(exception);
            lua.PushNil();
            return 1;
        }
        finally
        {
            callbackDepth.Value--;
        }
    });

    LuaFunction CreateAsyncFunction(
        Func<LuauState, LuaFuncArguments, CancellationToken, ValueTask<int>> callback) =>
        rootState.CreateFunction(async (lua, args, cancellationToken) =>
        {
            ValueTask<int> result;
            callbackDepth.Value++;
            try { result = callback(lua, args, cancellationToken); }
            finally { callbackDepth.Value--; }

            try { return await result.ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                callbackErrors.Enqueue(exception);
                lua.PushNil();
                return 1;
            }
        });

    LuaFunction OwnFunction(LuaFunction function)
    {
        ownedFunctions.Add(function);
        return function;
    }

    int Construct(DreamTypeBinding binding, LuauState lua, LuaFuncArguments args, int argumentOffset = 0)
    {
        var matches = new List<(ConstructorInfo Constructor, object?[] Arguments, int Score)>();
        foreach (var constructorBinding in binding.Constructors)
        {
            var constructor = constructorBinding.Constructor;
            if (TryConvertArguments(constructor.GetParameters(), args, argumentOffset, lua, out var values, out var score))
                matches.Add((constructor, values, score));
        }
        if (matches.Count == 0)
        {
            if (binding.HasDefaultValueTypeConstructor && args.Length == argumentOffset)
            {
                PushObject(lua, Activator.CreateInstance(binding.TargetType));
                return 1;
            }
            throw new MissingMethodException($"No constructor overload for '{binding.Name}' matches supplied arguments.");
        }
        var bestScore = matches.Min(item => item.Score);
        var best = matches.Where(item => item.Score == bestScore).ToArray();
        if (best.Length != 1)
            throw new AmbiguousMatchException($"Constructor call for '{binding.Name}' is ambiguous.");
        var instance = InvokeReflection(() => best[0].Constructor.Invoke(best[0].Arguments));
        PushObject(lua, instance);
        return 1;
    }

    int Index(DreamTypeBinding binding, LuauState lua, LuaFuncArguments args)
    {
        var target = ResolveTarget(args[0]);
        var key = args[1].Read<string>();
        var property = binding.Properties.FirstOrDefault(item => item.Property.Name == key);
        if (property is not null)
        {
            PushObject(lua, InvokeReflection(() => property.Property.GetValue(target)));
            return property.Property.PropertyType == typeof(void) ? 0 : 1;
        }

        if (!methodFunctions.TryGetValue((binding.TargetType, key), out var callable))
        {
            lua.PushNil();
            return 1;
        }
        lua.PushValue(callable.Reference);
        return 1;
    }

    int NewIndex(DreamTypeBinding binding, LuauState lua, LuaFuncArguments args)
    {
        _ = lua;
        var target = ResolveTarget(args[0]);
        var key = args[1].Read<string>();
        var propertyBinding = binding.Properties.FirstOrDefault(item => item.Property.Name == key)
            ?? throw new MissingMemberException($"Property '{key}' is not exposed on '{binding.Name}'.");
        if (!propertyBinding.CanWrite)
            throw new InvalidOperationException($"Property '{key}' is read-only.");
        var property = propertyBinding.Property;
        var value = ConvertValue(args[2], property.PropertyType, lua, out _);
        InvokeReflection(() =>
        {
            property.SetValue(target, value);
            return null;
        });
        return 0;
    }

    int FinalizeObject(LuaFuncArguments args)
    {
        if (args.Length > 0 && args[0].Type == LuaValueType.UserData)
        {
            var id = args[0].Read<long>();
            objects.Invalidate(new DreamObjectHandle(id));
        }
        return 0;
    }

    int InvokeMethods(
        DreamTypeBinding binding,
        IReadOnlyList<DreamMethodBinding> methods,
        object? target,
        LuauState lua,
        LuaFuncArguments args,
        int argumentOffset)
    {
        var candidates = methods.Select(item => item.Method).ToArray();
        var selected = ResolveMethod(candidates, args, argumentOffset, lua);
        var result = InvokeReflection(() => selected.Method.Invoke(target, selected.Arguments));
        if (selected.Method.ReturnType == typeof(void)) return 0;
        PushObject(lua, result);
        return 1;
    }

    async ValueTask<int> InvokeMethodsAsync(
        IReadOnlyList<DreamMethodBinding> methods,
        object? target,
        LuauState lua,
        LuaFuncArguments args,
        int argumentOffset,
        CancellationToken cancellationToken)
    {
        var selected = ResolveMethod(methods.Select(item => item.Method).ToArray(), args, argumentOffset, lua, cancellationToken);
        object? invocation;
        callbackDepth.Value++;
        try { invocation = InvokeReflection(() => selected.Method.Invoke(target, selected.Arguments)); }
        finally { callbackDepth.Value--; }

        var (result, hasResult) = await AwaitMethodResult(invocation, selected.Method.ReturnType)
            .ConfigureAwait(false);
        if (!hasResult) return 0;
        PushObject(lua, result);
        return 1;
    }

    static bool IsAsyncReturnType(Type type) =>
        typeof(Task).IsAssignableFrom(type)
        || type == typeof(ValueTask)
        || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);

    static async ValueTask<(object? Result, bool HasResult)> AwaitMethodResult(object? invocation, Type returnType)
    {
        if (invocation is Task task)
        {
            await task.ConfigureAwait(false);
            var hasResult = returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>);
            return (hasResult ? task.GetType().GetProperty("Result")!.GetValue(task) : null, hasResult);
        }

        if (returnType == typeof(ValueTask))
        {
            await ((ValueTask)invocation!).ConfigureAwait(false);
            return (null, false);
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var taskValue = (Task)returnType.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(invocation, null)!;
            await taskValue.ConfigureAwait(false);
            return (taskValue.GetType().GetProperty("Result")!.GetValue(taskValue), true);
        }

        return (invocation, returnType != typeof(void));
    }

    (MethodInfo Method, object?[] Arguments) ResolveMethod(
        IReadOnlyList<MethodInfo> methods,
        LuaFuncArguments args,
        int argumentOffset,
        LuauState lua,
        CancellationToken? injectedToken = null)
    {
        var matches = new List<(MethodInfo Method, object?[] Arguments, int Score)>();
        foreach (var method in methods)
        {
            if (TryConvertArguments(method.GetParameters(), args, argumentOffset, lua, out var converted, out var score, injectedToken))
                matches.Add((method, converted, score));
        }

        if (matches.Count == 0)
            throw new MissingMethodException("No exposed method overload matches supplied arguments.");
        var bestScore = matches.Min(item => item.Score);
        var best = matches.Where(item => item.Score == bestScore).ToArray();
        if (best.Length != 1)
            throw new AmbiguousMatchException("Method call matches multiple exposed overloads.");
        return (best[0].Method, best[0].Arguments);
    }

    bool TryConvertArguments(
        ParameterInfo[] parameters,
        LuaFuncArguments args,
        int offset,
        LuauState lua,
        out object?[] converted,
        out int score,
        CancellationToken? injectedToken = null)
    {
        var luaParameters = parameters
            .Where(parameter => injectedToken is null || parameter.ParameterType != typeof(CancellationToken))
            .ToArray();
        var supplied = args.Length - offset;
        var required = luaParameters.Count(parameter => !parameter.IsOptional);
        if (supplied < required || supplied > luaParameters.Length)
        {
            converted = [];
            score = int.MaxValue;
            return false;
        }

        converted = new object?[parameters.Length];
        score = luaParameters.Length - supplied;
        var luaIndex = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (injectedToken is { } token && parameter.ParameterType == typeof(CancellationToken))
            {
                converted[i] = token;
                continue;
            }
            if (luaIndex >= supplied)
            {
                converted[i] = parameter.DefaultValue;
                continue;
            }
            if (!TryConvertValue(args[offset + luaIndex], parameter.ParameterType, lua, out converted[i], out var itemScore))
                return false;
            score += itemScore;
            luaIndex++;
        }
        return true;
    }

    bool TryConvertValue(LuaValue value, Type targetType, LuauState lua, out object? result, out int score)
    {
        var nullable = Nullable.GetUnderlyingType(targetType);
        var effectiveType = nullable ?? targetType;
        if (value.Type == LuaValueType.Nil)
        {
            score = 0;
            return converter.TryConvert(null, targetType, out result);
        }

        object? source;
        if (typeof(Delegate).IsAssignableFrom(effectiveType) && value.Type == LuaValueType.Function)
        {
            try
            {
                source = CreateDelegate(effectiveType, lua, value.Read<LuaFunction>());
                result = source;
                score = 0;
                return true;
            }
            catch
            {
                result = null;
                score = int.MaxValue;
                return false;
            }
        }

        source = value.Type switch
        {
            LuaValueType.Boolean => value.Read<bool>(),
            LuaValueType.Number => value.Read<double>(),
            LuaValueType.String => value.Read<string>(),
            LuaValueType.Vector => value.Read<System.Numerics.Vector3>(),
            LuaValueType.UserData => ResolveUserdata(value.Read<long>()),
            LuaValueType.Buffer => ReadBuffer(value),
            LuaValueType.Table => new NuLuaDreamTable(this, value.Read<LuaTable>()),
            LuaValueType.Function => new NuLuaDreamFunction(this, value.Read<LuaFunction>()),
            _ => null,
        };

        if (!converter.TryConvert(source, targetType, out result))
        {
            score = int.MaxValue;
            return false;
        }

        score = source is not null && effectiveType.IsInstanceOfType(source)
            ? 0
            : source is double && IsNumeric(effectiveType)
                ? effectiveType == typeof(double) ? 0 : 1
                : effectiveType.IsEnum ? 2 : 3;
        return true;
    }

    object? ConvertValue(LuaValue value, Type targetType, LuauState lua, out int score)
    {
        if (TryConvertValue(value, targetType, lua, out var result, out score))
            return result;
        throw new InvalidCastException($"Cannot convert Luau {value.Type} to {targetType}.");
    }

    static byte[] ReadBuffer(LuaValue value)
    {
        var buffer = value.Read<LuauBuffer>();
        try { return buffer.ToArray(); }
        finally { buffer.Dispose(); }
    }

    static bool IsNumeric(Type type) => Type.GetTypeCode(type) is
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
        or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    Delegate CreateDelegate(Type delegateType, LuauState sourceState, LuaFunction sourceFunction)
    {
        sourceState.PushValue(sourceFunction.Reference);
        sourceState.XMove(state, 1);
        var function = state.ToLuaValue(-1).Read<LuaFunction>();
        state.Pop(1);
        sourceFunction.Dispose();
        ownedFunctions.Add(function);
        var invoke = delegateType.GetMethod("Invoke")
            ?? throw new ArgumentException($"'{delegateType}' is not a delegate type.");
        var parameters = invoke.GetParameters();
        if (parameters.Any(parameter => parameter.ParameterType.IsByRef || parameter.IsOut)
            || typeof(Task).IsAssignableFrom(invoke.ReturnType)
            || invoke.ReturnType.IsGenericType && invoke.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            throw new NotSupportedException("Lua callbacks support synchronous delegates without ref/out parameters.");

        var expressions = parameters.Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name)).ToArray();
        var boxedArguments = Expression.NewArrayInit(
            typeof(object), expressions.Select(parameter => Expression.Convert(parameter, typeof(object))));
        var invokeMethod = typeof(NuLuaVm).GetMethod(nameof(InvokeLuaDelegate), BindingFlags.Instance | BindingFlags.NonPublic)!;
        var call = Expression.Call(
            Expression.Constant(this),
            invokeMethod,
            Expression.Constant(function),
            Expression.Constant(invoke.ReturnType),
            boxedArguments);
        Expression body = invoke.ReturnType == typeof(void)
            ? Expression.Block(call, Expression.Empty())
            : Expression.Convert(call, invoke.ReturnType);
        return Expression.Lambda(delegateType, body, expressions).Compile();
    }

    object? InvokeLuaDelegate(LuaFunction function, Type returnType, object?[] arguments) => RunSynchronous(() =>
    {
        var values = arguments.Select(ToLuaValue).ToArray();
        var results = function.Invoke(values);
        ThrowCallbackError();
        if (returnType == typeof(void)) return null;
        if (results.Length == 0)
            throw new InvalidOperationException("Luau callback returned no value.");
        var result = WrapValue(results[0]);
        try
        {
            if (converter.TryConvert(result, returnType, out var converted)) return converted;
            throw new InvalidCastException($"Cannot convert callback result to {returnType}.");
        }
        finally
        {
            result.Dispose();
        }
    });

    object ResolveTarget(LuaValue value)
    {
        if (value.Type != LuaValueType.UserData)
            throw new InvalidCastException("Expected DreamLuau userdata as method receiver.");
        return ResolveUserdata(value.Read<long>());
    }

    void PushObject(LuauState lua, object? value)
    {
        switch (value)
        {
            case null:
                lua.PushNil();
                return;
            case NuLuaDreamValue dreamValue:
                if (!ReferenceEquals(dreamValue.Owner, this)) throw new InvalidOperationException("Value belongs to another context.");
                if (dreamValue.IsDisposed) throw new ObjectDisposedException(nameof(IDreamValue));
                lua.Push(dreamValue.RawValue);
                return;
            case NuLuaDreamFunction function:
                if (!ReferenceEquals(function.Owner, this)) throw new InvalidOperationException("Function belongs to another context.");
                if (function.IsDisposed) throw new ObjectDisposedException(nameof(IDreamFunction));
                lua.PushValue(function.RawFunction.Reference);
                return;
            case NuLuaDreamTable table:
                if (!ReferenceEquals(table.Owner, this)) throw new InvalidOperationException("Table belongs to another context.");
                if (table.IsDisposed) throw new ObjectDisposedException(nameof(IDreamTable));
                lua.PushValue(table.RawTable.Reference);
                return;
            case bool boolean:
                lua.PushBoolean(boolean);
                return;
            case string text:
                lua.PushString(text.AsSpan());
                return;
            case char character:
                lua.PushString(character.ToString().AsSpan());
                return;
            case System.Numerics.Vector3 vector:
                lua.Push(LuaValue.FromVector(vector));
                return;
            case byte[] bytes:
                {
                    var buffer = lua.NewBuffer(bytes.Length);
                    buffer.CopyFrom(bytes);
                    lua.PushValue(buffer.Reference);
                    buffer.Dispose();
                    return;
                }
            case Enum enumeration:
                lua.PushNumber(Convert.ToDouble(enumeration, System.Globalization.CultureInfo.InvariantCulture));
                return;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                lua.PushNumber(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
                return;
            default:
                if (value is IDreamValue dreamObject)
                {
                    if (dreamObject is NuLuaDreamUserdata userdata)
                        value = userdata.GetTarget();
                    else
                        value = dreamObject.ToObject();
                }
                PushBoundObject(lua, value);
                return;
        }
    }

    void PushBoundObject(LuauState lua, object? value)
    {
        if (value is null)
        {
            lua.PushNil();
            return;
        }
        var binding = FindBinding(value.GetType())
            ?? throw new InvalidOperationException($"CLR type '{value.GetType()}' is not registered for Luau userdata.");
        var handle = objects.Register(value);
        using var userData = lua.CreateUserData(handle.Value);
        lua.PushValue(userData.Reference);
        lua.SetMetatable(-1, metatables[binding.TargetType]);
    }

    DreamTypeBinding? FindBinding(Type type)
    {
        if (bindingsByType.TryGetValue(type, out var exact)) return exact;
        return bindingsByType.Values
            .Where(binding => binding.TargetType.IsAssignableFrom(type))
            .OrderBy(binding => InheritanceDistance(type, binding.TargetType))
            .FirstOrDefault();
    }

    static int InheritanceDistance(Type type, Type target)
    {
        var distance = 0;
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current == target) return distance;
            distance++;
        }
        return 1000;
    }

    static object? InvokeReflection(Func<object?> action)
    {
        try
        {
            return action();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    static void ValidateBinding(DreamTypeBinding binding)
    {
        if (binding.TargetType.IsAbstract || binding.TargetType.IsInterface)
        {
            if (binding.Constructors.Count > 0)
                throw new ArgumentException($"Abstract type '{binding.TargetType}' cannot expose constructors.");
        }
        foreach (var method in binding.Methods.Concat(binding.StaticMethods).Select(item => item.Method))
            ValidateParameters(method.GetParameters(), method.ToString()!);
        foreach (var constructor in binding.Constructors)
            ValidateParameters(constructor.Constructor.GetParameters(), constructor.Constructor.ToString()!);
        foreach (var property in binding.Properties)
        {
            if (property.Property.PropertyType.IsByRefLike || property.Property.PropertyType.IsPointer)
                throw new ArgumentException($"Property '{property.Property.Name}' has unsupported type.");
        }
    }

    static void ValidateParameters(IEnumerable<ParameterInfo> parameters, string memberName)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.ParameterType.IsByRef || parameter.IsOut || parameter.ParameterType.IsPointer
                || parameter.ParameterType.IsByRefLike || parameter.GetCustomAttribute<ParamArrayAttribute>() is not null)
                throw new ArgumentException($"Member '{memberName}' has unsupported parameter '{parameter.Name}'.");
        }
    }

    void ThrowCallbackError()
    {
        var errors = DrainCallbackErrors();
        if (errors.Count == 1)
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1)
            throw new AggregateException("Multiple NuLua callbacks failed.", errors);
    }

    IReadOnlyList<Exception> DrainCallbackErrors()
    {
        List<Exception>? errors = null;
        while (callbackErrors.TryDequeue(out var error))
            (errors ??= []).Add(error);
        return errors ?? (IReadOnlyList<Exception>)Array.Empty<Exception>();
    }

    LuaValue ToLuaValue(object? value)
    {
        if (value is NuLuaDreamValue dreamValue)
        {
            if (!ReferenceEquals(dreamValue.Owner, this)) throw new InvalidOperationException("Value belongs to another context.");
            return dreamValue.RawValue;
        }
        return value switch
        {
            null => LuaValue.Nil,
            bool boolean => boolean,
            string text => text,
            char character => character.ToString(),
            byte[] bytes => CreateBufferValue(bytes),
            System.Numerics.Vector3 vector => LuaValue.FromVector(vector),
            Enum enumeration => Convert.ToDouble(enumeration, System.Globalization.CultureInfo.InvariantCulture),
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
            _ => CreateReferenceValue(value),
        };
    }

    LuaValue CreateBufferValue(byte[] bytes)
    {
        var buffer = rootState.NewBuffer(bytes.Length);
        buffer.CopyFrom(bytes);
        ownedBuffers.Add(buffer);
        return LuaValue.FromBuffer(buffer);
    }

    LuaValue CreateReferenceValue(object? value)
    {
        if (value is NuLuaDreamFunction function && ReferenceEquals(function.Owner, this))
            return LuaValue.FromFunction(function.RawFunction);
        if (value is NuLuaDreamTable table && ReferenceEquals(table.Owner, this))
            return LuaValue.FromTable(table.RawTable);
        throw new InvalidOperationException($"CLR value '{value?.GetType()}' cannot be passed as a Luau argument.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            List<Exception>? failures = null;
            foreach (var function in ownedFunctions)
            {
                try { function.Dispose(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
            ownedFunctions.Clear();
            foreach (var buffer in ownedBuffers)
            {
                try { buffer.Dispose(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
            ownedBuffers.Clear();
            foreach (var metatable in metatables.Values)
            {
                try { metatable.Dispose(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
            metatables.Clear();
            try { state.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            if (!ReferenceEquals(state, rootState))
            {
                try { rootState.Dispose(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
            try { objects.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            var callbackFailures = DrainCallbackErrors();
            if (callbackFailures.Count > 0)
                (failures ??= []).AddRange(callbackFailures);
            if (failures is not null)
                throw new AggregateException("NuLua VM cleanup failed.", failures);
        }
        finally
        {
            gate.Release();
            callbackDepth.Dispose();
        }
    }
}
