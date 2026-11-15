# Godot integration

`DreamLuau.Godot` is an optional .NET 10 project. It references GodotSharp and the NuLua backend; the Core project has no Godot dependency.

## Runtime node

Add `DreamGodotRuntime` to the scene tree. Its `_Ready()` initializes a NuLua runtime and context, registers the Godot bridge types, and sets the `Engine` global. The node disposes its runtime when it exits the tree.

From a script-host node, call `ExecuteStringAsync` and handle the returned result in your application's async flow:

```csharp
using var result = await dreamRuntime.ExecuteStringAsync("return Engine.Current.Name");
var currentNodeName = result.Values[0].As<string>();
```

The method dispatches execution to the main thread when needed. Keep subsequent Godot API calls on the main thread too.

## Available Luau bridges

```luau
local root = Engine.Scene.Root
local button = Engine.Current:GetNode("StartButton")
local pressed = button:GetSignal("pressed")

local connection = pressed:Connect(function(args)
    print("pressed", args.Count)
end)

local resource = Engine:LoadResource("res://data/example.tres")
print(resource.Path)
```

Registered bridges cover `Node`, `SceneTree`, `Resource`, `GodotObject`, `Engine`, native signals, and signal arguments. Signal callbacks accept up to eight arguments. `GodotSignalArguments:Get(index)` uses a zero-based index.

Bridges hold weak references and validate Godot object liveness on every native operation. Calls are dispatched to Godot's main thread. Native signal connections are disconnected with the owning context. Callback failures are sent to Godot's error log.

The project has been build-verified against GodotSharp 4.7.2. Run the integration in the exact Godot .NET version used by your game to verify runtime behavior.
