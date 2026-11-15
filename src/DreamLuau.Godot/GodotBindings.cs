using DreamLuau.Binding;
using Godot;

namespace DreamLuau.Godot;

/// <summary>Registers the supported Godot bridges with a DreamLuau runtime.</summary>
public static class GodotBindings
{
    /// <summary>Adds explicit node, scene tree, resource, and engine bridge APIs.</summary>
    public static void Register(DreamBindingRegistry bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        bindings.Register<GodotNodeBridge>("Node")
            .Property(node => node.Name)
            .ReadOnlyProperty(node => node.ChildCount)
            .ReadOnlyProperty(node => node.Parent)
            .Method(node => node.GetChild(0))
            .Method(node => node.GetNode(""))
            .Method(node => node.GetSignal(""))
            .Method(node => node.AddChild(null!))
            .Method(node => node.QueueFree());

        bindings.Register<GodotSceneTreeBridge>("SceneTree")
            .ReadOnlyProperty(tree => tree.Root)
            .ReadOnlyProperty(tree => tree.CurrentScene);

        bindings.Register<GodotResourceBridge>("Resource")
            .ReadOnlyProperty(resource => resource.Name)
            .ReadOnlyProperty(resource => resource.Path);

        bindings.Register<GodotObjectReferenceBridge>("GodotObject")
            .ReadOnlyProperty(target => target.ClassName)
            .ReadOnlyProperty(target => target.InstanceId);

        bindings.Register<GodotSignalBridge>("GodotSignal")
            .Method(signal => signal.Connect(null!));

        bindings.Register<GodotSignalConnection>("GodotSignalConnection")
            .ReadOnlyProperty(connection => connection.IsConnected)
            .Method(connection => connection.Disconnect());

        bindings.Register<GodotSignalArguments>("GodotSignalArguments")
            .ReadOnlyProperty(arguments => arguments.Count)
            .Method(arguments => arguments.Get(0));

        bindings.Register<Vector2>("Vector2")
            .ReadOnlyProperty(value => value.X)
            .ReadOnlyProperty(value => value.Y);

        bindings.Register<Vector3>("Vector3")
            .ReadOnlyProperty(value => value.X)
            .ReadOnlyProperty(value => value.Y)
            .ReadOnlyProperty(value => value.Z);

        bindings.Register<Vector4>("Vector4")
            .ReadOnlyProperty(value => value.X)
            .ReadOnlyProperty(value => value.Y)
            .ReadOnlyProperty(value => value.Z)
            .ReadOnlyProperty(value => value.W);

        bindings.Register<Color>("Color")
            .ReadOnlyProperty(value => value.R)
            .ReadOnlyProperty(value => value.G)
            .ReadOnlyProperty(value => value.B)
            .ReadOnlyProperty(value => value.A);

        bindings.Register<GodotEngineBridge>("Engine")
            .ReadOnlyProperty(engine => engine.Current)
            .ReadOnlyProperty(engine => engine.Scene)
            .Method(engine => engine.LoadResource(""));
    }
}
