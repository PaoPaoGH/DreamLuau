using DreamLuau.Backend;
using DreamLuau.NuLua;
using Godot;

namespace DreamLuau.Godot;

/// <summary>Godot Node that owns one DreamLuau runtime and context.</summary>
public partial class DreamGodotRuntime : Node
{
    DreamRuntime? runtime;
    DreamContext? context;
    GodotMainThreadDispatcher? dispatcher;
    bool disposed;

    /// <summary>Runtime owned by this node after initialization.</summary>
    public DreamRuntime Runtime => runtime ?? throw new InvalidOperationException("Runtime is not initialized.");

    /// <summary>Context owned by this node after initialization.</summary>
    public DreamContext Context => context ?? throw new InvalidOperationException("Runtime is not initialized.");

    /// <inheritdoc />
    public override void _Ready() => Initialize();

    /// <summary>Initializes backend, bindings, context, and main-thread dispatcher.</summary>
    public void Initialize(DreamRuntimeOptions? options = null)
    {
        GodotMainThreadDispatcher.EnsureMainThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (runtime is not null) return;

        dispatcher = new GodotMainThreadDispatcher();
        AddChild(dispatcher);
        runtime = new DreamRuntime(new NuLuaBackend(), options);
        GodotBindings.Register(runtime.Bindings);
        context = runtime.CreateContext();
        context.SetGlobal("Engine", new GodotEngineBridge(this, dispatcher, context));
    }

    /// <summary>Runs source on Godot's main thread and returns script results.</summary>
    public ValueTask<DreamResult> ExecuteStringAsync(
        string source,
        string name = "chunk",
        CancellationToken cancellationToken = default)
    {
        var activeContext = Context;
        if (GodotMainThreadDispatcher.IsMainThread())
            return activeContext.ExecuteStringAsync(source, name, cancellationToken);

        var activeDispatcher = dispatcher
            ?? throw new InvalidOperationException("Main-thread dispatcher is not initialized.");
        return new ValueTask<DreamResult>(activeDispatcher.DispatchAsync(
            () => activeContext.ExecuteStringAsync(source, name, cancellationToken),
            cancellationToken));
    }

    /// <summary>Loads source asynchronously, then executes it on Godot's main thread.</summary>
    public async ValueTask<DreamResult> ExecuteFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var source = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return await ExecuteStringAsync(source, path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void _ExitTree() => DisposeRuntime();

    /// <summary>Disposes context and runtime before this node leaves the tree.</summary>
    public void DisposeRuntime()
    {
        if (disposed) return;
        disposed = true;
        runtime?.Dispose();
        dispatcher?.Close();
        context = null;
        runtime = null;
        dispatcher = null;
    }
}
