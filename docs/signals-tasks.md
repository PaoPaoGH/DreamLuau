# Signals and scheduled work

## Host signals

`DreamSignal<T>` is a thread-safe typed signal. Disposing a `DreamConnection` removes its listener. Pass a context to `Connect` to make the context own the subscription:

```csharp
using DreamLuau.Signals;

using var joined = new DreamSignal<string>();
using var runtime = new DreamRuntime(new NuLuaBackend());
await using var context = runtime.CreateContext();

context.Connect(joined, playerName => Console.WriteLine($"Joined: {playerName}"));
joined.Fire("Ada");
```

Listener failures are collected while the other active listeners run, then thrown as an `AggregateException`.

## Luau-connectable signals

Create a signal owned by a context, expose it through a binding or global, and let Luau subscribe:

```csharp
var signal = context.CreateSignal();
context.SetGlobal("PlayerJoined", signal);
```

```luau
local connection = PlayerJoined:Connect(function(playerName)
    print(playerName)
end)
```

`DreamScriptSignal.Publish(value)` publishes to active listeners. The context disconnects subscriptions when disposed.

## Scheduled work

`context.Scheduler.Schedule` runs asynchronous host work on the .NET thread pool and gives it a cancellation token:

```csharp
using DreamLuau.Tasks;

var task = context.Scheduler.Schedule(async cancellationToken =>
{
    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
});

await task.Completion;
```

Context disposal cancels scheduled work and awaits its completion. Scheduled tasks are host-side work; they do not interrupt an actively executing pure Luau loop.
