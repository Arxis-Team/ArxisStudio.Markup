using System;
using System.Buffers;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// One argument of a markup extension: a value, and the name it was given if it had one.
/// </summary>
/// <param name="Name">The argument's name, or <see langword="null"/> when it is positional.</param>
/// <param name="Value">The argument's value, which may itself be a nested extension.</param>
public sealed record XamlMarkupExtensionArgument(string? Name, XamlValue Value)
{
    /// <summary>
    /// Characters that end a bare argument or change what it means: a separator, a brace that opens or
    /// closes an extension, the equals sign that would make a positional value a name, and a quote.
    /// </summary>
    private static readonly SearchValues<char> Reserved = SearchValues.Create(",{}='\"");

    /// <summary>Gets the argument's value.</summary>
    public XamlValue Value { get; } = Value ?? throw new ArgumentNullException(nameof(Value));

    /// <summary>Gets a value indicating whether the argument was given by position rather than name.</summary>
    public bool IsPositional => Name is null;

    /// <summary>Returns the argument as it would be written.</summary>
    /// <returns>The argument text.</returns>
    /// <exception cref="ArgumentException">The value is a literal holding both kinds of quote.</exception>
    /// <remarks>
    /// A literal that a bare argument would cut short or read as something else — one holding a comma, a
    /// brace, an equals sign or a quote, one starting or ending with white space, an empty one — is written
    /// in single quotes, or in double quotes when it holds an apostrophe; the document's writer escapes a
    /// quote that would end the attribute. One starting with a brace takes the <c>{}</c> escape inside the
    /// quotes too: Avalonia reads <c>'{0:N2}'</c> as an extension. A quoted argument has no escape for a
    /// quote, so a literal holding both kinds cannot be written at all.
    /// </remarks>
    public override string ToString() => Write(Name, Value);

    /// <summary>Writes an argument: the name, if it has one, and the value as an argument has to hold it.</summary>
    /// <exception cref="ArgumentException">The value is a literal holding both kinds of quote.</exception>
    internal static string Write(string? name, XamlValue value)
    {
        string written = value is XamlLiteralValue literal ? Quoted(literal.Text) : value.ToXamlText();

        return name is null ? written : $"{name}={written}";
    }

    /// <summary>Puts a literal in quotes when a bare argument would not read it back as itself.</summary>
    private static string Quoted(string text)
    {
        bool bare = text.Length > 0
            && !char.IsWhiteSpace(text[0])
            && !char.IsWhiteSpace(text[^1])
            && text.AsSpan().IndexOfAny(Reserved) < 0;

        if (bare)
        {
            return text;
        }

        // Inside quotes a leading brace still opens an extension, so it takes the escape a whole attribute takes.
        string escaped = text.StartsWith('{') ? "{}" + text : text;

        if (!text.Contains('\'', StringComparison.Ordinal))
        {
            return "'" + escaped + "'";
        }

        if (!text.Contains('"', StringComparison.Ordinal))
        {
            return "\"" + escaped + "\"";
        }

        throw new ArgumentException(
            $"'{text}' holds both kinds of quote, and a quoted argument has no way to write one inside the other.",
            nameof(text));
    }
}
