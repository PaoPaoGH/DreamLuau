namespace DreamLuau.Binding;

/// <summary>Tracks managed objects exposed as userdata and rejects stale IDs.</summary>
public sealed class DreamObjectRegistry : IDisposable
{
    readonly object sync = new();
    readonly Dictionary<long, object> objects = [];
    long nextId;
    bool disposed;

    /// <summary>Registers an object and returns its opaque handle.</summary>
    public DreamObjectHandle Register(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var id = checked(++nextId);
            objects.Add(id, target);
            return new DreamObjectHandle(id);
        }
    }

    /// <summary>Gets a live target or throws when handle was invalidated.</summary>
    public object GetTarget(DreamObjectHandle handle)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (objects.TryGetValue(handle.Value, out var target))
                return target;
            throw new InvalidOperationException($"Dream object handle {handle.Value} is stale.");
        }
    }

    /// <summary>Returns whether handle still points to a live object.</summary>
    public bool IsValid(DreamObjectHandle handle)
    {
        lock (sync)
            return !disposed && objects.ContainsKey(handle.Value);
    }

    /// <summary>Invalidates one handle. Repeated invalidation returns false.</summary>
    public bool Invalidate(DreamObjectHandle handle)
    {
        lock (sync)
            return !disposed && objects.Remove(handle.Value);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
            objects.Clear();
        }
    }
}
