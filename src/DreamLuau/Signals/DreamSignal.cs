namespace DreamLuau.Signals;

/// <summary>Thread-safe typed event signal with disposable subscriptions.</summary>
public sealed class DreamSignal<T> : IDisposable
{
    readonly object sync = new();
    readonly Dictionary<long, DreamConnection> connections = [];
    long nextId;
    bool disposed;

    /// <summary>Number of live listeners.</summary>
    public int ConnectionCount
    {
        get { lock (sync) return connections.Count; }
    }

    /// <summary>Subscribes a handler; optional context owns and disposes connection.</summary>
    public DreamConnection Connect(Action<T> handler, DreamContext? owner = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        DreamConnection? connection = null;
        long id;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            id = checked(++nextId);
            connection = new DreamConnection(handler, () => Remove(id));
            connections.Add(id, connection);
        }

        try
        {
            owner?.Track(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Invokes active listeners. Handler failures aggregate after all listeners run.</summary>
    public void Fire(T value)
    {
        DreamConnection[] snapshot;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            snapshot = [.. connections.Values];
        }

        List<Exception>? failures = null;
        foreach (var connection in snapshot)
        {
            try
            {
                connection.Invoke(value);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }
        if (failures is not null)
            throw new AggregateException("One or more DreamSignal listeners failed.", failures);
    }

    void Remove(long id)
    {
        lock (sync)
            connections.Remove(id);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DreamConnection[] snapshot;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            snapshot = [.. connections.Values];
            connections.Clear();
        }
        foreach (var connection in snapshot)
            connection.Dispose();
    }
}
