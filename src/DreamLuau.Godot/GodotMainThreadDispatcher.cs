using System.Collections.Concurrent;
using Godot;

namespace DreamLuau.Godot;

/// <summary>Queues synchronous Godot work for execution by Node's main-thread process callback.</summary>
public partial class GodotMainThreadDispatcher : Node
{
    readonly ConcurrentQueue<IWorkItem> work = new();
    int closing;

    /// <inheritdoc />
    public override void _Ready() => SetProcess(true);

    /// <summary>Runs operation now on main thread or queues it for the next process callback.</summary>
    public Task<T> DispatchAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);
        if (IsMainThread())
        {
            try { return Task.FromResult(operation()); }
            catch (Exception exception) { return Task.FromException<T>(exception); }
        }
        if (Volatile.Read(ref closing) != 0)
            return Task.FromException<T>(new ObjectDisposedException(nameof(GodotMainThreadDispatcher)));

        var item = new WorkItem<T>(operation, cancellationToken);
        work.Enqueue(item);
        if (Volatile.Read(ref closing) != 0)
            item.Fail(new ObjectDisposedException(nameof(GodotMainThreadDispatcher)));
        return item.Task;
    }

    /// <summary>Runs async work after entering the main-thread process loop.</summary>
    public Task<T> DispatchAsync<T>(Func<ValueTask<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);
        if (IsMainThread())
            return operation().AsTask();
        if (Volatile.Read(ref closing) != 0)
            return Task.FromException<T>(new ObjectDisposedException(nameof(GodotMainThreadDispatcher)));

        var item = new AsyncWorkItem<T>(operation, cancellationToken);
        work.Enqueue(item);
        if (Volatile.Read(ref closing) != 0)
            item.Fail(new ObjectDisposedException(nameof(GodotMainThreadDispatcher)));
        return item.Task;
    }

    /// <summary>Runs an operation on the main thread and waits for its result.</summary>
    public T Invoke<T>(Func<T> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (IsMainThread()) return operation();
        return DispatchAsync(operation, cancellationToken).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        while (work.TryDequeue(out var item))
            item.Start();
    }

    /// <inheritdoc />
    public override void _ExitTree() => Close();

    /// <summary>Rejects pending work during node shutdown.</summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref closing, 1) != 0) return;
        while (work.TryDequeue(out var item))
            item.Fail(new ObjectDisposedException(nameof(GodotMainThreadDispatcher)));
    }

    /// <summary>Throws when Godot API access occurs away from its main thread.</summary>
    public static void EnsureMainThread()
    {
        if (!IsMainThread())
            throw new InvalidOperationException("Godot objects can only be accessed on the main thread.");
    }

    internal static bool IsMainThread() => GodotThread.IsMainThread();

    interface IWorkItem
    {
        void Start();
        void Fail(Exception exception);
    }

    sealed class WorkItem<T>(Func<T> operation, CancellationToken cancellationToken) : IWorkItem
    {
        readonly TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T> Task => completion.Task;

        public void Start()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }
            try { completion.TrySetResult(operation()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }

        public void Fail(Exception exception) => completion.TrySetException(exception);
    }

    sealed class AsyncWorkItem<T>(Func<ValueTask<T>> operation, CancellationToken cancellationToken) : IWorkItem
    {
        readonly TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T> Task => completion.Task;

        public void Start() => _ = RunAsync();

        async Task RunAsync()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }
            try { completion.TrySetResult(await operation().ConfigureAwait(false)); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }

        public void Fail(Exception exception) => completion.TrySetException(exception);
    }
}
