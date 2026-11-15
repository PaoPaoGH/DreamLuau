using DreamLuau.Binding;

namespace DreamLuau.Tests;

public sealed class BindingTests
{
    [Fact]
    public void Converter_handles_nullable_primitives_enums_and_custom_pairs()
    {
        var converter = new DreamValueConverter();
        converter.Register<string, Guid>(Guid.Parse);

        Assert.True(converter.TryConvert(12.0, typeof(int?), out var nullableNumber));
        Assert.Equal(12, nullableNumber);
        Assert.True(converter.TryConvert("Ready", typeof(Mode), out var mode));
        Assert.Equal(Mode.Ready, mode);
        Assert.True(converter.TryConvert(null, typeof(string), out var nullString));
        Assert.Null(nullString);
        Assert.True(converter.TryConvert("bfbf1e89-55fc-44c0-8358-b4f0f8c226bf", typeof(Guid), out var id));
        Assert.Equal(Guid.Parse("bfbf1e89-55fc-44c0-8358-b4f0f8c226bf"), id);
    }

    [Fact]
    public void Object_registry_rejects_invalidated_and_disposed_handles()
    {
        using var registry = new DreamObjectRegistry();
        var target = new object();
        var handle = registry.Register(target);

        Assert.Same(target, registry.GetTarget(handle));
        Assert.True(registry.Invalidate(handle));
        Assert.False(registry.IsValid(handle));
        Assert.Throws<InvalidOperationException>(() => registry.GetTarget(handle));
    }

    enum Mode { Ready }
}
