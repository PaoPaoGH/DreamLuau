using DreamLuau.Backend;
using NuLua;
using NuLua.Luau;

namespace DreamLuau.NuLua;

/// <summary>NuLua Luau backend. Each call creates a fresh native VM.</summary>
public sealed class NuLuaBackend : IDreamBackend
{
    /// <inheritdoc />
    public IDreamVm CreateVm(DreamRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var rootState = LuauState.Create();
        try
        {
            OpenLibraries(rootState, options.Sandbox.Libraries);
            ArgumentNullException.ThrowIfNull(options.Modules);
            if (options.Modules.Enabled)
                rootState.UseModuleLoader(new NuLuaModuleLoader(options.Modules));
            if (!options.Sandbox.Enabled)
                return new NuLuaVm(rootState, rootState);

            var sandboxState = rootState.CreateSandboxThread();
            rootState.Pop(1);
            return new NuLuaVm(rootState, sandboxState);
        }
        catch
        {
            rootState.Dispose();
            throw;
        }
    }

    static void OpenLibraries(LuauState state, DreamLibrary libraries)
    {
        if (libraries.HasFlag(DreamLibrary.Base)) state.OpenBaseLibrary();
        if (libraries.HasFlag(DreamLibrary.Coroutine)) state.OpenCoroutineLibrary();
        if (libraries.HasFlag(DreamLibrary.Table)) state.OpenTableLibrary();
        if (libraries.HasFlag(DreamLibrary.String)) state.OpenStringLibrary();
        if (libraries.HasFlag(DreamLibrary.Math)) state.OpenMathLibrary();
        if (libraries.HasFlag(DreamLibrary.Os)) state.OpenOsLibrary();
        if (libraries.HasFlag(DreamLibrary.Debug)) state.OpenDebugLibrary();
        if (libraries.HasFlag(DreamLibrary.Bit32)) state.OpenBit32Library();
        if (libraries.HasFlag(DreamLibrary.Buffer)) state.OpenBufferLibrary();
        if (libraries.HasFlag(DreamLibrary.Utf8)) state.OpenUtf8Library();
        if (libraries.HasFlag(DreamLibrary.Vector)) state.OpenVectorLibrary();
        if (libraries.HasFlag(DreamLibrary.Integer)) state.OpenIntegerLibrary();
    }
}
