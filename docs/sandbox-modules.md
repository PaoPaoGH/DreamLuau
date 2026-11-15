# Sandbox and modules

## Library policy

The default NuLua sandbox opens base, coroutine, table, string, math, bit32, buffer, UTF-8, vector, and integer libraries. `os` and `debug` are excluded by default.

Configure libraries when creating the runtime:

```csharp
var options = new DreamRuntimeOptions
{
    Sandbox = new DreamSandboxPolicy
    {
        Libraries = DreamLibrary.Base | DreamLibrary.Table | DreamLibrary.Math
    }
};
```

Setting `Sandbox.Enabled` to `false` disables backend sandbox-thread setup. Keep it enabled for the normal restricted-library setup.

## Security boundary

Library selection and binding allowlists reduce exposed APIs. They are not a complete security boundary against native VM defects or unsafe host callbacks. Do not expose dangerous host objects, unrestricted reflection, or assembly loading to untrusted scripts.

NuLua 0.2.0 does not expose allocator injection through its public state factory. It has debug stepping, but a coroutine paused by a debugger break cannot be safely finalized through the public resume wrapper. DreamLuau therefore does not enforce memory or instruction quotas. For hostile scripts, run execution in a worker process and apply OS-level resource limits, or use a backend with supported quota and interruption hooks.

Managed callback exceptions cannot safely cross NuLua's native callback boundary. DreamLuau captures them and reports them after the top-level execution or function call returns; Luau `pcall` cannot catch those host callback failures.

## Load modules

Module loading is disabled by default, so `require` is absent. Configure an explicit memory map, filesystem root, or both:

```csharp
var options = new DreamRuntimeOptions
{
    Modules = new DreamModulePolicy
    {
        Enabled = true,
        InMemoryModules = new Dictionary<string, string>
        {
            ["math/extra"] = "return { double = function(n) return n * 2 end }"
        },
        FileSystemRoot = "Scripts/Modules",
        MaxSourceBytes = 256 * 1024
    }
};
```

Then scripts can load configured modules:

```luau
local extra = require("math/extra")
return extra.double(21)
```

Names are slash-separated ASCII identifiers. Files use the configured `.luau` extension by default, resolve beneath the configured root, and reject symlinks/reparse points. Each context has its own module cache. `MaxSourceBytes` limits each module source file.
