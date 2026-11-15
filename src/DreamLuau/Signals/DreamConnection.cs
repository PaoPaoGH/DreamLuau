namespace DreamLuau.Signals;

/// <summary>Disposable subscription to a Dream signal.</summary>
public sealed class DreamConnection : IDisposable
{
    readonly Action dispose;
    readonly Delegate handler;
    int connected = 1;

    internal DreamConnection(Delegate handler, Action dispose)
    {
        this.handler = handler;
        this.dispose = dispose;
    }

    /// <summary>Whether this subscription remains active.</summary>
    public bool IsConnected => Volatile.Read(ref connected) != 0;

    /// <summary>Disconnects this subscription.</summary>
    public void Disconnect() => Dispose();

    internal void Invoke<T>(T value)
    {
        if (IsConnected)
            ((Action<T>)handler)(value);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref connected, 0) != 0)
            dispose();
    }
}
