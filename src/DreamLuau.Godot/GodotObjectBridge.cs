using Godot;

namespace DreamLuau.Godot;

/// <summary>Weak, validity-checked bridge to one Godot object.</summary>
public abstract class GodotObjectBridge<T> where T : GodotObject
{
    readonly WeakReference<T> target;
    readonly GodotMainThreadDispatcher dispatcher;

    /// <summary>Creates a weak bridge to a Godot object.</summary>
    /// <param name="target">Godot object to expose.</param>
    /// <param name="dispatcher">Dispatcher used to marshal Godot calls to the main thread.</param>
    protected GodotObjectBridge(T target, GodotMainThreadDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(target);
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.target = new WeakReference<T>(target);
    }

    /// <summary>Runs an operation on a live Godot object on the main thread.</summary>
    protected TResult Invoke<TResult>(Func<T, TResult> operation) => dispatcher.Invoke(() => operation(ResolveTarget()));

    /// <summary>Runs a Godot operation on a live object on the main thread.</summary>
    protected void Invoke(Action<T> operation) => dispatcher.Invoke(() =>
    {
        operation(ResolveTarget());
        return true;
    });

    T ResolveTarget()
    {
        if (!target.TryGetTarget(out var value) || !GodotObject.IsInstanceValid(value))
            throw new ObjectDisposedException(typeof(T).Name, "Godot object was freed or is no longer valid.");
        return value;
    }

    /// <summary>Dispatcher shared by objects created from this bridge.</summary>
    protected GodotMainThreadDispatcher Dispatcher => dispatcher;
}

/// <summary>Exposes a small, explicit subset of a Godot Node.</summary>
public sealed class GodotNodeBridge(Node node, GodotMainThreadDispatcher dispatcher, DreamContext context)
    : GodotObjectBridge<Node>(node, dispatcher)
{
    readonly DreamContext context = context ?? throw new ArgumentNullException(nameof(context));
    /// <summary>Node name.</summary>
    public string Name
    {
        get => Invoke(node => node.Name);
        set => Invoke(node => node.Name = value);
    }

    /// <summary>Number of direct children.</summary>
    public int ChildCount => Invoke(node => node.GetChildCount());

    /// <summary>Parent node, if one exists.</summary>
    public GodotNodeBridge? Parent
    {
        get
        {
            return Invoke(node =>
            {
                var parent = node.GetParent();
                return parent is null ? null : new GodotNodeBridge(parent, Dispatcher, context);
            });
        }
    }

    /// <summary>Returns one direct child.</summary>
    public GodotNodeBridge GetChild(int index) => Invoke(node => new GodotNodeBridge(node.GetChild(index), Dispatcher, context));

    /// <summary>Returns a node at a relative NodePath.</summary>
    public GodotNodeBridge GetNode(string path) => Invoke(node => new GodotNodeBridge(node.GetNode(path), Dispatcher, context));

    /// <summary>Returns a bridge to one native Godot signal.</summary>
    public GodotSignalBridge GetSignal(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Invoke(node => new GodotSignalBridge(node, name, Dispatcher, context));
    }

    /// <summary>Adds another bridged node as a child.</summary>
    public void AddChild(GodotNodeBridge child)
    {
        ArgumentNullException.ThrowIfNull(child);
        Invoke(node => node.AddChild(child.GetNode()));
    }

    /// <summary>Queues this node for deletion at the end of the frame.</summary>
    public void QueueFree() => Invoke(node => node.QueueFree());

    internal Node GetNode() => Invoke(node => node);
}

/// <summary>Exposes the root node from one SceneTree.</summary>
public sealed class GodotSceneTreeBridge(SceneTree tree, GodotMainThreadDispatcher dispatcher, DreamContext context)
    : GodotObjectBridge<SceneTree>(tree, dispatcher)
{
    /// <summary>Scene tree root.</summary>
    public GodotNodeBridge Root => Invoke(tree => new GodotNodeBridge(tree.Root, Dispatcher, context));

    /// <summary>Currently active scene, when one is assigned.</summary>
    public GodotNodeBridge? CurrentScene => Invoke(tree =>
        tree.CurrentScene is { } scene ? new GodotNodeBridge(scene, Dispatcher, context) : null);
}

/// <summary>Exposes resource identity without exposing unrestricted Godot reflection.</summary>
public sealed class GodotResourceBridge(Resource resource, GodotMainThreadDispatcher dispatcher) : GodotObjectBridge<Resource>(resource, dispatcher)
{
    /// <summary>Resource name.</summary>
    public string Name => Invoke(resource => resource.ResourceName);

    /// <summary>Resource path.</summary>
    public string Path => Invoke(resource => resource.ResourcePath);
}

/// <summary>Validity-checked reference to a Godot object without exposing reflection.</summary>
public sealed class GodotObjectReferenceBridge(GodotObject target, GodotMainThreadDispatcher dispatcher)
    : GodotObjectBridge<GodotObject>(target, dispatcher)
{
    /// <summary>Godot class name.</summary>
    public string ClassName => Invoke(target => target.GetClass());

    /// <summary>Stable instance identifier while the object remains alive.</summary>
    public ulong InstanceId => Invoke(target => target.GetInstanceId());
}

/// <summary>Engine services made available to Luau through explicit binding metadata.</summary>
public sealed class GodotEngineBridge(Node owner, GodotMainThreadDispatcher dispatcher, DreamContext context)
    : GodotObjectBridge<Node>(owner, dispatcher)
{
    /// <summary>Owning node.</summary>
    public GodotNodeBridge Current => Invoke(node => new GodotNodeBridge(node, Dispatcher, context));

    /// <summary>Owning scene tree.</summary>
    public GodotSceneTreeBridge Scene => Invoke(node => new GodotSceneTreeBridge(node.GetTree(), Dispatcher, context));

    /// <summary>Loads a resource by Godot resource path.</summary>
    public GodotResourceBridge LoadResource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Invoke(_ =>
        {
            var resource = ResourceLoader.Load<Resource>(path)
                ?? throw new FileNotFoundException($"Godot resource '{path}' was not found.", path);
            return new GodotResourceBridge(resource, Dispatcher);
        });
    }
}
