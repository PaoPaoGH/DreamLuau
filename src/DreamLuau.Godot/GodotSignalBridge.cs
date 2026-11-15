using System.Linq.Expressions;
using System.Reflection;
using Godot;
using Expression = System.Linq.Expressions.Expression;

namespace DreamLuau.Godot;

/// <summary>Connects Luau callbacks to one native Godot signal.</summary>
public sealed class GodotSignalBridge : GodotObjectBridge<GodotObject>
{
    readonly string name;
    readonly DreamContext context;

    internal GodotSignalBridge(
        GodotObject target,
        string name,
        GodotMainThreadDispatcher dispatcher,
        DreamContext context) : base(target, dispatcher)
    {
        this.name = name;
        this.context = context;
    }

    /// <summary>Connects a callback; its one argument provides typed access to all signal arguments.</summary>
    public GodotSignalConnection Connect(Action<GodotSignalArguments> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var connection = Invoke(target =>
        {
            var signalName = new StringName(name);
            if (!target.HasSignal(signalName))
                throw new ArgumentException($"Godot object has no signal named '{name}'.", nameof(name));

            var argumentCount = GetArgumentCount(target, signalName);
            var callable = CreateCallable(argumentCount, callback, Dispatcher, context);
            var error = target.Connect(signalName, callable);
            if (error != Error.Ok)
                throw new InvalidOperationException($"Could not connect Godot signal '{name}': {error}.");
            return new GodotSignalConnection(target, signalName, callable, Dispatcher);
        });
        return context.OwnResource(connection);
    }

    static int GetArgumentCount(GodotObject target, StringName signalName)
    {
        foreach (var signal in target.GetSignalList())
        {
            if (signal["name"].AsStringName() == signalName)
                return signal["args"].AsGodotArray().Count;
        }
        throw new ArgumentException($"Could not inspect Godot signal '{signalName}'.", nameof(signalName));
    }

    static Callable CreateCallable(
        int argumentCount,
        Action<GodotSignalArguments> callback,
        GodotMainThreadDispatcher dispatcher,
        DreamContext context)
    {
        if (argumentCount is < 0 or > 8)
            throw new NotSupportedException("Godot signal callbacks support up to eight arguments.");

        var parameterTypes = Enumerable.Repeat(typeof(Variant), argumentCount).ToArray();
        var parameters = parameterTypes.Select((type, index) => Expression.Parameter(type, $"arg{index}")).ToArray();
        var argumentArray = Expression.NewArrayInit(typeof(Variant), parameters);
        // Callable.From supports static Action signatures through eight parameters.
        var actionType = argumentCount == 0
            ? typeof(Action)
            : Expression.GetActionType(parameterTypes);
        var invokeCallback = typeof(GodotSignalBridge).GetMethod(
            nameof(InvokeCallback), BindingFlags.Static | BindingFlags.NonPublic)!;
        var callbackExpression = Expression.Call(
            invokeCallback,
            argumentArray,
            Expression.Constant(callback),
            Expression.Constant(dispatcher),
            Expression.Constant(context));
        var delegateExpression = Expression.Lambda(actionType, callbackExpression, parameters).Compile();
        var factory = typeof(Callable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(Callable.From)
                && method.IsGenericMethodDefinition == (argumentCount > 0)
                && method.GetGenericArguments().Length == argumentCount
                && method.GetParameters()[0].ParameterType.GetGenericTypeDefinitionOrSelf() == actionType.GetGenericTypeDefinitionOrSelf());
        return argumentCount == 0
            ? (Callable)factory.Invoke(null, [delegateExpression])!
            : (Callable)factory.MakeGenericMethod(parameterTypes).Invoke(null, [delegateExpression])!;
    }

    static void InvokeCallback(
        Variant[] values,
        Action<GodotSignalArguments> callback,
        GodotMainThreadDispatcher dispatcher,
        DreamContext context)
    {
        dispatcher.Invoke(() =>
        {
            try { callback(new GodotSignalArguments(values, dispatcher, context)); }
            catch (Exception exception)
            {
                GD.PushError($"DreamLuau callback for Godot signal failed: {exception}");
            }
            return true;
        });
    }
}

/// <summary>Provides typed access to arguments from one Godot signal emission.</summary>
public sealed class GodotSignalArguments
{
    readonly Variant[] values;
    readonly GodotMainThreadDispatcher dispatcher;
    readonly DreamContext context;

    internal GodotSignalArguments(Variant[] values, GodotMainThreadDispatcher dispatcher, DreamContext context)
    {
        this.values = values;
        this.dispatcher = dispatcher;
        this.context = context;
    }

    /// <summary>Number of arguments in this signal emission.</summary>
    public int Count => values.Length;

    /// <summary>Returns the argument converted to its corresponding CLR value.</summary>
    public object? Get(int index) => dispatcher.Invoke(() =>
    {
        if ((uint)index >= (uint)values.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        var value = values[index].Obj;
        return value switch
        {
            null => null,
            Node node => new GodotNodeBridge(node, dispatcher, context),
            Resource resource => new GodotResourceBridge(resource, dispatcher),
            GodotObject target => new GodotObjectReferenceBridge(target, dispatcher),
            StringName text => text.ToString(),
            NodePath path => path.ToString(),
            _ => value,
        };
    });

}

/// <summary>Owns one native Godot signal connection.</summary>
public sealed class GodotSignalConnection : IDisposable
{
    readonly WeakReference<GodotObject> target;
    readonly StringName name;
    readonly Callable callable;
    readonly GodotMainThreadDispatcher dispatcher;
    int connected = 1;

    internal GodotSignalConnection(
        GodotObject target,
        StringName name,
        Callable callable,
        GodotMainThreadDispatcher dispatcher)
    {
        this.target = new WeakReference<GodotObject>(target);
        this.name = name;
        this.callable = callable;
        this.dispatcher = dispatcher;
    }

    /// <summary>Whether the connection has not been disconnected.</summary>
    public bool IsConnected => Volatile.Read(ref connected) != 0 && dispatcher.Invoke(() =>
        target.TryGetTarget(out var value)
        && GodotObject.IsInstanceValid(value)
        && value.IsConnected(name, callable));

    /// <summary>Disconnects the native signal.</summary>
    public void Disconnect() => Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref connected, 0) == 0) return;
        dispatcher.Invoke(() =>
        {
            if (target.TryGetTarget(out var value) && GodotObject.IsInstanceValid(value)
                && value.IsConnected(name, callable))
                value.Disconnect(name, callable);
            return true;
        });
    }
}

static class TypeExtensions
{
    internal static Type GetGenericTypeDefinitionOrSelf(this Type type) =>
        type.IsGenericType ? type.GetGenericTypeDefinition() : type;
}
