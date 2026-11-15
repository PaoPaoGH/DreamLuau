using System.Linq.Expressions;
using System.Reflection;

namespace DreamLuau.Binding;

/// <summary>Owns type registrations and custom value converters for a runtime.</summary>
public sealed class DreamBindingRegistry
{
    readonly object sync = new();
    readonly Dictionary<string, DreamTypeBinding> byName = new(StringComparer.Ordinal);
    readonly Dictionary<Type, DreamTypeBinding> byType = [];

    /// <summary>Converters used by all registered type bindings.</summary>
    public DreamValueConverter Converter { get; } = new();

    /// <summary>Starts a binding for a CLR type under a Luau global name.</summary>
    public DreamBindingBuilder<T> Register<T>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (sync)
        {
            if (byName.ContainsKey(name))
                throw new InvalidOperationException($"A binding named '{name}' already exists.");
            if (byType.ContainsKey(typeof(T)))
                throw new InvalidOperationException($"Type '{typeof(T)}' is already registered.");
            var binding = new DreamTypeBinding(name, typeof(T), Converter);
            byName.Add(name, binding);
            byType.Add(typeof(T), binding);
            return new DreamBindingBuilder<T>(binding);
        }
    }

    internal IReadOnlyList<DreamTypeBinding> Snapshot()
    {
        lock (sync)
            return byName.Values.Select(binding => binding.Snapshot()).ToArray();
    }
}

/// <summary>Fluent selector for constructors, properties, and methods exposed to Luau.</summary>
public sealed class DreamBindingBuilder<T>
{
    readonly DreamTypeBinding binding;

    internal DreamBindingBuilder(DreamTypeBinding binding) => this.binding = binding;

    /// <summary>Exposes a public constructor with exact parameter types.</summary>
    public DreamBindingBuilder<T> Constructor(params Type[] parameterTypes)
    {
        ArgumentNullException.ThrowIfNull(parameterTypes);
        var constructor = typeof(T).GetConstructor(parameterTypes);
        if (constructor is null && typeof(T).IsValueType && parameterTypes.Length == 0)
        {
            binding.AddDefaultValueTypeConstructor();
            return this;
        }
        if (constructor is null)
            throw new ArgumentException($"Public constructor for '{typeof(T)}' was not found.", nameof(parameterTypes));
        binding.AddConstructor(constructor);
        return this;
    }

    /// <summary>Exposes a parameterless constructor.</summary>
    public DreamBindingBuilder<T> Constructor() => Constructor(Type.EmptyTypes);

    /// <summary>Exposes a constructor selected by its parameter type.</summary>
    public DreamBindingBuilder<T> Constructor<T1>() => Constructor(typeof(T1));

    /// <summary>Exposes a constructor selected by its parameter types.</summary>
    public DreamBindingBuilder<T> Constructor<T1, T2>() => Constructor(typeof(T1), typeof(T2));

    /// <summary>Exposes a constructor selected by its parameter types.</summary>
    public DreamBindingBuilder<T> Constructor<T1, T2, T3>() => Constructor(typeof(T1), typeof(T2), typeof(T3));

    /// <summary>Exposes a public property, including its public setter when present.</summary>
    public DreamBindingBuilder<T> Property<TValue>(Expression<Func<T, TValue>> selector)
    {
        var property = GetProperty(selector);
        if (property.GetMethod!.IsStatic)
            throw new ArgumentException("Static properties cannot be registered as instance properties.", nameof(selector));
        binding.AddProperty(property, allowWrite: property.SetMethod is { IsPublic: true });
        return this;
    }

    /// <summary>Exposes a property as read-only.</summary>
    public DreamBindingBuilder<T> ReadOnlyProperty<TValue>(Expression<Func<T, TValue>> selector)
    {
        var property = GetProperty(selector);
        if (property.GetMethod!.IsStatic)
            throw new ArgumentException("Static properties cannot be registered as instance properties.", nameof(selector));
        binding.AddProperty(property, allowWrite: false);
        return this;
    }

    /// <summary>Exposes an instance method selected by an expression.</summary>
    public DreamBindingBuilder<T> Method(Expression<Action<T>> selector)
    {
        var method = GetMethod(selector.Body);
        if (method.IsStatic)
            throw new ArgumentException("Static methods must use StaticMethod.", nameof(selector));
        binding.AddMethod(method, isStatic: false);
        return this;
    }

    /// <summary>Exposes an instance method selected by an expression.</summary>
    public DreamBindingBuilder<T> Method<TResult>(Expression<Func<T, TResult>> selector)
    {
        var method = GetMethod(selector.Body);
        if (method.IsStatic)
            throw new ArgumentException("Static methods must use StaticMethod.", nameof(selector));
        binding.AddMethod(method, isStatic: false);
        return this;
    }

    /// <summary>Exposes a static void method selected by an expression.</summary>
    public DreamBindingBuilder<T> StaticMethod(Expression<Action> selector)
    {
        var method = GetMethod(selector.Body);
        if (!method.IsStatic)
            throw new ArgumentException("Instance methods must use Method.", nameof(selector));
        binding.AddMethod(method, isStatic: true);
        return this;
    }

    /// <summary>Exposes a static method selected by an expression.</summary>
    public DreamBindingBuilder<T> StaticMethod<TResult>(Expression<Func<TResult>> selector)
    {
        var method = GetMethod(selector.Body);
        if (!method.IsStatic)
            throw new ArgumentException("Instance methods must use Method.", nameof(selector));
        binding.AddMethod(method, isStatic: true);
        return this;
    }

    static PropertyInfo GetProperty<TValue>(Expression<Func<T, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var expression = StripConvert(selector.Body);
        if (expression is not MemberExpression { Member: PropertyInfo property })
            throw new ArgumentException("Selector must identify one property.", nameof(selector));
        if (property.GetMethod is not { IsPublic: true })
            throw new ArgumentException("Bound property must have a public getter.", nameof(selector));
        return property;
    }

    static MethodInfo GetMethod(Expression expression)
    {
        expression = StripConvert(expression);
        if (expression is not MethodCallExpression call || !call.Method.IsPublic)
            throw new ArgumentException("Selector must identify one public method.");
        return call.Method;
    }

    static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            expression = unary.Operand;
        return expression;
    }
}
