# NuLua compatibility notes

The NuLua adapter was checked against NuLua.Luau 0.2.0 source commit `5a901008493b504c7ec061ff6c835617100dcd13`.

| Capability | Verified NuLua surface | DreamLuau use |
| --- | --- | --- |
| Independent VM | `LuauState.Create()` | One root state per context |
| Sandbox thread | `CreateSandboxThread()` | Per-context execution environment |
| Userdata | `CreateUserData<T>() where T : unmanaged`, `LuaUserData.Read<T>()` | Store opaque managed object IDs |
| Metatables | `CreateTable()`, `SetMetatable()` | Userdata indexing, assignment, collection and callable constructor tables |
| Synchronous callbacks | `CreateFunction(LuaFunc<LuauState>)` | Constructors and bound member dispatch |
| Asynchronous callbacks | `CreateFunction(AsyncLuaFunc<LuauState>)`, `ILuaState.ResumeAsync` | Awaited bound `Task` and `ValueTask` methods |
| Modules | `LuaModuleLoader`, `LuauState.UseModuleLoader()` | Restricted loader; replaces Luau `require` |
| Cleanup | `__gc`, `ILuaState.Dispose()` | Invalidate collected handles and close native VM |
| Buffers | `NewBuffer(int)` | Backend buffer creation |

This compatibility check prevents the backend adapter from relying on guessed NuLua APIs. Recheck the table when upgrading NuLua.
