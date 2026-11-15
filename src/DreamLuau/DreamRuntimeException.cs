namespace DreamLuau;

/// <summary>Wraps a script or backend failure with the source name.</summary>
public sealed class DreamRuntimeException : Exception
{
    /// <summary>Creates an exception for a failed script.</summary>
    public DreamRuntimeException(string scriptName, Exception innerException)
        : base($"DreamLuau script '{scriptName}' failed: {innerException.Message}", innerException)
    {
        ScriptName = scriptName;
    }

    /// <summary>Name of the failed script.</summary>
    public string ScriptName { get; }
}
