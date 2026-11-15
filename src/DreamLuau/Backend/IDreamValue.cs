namespace DreamLuau.Backend;

/// <summary>Backend-neutral Luau value. Dispose table and function values when no longer needed.</summary>
public interface IDreamValue : IDisposable
{
    /// <summary>Kind of Luau value.</summary>
    DreamValueKind Kind { get; }

    /// <summary>Converts primitive values to CLR values; composite values remain backend-neutral wrappers.</summary>
    object? ToObject();

    /// <summary>Converts this value to a CLR type.</summary>
    T? As<T>();
}

/// <summary>Value categories shared by supported backends.</summary>
public enum DreamValueKind
{
    /// <summary>Luau nil.</summary>
    Nil,
    /// <summary>Luau boolean.</summary>
    Boolean,
    /// <summary>Luau number.</summary>
    Number,
    /// <summary>Luau string.</summary>
    String,
    /// <summary>Luau table.</summary>
    Table,
    /// <summary>Luau function.</summary>
    Function,
    /// <summary>Managed or native userdata.</summary>
    Userdata,
    /// <summary>Backend-specific value without a dedicated category.</summary>
    Other,
}

/// <summary>A callable Luau function.</summary>
public interface IDreamFunction : IDreamValue
{
    /// <summary>Invokes function with supplied arguments.</summary>
    ValueTask<IReadOnlyList<IDreamValue>> InvokeAsync(
        IReadOnlyList<object?> arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>A Luau table with string-key access.</summary>
public interface IDreamTable : IDreamValue
{
    /// <summary>Reads a table field.</summary>
    IDreamValue Get(string key);

    /// <summary>Writes a table field.</summary>
    void Set(string key, object? value);
}

/// <summary>A managed object represented by backend userdata.</summary>
public interface IDreamUserdata : IDreamValue
{
    /// <summary>Returns the managed object while its context remains alive.</summary>
    object GetTarget();
}
