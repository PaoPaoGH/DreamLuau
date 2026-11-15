namespace DreamLuau;

/// <summary>Controls which Luau modules each context can require.</summary>
public sealed record DreamModulePolicy
{
    /// <summary>Enables module loading. Disabled by default.</summary>
    public bool Enabled { get; init; }

    /// <summary>Exact module-name to source map. Modules are copied when each VM is created.</summary>
    public IReadOnlyDictionary<string, string> InMemoryModules { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Optional root for filesystem modules. Names are slash-separated and use <see cref="SourceExtension"/>.</summary>
    public string? FileSystemRoot { get; init; }

    /// <summary>Extension appended to filesystem module names.</summary>
    public string SourceExtension { get; init; } = ".luau";

    /// <summary>Maximum UTF-8 source size for one module.</summary>
    public int MaxSourceBytes { get; init; } = 1024 * 1024;
}
