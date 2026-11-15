# DreamLuau

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.md)

DreamLuau embeds [Luau](https://luau.org/) in .NET applications through a layered, backend-neutral API. Each `DreamContext` owns an isolated VM, and C# APIs are exposed through explicit bindings. An optional package adds Godot .NET bridges.

## ✨ Highlights

- 🧩 Backend-neutral Core with a NuLua.Luau backend.
- 🔒 Isolated VM and globals per context, with explicit C# API bindings.
- 📦 Split packages: Core, NuLua backend, and optional Godot integration.
- 🔔 Context-owned signals, scheduled work, and configurable module loading.

## 📦 Install

After the packages are published to NuGet, install the backend most applications need:

```sh
dotnet add package DreamLuau.NuLua --version 1.0.0
```

This package brings in `DreamLuau` and `NuLua.Luau`. For a custom backend, install `DreamLuau`. For Godot .NET, install `DreamLuau.Godot` in a compatible Godot project.

Packages are configured for local packing now. See the [NuGet guide](docs/nuget.md) to build and try them from the local `nupkg` feed before publication.

## 🚀 Quick start

```csharp
using DreamLuau;
using DreamLuau.NuLua;

await using var runtime = new DreamRuntime(new NuLuaBackend());
await using var context = runtime.CreateContext();

using var result = await context.ExecuteStringAsync("return 40 + 2");
Console.WriteLine(result.Values[0].As<double>()); // 42
```

## 📚 Documentation

- [Documentation home](https://paopaogh.github.io/DreamLuau/)
- [Getting started](docs/getting-started.md)
- [C# bindings](docs/bindings.md)
- [Sandbox and modules](docs/sandbox-modules.md)
- [Godot integration](docs/godot.md)
- [NuGet packaging](docs/nuget.md)

## 👤 About

DreamLuau is maintained by [PaoPaoGH](https://github.com/PaoPaoGH).

## 📄 License

MIT. See [LICENSE.md](LICENSE.md).
