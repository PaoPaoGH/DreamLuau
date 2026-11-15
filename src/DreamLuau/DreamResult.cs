using DreamLuau.Backend;

namespace DreamLuau;

/// <summary>Values returned by one script execution.</summary>
public sealed class DreamResult(IReadOnlyList<IDreamValue> values, TimeSpan elapsed) : IDisposable
{
    /// <summary>Returned values in Luau order.</summary>
    public IReadOnlyList<IDreamValue> Values { get; } = values;

    /// <summary>Wall time spent executing script.</summary>
    public TimeSpan Elapsed { get; } = elapsed;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var value in Values)
            value.Dispose();
    }
}
