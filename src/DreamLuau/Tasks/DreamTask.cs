using System.Runtime.ExceptionServices;

namespace DreamLuau.Tasks;

/// <summary>Owns one scheduled asynchronous operation and its cancellation source.</summary>
public sealed class DreamTask : IDisposable, IAsyncDisposable
{
    readonly object sync = new();
    readonly CancellationTokenSource cancellation;
    int disposed;

    internal DreamTask(Task completion, CancellationTokenSource cancellation)
    {
        Completion = completion;
        this.cancellation = cancellation;
    }

    /// <summary>Underlying operation completion.</summary>
    public Task Completion { get; }

    /// <summary>Whether operation has completed.</summary>
    public bool IsCompleted => Completion.IsCompleted;

    /// <summary>Requests cooperative cancellation.</summary>
    public void Cancel()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        lock (sync)
        {
            if (disposed == 0)
                cancellation.Cancel();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Exception? cancellationFailure = null;
        lock (sync)
        {
            try { cancellation.Cancel(throwOnFirstException: false); }
            catch (Exception exception) { cancellationFailure = exception; }
            if (Completion.IsCompleted)
                cancellation.Dispose();
            else
                _ = Completion.ContinueWith(
                    _ => cancellation.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }
        if (cancellationFailure is not null)
            ExceptionDispatchInfo.Capture(cancellationFailure).Throw();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        List<Exception>? failures = null;
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            try
            {
                lock (sync)
                    cancellation.Cancel(throwOnFirstException: false);
            }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        finally
        {
            cancellation.Dispose();
        }
        if (failures is { Count: 1 })
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 })
            throw new AggregateException("DreamTask cleanup failed.", failures);
    }

    internal void ReleaseCompleted()
    {
        if (!Completion.IsCompleted || Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        lock (sync)
            cancellation.Dispose();
    }
}
