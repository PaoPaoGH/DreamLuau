# Getting started

## Requirements

- .NET SDK 10.0 or later.
- The DreamLuau solution and NuGet restore access.
- Godot .NET 4.7 only when using `DreamLuau.Godot`.

DreamLuau packages are not documented as published NuGet packages yet. To use the source in this repository, add project references to `DreamLuau.csproj` and `DreamLuau.NuLua.csproj` from your application.

```powershell
dotnet add path\to\YourApp.csproj reference .\src\DreamLuau\DreamLuau.csproj
dotnet add path\to\YourApp.csproj reference .\src\DreamLuau.NuLua\DreamLuau.NuLua.csproj
```

## Run your first script

```csharp
using DreamLuau;
using DreamLuau.NuLua;

await using var runtime = new DreamRuntime(new NuLuaBackend());
await using var context = runtime.CreateContext();

using var result = await context.ExecuteStringAsync("return 40 + 2", "hello.luau");
Console.WriteLine(result.Values[0].As<double>()); // 42
```

`DreamRuntime` owns configuration and tracks contexts. Each call to `CreateContext()` creates an independent VM and global environment. Dispose both runtime and contexts; disposing the runtime also disposes its contexts.

## Build and test this repository

```powershell
dotnet restore .\DreamLuau.sln
dotnet build .\DreamLuau.sln --no-restore
dotnet test .\DreamLuau.sln --no-build
```

For API examples, continue to [Bindings](bindings.md), [Execution and lifecycle](execution.md), and [Sandbox and modules](sandbox-modules.md).
