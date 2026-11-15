using System.Numerics;
using DreamLuau.Backend;
using NuLua;
using NuLua.Luau;

namespace DreamLuau.NuLua;

sealed class NuLuaDreamValue(NuLuaVm owner, LuaValue value) : IDreamValue
{
    internal NuLuaVm Owner => owner;
    int disposed;
    internal bool IsDisposed => Volatile.Read(ref disposed) != 0;
    public DreamValueKind Kind => value.Type switch
    {
        LuaValueType.Nil => DreamValueKind.Nil,
        LuaValueType.Boolean => DreamValueKind.Boolean,
        LuaValueType.Number => DreamValueKind.Number,
        LuaValueType.String => DreamValueKind.String,
        LuaValueType.Table => DreamValueKind.Table,
        LuaValueType.Function => DreamValueKind.Function,
        LuaValueType.UserData => DreamValueKind.Userdata,
        _ => DreamValueKind.Other,
    };

    public object? ToObject() => value.Type switch
    {
        LuaValueType.Nil => null,
        LuaValueType.Boolean => value.Read<bool>(),
        LuaValueType.Number => value.Read<double>(),
        LuaValueType.String => value.Read<string>(),
        LuaValueType.Vector => value.Read<Vector3>(),
        LuaValueType.Buffer => value.Read<LuauBuffer>().ToArray(),
        _ => throw new InvalidOperationException("Composite NuLua values require their backend wrapper."),
    };

    public T? As<T>()
    {
        if (owner.Converter.TryConvert(ToObject(), typeof(T), out var converted))
            return (T?)converted;
        throw new InvalidCastException($"Cannot convert Luau {Kind} to {typeof(T)}.");
    }

    internal LuaValue RawValue => value;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0 && value.Type == LuaValueType.Buffer)
            value.Read<LuauBuffer>().Dispose();
    }
}

sealed class NuLuaDreamFunction(NuLuaVm owner, LuaFunction function) : IDreamFunction
{
    internal NuLuaVm Owner => owner;
    int disposed;
    internal bool IsDisposed => Volatile.Read(ref disposed) != 0;
    public DreamValueKind Kind => DreamValueKind.Function;
    public object ToObject() => this;

    public T? As<T>()
    {
        if (typeof(T).IsInstanceOfType(this)) return (T)(object)this;
        throw new InvalidCastException($"Cannot convert Luau function to {typeof(T)}.");
    }

    public ValueTask<IReadOnlyList<IDreamValue>> InvokeAsync(
        IReadOnlyList<object?> arguments,
        CancellationToken cancellationToken = default) => owner.InvokeAsync(function, arguments, cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
            function.Dispose();
    }
    internal LuaFunction RawFunction => function;
}

sealed class NuLuaDreamTable(NuLuaVm owner, LuaTable table) : IDreamTable
{
    internal NuLuaVm Owner => owner;
    int disposed;
    internal bool IsDisposed => Volatile.Read(ref disposed) != 0;
    public DreamValueKind Kind => DreamValueKind.Table;
    public object ToObject() => this;

    public IDreamValue Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return owner.ReadTableField(table, key);
    }

    public void Set(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        owner.WriteTableField(table, key, value);
    }

    public T? As<T>()
    {
        if (typeof(T).IsInstanceOfType(this)) return (T)(object)this;
        throw new InvalidCastException($"Cannot convert Luau table to {typeof(T)}.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
            table.Dispose();
    }
    internal LuaTable RawTable => table;
}

sealed class NuLuaDreamUserdata(NuLuaVm owner, long handle) : IDreamUserdata
{
    internal NuLuaVm Owner => owner;
    public DreamValueKind Kind => DreamValueKind.Userdata;
    public object GetTarget() => owner.ResolveUserdata(handle);
    public object ToObject() => GetTarget();

    public T? As<T>()
    {
        if (this is T wrapper) return wrapper;
        var target = GetTarget();
        if (target is T result) return result;
        throw new InvalidCastException($"Cannot convert userdata to {typeof(T)}.");
    }

    public void Dispose() { }
}
