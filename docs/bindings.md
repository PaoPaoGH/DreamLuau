# C# bindings

Bindings explicitly select which C# members scripts can access. DreamLuau does not expose general CLR reflection to Luau.

## Register a type

```csharp
public sealed class Character(string name)
{
    public string Name { get; } = name;
    public float Health { get; set; } = 100;

    public void TakeDamage(float amount) => Health -= amount;
}

runtime.Bindings.Register<Character>("Character")
    .Constructor<string>()
    .Property(character => character.Name)
    .Property(character => character.Health)
    .Method(character => character.TakeDamage(0f));
```

The `0f` selects the `TakeDamage(float)` overload while building the registration expression. It does not invoke the method during registration.

Luau can then use the selected members:

```luau
local character = Character.new("Player")
character:TakeDamage(25)
return character.Name, character.Health
```

Register types before creating contexts. Each new context receives a snapshot of the runtime's bindings.

## Supported selections

- Public constructors, including parameterless and value-type default constructors.
- Instance and static methods selected with expression lambdas.
- Properties with public getters; writable properties are settable unless registered with `ReadOnlyProperty`.
- Overloads, optional parameters, nullable values, enums, and primitive conversions.
- C# delegates for synchronous Luau callbacks.
- Custom conversion rules through `runtime.Bindings.Converter`.

Async bound methods returning `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>` are awaited by yieldable Luau calls. Async methods may accept a `CancellationToken`, which DreamLuau injects from the active execution. Constructor and metamethod callbacks stay synchronous.

Structs are supported through boxed userdata. Luau property writes mutate the boxed value held by that userdata; they do not mutate the caller's original struct value.

## Restrict registered APIs

Use `DreamSandboxPolicy.AllowedBindingNames` to expose only selected registered globals:

```csharp
var options = new DreamRuntimeOptions
{
    Sandbox = new DreamSandboxPolicy
    {
        AllowedBindingNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Character"
        }
    }
};

await using var runtime = new DreamRuntime(new NuLuaBackend(), options);
```

The allowlist controls host binding globals. It does not prevent scripts from creating their own globals.
