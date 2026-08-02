using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Turns attribute text into the value a member holds.
/// </summary>
/// <remarks>
/// <para>
/// The same conversion an edit, an update and a design-time value all need, in one place — and
/// the same one a tool asks for through <see cref="XamlMemberDescriptor.ConvertFromText"/> before
/// it writes anything, so that validating a field and writing it cannot disagree.
/// </para>
/// <para>
/// Text the member cannot hold is an ordinary user error, so it comes back as a result. What each
/// caller does with the refusal — which diagnostic code, which span — is the caller's, because
/// only it knows where the text was written.
/// </para>
/// </remarks>
internal static class XamlValueConversion
{
    /// <summary>Reads attribute text as a value of a type.</summary>
    /// <param name="targetType">The type the value has to end up as.</param>
    /// <param name="text">The text as the document wrote it.</param>
    /// <returns>The value, or what was wrong with the text.</returns>
    internal static XamlValueConversionResult Convert(Type targetType, string text)
    {
        if (targetType == typeof(string) || targetType == typeof(object))
        {
            return XamlValueConversionResult.FromValue(text);
        }

        try
        {
            TypeConverter converter = TypeDescriptor.GetConverter(targetType);

            if (converter.CanConvertFrom(typeof(string)))
            {
                return Held(targetType, converter.ConvertFromInvariantString(text), text);
            }

            // Not every type Avalonia writes as text declares a converter: Thickness, CornerRadius
            // and their like are parsed by the XAML compiler through a static Parse, and a document
            // updated at runtime has to reach the same value the same text reached at load.
            if (Parse(targetType, text, out object? parsed))
            {
                return Held(targetType, parsed, text);
            }
        }
        catch (Exception error) when (error is NotSupportedException or FormatException or ArgumentException)
        {
            return XamlValueConversionResult.FromError(
                $"'{text}' could not be read as {targetType.Name}: {error.Message}");
        }

        return XamlValueConversionResult.FromError(
            $"{targetType.Name} has no way of being read from text, so '{text}' cannot be one.");
    }

    /// <summary>Reports a converted value, unless the member could not hold it after all.</summary>
    /// <remarks>
    /// A converter is free to answer with something else entirely, and a value of the wrong type
    /// reaches an Avalonia setter as an exception. Checking here is what keeps that an ordinary
    /// refusal rather than a throw out of an update.
    /// </remarks>
    private static XamlValueConversionResult Held(Type targetType, object? value, string text)
    {
        if (value is null)
        {
            return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
                ? XamlValueConversionResult.FromError($"{targetType.Name} cannot be nothing.")
                : XamlValueConversionResult.FromValue(null);
        }

        return targetType.IsInstanceOfType(value)
            ? XamlValueConversionResult.FromValue(value)
            : XamlValueConversionResult.FromError(
                $"'{text}' was read as {value.GetType().Name}, which is not a {targetType.Name}.");
    }

    /// <summary>Converts through a static <c>Parse</c> the type declares, if it declares one.</summary>
    /// <remarks>
    /// The culture-aware overload first and the plain one after it, both read with the invariant
    /// culture — a document says <c>8,4</c> in every locale, and reading it under a locale where
    /// the comma is a decimal separator would turn one thickness into another.
    /// </remarks>
    private static bool Parse(Type targetType, string text, out object? value)
    {
        MethodInfo? method =
            targetType.GetMethod(
                "Parse",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                [typeof(string), typeof(IFormatProvider)],
                modifiers: null)
            ?? targetType.GetMethod(
                "Parse",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                [typeof(string)],
                modifiers: null);

        if (method is null || !targetType.IsAssignableFrom(method.ReturnType))
        {
            value = null;

            return false;
        }

        object?[] arguments = method.GetParameters().Length == 2
            ? [text, CultureInfo.InvariantCulture]
            : [text];

        try
        {
            value = method.Invoke(null, arguments);

            return true;
        }
        catch (TargetInvocationException error)
        {
            // The type knows how to read its own text and says this is not it. Whatever it threw
            // is a refused conversion — PathFigures.Parse raises InvalidDataException, others
            // raise FormatException — and listing the ones seen so far would let the next type
            // throw its way out of an update.
            throw new FormatException(
                error.InnerException?.Message ?? error.Message, error.InnerException ?? error);
        }
    }
}
