using DreamLuau.Binding;

namespace DreamLuau.Backend;

/// <summary>One isolated scripting VM and its global environment.</summary>
public interface IDreamVm : IAsyncDisposable
{
    /// <summary>Runs a script in this VM.</summary>
    ValueTask<IReadOnlyList<IDreamValue>> ExecuteAsync(
        DreamScript script,
        IReadOnlyList<object?> arguments,
        CancellationToken cancellationToken);

    /// <summary>Reads a global value.</summary>
    IDreamValue GetGlobal(string name);

    /// <summary>Writes a global value.</summary>
    void SetGlobal(string name, object? value);

    /// <summary>Calls a global function by name.</summary>
    ValueTask<IReadOnlyList<IDreamValue>> CallGlobalAsync(
        string name,
        IReadOnlyList<object?> arguments,
        CancellationToken cancellationToken);

    /// <summary>Registers one C# type binding in this VM.</summary>
    void RegisterType(DreamTypeBinding binding);
}
