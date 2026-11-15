# NuGet packages

DreamLuau produces three packages, all targeting `net10.0`:

| Package | Use | Dependencies |
| --- | --- | --- |
| `DreamLuau` | Backend-neutral runtime and public contracts | None |
| `DreamLuau.NuLua` | NuLua.Luau VM backend | `DreamLuau`, `NuLua.Luau` |
| `DreamLuau.Godot` | Godot .NET runtime and object bridges | `DreamLuau`, `DreamLuau.NuLua`, `GodotSharp` |

The package IDs and version `1.0.0` are declared by the projects and `Directory.Build.props`. Project references become package dependencies, so package consumers receive the matching Core/backend packages transitively.

## Pack locally

From the repository root, pack all packable projects:

```powershell
dotnet pack .\DreamLuau.sln --configuration Release --output .\nupkg
```

The output contains `.nupkg` files and `.snupkg` symbol packages. Unit test projects are not packable. The `nupkg/` directory is ignored by Git.

## Try a local package

In a separate .NET 10 application, set `$repo` to the DreamLuau checkout path, then install the backend package from that local feed:

```powershell
$repo = "F:\path\to\DreamLuau"
dotnet add package DreamLuau.NuLua --version 1.0.0 --source "$repo\nupkg"
dotnet restore --source "$repo\nupkg" --source https://api.nuget.org/v3/index.json
```

NuGet.org remains available for transitive dependencies such as `NuLua.Luau`. The local package feed is not written into repository-wide NuGet configuration.

For Core-only backend development, install `DreamLuau`. For a Godot .NET application, install `DreamLuau.Godot`; its package dependency graph includes the runtime and NuLua backend. Match the `GodotSharp` dependency with the Godot .NET version used by the application.

## Publish

Packages are prepared for packing but this repository does not publish them automatically. Review package metadata and test the local packages before publishing. To publish a package manually, use a NuGet API key through a secret store or environment variable:

```powershell
dotnet nuget push .\nupkg\DreamLuau.1.0.0.nupkg --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json
```

Repeat for the backend packages after their dependencies are available on the target feed. Do not commit API keys or credentials.
