namespace DreamLuau.Backend;

/// <summary>Creates independent virtual machines for DreamLuau contexts.</summary>
public interface IDreamBackend
{
    /// <summary>Creates a new VM with its own global environment.</summary>
    IDreamVm CreateVm(DreamRuntimeOptions options);
}
