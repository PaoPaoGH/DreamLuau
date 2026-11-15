# DreamLuau architecture

DreamLuau separates VM-independent contracts from the NuLua adapter. The core project has no NuLua dependency. Each `DreamContext` owns one VM, serializes its API calls, and releases VM state and tracked resources on disposal.

## Quick start

```csharp
using DreamLuau;
using DreamLuau.NuLua;

await using var runtime = new DreamRuntime(new NuLuaBackend());
await using var context = runtime.CreateContext();

using var result = await context.ExecuteStringAsync("return 40 + 2");
var answer = result.Values[0].As<double>();
```

Bindings are explicit. Only selected public members are visible to Luau:

```csharp
runtime.Bindings.Register<Character>("Character")
    .Constructor<string>()
    .Property(character => character.Name)
    .Property(character => character.Health)
    .Method(character => character.TakeDamage(0f));

await using var context = runtime.CreateContext();
using var result = await context.ExecuteStringAsync("""
    local character = Character.new("Player")
    character.Health = 100
    character:TakeDamage(25)
    return character.Name, character.Health
    """);
```

Register bindings before creating contexts. Each new context receives a snapshot of runtime bindings.

`IDreamValue` results own backend references when they represent tables or functions. Dispose them when finished; `DreamResult` disposes its values. Primitive values need no native cleanup.

## Modules

`require` is absent by default. Enable `DreamRuntimeOptions.Modules` and provide explicit in-memory modules, a filesystem root, or both. Module names use slash-separated ASCII identifiers; filesystem paths remain under the configured root, symlinks are rejected, and every source has a size limit. Each context has an independent module cache.

## Async host methods

Registered C# methods returning `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>` can be awaited from yieldable Luau calls. An async method may accept a `CancellationToken`; DreamLuau injects the token from the active script execution. Constructors and metamethods remain synchronous. Lua functions passed as C# delegates remain synchronous.

## Concurrency and cancellation

NuLua states are not thread-safe. `DreamContext` serializes operations and the NuLua adapter also guards backend wrappers used after a context call returns.

NuLua resumes scripts synchronously between yields. Cancellation can stop waiting for a context or be observed at a backend resume boundary, but it cannot interrupt a pure Luau loop already running. Do not use a cancellation token as a time limit or instruction budget.

## Sandbox limits

The default policy opens base, coroutine, table, string, math, bit32, buffer, UTF-8, vector, and integer libraries. `os` and `debug` remain closed. The backend opens selected libraries on a private root state, then executes in a sandbox thread. This restricts library exposure. Host bindings expose only selected members, and `AllowedBindingNames` can allowlist registered APIs per runtime; it does not restrict script-created globals and is not a complete security boundary against native backend defects or unsafe host callbacks.

NuLua 0.2.0 exposes debug single-stepping, but its resume wrapper treats the resulting `LUA_BREAK` as an error and explicitly cannot finalize a coroutine paused at a debugger break. DreamLuau therefore does not turn that hook into an instruction quota: doing so would leave VM cleanup in an unsupported state. NuLua also does not expose allocator injection through its public state factory. DreamLuau does not claim memory or CPU limits. For hostile scripts, use process isolation with OS-enforced resource limits, or a backend that exposes supported quota and interruption hooks. Never pass untrusted objects with dangerous methods, unrestricted reflection, or arbitrary assembly loading into the VM.

Managed callback exceptions cannot safely cross NuLua's unmanaged callback boundary. The adapter catches them, returns `nil` to the active Luau callback, and raises the captured exception after the top-level execution or function call returns. Luau `pcall` therefore cannot catch those host callback failures.

Godot scripts can connect native signals through the explicit bridge:

```luau
local button = Engine.Current:GetNode("StartButton")
local connection = button:GetSignal("pressed"):Connect(function(args)
    print(args.Count)
end)
```

`GodotSignalArguments:Get(index)` uses a zero-based index and returns each argument as a supported CLR value or validity-checked Godot bridge. Connections are disconnected when their owning context is disposed.

## Implemented scope and open work

- Core: isolated contexts, execution and global APIs, explicit class/struct bindings, overload and optional argument resolution, nullable and enum conversion, stale userdata handles, script callbacks, context-owned signals, and cooperative scheduled tasks.
- NuLua backend: async bound host methods, restricted module resolution, and per-context module caching.
- Optional Godot extension: Node, SceneTree, Resource, Engine, and native signal bridges; weak references with validity checks for every native operation; main-thread dispatch; context-owned signal cleanup; and Node-owned runtime lifecycle. Native signal callbacks receive `GodotSignalArguments`, support up to eight parameters, and expose primitives, Godot objects, Vector2/3/4, and Color values.
- Explicit binding members control object access; `AllowedBindingNames` allowlists host API globals. The allowlist does not restrict script-created globals.

Known limits: NuLua 0.2.0 exposes no supported allocator limit or safe instruction interruption path, so DreamLuau cannot enforce memory or CPU limits. Managed callback exceptions cannot cross NuLua's synchronous native callback boundary; they are surfaced after the enclosing execution returns. Godot native signal callback failures are written to Godot's error log. Async host methods should not access the same context after awaiting; the executing script holds its VM serialization gate until the callback completes.
