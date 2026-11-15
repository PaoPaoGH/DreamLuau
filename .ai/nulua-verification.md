# NuLua 0.2.0 API verification

Backend implementation checked against NuLua repository commit `5a901008493b504c7ec061ff6c835617100dcd13`, the commit pinned by NuLua.Luau 0.2.0 package metadata.

| Capability | Verified API / behavior | DreamLuau use |
| --- | --- | --- |
| Independent VM | `LuauState.Create()` allocates native state | One root per context |
| Sandbox thread | `CreateSandboxThread()` calls `luaL_sandboxthread` | Private context execution environment |
| Userdata | `CreateUserData<T>() where T : unmanaged`; `LuaUserData.Read<T>()` | Store `long` object ID in userdata memory |
| Metatables and callability | `CreateTable()`, `SetMetatable()`, `LuaTable` fields | Userdata `__index`, `__newindex`, `__gc`; constructor tables use `__call` |
| C# callbacks | `CreateFunction(LuaFunc<LuauState>)` | Constructors and bound member dispatch |
| Async callbacks | `CreateFunction(AsyncLuaFunc<LuauState>)`; `ILuaState.ResumeAsync` | Awaited `Task`/`ValueTask` bound methods; metamethods stay synchronous |
| Module loading | `LuaModuleLoader`; `LuauState.UseModuleLoader()` replaces Luau `require` | Disabled by default; backend loader restricts names, source size, and configured root |
| Cleanup | `__gc` is a Luau metamethod; `ILuaState.Dispose()` closes native VM | Invalidate object IDs on collection; clear registry on disposal |
| Async resume | `ILuaState.ResumeAsync(int, CancellationToken)` | Cancellation passed to backend resume |
| Buffers | Source provides `NewBuffer(int)`; package README calls it `CreateBuffer` | Backend uses verified source method `NewBuffer` |

The core project references none of these types. NuLua types stay in `DreamLuau.NuLua`.
