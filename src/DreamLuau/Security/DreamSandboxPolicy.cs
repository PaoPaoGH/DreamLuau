namespace DreamLuau;

/// <summary>Libraries a VM may expose to scripts.</summary>
[Flags]
public enum DreamLibrary
{
    /// <summary>No libraries.</summary>
    None = 0,
    /// <summary>Base functions.</summary>
    Base = 1 << 0,
    /// <summary>Coroutine functions.</summary>
    Coroutine = 1 << 1,
    /// <summary>Table helpers.</summary>
    Table = 1 << 2,
    /// <summary>String helpers.</summary>
    String = 1 << 3,
    /// <summary>Math helpers.</summary>
    Math = 1 << 4,
    /// <summary>Operating system helpers.</summary>
    Os = 1 << 5,
    /// <summary>Debug helpers.</summary>
    Debug = 1 << 6,
    /// <summary>Bit32 helpers.</summary>
    Bit32 = 1 << 7,
    /// <summary>Buffer helpers.</summary>
    Buffer = 1 << 8,
    /// <summary>UTF-8 helpers.</summary>
    Utf8 = 1 << 9,
    /// <summary>Vector helpers.</summary>
    Vector = 1 << 10,
    /// <summary>Integer helpers.</summary>
    Integer = 1 << 11,
    /// <summary>All libraries supported by the backend.</summary>
    All = int.MaxValue,
}

/// <summary>Sandbox and library policy applied when each VM is created.</summary>
public sealed record DreamSandboxPolicy
{
    /// <summary>Enables backend sandbox setup when available.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Libraries opened in each VM. Dangerous libraries are excluded by default.</summary>
    public DreamLibrary Libraries { get; init; } =
        DreamLibrary.Base
        | DreamLibrary.Coroutine
        | DreamLibrary.Table
        | DreamLibrary.String
        | DreamLibrary.Math
        | DreamLibrary.Bit32
        | DreamLibrary.Buffer
        | DreamLibrary.Utf8
        | DreamLibrary.Vector
        | DreamLibrary.Integer;

    /// <summary>Optional allowlist of registered host API globals.</summary>
    /// <remarks>Each listed name must exactly match a binding name. This does not restrict script-created globals.</remarks>
    public IReadOnlySet<string>? AllowedBindingNames { get; init; }
}
