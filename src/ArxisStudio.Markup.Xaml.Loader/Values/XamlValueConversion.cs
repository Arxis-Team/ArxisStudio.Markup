using System;
using System.Collections.Generic;
using System.ComponentModel;

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
        }
        catch (Exception error) when (error is NotSupportedException or FormatException or ArgumentException)
        {
            diagnostics.Add(MarkupDiagnostic.Load(
                XamlLoaderDiagnosticCodes.TypeConverterFailure,
                $"'{text}' could not be converted to {targetType.Name}: {error.Message}"));
        }

        return text;
    }
}
