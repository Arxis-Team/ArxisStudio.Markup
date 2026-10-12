using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Reads <c>{Extension arguments}</c> expressions out of attribute text.
/// </summary>
/// <remarks>
/// <para>
/// Parsing only. Nothing here resolves an extension's type, converts an argument or runs
/// anything — this package has no CLR metadata and creates no objects.
/// </para>
/// <para>
/// Every expression keeps the text it was parsed from, so rendering it back returns exactly
/// what the document said, spacing and argument order included.
/// </para>
/// </remarks>
internal static class XamlMarkupExtensionParser
{
    /// <summary>Reads attribute text into the value form it expresses.</summary>
    public static XamlValue ParseValue(string text, out ImmutableArray<MarkupDiagnostic> diagnostics)
    {
        var reported = new List<MarkupDiagnostic>();
        XamlValue value = Read(text, reported);

        diagnostics = [.. reported];

        return value;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "False positive. This method returns XamlLiteralValue on two paths and " +
            "XamlMarkupExtensionValue on two others; the analyzer appears not to see through the " +
            "'with' expressions. XamlValue is the only type that covers all four.")]
    private static XamlValue Read(string text, List<MarkupDiagnostic> diagnostics)
    {
        // "{}" is XAML's escape, and exists precisely so a literal can start with a brace.
        if (text.StartsWith("{}", StringComparison.Ordinal))
        {
            return new XamlLiteralValue(text[2..]);
        }

        if (!text.StartsWith('{'))
        {
            return new XamlLiteralValue(text);
        }

        var position = 0;
        XamlMarkupExtensionValue extension = ParseExtension(text, ref position, diagnostics);

        if (position < text.Length && !IsAllWhitespace(text, position))
        {
            // Something follows the closing brace. The text is kept whole so it still writes
            // back unchanged, but the caller is told it is not a clean expression.
            diagnostics.Add(Diagnostic(
                XamlDiagnosticCodes.MalformedMarkupExtension,
                $"Text follows the markup extension: '{text[position..]}'.",
                new TextSpan(position, text.Length - position)));

            return extension with { RawText = text };
        }

        return extension with { RawText = text[..Math.Min(position, text.Length)] };
    }

    /// <summary>Parses one <c>{...}</c> expression starting at the opening brace.</summary>
    private static XamlMarkupExtensionValue ParseExtension(
        string text,
        ref int position,
        List<MarkupDiagnostic> diagnostics)
    {
        int start = position;

        position++; // '{'
        SkipWhitespace(text, ref position);

        int nameStart = position;

        while (position < text.Length && !IsNameStop(text[position]))
        {
            position++;
        }

        string name = text[nameStart..position];

        if (name.Length == 0)
        {
            diagnostics.Add(Diagnostic(
                XamlDiagnosticCodes.ExpectedMarkupExtensionName,
                "The markup extension has no type name.",
                new TextSpan(nameStart, 0)));
        }

        ImmutableArray<XamlMarkupExtensionArgument> arguments =
            ParseArguments(text, ref position, diagnostics, start, out ImmutableArray<TextSpan> spans);

        if (position < text.Length && text[position] == '}')
        {
            position++;
        }
        else
        {
            diagnostics.Add(Diagnostic(
                XamlDiagnosticCodes.UnterminatedMarkupExtension,
                "The markup extension is missing its closing '}'.",
                TextSpan.FromBounds(start, Math.Min(position, text.Length))));
        }

        return new XamlMarkupExtensionValue(XamlQualifiedName.Parse(name), arguments)
        {
            RawText = text[start..Math.Min(position, text.Length)],
            ArgumentSpans = spans,
        };
    }

    /// <summary>
    /// Parses the arguments and notes where each one stands, counted from <paramref name="origin"/> — the
    /// extension's opening brace — so an edit can replace one argument and leave the others' text alone.
    /// </summary>
    private static ImmutableArray<XamlMarkupExtensionArgument> ParseArguments(
        string text,
        ref int position,
        List<MarkupDiagnostic> diagnostics,
        int origin,
        out ImmutableArray<TextSpan> spans)
    {
        ImmutableArray<XamlMarkupExtensionArgument>.Builder arguments =
            ImmutableArray.CreateBuilder<XamlMarkupExtensionArgument>();
        ImmutableArray<TextSpan>.Builder found = ImmutableArray.CreateBuilder<TextSpan>();

        while (true)
        {
            SkipWhitespace(text, ref position);

            if (position >= text.Length || text[position] == '}')
            {
                spans = found.ToImmutable();

                return arguments.ToImmutable();
            }

            int argumentStart = position;

            arguments.Add(ParseArgument(text, ref position, diagnostics));

            // A bare value runs up to the separator and is trimmed; its span ends where its text does.
            int argumentEnd = Math.Min(position, text.Length);

            while (argumentEnd > argumentStart && char.IsWhiteSpace(text[argumentEnd - 1]))
            {
                argumentEnd--;
            }

            found.Add(TextSpan.FromBounds(argumentStart - origin, argumentEnd - origin));

            SkipWhitespace(text, ref position);

            if (position < text.Length && text[position] == ',')
            {
                position++;

                continue;
            }

            spans = found.ToImmutable();

            return arguments.ToImmutable();
        }
    }

    private static XamlMarkupExtensionArgument ParseArgument(
        string text,
        ref int position,
        List<MarkupDiagnostic> diagnostics)
    {
        int probe = position;
        string? name = TryReadArgumentName(text, ref probe);

        if (name is not null)
        {
            position = probe;

            return new XamlMarkupExtensionArgument(name, ParseArgumentValue(text, ref position, diagnostics));
        }

        return new XamlMarkupExtensionArgument(null, ParseArgumentValue(text, ref position, diagnostics));
    }

    /// <summary>
    /// Reads an argument name, but only if an equals sign actually follows it.
    /// </summary>
    /// <remarks>
    /// <c>{Binding Customer.Name}</c> and <c>{Binding Path=Customer.Name}</c> start the same
    /// way, so the decision cannot be made until the equals sign is either found or ruled out.
    /// </remarks>
    private static string? TryReadArgumentName(string text, ref int position)
    {
        int start = position;

        while (position < text.Length && !IsNameStop(text[position]))
        {
            position++;
        }

        if (position == start)
        {
            return null;
        }

        string candidate = text[start..position];

        SkipWhitespace(text, ref position);

        if (position < text.Length && text[position] == '=')
        {
            position++;
            SkipWhitespace(text, ref position);

            return candidate;
        }

        position = start;

        return null;
    }

    private static XamlValue ParseArgumentValue(
        string text,
        ref int position,
        List<MarkupDiagnostic> diagnostics)
    {
        if (position >= text.Length)
        {
            return XamlValue.Unset;
        }

        char current = text[position];

        if (current == '{')
        {
            return ParseExtension(text, ref position, diagnostics);
        }

        if (current is '\'' or '"')
        {
            return ParseQuotedArgument(text, ref position, current, diagnostics);
        }

        int start = position;

        // A bare argument runs to the next separator. Commas and braces are the only things
        // that can end it, so a value containing either has to be quoted.
        while (position < text.Length && text[position] is not (',' or '}'))
        {
            position++;
        }

        return new XamlLiteralValue(text[start..position].TrimEnd());
    }

    private static XamlLiteralValue ParseQuotedArgument(
        string text,
        ref int position,
        char quote,
        List<MarkupDiagnostic> diagnostics)
    {
        int start = position;

        position++; // opening quote

        int contentStart = position;

        while (position < text.Length && text[position] != quote)
        {
            position++;
        }

        // Quotes do not keep a leading brace from opening an extension — Avalonia reads '{0:N2}' as one — so a
        // quoted literal that starts with a brace carries the same {} escape a whole attribute does, and the escape
        // is how it was written, not part of it.
        string content = text[contentStart..position];

        if (content.StartsWith("{}", StringComparison.Ordinal))
        {
            content = content[2..];
        }

        if (position < text.Length)
        {
            position++; // closing quote
        }
        else
        {
            diagnostics.Add(Diagnostic(
                XamlDiagnosticCodes.UnterminatedQuotedArgument,
                $"The quoted argument is missing its closing {quote} character.",
                TextSpan.FromBounds(start, text.Length)));
        }

        return new XamlLiteralValue(content);
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }

    private static bool IsAllWhitespace(string text, int from)
    {
        for (int index = from; index < text.Length; index++)
        {
            if (!char.IsWhiteSpace(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Characters that end a type or argument name.</summary>
    private static bool IsNameStop(char value) =>
        char.IsWhiteSpace(value) || value is '=' or ',' or '{' or '}' or '\'' or '"';

    private static MarkupDiagnostic Diagnostic(string code, string message, TextSpan span) =>
        MarkupDiagnostic.Parse(code, message, MarkupDiagnosticSeverity.Error, documentUri: null, span);
}
