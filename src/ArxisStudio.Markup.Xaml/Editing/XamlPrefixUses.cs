using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Finds where markup names a namespace by a prefix — the places a rename has to reach, and the
/// mentions a fragment has to carry a declaration for.
/// </summary>
/// <remarks>
/// <para>
/// Two questions with two standards of proof. Whether a prefix is <em>mentioned</em> decides which
/// declarations travel with a fragment, and there a false yes costs one unused declaration while a
/// false no costs a fragment that does not load — so it is answered generously, from the text.
/// Where a prefix is <em>used</em> decides what a rename rewrites, and there a false yes changes
/// what a document says — so it is answered from the syntax, at the places the syntax says hold a
/// name, and nowhere else.
/// </para>
/// <para>
/// Those places: element names, start and end tag alike; attribute names; the type name of every
/// markup extension, nested ones included; a value — or an extension's argument — that is nothing
/// but names, as <c>{x:Type local:Badge}</c>, <c>x:DataType="vm:Customer"</c> and
/// <c>x:TypeArguments="x:String, local:Badge"</c> are; an attached property written in parentheses,
/// as a binding path writes it; and the type steps of a style's <c>Selector</c>, where the
/// separator is <c>|</c>. Text a control displays is never one of them.
/// </para>
/// <para>
/// The extension grammar is the one <see cref="XamlMarkupExtensionParser"/> reads, step for step,
/// rather than a looser one of its own: a rename that split an argument differently from the
/// parser would rewrite text the parser reads as something else.
/// </para>
/// </remarks>
internal static class XamlPrefixUses
{
    /// <summary>Finds every place the syntax says names a namespace by a prefix.</summary>
    /// <param name="element">The element to search, along with everything inside it.</param>
    /// <param name="prefix">The prefix.</param>
    /// <param name="namespaceUri">
    /// The namespace the prefix has to mean where it is used. An inner declaration that binds the
    /// same prefix to something else names something else, and is not reached.
    /// </param>
    /// <returns>The spans of the prefix itself, in the element's document.</returns>
    public static List<TextSpan> Find(XamlElement element, string prefix, string namespaceUri)
    {
        var spans = new List<TextSpan>();
        SourceText text = element.Document.SourceText;

        foreach (XamlElement current in element.DescendantElements().Prepend(element))
        {
            if (!string.Equals(current.NamespaceContext.LookupNamespace(prefix), namespaceUri, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(current.Name.Prefix, prefix, StringComparison.Ordinal))
            {
                spans.Add(new TextSpan(current.NameSpan.Start, prefix.Length));
            }

            // "</" and then the name, which XML allows no space inside.
            if (current.EndTagSpan is { } end
                && string.Equals(current.EndTagName?.Prefix, prefix, StringComparison.Ordinal)
                && end.Start + 2 + prefix.Length < text.Length
                && string.Equals(text.GetText(new TextSpan(end.Start + 2, prefix.Length + 1)), prefix + ":", StringComparison.Ordinal))
            {
                spans.Add(new TextSpan(end.Start + 2, prefix.Length));
            }

            foreach (XamlAttribute attribute in current.Attributes)
            {
                if (attribute is XamlNamespaceDeclaration)
                {
                    continue;
                }

                if (string.Equals(attribute.Name.Prefix, prefix, StringComparison.Ordinal))
                {
                    spans.Add(new TextSpan(attribute.NameSpan.Start, prefix.Length));
                }

                if (attribute.ValueSpan is { } value)
                {
                    foreach (int offset in InValue(text.GetText(value), prefix, attribute.Name.IsUnprefixed("Selector")))
                    {
                        spans.Add(new TextSpan(value.Start + offset, prefix.Length));
                    }
                }
            }
        }

        return spans;
    }

    /// <summary>Reports whether text mentions a prefix anywhere a name could start with it.</summary>
    /// <param name="text">The text to search.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>
    /// <see langword="true"/> when the prefix is followed by <c>:</c> or <c>|</c> and a name, and
    /// preceded by nothing a name could continue from.
    /// </returns>
    public static bool IsMentioned(string text, string prefix)
    {
        if (prefix.Length == 0)
        {
            return false;
        }

        for (int index = text.IndexOf(prefix, StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf(prefix, index + 1, StringComparison.Ordinal))
        {
            if ((index == 0 || !Continues(text[index - 1]))
                && (IsPrefixAt(text, index, prefix, ':') || IsPrefixAt(text, index, prefix, '|')))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reports whether an element mentions a prefix anywhere outside the given spans.
    /// </summary>
    /// <param name="element">The element to search, along with everything inside it.</param>
    /// <param name="prefix">The prefix.</param>
    /// <param name="excluded">Spans of the element's document that are about to be removed.</param>
    /// <returns><see langword="true"/> when the remaining text mentions the prefix.</returns>
    public static bool IsMentioned(XamlElement element, string prefix, IReadOnlyCollection<TextSpan> excluded)
    {
        string text = element.GetText();

        if (excluded.Count == 0)
        {
            return IsMentioned(text, prefix);
        }

        // Blanked rather than cut, so that what is left of the text keeps its shape around the gap
        // and two words never run into one.
        var blanked = new StringBuilder(text);

        foreach (TextSpan span in excluded)
        {
            for (int index = Math.Max(span.Start, element.Span.Start); index < Math.Min(span.End, element.Span.End); index++)
            {
                blanked[index - element.Span.Start] = ' ';
            }
        }

        return IsMentioned(blanked.ToString(), prefix);
    }

    /// <summary>
    /// Reports whether an element writes anything that resolves against the default namespace.
    /// </summary>
    /// <remarks>
    /// An unprefixed element name, a property element or an attached property whose owner is
    /// unprefixed, and an unprefixed markup extension all name a type in whatever the default
    /// namespace is where they sit.
    /// </remarks>
    /// <param name="element">The element to search, along with everything inside it.</param>
    /// <returns><see langword="true"/> when something in it does.</returns>
    public static bool UsesDefaultNamespace(XamlElement element)
    {
        foreach (XamlElement current in element.DescendantElements().Prepend(element))
        {
            if (current.Name.Prefix is null)
            {
                return true;
            }

            foreach (XamlAttribute attribute in current.Attributes)
            {
                if (attribute is XamlNamespaceDeclaration)
                {
                    continue;
                }

                if ((attribute.Name.Prefix is null && attribute.Name.IsDotted)
                    || (attribute.HasValue && NamesUnprefixedExtension(attribute.GetValue())))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool NamesUnprefixedExtension(XamlValue value) =>
        value is XamlMarkupExtensionValue extension
        && (extension.TypeName.Prefix is null
            || extension.Arguments.Any(static argument => NamesUnprefixedExtension(argument.Value)));

    /// <summary>Finds a prefix's uses in an attribute's raw value, as offsets into it.</summary>
    private static List<int> InValue(string raw, string prefix, bool isSelector)
    {
        var offsets = new List<int>();

        if (raw.StartsWith("{}", StringComparison.Ordinal))
        {
            // XAML's escape: what follows is a literal that happens to start with a brace.
            InLiteral(raw[2..], 2, prefix, offsets);
        }
        else if (raw.StartsWith('{'))
        {
            var position = 0;

            InExtension(raw, ref position, prefix, offsets);
        }
        else
        {
            InLiteral(raw, 0, prefix, offsets);

            if (isSelector)
            {
                InSelector(raw, prefix, offsets);
            }
        }

        return offsets;
    }

    /// <summary>Walks one <c>{…}</c> expression the way the parser reads it.</summary>
    private static void InExtension(string text, ref int position, string prefix, List<int> offsets)
    {
        position++; // '{'
        SkipWhitespace(text, ref position);

        int nameStart = position;

        while (position < text.Length && !IsNameStop(text[position]))
        {
            position++;
        }

        if (IsPrefixAt(text, nameStart, prefix, ':') && nameStart + prefix.Length < position)
        {
            offsets.Add(nameStart);
        }

        while (true)
        {
            SkipWhitespace(text, ref position);

            if (position >= text.Length || text[position] == '}')
            {
                break;
            }

            // A name, but only if an equals sign follows it — the test the parser makes.
            int probe = position;

            while (probe < text.Length && !IsNameStop(text[probe]))
            {
                probe++;
            }

            int equals = probe;

            SkipWhitespace(text, ref equals);

            if (probe > position && equals < text.Length && text[equals] == '=')
            {
                position = equals + 1;
                SkipWhitespace(text, ref position);
            }

            if (position >= text.Length)
            {
                break;
            }

            char current = text[position];

            if (current == '{')
            {
                InExtension(text, ref position, prefix, offsets);
            }
            else if (current is '\'' or '"')
            {
                int start = ++position;

                while (position < text.Length && text[position] != current)
                {
                    position++;
                }

                InLiteral(text[start..position], start, prefix, offsets);

                if (position < text.Length)
                {
                    position++;
                }
            }
            else
            {
                int start = position;

                while (position < text.Length && text[position] is not (',' or '}'))
                {
                    position++;
                }

                InLiteral(text[start..position].TrimEnd(), start, prefix, offsets);
            }

            SkipWhitespace(text, ref position);

            if (position < text.Length && text[position] == ',')
            {
                position++;

                continue;
            }

            break;
        }

        if (position < text.Length && text[position] == '}')
        {
            position++;
        }
    }

    /// <summary>
    /// Finds a prefix's uses in literal text: a list of names, or attached properties in
    /// parentheses.
    /// </summary>
    private static void InLiteral(string literal, int offset, string prefix, List<int> offsets)
    {
        if (TryReadNames(literal, prefix, out List<int>? names))
        {
            foreach (int name in names)
            {
                offsets.Add(offset + name);
            }

            return;
        }

        for (int index = literal.IndexOf('(', StringComparison.Ordinal);
            index >= 0;
            index = literal.IndexOf('(', index + 1))
        {
            int start = index + 1;

            while (start < literal.Length && char.IsWhiteSpace(literal[start]))
            {
                start++;
            }

            if (IsPrefixAt(literal, start, prefix, ':'))
            {
                offsets.Add(offset + start);
            }
        }
    }

    /// <summary>
    /// Reads text that is nothing but names separated by commas, and says where the prefix
    /// starts any of them.
    /// </summary>
    /// <returns><see langword="false"/> when anything else is in the text.</returns>
    private static bool TryReadNames(string text, string prefix, out List<int> found)
    {
        found = [];

        var position = 0;

        while (true)
        {
            SkipWhitespace(text, ref position);

            int start = position;

            if (!TryReadName(text, ref position, out int colon))
            {
                return false;
            }

            if (colon - start == prefix.Length && string.CompareOrdinal(text, start, prefix, 0, prefix.Length) == 0)
            {
                found.Add(start);
            }

            SkipWhitespace(text, ref position);

            if (position == text.Length)
            {
                return true;
            }

            if (text[position] != ',')
            {
                return false;
            }

            position++;
        }
    }

    /// <summary>Reads <c>prefix:Name.Member</c>, the prefix and the members optional.</summary>
    /// <param name="text">The text.</param>
    /// <param name="position">Where to start; advanced past the name.</param>
    /// <param name="colon">Where the colon is, or <c>-1</c> when the name has no prefix.</param>
    private static bool TryReadName(string text, ref int position, out int colon)
    {
        colon = -1;

        if (!TryReadIdentifier(text, ref position, allowDash: true))
        {
            return false;
        }

        if (position < text.Length && text[position] == ':')
        {
            colon = position;
            position++;

            if (!TryReadIdentifier(text, ref position, allowDash: false))
            {
                return false;
            }
        }

        while (position < text.Length && text[position] == '.')
        {
            position++;

            if (!TryReadIdentifier(text, ref position, allowDash: false))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadIdentifier(string text, ref int position, bool allowDash)
    {
        if (position >= text.Length || !IsNameStart(text[position]))
        {
            return false;
        }

        position++;

        while (position < text.Length
            && (char.IsLetterOrDigit(text[position]) || text[position] == '_' || (allowDash && text[position] == '-')))
        {
            position++;
        }

        return true;
    }

    /// <summary>Finds a prefix's uses in a style selector, where a namespace is written <c>local|Badge</c>.</summary>
    private static void InSelector(string selector, string prefix, List<int> offsets)
    {
        for (int index = selector.IndexOf(prefix, StringComparison.Ordinal);
            index >= 0;
            index = selector.IndexOf(prefix, index + 1, StringComparison.Ordinal))
        {
            if ((index == 0 || !Continues(selector[index - 1])) && IsPrefixAt(selector, index, prefix, '|'))
            {
                offsets.Add(index);
            }
        }
    }

    /// <summary>Reports whether a prefix stands at a position, followed by its separator and a name.</summary>
    private static bool IsPrefixAt(string text, int start, string prefix, char separator) =>
        start >= 0
        && start + prefix.Length + 1 < text.Length
        && string.CompareOrdinal(text, start, prefix, 0, prefix.Length) == 0
        && text[start + prefix.Length] == separator
        && IsNameStart(text[start + prefix.Length + 1]);

    private static bool IsNameStart(char value) => char.IsLetter(value) || value == '_';

    /// <summary>
    /// Characters a name could be running on from, so that a prefix after one of them is the tail
    /// of something else: <c>mylocal:</c> does not mention <c>local</c>.
    /// </summary>
    private static bool Continues(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-' or '.' or ':' or '|';

    /// <summary>Characters that end a type or argument name, exactly as the parser has them.</summary>
    private static bool IsNameStop(char value) =>
        char.IsWhiteSpace(value) || value is '=' or ',' or '{' or '}' or '\'' or '"';

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }
}
