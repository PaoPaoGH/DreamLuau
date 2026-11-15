namespace DreamLuau;

/// <summary>Configuration shared by contexts created from a runtime.</summary>
public sealed record DreamRuntimeOptions
{
    /// <summary>Sandbox policy used for every new context.</summary>
    public DreamSandboxPolicy Sandbox { get; init; } = new();

    /// <summary>Module loading policy used for every new context.</summary>
    public DreamModulePolicy Modules { get; init; } = new();

    /// <summary>Whether file execution is allowed.</summary>
    public bool AllowFileExecution { get; init; } = true;
}
