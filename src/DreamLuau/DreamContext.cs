using System.Diagnostics;
using DreamLuau.Backend;
using DreamLuau.Signals;
using DreamLuau.Tasks;

namespace DreamLuau;

/// <summary>Owns one VM and serializes operations against its non-thread-safe state.</summary>
public sealed class DreamContext : IDisposable, IAsyncDisposable
{
    readonly DreamRuntime runtime;
    readonly IDreamVm vm;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly TaskCompletionSource disposalCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly object lifetimeSync = new();
    readonly HashSet<IDisposable> ownedResources = [];
    int state;

    internal DreamContext(DreamRuntime runtime, IDreamVm vm)
    {
        this.runtime = runtime;
        this.vm = vm;
        Scheduler = new DreamScheduler();
    }

    /// <summary>True after disposal begins.</summary>
    public bool IsDisposed => Volatile.Read(ref state) != 0;

    /// <summary>Context-owned scheduler for cooperative asynchronous host work.</summary>
    public DreamScheduler Scheduler { get; }

    /// <summary>Subscribes a context-owned listener to a host signal.</summary>
    public DreamConnection Connect<T>(DreamSignal<T> signal, Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return signal.Connect(handler, this);
    }

    /// <summary>Creates a Luau-connectable signal owned and disposed by this context.</summary>
    public DreamScriptSignal CreateSignal()
    {
        ThrowIfDisposed();
        var signal = new DreamScriptSignal(this);
        Track(signal);
        return signal;
    }

    /// <summary>Registers a host resource for disposal with this context.</summary>
    public T OwnResource<T>(T resource) where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        Track(resource);
        return resource;
    }

    internal void Track(IDisposable resource)
    {
        lock (lifetimeSync)
        {
            if (IsDisposed)
            {
                resource.Dispose();
                throw new ObjectDisposedException(nameof(DreamContext));
            }
            ownedResources.Add(resource);
        }
    }

    /// <summary>Executes source and returns its values.</summary>
    public async ValueTask<DreamResult> ExecuteAsync(
        DreamScript script,
        IReadOnlyList<object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(script.Source);
        ArgumentNullException.ThrowIfNull(script.Name);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var values = await vm.ExecuteAsync(
                    script,
                    arguments ?? Array.Empty<object?>(),
                    cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                return new DreamResult(values, stopwatch.Elapsed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new DreamRuntimeException(script.Name, exception);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Executes source text.</summary>
    public ValueTask<DreamResult> ExecuteStringAsync(
        string source,
        string name = "chunk",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(new DreamScript(source, name), cancellationToken: cancellationToken);

    /// <summary>Reads and executes a UTF-8 script file when file execution is enabled.</summary>
    public async ValueTask<DreamResult> ExecuteFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!runtime.Options.AllowFileExecution)
            throw new InvalidOperationException("File execution is disabled for this runtime.");

        var source = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return await ExecuteAsync(new DreamScript(source, path), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads one global value. Dispose returned table and function values.</summary>
    public IDreamValue GetGlobal(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        gate.Wait();
        try
        {
            ThrowIfDisposed();
            return vm.GetGlobal(name);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Sets one global value.</summary>
    public void SetGlobal(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        gate.Wait();
        try
        {
            ThrowIfDisposed();
            vm.SetGlobal(name, value);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Calls a global function and returns its values. Dispose returned table and function values.</summary>
    public async ValueTask<IReadOnlyList<IDreamValue>> CallFunctionAsync(
        string name,
        IReadOnlyList<object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            try
            {
                return await vm.CallGlobalAsync(
                    name,
                    arguments ?? Array.Empty<object?>(),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new DreamRuntimeException(name, exception);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
        {
            await disposalCompletion.Task.ConfigureAwait(false);
            return;
        }

        List<Exception>? failures = null;
        try
        {
            IDisposable[] resources;
            lock (lifetimeSync)
            {
                resources = [.. ownedResources];
                ownedResources.Clear();
            }
            foreach (var resource in resources)
            {
                try { resource.Dispose(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }

            try { await Scheduler.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { (failures ??= []).Add(exception); }

            await gate.WaitAsync().ConfigureAwait(false);
            try { await vm.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            finally { gate.Release(); }

            if (failures is not null)
                throw new AggregateException("DreamContext cleanup failed.", failures);
        }
        catch (Exception exception)
        {
            disposalCompletion.TrySetException(exception);
            throw;
        }
        finally
        {
            Volatile.Write(ref state, 2);
            runtime.Remove(this);
            if (!disposalCompletion.Task.IsCompleted)
                disposalCompletion.TrySetResult();
        }
    }

    void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }
}
