# Execution and lifecycle

## Context isolation

Create one `DreamContext` per independent script environment. Contexts own separate VMs and global environments; operations within one context are serialized because the VM is not thread-safe.

```csharp
await using var runtime = new DreamRuntime(new NuLuaBackend());
await using var first = runtime.CreateContext();
await using var second = runtime.CreateContext();

first.SetGlobal("score", 10);
using var value = second.GetGlobal("score");
Console.WriteLine(value.Kind); // Nil
```

## Execute source and files

```csharp
using var result = await context.ExecuteStringAsync(source, "player-script.luau", cancellationToken);

var values = await context.CallFunctionAsync("onStart", ["level-1"], cancellationToken);
foreach (var value in values)
    value.Dispose();
```

`ExecuteFileAsync` reads a UTF-8 script file when `AllowFileExecution` is enabled. File execution is enabled by default. Returned `DreamResult` and backend composite values should be disposed. `DreamResult.Dispose()` disposes all values it contains.

`GetGlobal` returns an `IDreamValue`; dispose returned tables and functions when finished. Primitive wrappers also implement `IDisposable` and can be disposed safely.

## Errors and cancellation

Script failures are wrapped in `DreamRuntimeException`, which includes the script name and inner exception. `OperationCanceledException` is passed through.

Cancellation is cooperative around async resume points. It can stop waiting for a context or be observed at a backend resume boundary. It cannot interrupt a pure Luau infinite loop already running inside NuLua. Do not treat cancellation as an execution timeout.

## Cleanup

Contexts dispose their VM, scheduler work, and tracked resources. Context-owned signal subscriptions are removed automatically. Disposing a runtime disposes every context it created.

Prefer `await using` for runtime and contexts. Explicit resource ownership is also available through `context.OwnResource(resource)`.
