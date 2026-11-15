namespace DreamLuau.Tasks;

/// <summary>Schedules context-owned async work and cancels it during shutdown.</summary>
public sealed class DreamScheduler : IAsyncDisposable
{
    readonly object sync = new();
    readonly CancellationTokenSource shutdown = new();
    readonly HashSet<DreamTask> tasks = [];
    bool disposed;

    /// <summary>Schedules work on the .NET thread pool.</summary>
    public DreamTask Schedule(Func<CancellationToken, ValueTask> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, cancellationToken);
            var task = Task.Run(async () => await work(cancellation.Token).ConfigureAwait(false));
            var dreamTask = new DreamTask(task, cancellation);
            tasks.Add(dreamTask);
            _ = task.ContinueWith(
                _ => Remove(dreamTask),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return dreamTask;
        }
    }

    void Remove(DreamTask task)
    {
        task.ReleaseCompleted();
        lock (sync)
            tasks.Remove(task);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        DreamTask[] snapshot;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            snapshot = [.. tasks];
        }

        List<Exception>? failures = null;
        try { shutdown.Cancel(throwOnFirstException: false); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        foreach (var task in snapshot)
        {
            try { task.Cancel(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        try
        {
            await Task.WhenAll(snapshot.Select(task => task.Completion)).ConfigureAwait(false);
        }
        catch
        {
            var taskFailures = snapshot
                .Where(task => task.Completion.IsFaulted)
                .SelectMany(task => task.Completion.Exception!.InnerExceptions)
                .ToArray();
            if (taskFailures.Length > 0)
                (failures ??= []).AddRange(taskFailures);
        }
        finally
        {
            foreach (var task in snapshot)
            {
                try { task.Dispose(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
            shutdown.Dispose();
        }
        if (failures is not null)
            throw new AggregateException("Scheduled Dream tasks failed during shutdown.", failures);
    }
}
