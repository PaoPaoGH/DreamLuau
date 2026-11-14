# Project: DreamLuau

You are a senior .NET runtime engineer specializing in C#, language interoperability, scripting engines, virtual machines, and game engine architecture.

Your task is to design and implement **DreamLuau**, a production-oriented Luau scripting framework for .NET 10.0.

Do not treat this as a simple wrapper around NuLua. Build a properly layered scripting framework with a clean public API, extensible backend architecture, and robust C# ↔ Luau interoperability.

## 1. Technology Stack

- C# / .NET 10.0
- Luau scripting language
- NuLua.Luau as the initial VM backend
- Nullable Reference Types enabled
- Implicit Usings enabled
- Modern C# language features
- Godot .NET integration as an optional extension

Target Framework: `net10.0+`.

## 2. Core Architecture

DreamLuau must use an isolated VM architecture.

Each DreamContext must own an independent Luau VM and Global Environment.

Requirements:

- No accidental Global Environment sharing.
- No shared mutable VM state between contexts.
- Explicit VM ownership and disposal.
- Safe execution serialization per VM.
- Runtime lifecycle management.
- Proper exception propagation.
- Cancellation support where supported by the backend.
- Resource cleanup when a context is disposed.

The main Runtime must support creating, tracking, and disposing multiple contexts.

## 3. Project Structure

Create a multi-project solution:

DreamLuau/
├── src/
│   ├── DreamLuau/
│   │   ├── Backend/
│   │   ├── Binding/
│   │   ├── Objects/
│   │   ├── Execution/
│   │   ├── Signals/
│   │   ├── Tasks/
│   │   ├── Security/
│   │   ├── Modules/
│   │   ├── DreamRuntime.cs
│   │   ├── DreamContext.cs
│   │   ├── DreamScript.cs
│   │   ├── DreamResult.cs
│   │   └── DreamRuntimeOptions.cs
│   │
│   ├── DreamLuau.NuLua/
│   │   ├── NuLuaBackend.cs
│   │   ├── NuLuaVm.cs
│   │   ├── NuLuaBindingAdapter.cs
│   │   └── NuLuaValueConverter.cs
│   │
│   └── DreamLuau.Godot/
│       ├── DreamGodotRuntime.cs
│       ├── GodotObjectBridge.cs
│       └── GodotBindings.cs
│
└── tests/
    ├── DreamLuau.Tests/
    └── DreamLuau.NuLua.Tests/

All projects must target net10.0 unless an actual dependency constraint requires otherwise. Document any exception.

## 4. Backend Abstraction

The Core project must never directly depend on NuLua.

Create backend interfaces such as:

- IDreamBackend
- IDreamVm
- IDreamValue
- IDreamFunction
- IDreamTable
- IDreamUserdata

NuLua-specific implementations belong exclusively in DreamLuau.NuLua.

Do not leak NuLua types into public Core APIs.

## 5. C# ↔ Luau Binding System

Use Userdata-based interoperability.

Implement:

- DreamBindingRegistry
- DreamBindingBuilder
- DreamTypeBinding
- DreamMethodBinding
- DreamPropertyBinding
- DreamConstructorBinding
- DreamObjectHandle
- DreamObjectRegistry
- DreamValueConverter

Support:

- C# class registration.
- C# struct registration where supported.
- Constructors.
- Instance methods.
- Static methods.
- Read-only properties.
- Read/write properties.
- Optional parameters.
- Overloaded methods.
- Enum conversion.
- Nullable values.
- Primitive conversions.
- C# delegates and callbacks.
- Custom converters.

Example desired C# API:

    runtime.Bindings.Register<Character>("Character")
        .Constructor<string>()
        .Property(c => c.Name)
        .Property(c => c.Health)
        .Method(c => c.TakeDamage(0f));

Desired Luau API:

    local character = Character.new("Player")

    print(character.Name)

    character.Health = 100
    character:TakeDamage(25)

The API above is a design target. Implement it only through verified backend capabilities.

## 6. Userdata and Object Lifetime

NuLua's Userdata API must be investigated before implementing the binding layer.

Verify:

- Userdata creation.
- Userdata type handling.
- Metatable creation and assignment.
- __index.
- __newindex.
- __call.
- __gc.
- Native callback registration.
- Object finalization.

Do not invent API names or assume these capabilities exist.

If NuLua does not expose a required capability:

1. Inspect its actual source code.
2. Identify whether the capability can be implemented safely.
3. Create a backend-specific extension if possible.
4. Otherwise document the limitation and provide an alternative design.

Use unmanaged handles or an equivalent safe representation for managed C# objects.

Implement lifetime policies, stale-handle detection, object invalidation, and cleanup.

For Godot Objects, verify Godot instance validity before every native operation.

Never allow disposed or freed objects to be accessed through stale userdata.

## 7. Execution System

Implement:

- ExecuteAsync.
- ExecuteFileAsync.
- ExecuteStringAsync.
- CallFunction.
- Global variable access.
- Execution results.
- Runtime error reporting.
- Script lifecycle.
- Execution serialization.

Support asynchronous cancellation only if the backend can actually interrupt execution. Do not pretend that cancelling a .NET Task automatically interrupts a running Luau VM.

## 8. Event and Task Systems

Design:

- DreamSignal.
- DreamConnection.
- DreamTask.
- DreamScheduler.

Allow Luau scripts to subscribe to C# events.

Example:

    Engine.Events.PlayerJoined:Connect(function(player)
        print(player.Name)
    end)

Ensure subscriptions are automatically disposed when their owning context is disposed.

Godot operations must execute on the appropriate main thread.

## 9. Sandbox

Implement configurable sandbox policies.

Support:

- Library permissions.
- Global API permissions.
- Object access permissions.
- Memory limits where supported.
- Instruction limits where supported.
- Execution isolation.
- Safe native callback boundaries.

Never expose unrestricted .NET reflection or arbitrary assembly loading to untrusted scripts.

Clearly distinguish between actual security guarantees and best-effort restrictions.

## 10. Godot Integration

Godot must remain optional.

DreamLuau.Godot should provide:

- GodotObject binding.
- Node binding.
- SceneTree integration.
- Main-thread dispatch.
- Signal integration.
- Resource integration.
- Object validity checks.
- Lifecycle integration.

The Core project must not reference Godot assemblies.

## 11. Code Quality

Follow these requirements:

- Use sealed classes where appropriate.
- Prefer explicit ownership and disposal.
- Use IDisposable or IAsyncDisposable appropriately.
- Avoid static global mutable state.
- Avoid unnecessary reflection at runtime.
- Cache compiled binding metadata.
- Use meaningful exceptions.
- Include XML documentation for public APIs.
- Avoid placeholder implementations in completed features.
- Do not silently swallow exceptions.
- Avoid async void.
- Make thread-safety guarantees explicit.
- Keep public API independent from the underlying VM.

## 12. Testing

Create automated tests for:

1. VM isolation.
2. Independent Global Environments.
3. Primitive conversion.
4. C# object registration.
5. Userdata access.
6. Method invocation.
7. Property access.
8. Constructor invocation.
9. Overload resolution.
10. Null handling.
11. Invalid object handles.
12. Context disposal.
13. Runtime disposal.
14. Concurrent execution protection.
15. Event subscription cleanup.
16. Error propagation.
17. Sandbox restrictions.

All backend integration tests must use the actual NuLua implementation.

## 13. Development Workflow

Implement incrementally.

For each milestone:

1. Inspect the existing repository.
2. Inspect the installed NuLua package and source.
3. Verify all external APIs.
4. Design the interfaces.
5. Implement complete source files.
6. Build the solution.
7. Fix all compiler errors.
8. Run tests.
9. Document limitations.
10. Proceed to the next milestone.

Never fabricate external API methods.

Never claim code compiles unless an actual build has succeeded.

Do not replace missing functionality with fake implementations or NotImplementedException while claiming a feature is complete.

## 14. Final Goal

DreamLuau should become an extensible, engine-independent Luau framework for .NET.

It must support:

- Standalone .NET scripting.
- Game gameplay scripting.
- Godot integration.
- User-created mods.
- C# API exposure.
- Isolated script execution.
- Secure and controlled interoperability.
- Extensible backend implementations.

The public API should be simple for developers, while the internal architecture should be robust enough for a production game engine.

**Priority: Correctness, maintainability, isolation, and real NuLua compatibility over implementation speed.**