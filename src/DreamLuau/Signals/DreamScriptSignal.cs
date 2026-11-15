namespace DreamLuau.Signals;

/// <summary>Context-owned signal that lets Luau subscribe to host-published values.</summary>
public sealed class DreamScriptSignal : IDisposable
{
    readonly DreamContext owner;
    readonly DreamSignal<object?> signal = new();

    internal DreamScriptSignal(DreamContext owner) => this.owner = owner;

    /// <summary>Subscribes a Luau or CLR callback; context disposal removes the subscription.</summary>
    public DreamConnection Connect(Action<object?> handler) => signal.Connect(handler, owner);

    /// <summary>Publishes a value to active listeners.</summary>
    public void Publish(object? value) => signal.Fire(value);

    /// <inheritdoc />
    public void Dispose() => signal.Dispose();
}
