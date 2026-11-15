# DreamLuau

DreamLuau is an extensible Luau scripting framework for .NET 10 and Godot. It keeps the scripting API independent from the VM backend and gives each script context its own VM and global environment.

The current backend uses [NuLua.Luau](https://github.com/nuskey8/NuLua), while the optional Godot extension adds validity-checked bridges for nodes, resources, scene trees, engine services, and native signals.

## What you can do

- Run independent Luau contexts in a .NET application.
- Expose selected C# constructors, properties, and methods to scripts.
- Configure standard libraries and host API bindings per runtime.
- Load explicitly configured Luau modules from memory or a confined directory.
- Connect host events and schedule context-owned asynchronous work.
- Use the optional Godot integration to access engine objects on the main thread.

## Quick example

```csharp
using DreamLuau;
using DreamLuau.NuLua;

await using var runtime = new DreamRuntime(new NuLuaBackend());
await using var context = runtime.CreateContext();

using var result = await context.ExecuteStringAsync("return 40 + 2");
Console.WriteLine(result.Values[0].As<double>());
```

Start with [Getting started](getting-started.md), then see [Bindings](bindings.md) to expose a controlled C# API.

## Project status

DreamLuau is under active development. Its NuLua integration and core API are covered by automated tests. The Godot extension builds against GodotSharp; runtime behavior should also be checked in the target Godot version.

Resource and instruction quotas are not currently enforced by NuLua. Read [Sandbox and modules](sandbox-modules.md) before running untrusted scripts.
