using System.Collections.Concurrent;
using System.Globalization;

namespace DreamLuau.Binding;

/// <summary>Converts primitive CLR values and registered custom pairs for bindings.</summary>
public sealed class DreamValueConverter
{
    readonly ConcurrentDictionary<(Type Source, Type Target), Func<object, object?>> custom = new();

    /// <summary>Registers an explicit conversion from one CLR type to another.</summary>
    public void Register<TSource, TDestination>(Func<TSource, TDestination> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        custom[(typeof(TSource), typeof(TDestination))] = value => converter((TSource)value);
    }

    /// <summary>Attempts a safe built-in or explicitly registered conversion.</summary>
    public bool TryConvert(object? source, Type destinationType, out object? result)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (source is Backend.IDreamValue dreamValue)
            source = dreamValue.ToObject();

        var nullableType = Nullable.GetUnderlyingType(destinationType);
        var targetType = nullableType ?? destinationType;
        if (source is null)
        {
            result = null;
            return !destinationType.IsValueType || nullableType is not null;
        }

        var sourceType = source.GetType();
        if (targetType.IsInstanceOfType(source))
        {
            result = source;
            return true;
        }

        if (custom.TryGetValue((sourceType, targetType), out var customConverter))
        {
            try
            {
                result = customConverter(source);
                return result is null ? nullableType is not null : targetType.IsInstanceOfType(result);
            }
            catch
            {
                result = null;
                return false;
            }
        }

        try
        {
            if (targetType.IsEnum)
            {
                result = source is string text
                    ? Enum.Parse(targetType, text, ignoreCase: false)
                    : Enum.ToObject(targetType, Convert.ChangeType(source, Enum.GetUnderlyingType(targetType), CultureInfo.InvariantCulture)!);
                return true;
            }

            if (source is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
            {
                result = Convert.ChangeType(source, targetType, CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            // Conversion failure is expected while trying overloaded methods.
        }

        result = null;
        return false;
    }
}
