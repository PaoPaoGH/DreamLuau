namespace DreamLuau;

/// <summary>Source text and diagnostic name for one script.</summary>
public sealed record DreamScript(string Source, string Name = "chunk");
