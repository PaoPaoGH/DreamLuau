using System.Reflection;

namespace DreamLuau.Binding;

/// <summary>Reflection metadata selected explicitly for one bound C# type.</summary>
public sealed class DreamTypeBinding
{
    readonly List<DreamConstructorBinding> constructors = [];
    readonly List<DreamPropertyBinding> properties = [];
    readonly List<DreamMethodBinding> methods = [];
    readonly List<DreamMethodBinding> staticMethods = [];
    readonly object sync = new();
    bool hasDefaultValueTypeConstructor;

    internal DreamTypeBinding(string name, Type targetType, DreamValueConverter converter)
    {
        Name = name;
        TargetType = targetType;
        Converter = converter;
    }

    DreamTypeBinding(DreamTypeBinding source)
    {
        Name = source.Name;
        TargetType = source.TargetType;
        Converter = source.Converter;
        constructors.AddRange(source.constructors);
        properties.AddRange(source.properties);
        methods.AddRange(source.methods);
        staticMethods.AddRange(source.staticMethods);
        hasDefaultValueTypeConstructor = source.hasDefaultValueTypeConstructor;
    }

    /// <summary>Luau global name for this type.</summary>
    public string Name { get; }

    /// <summary>Bound CLR type.</summary>
    public Type TargetType { get; }

    /// <summary>Shared converter used for method and property values.</summary>
    public DreamValueConverter Converter { get; }

    /// <summary>Selected public constructors.</summary>
    public IReadOnlyList<DreamConstructorBinding> Constructors { get { lock (sync) return constructors.ToArray(); } }

    /// <summary>Whether a boxed default value type constructor was explicitly exposed.</summary>
    public bool HasDefaultValueTypeConstructor { get { lock (sync) return hasDefaultValueTypeConstructor; } }

    /// <summary>Selected instance properties.</summary>
    public IReadOnlyList<DreamPropertyBinding> Properties { get { lock (sync) return properties.ToArray(); } }

    /// <summary>Selected instance methods.</summary>
    public IReadOnlyList<DreamMethodBinding> Methods { get { lock (sync) return methods.ToArray(); } }

    /// <summary>Selected static methods.</summary>
    public IReadOnlyList<DreamMethodBinding> StaticMethods { get { lock (sync) return staticMethods.ToArray(); } }

    internal DreamTypeBinding Snapshot()
    {
        lock (sync)
            return new(this);
    }

    internal void AddConstructor(ConstructorInfo constructor)
    {
        lock (sync)
            if (constructors.All(binding => binding.Constructor != constructor))
                constructors.Add(new DreamConstructorBinding(constructor));
    }

    internal void AddDefaultValueTypeConstructor()
    {
        lock (sync)
            hasDefaultValueTypeConstructor = true;
    }

    internal void AddProperty(PropertyInfo property, bool allowWrite)
    {
        lock (sync)
        {
            var existing = properties.FindIndex(binding => binding.Property == property);
            if (existing < 0)
                properties.Add(new DreamPropertyBinding(property, allowWrite));
            else if (allowWrite && !properties[existing].CanWrite)
                properties[existing] = new DreamPropertyBinding(property, allowWrite: true);
        }
    }

    internal void AddMethod(MethodInfo method, bool isStatic)
    {
        lock (sync)
        {
            var list = isStatic ? staticMethods : methods;
            if (list.All(binding => binding.Method != method))
                list.Add(new DreamMethodBinding(method));
        }
    }
}

/// <summary>One explicitly exposed public constructor.</summary>
public sealed class DreamConstructorBinding(ConstructorInfo constructor)
{
    /// <summary>Selected CLR constructor.</summary>
    public ConstructorInfo Constructor { get; } = constructor;
}

/// <summary>One explicitly exposed instance property.</summary>
public sealed class DreamPropertyBinding(PropertyInfo property, bool allowWrite)
{
    /// <summary>Selected CLR property.</summary>
    public PropertyInfo Property { get; } = property;

    /// <summary>Whether Luau assignment may call the public setter.</summary>
    public bool CanWrite { get; } = allowWrite && property.SetMethod is { IsPublic: true };
}

/// <summary>One explicitly exposed method.</summary>
public sealed class DreamMethodBinding(MethodInfo method)
{
    /// <summary>Selected CLR method.</summary>
    public MethodInfo Method { get; } = method;
}
