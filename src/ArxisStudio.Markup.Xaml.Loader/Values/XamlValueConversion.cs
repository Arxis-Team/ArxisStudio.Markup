using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Turns attribute text into the value a member holds.
/// </summary>
/// <remarks>
/// The same conversion an edit and a design-time value both need, in one place. A converter that
/// refuses the text is an ordinary user error — a diagnostic and the text unchanged — because
/// the alternative is losing what the document said over a value the author is still typing.
/// </remarks>
internal static class XamlValueConversion
{
    /// <summary>Converts attribute text to a type.</summary>
    /// <param name="targetType">The type the value has to end up as.</param>
    /// <param name="text">The text as the document wrote it.</param>
    /// <param name="diagnostics">Collects a report when the conversion is refused.</param>
    /// <returns>The converted value, or the text itself when it could not be converted.</returns>
    internal static object? Convert(Type targetType, string text, List<MarkupDiagnostic> diagnostics)
    {
        if (targetType == typeof(string) || targetType == typeof(object))
        {
            return text;
        }

        try
        {
            TypeConverter converter = TypeDescriptor.GetConverter(targetType);

            if (converter.CanConvertFrom(typeof(string)))
            {
                return converter.ConvertFromInvariantString(text);
            }

            // Not every type Avalonia writes as text declares a converter: Thickness, CornerRadius
            // and their like are parsed by the XAML compiler through a static Parse, and a document
            // updated at runtime has to reach the same value the same text reached at load.
            if (Parse(targetType, text, out object? parsed))
            {
                return parsed;
            }
        }
        catch (Exception error) when (error is NotSupportedException or FormatException or ArgumentException)
        {
            diagnostics.Add(MarkupDiagnostic.Load(
                XamlLoaderDiagnosticCodes.TypeConverterFailure,
                $"'{text}' could not be converted to {targetType.Name}: {error.Message}"));
        }

        return text;
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
