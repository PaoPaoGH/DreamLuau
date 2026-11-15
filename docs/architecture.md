# Architecture

DreamLuau separates a VM-independent Core from backend adapters and optional engine integrations.

```text
Application / Godot game
        │
        ├── DreamLuau.Godot (optional bridges and dispatcher)
        │
        └── DreamLuau.NuLua ── NuLua.Luau
                  │
             DreamLuau Core
```

Core owns the public runtime, contexts, binding metadata, values, signals, scheduled tasks, sandbox policy, and module policy. `IDreamBackend` creates an `IDreamVm`; the NuLua adapter implements those contracts without exposing NuLua types through Core APIs.

## Ownership and isolation

Each `DreamContext` owns one VM and one global environment. The runtime tracks contexts and disposes them when it is disposed. Context operations serialize on a gate to protect the non-thread-safe VM. Values representing tables and functions own backend references and need disposal.

## Binding flow

The host registers selected CLR types on `DreamRuntime.Bindings`. A context snapshots these definitions when created, then its backend builds native userdata metatables and constructor globals. A registry maps managed objects to opaque handles; userdata stores the handle rather than an unmanaged object pointer. Collected or disposed handles are invalidated and stale access fails.

## Extension points

Implement `IDreamBackend` and `IDreamVm` to add another VM backend. Core-facing values use `IDreamValue`, `IDreamTable`, `IDreamFunction`, and `IDreamUserdata`. An engine integration can add explicit bound bridge types without adding engine dependencies to Core.

## Current limits

NuLua 0.2.0 does not expose a supported allocator limit or safe instruction interruption path. DreamLuau does not claim memory or CPU limits. See [Sandbox and modules](sandbox-modules.md) for the recommended process-isolation alternative.
