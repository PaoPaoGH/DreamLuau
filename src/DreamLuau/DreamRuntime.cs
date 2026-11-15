using DreamLuau.Backend;
using DreamLuau.Binding;
using DreamLuau.Signals;

namespace DreamLuau;

/// <summary>Owns backend configuration and tracks independent script contexts.</summary>
public sealed class DreamRuntime : IDisposable, IAsyncDisposable
{
    readonly IDreamBackend backend;
    readonly object sync = new();
    readonly HashSet<DreamContext> contexts = [];
    readonly TaskCompletionSource disposalCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool disposed;

    /// <summary>Creates a runtime using the selected VM backend.</summary>
    public DreamRuntime(IDreamBackend backend, DreamRuntimeOptions? options = null)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        Options = options ?? new DreamRuntimeOptions();
        ArgumentNullException.ThrowIfNull(Options.Sandbox);
        ArgumentNullException.ThrowIfNull(Options.Modules);
        Bindings = new DreamBindingRegistry();
        Bindings.Register<DreamScriptSignal>("Signal")
            .Method(signal => signal.Connect(null!));
        Bindings.Register<DreamConnection>("Connection")
            .ReadOnlyProperty(connection => connection.IsConnected)
            .Method(connection => connection.Disconnect());
    }

    /// <summary>Runtime configuration applied to each new VM.</summary>
    public DreamRuntimeOptions Options { get; }

    /// <summary>Types exposed to contexts created by this runtime.</summary>
    public DreamBindingRegistry Bindings { get; }

    /// <summary>Creates a context with an independent VM and global environment.</summary>
    public DreamContext CreateContext()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            var vm = backend.CreateVm(Options);
            try
            {
                foreach (var binding in Bindings.Snapshot())
                {
                    var allowedNames = Options.Sandbox.AllowedBindingNames;
                    if (allowedNames is null || allowedNames.Contains(binding.Name))
                        vm.RegisterType(binding);
                }
                var context = new DreamContext(this, vm);
                contexts.Add(context);
                return context;
            }
            catch
            {
                vm.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw;
            }
        }
    }

    internal void Remove(DreamContext context)
    {
        lock (sync)
            contexts.Remove(context);
    }

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        DreamContext[] snapshot;
        var ownsDisposal = false;
        lock (sync)
        {
            if (disposed)
                snapshot = [];
            else
            {
                disposed = true;
                snapshot = [.. contexts];
                ownsDisposal = true;
            }
        }

        if (!ownsDisposal)
        {
            await disposalCompletion.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            List<Exception>? failures = null;
            foreach (var context in snapshot)
            {
                try { await context.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
            if (failures is not null)
                throw new AggregateException("One or more DreamContext instances failed to dispose.", failures);
        }
        catch (Exception exception)
        {
            disposalCompletion.TrySetException(exception);
            throw;
        }
        finally
        {
            if (!disposalCompletion.Task.IsCompleted)
                disposalCompletion.TrySetResult();
        }
    }

    void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
