using DreamLuau.Binding;
using DreamLuau.Backend;
using DreamLuau.NuLua;
using DreamLuau.Signals;

namespace DreamLuau.NuLua.Tests;

public sealed class NuLuaRuntimeTests
{
    static DreamRuntime CreateRuntime(Action<DreamBindingRegistry>? configure = null)
    {
        var runtime = new DreamRuntime(new NuLuaBackend());
        configure?.Invoke(runtime.Bindings);
        return runtime;
    }

    [Fact]
    public async Task Contexts_have_independent_vms_and_globals()
    {
        await using var runtime = CreateRuntime();
        await using var first = runtime.CreateContext();
        await using var second = runtime.CreateContext();

        first.SetGlobal("score", 41);
        Assert.Null(second.GetGlobal("score").ToObject());

        using var result = await first.ExecuteStringAsync("return score + 1");
        Assert.Equal(42d, result.Values[0].As<double>());
    }

    [Fact]
    public async Task Function_calls_and_nil_values_round_trip()
    {
        await using var runtime = CreateRuntime();
        await using var context = runtime.CreateContext();
        context.SetGlobal("missing", null);
        using var declaration = await context.ExecuteStringAsync("function add(a, b) return a + b end");

        var values = await context.CallFunctionAsync("add", [20, 22]);
        using var result = values[0];
        Assert.Equal(42d, result.As<double>());
        Assert.Null(context.GetGlobal("missing").As<string>());
    }

    [Fact]
    public async Task Execute_file_reads_source_and_preserves_path_in_errors()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dreamluau-{Guid.NewGuid():N}.luau");
        await File.WriteAllTextAsync(path, "return 9");
        try
        {
            await using var runtime = CreateRuntime();
            await using var context = runtime.CreateContext();
            using var result = await context.ExecuteFileAsync(path);
            Assert.Equal(9d, result.Values[0].As<double>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Registered_userdata_supports_construction_properties_methods_and_overloads()
    {
        await using var runtime = CreateRuntime(bindings =>
        {
            bindings.Register<Character>("Character")
                .Constructor<string>()
                .Property(character => character.Name)
                .Property(character => character.Health)
                .Method(character => character.TakeDamage(0f))
                .Method(character => character.Describe(0))
                .Method(character => character.Describe(""));
        });
        await using var context = runtime.CreateContext();

        using var result = await context.ExecuteStringAsync("""
            local character = Character("Player")
            character.Health = 100
            character:TakeDamage(25)
            local callableCharacter = Character("Direct")
            return character.Name, character.Health, character:Describe(7), character:Describe("x"), callableCharacter.Name
            """);

        Assert.Equal("Player", result.Values[0].As<string>());
        Assert.Equal(75d, result.Values[1].As<double>());
        Assert.Equal("int:7", result.Values[2].As<string>());
        Assert.Equal("string:x", result.Values[3].As<string>());
        Assert.Equal("Direct", result.Values[4].As<string>());
    }

    [Fact]
    public async Task Value_types_can_use_boxed_default_userdata()
    {
        await using var runtime = CreateRuntime(bindings => bindings.Register<Point>("Point")
            .Constructor()
            .Property(point => point.X)
            .Property(point => point.Y)
            .Method(point => point.Sum()));
        await using var context = runtime.CreateContext();

        using var result = await context.ExecuteStringAsync("""
            local point = Point.new()
            point.X = 4
            point.Y = 5
            return point:Sum()
            """);

        Assert.Equal(9d, result.Values[0].As<double>());
    }

    [Fact]
    public async Task Nullable_bound_properties_accept_luau_nil()
    {
        await using var runtime = CreateRuntime(bindings => bindings.Register<NullableHost>("NullableHost")
            .Constructor()
            .Property(host => host.Value));
        await using var context = runtime.CreateContext();

        using var result = await context.ExecuteStringAsync("""
            local host = NullableHost.new()
            host.Value = "set"
            host.Value = nil
            return host.Value == nil
            """);

        Assert.True(result.Values[0].As<bool>());
    }

    [Fact]
    public async Task Optional_parameters_and_lua_callbacks_work()
    {
        await using var runtime = CreateRuntime(bindings =>
        {
            bindings.Register<CallbackHost>("CallbackHost")
                .Constructor()
                .ReadOnlyProperty(host => host.Label)
                .Method(host => host.Run(null!))
                .Method(host => host.SetLabel(""));
        });
        await using var context = runtime.CreateContext();

        using var result = await context.ExecuteStringAsync("""
            local host = CallbackHost.new()
            host:SetLabel("ready")
            local callbackResult = host:Run(function(value) return value + 1 end)
            return callbackResult, host.Label
            """);

        Assert.Equal(5d, result.Values[0].As<double>());
        Assert.Equal("ready", result.Values[1].As<string>());
    }

    [Fact]
    public async Task Async_host_methods_are_awaited_and_propagate_failures()
    {
        await using var runtime = CreateRuntime(bindings => bindings.Register<AsyncHostApi>("AsyncHost")
            .Constructor()
            .Method(host => host.AddAsync(0))
            .Method(host => host.GetTextAsync())
            .Method(host => host.WaitAsync(default))
            .Method(host => host.FailAsync()));
        await using var context = runtime.CreateContext();

        using var result = await context.ExecuteStringAsync("""
            local host = AsyncHost.new()
            local total = host:AddAsync(40)
            local text = host:GetTextAsync()
            return total, text
            """);
        Assert.Equal(42d, result.Values[0].As<double>());
        Assert.Equal("ready", result.Values[1].As<string>());

        var exception = await Assert.ThrowsAsync<DreamRuntimeException>(async () =>
            await context.ExecuteStringAsync("local host = AsyncHost.new(); host:FailAsync()", "async-failure").AsTask());
        Assert.Contains("async host failure", exception.ToString(), StringComparison.OrdinalIgnoreCase);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await context.ExecuteStringAsync("local host = AsyncHost.new(); host:WaitAsync()", cancellationToken: cancellation.Token).AsTask());
        using var recovered = await context.ExecuteStringAsync("return 42");
        Assert.Equal(42d, recovered.Values[0].As<double>());
    }

    [Fact]
    public async Task Concurrent_scripts_are_serialized_per_vm()
    {
        await using var runtime = CreateRuntime();
        await using var context = runtime.CreateContext();
        context.SetGlobal("counter", 0);

        var executions = Enumerable.Range(0, 40)
            .Select(_ => context.ExecuteStringAsync("counter = counter + 1").AsTask());
        await Task.WhenAll(executions);

        Assert.Equal(40d, context.GetGlobal("counter").As<double>());
    }

    [Fact]
    public async Task Runtime_errors_propagate_with_script_name()
    {
        await using var runtime = CreateRuntime();
        await using var context = runtime.CreateContext();

        var exception = await Assert.ThrowsAsync<DreamRuntimeException>(async () =>
            await context.ExecuteStringAsync("error('failure')", "broken-script").AsTask());

        Assert.Equal("broken-script", exception.ScriptName);
        Assert.Contains("failure", exception.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disposed_context_invalidates_userdata_and_rejects_execution()
    {
        await using var runtime = CreateRuntime(bindings => bindings.Register<Character>("Character")
            .Constructor<string>()
            .Property(character => character.Name));
        var context = runtime.CreateContext();
        context.SetGlobal("character", new Character("Player"));
        var userdata = context.GetGlobal("character").As<IDreamUserdata>()!;
        Assert.Equal("Player", ((Character)userdata.GetTarget()).Name);

        await context.DisposeAsync();

        var staleHandleException = Record.Exception(userdata.GetTarget);
        Assert.IsType<ObjectDisposedException>(staleHandleException);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await context.ExecuteStringAsync("return 1").AsTask());
    }

    [Fact]
    public async Task Sandbox_omits_os_and_debug_libraries_by_default()
    {
        await using var runtime = CreateRuntime();
        await using var context = runtime.CreateContext();

        Assert.Null(context.GetGlobal("os").ToObject());
        Assert.Null(context.GetGlobal("debug").ToObject());
        Assert.Null(context.GetGlobal("require").ToObject());
    }

    [Fact]
    public async Task Sandbox_binding_allowlist_exposes_only_named_host_apis()
    {
        await using var runtime = new DreamRuntime(new NuLuaBackend(), new DreamRuntimeOptions
        {
            Sandbox = new DreamSandboxPolicy
            {
                AllowedBindingNames = new HashSet<string>(["Allowed"], StringComparer.Ordinal),
            },
        });
        runtime.Bindings.Register<AllowedHostApi>("Allowed").Constructor();
        runtime.Bindings.Register<BlockedHostApi>("Blocked").Constructor();
        await using var context = runtime.CreateContext();

        using var result = await context.ExecuteStringAsync("return Allowed ~= nil, Blocked == nil");

        Assert.True(result.Values[0].As<bool>());
        Assert.True(result.Values[1].As<bool>());
    }

    [Fact]
    public async Task Module_policy_loads_and_caches_allowlisted_memory_modules()
    {
        await using var runtime = new DreamRuntime(new NuLuaBackend(), new DreamRuntimeOptions
        {
            Modules = new DreamModulePolicy
            {
                Enabled = true,
                InMemoryModules = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["answer"] = "return { Value = 42 }",
                },
            },
        });
        await using var context = runtime.CreateContext();

        using var result = await context.ExecuteStringAsync("""
            local first = require("answer")
            local second = require("answer")
            return first.Value, first == second
            """);

        Assert.Equal(42d, result.Values[0].As<double>());
        Assert.True(result.Values[1].As<bool>());
    }

    [Fact]
    public async Task Module_policy_confines_files_to_configured_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dreamluau-modules-{Guid.NewGuid():N}");
        var outsidePath = $"{root}-outside.luau";
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "greeting.luau"), "return 'hello'");
        await File.WriteAllTextAsync(outsidePath, "return 'outside'");
        try
        {
            await using var runtime = new DreamRuntime(new NuLuaBackend(), new DreamRuntimeOptions
            {
                Modules = new DreamModulePolicy { Enabled = true, FileSystemRoot = root },
            });
            await using var context = runtime.CreateContext();
            using var loaded = await context.ExecuteStringAsync("return require('greeting')");
            Assert.Equal("hello", loaded.Values[0].As<string>());

            var outsideName = Path.GetFileNameWithoutExtension(outsidePath);
            var escape = await Assert.ThrowsAsync<DreamRuntimeException>(async () =>
                await context.ExecuteStringAsync($"return require('../{outsideName}')").AsTask());
            Assert.DoesNotContain(outsidePath, escape.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            File.Delete(outsidePath);
        }
    }

    [Fact]
    public async Task Module_policy_rejects_sources_over_configured_size()
    {
        await using var runtime = new DreamRuntime(new NuLuaBackend(), new DreamRuntimeOptions
        {
            Modules = new DreamModulePolicy
            {
                Enabled = true,
                MaxSourceBytes = 12,
                InMemoryModules = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["large"] = "return 'too large'",
                },
            },
        });

        Assert.Throws<ArgumentException>(() => runtime.CreateContext());
    }

    [Fact]
    public async Task Context_disposal_removes_signal_subscriptions_and_cancels_scheduled_work()
    {
        var signal = new DreamSignal<int>();
        await using var runtime = CreateRuntime();
        var context = runtime.CreateContext();
        var total = 0;
        context.Connect(signal, value => total += value);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduled = context.Scheduler.Schedule(async cancellationToken =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        await started.Task;

        signal.Fire(3);
        await context.DisposeAsync();
        signal.Fire(5);

        Assert.Equal(3, total);
        Assert.Equal(0, signal.ConnectionCount);
        Assert.True(scheduled.IsCompleted);
    }

    [Fact]
    public async Task Luau_connects_to_context_owned_host_signals()
    {
        await using var runtime = CreateRuntime();
        var context = runtime.CreateContext();
        var signal = context.CreateSignal();
        context.SetGlobal("PlayerJoined", signal);
        using var script = await context.ExecuteStringAsync("""
            joinedPlayer = nil
            local connection = PlayerJoined:Connect(function(player)
                joinedPlayer = player
            end)
            return connection.IsConnected
            """);

        Assert.True(script.Values[0].As<bool>());
        signal.Publish("PlayerOne");
        Assert.Equal("PlayerOne", context.GetGlobal("joinedPlayer").As<string>());

        await context.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => signal.Publish("PlayerTwo"));
    }

    [Fact]
    public async Task Runtime_disposal_disposes_all_owned_contexts()
    {
        var runtime = CreateRuntime();
        var first = runtime.CreateContext();
        var second = runtime.CreateContext();

        await runtime.DisposeAsync();

        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await first.ExecuteStringAsync("return 1").AsTask());
    }

    sealed class Character(string name)
    {
        public string Name { get; set; } = name;
        public float Health { get; set; } = 100;
        public void TakeDamage(float amount) => Health -= amount;
        public string Describe(int value) => $"int:{value}";
        public string Describe(string value) => $"string:{value}";
    }

    sealed class CallbackHost
    {
        public string Label { get; private set; } = "";
        public int Run(Func<int, int> callback) => callback(4);
        public void SetLabel(string label, bool enabled = true)
        {
            if (enabled) Label = label;
        }
    }

    sealed class AsyncHostApi
    {
        public async Task<int> AddAsync(int value)
        {
            await Task.Delay(1).ConfigureAwait(false);
            return value + 2;
        }

        public async ValueTask<string> GetTextAsync()
        {
            await Task.Delay(1).ConfigureAwait(false);
            return "ready";
        }

        public Task WaitAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public async Task FailAsync()
        {
            await Task.Delay(1).ConfigureAwait(false);
            throw new InvalidOperationException("async host failure");
        }
    }

    sealed class AllowedHostApi { }
    sealed class BlockedHostApi { }

    sealed class NullableHost
    {
        public string? Value { get; set; }
    }

    struct Point
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Sum() => X + Y;
    }
}
