using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Reads which types a style selector applies to, without resolving any of them.
/// </summary>
/// <remarks>
/// <para>
/// Not a parser of the selector grammar, and deliberately so: of everything a selector can say, a
/// tool asking whose style this is needs only the type of each alternative's last step. The
/// reader splits the alternatives at their commas, finds each one's last step and reads the type
/// that opens it — <c>Type</c>, <c>prefix|Type</c>, <c>:is(Type)</c>, or <c>^</c> for the
/// parent's types — and passes over the classes, names, pseudo-classes and property matches
/// around it. Brackets are counted, so a comma or a space inside <c>:not(…)</c> or
/// <c>[Tag=…]</c> splits nothing.
/// </para>
/// <para>
/// A name is read by the rule Avalonia's own selector parser uses — a letter or an underscore,
/// then letters, digits, underscores and combining marks — so a step this reader takes for a type
/// is one Avalonia would take for a type too.
/// </para>
/// <para>
/// The attribute's text is raw, with entity references unexpanded, because that is what lets a
/// document write back unchanged. <c>StackPanel &amp;gt; Button</c> is a child selector all the
/// same, so the reader expands them for itself and maps every position back: the span of a type
/// is where the type is written, not where it would be after expansion.
/// </para>
/// </remarks>
internal static class XamlSelectorReader
{
    private const string TemplateCombinator = "/template/";
    private const string IsPseudoClass = ":is(";

    /// <summary>Reads the types a selector's setters apply to.</summary>
    /// <param name="raw">The <c>Selector</c> attribute's text, as written.</param>
    /// <param name="origin">Where that text starts in the document.</param>
    /// <param name="context">The namespaces in scope on the declaring element.</param>
    /// <param name="inherited">What <c>^</c> stands for: the parent declaration's targets.</param>
    /// <returns>The targets, in the order the alternatives name them.</returns>
    public static ImmutableArray<XamlTypeReference> ReadTargets(
        string raw,
        int origin,
        XamlNamespaceContext context,
        ImmutableArray<XamlTypeReference> inherited)
    {
        var reading = new Reading(ExpandedText.From(raw), origin, context, inherited);
        string selector = reading.Text.Value;
        int depth = 0;
        int alternative = 0;

        for (int i = 0; i <= selector.Length; i++)
        {
            if (i < selector.Length)
            {
                char c = selector[i];

                if (c is '(' or '[')
                {
                    depth++;
                }
                else if (c is ')' or ']')
                {
                    depth = Math.Max(0, depth - 1);
                }

                if (c != ',' || depth > 0)
                {
                    continue;
                }
            }

            ReadAlternative(reading, alternative, i);
            alternative = i + 1;
        }

        return reading.Targets.ToImmutable();
    }

    /// <summary>Finds an alternative's last step and reads the type it opens with.</summary>
    /// <remarks>
    /// The last step is what follows the last combinator outside brackets — whitespace, <c>&gt;</c>
    /// or <c>/template/</c> — and it is the step the setters land on.
    /// </remarks>
    private static void ReadAlternative(Reading reading, int start, int end)
    {
        string selector = reading.Text.Value;

        // Trailing whitespace is not a combinator with nothing after it; it is just the space
        // before the next comma.
        while (end > start && char.IsWhiteSpace(selector[end - 1]))
        {
            end--;
        }

        int step = start;
        int depth = 0;

        for (int i = start; i < end; i++)
        {
            char c = selector[i];

            if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth > 0)
            {
                continue;
            }
            else if (char.IsWhiteSpace(c) || c == '>')
            {
                step = i + 1;
            }
            else if (c == '/' && string.CompareOrdinal(selector, i, TemplateCombinator, 0, TemplateCombinator.Length) == 0)
            {
                i += TemplateCombinator.Length - 1;
                step = i + 1;
            }
        }

        if (step < end)
        {
            ReadStep(reading, step, end);
        }
    }

    /// <summary>Reads the type one step names, if it names one.</summary>
    private static void ReadStep(Reading reading, int start, int end)
    {
        string selector = reading.Text.Value;
        char first = selector[start];

        if (first == '^')
        {
            reading.Targets.AddRange(reading.Inherited);

            return;
        }

        if (IsNameStart(first))
        {
            ReadType(reading, start, end);

            return;
        }

        // A step that opens with no type may still name one in :is(), which also admits whatever
        // derives from it. Inside :not(…) it names what the step is not, so only the top level
        // of the step counts.
        int depth = 0;

        for (int i = start; i < end; i++)
        {
            char c = selector[i];

            if (depth == 0 && c == ':'
                && string.CompareOrdinal(selector, i, IsPseudoClass, 0, IsPseudoClass.Length) == 0)
            {
                int name = i + IsPseudoClass.Length;

                while (name < end && char.IsWhiteSpace(selector[name]))
                {
                    name++;
                }

                if (name < end && IsNameStart(selector[name]))
                {
                    ReadType(reading, name, end);
                }

                return;
            }

            if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth = Math.Max(0, depth - 1);
            }
        }
    }

    /// <summary>Reads <c>Type</c> or <c>prefix|Type</c> starting at a name character.</summary>
    private static void ReadType(Reading reading, int start, int end)
    {
        string selector = reading.Text.Value;
        int i = SkipName(selector, start, end);
        string? prefix = null;
        int local = start;

        if (i < end && selector[i] == '|')
        {
            prefix = selector[start..i];
            local = i + 1;

            // "prefix|" with nothing after it names a namespace and no type in it.
            if (local >= end || !IsNameStart(selector[local]))
            {
                return;
            }

            i = SkipName(selector, local, end);
        }

        var span = TextSpan.FromBounds(
            reading.Origin + reading.Text.RawOffset(start),
            reading.Origin + reading.Text.RawOffset(i));

        reading.Targets.Add(new XamlTypeReference(
            new XamlQualifiedName(prefix, selector[local..i]),
            reading.Context.LookupNamespace(prefix),
            span));
    }

    private static int SkipName(string selector, int start, int end)
    {
        int i = start;

        while (i < end && IsNamePart(selector[i]))
        {
            i++;
        }

        return i;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsNamePart(char c) =>
        IsNameStart(c)
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.Format;

    /// <summary>One selector being read: its text, where it sits, and what it has found.</summary>
    private sealed class Reading(
        ExpandedText text,
        int origin,
        XamlNamespaceContext context,
        ImmutableArray<XamlTypeReference> inherited)
    {
        public ExpandedText Text { get; } = text;

        public int Origin { get; } = origin;

        public XamlNamespaceContext Context { get; } = context;

        public ImmutableArray<XamlTypeReference> Inherited { get; } = inherited;

        public ImmutableArray<XamlTypeReference>.Builder Targets { get; } =
            ImmutableArray.CreateBuilder<XamlTypeReference>();
    }

    /// <summary>A raw attribute value with its entity references expanded, and the way back.</summary>
    private sealed class ExpandedText
    {
        private readonly int[]? _offsets;

        private ExpandedText(string value, int[]? offsets)
        {
            Value = value;
            _offsets = offsets;
        }

        /// <summary>Gets the text with entity references expanded.</summary>
        public string Value { get; }

        /// <summary>
        /// Gets where the character at an index of <see cref="Value"/> starts in the raw text;
        /// the length of <see cref="Value"/> maps to the length of the raw text.
        /// </summary>
        public int RawOffset(int index) => _offsets is null ? index : _offsets[index];

        public static ExpandedText From(string raw)
        {
            if (!raw.Contains('&', StringComparison.Ordinal))
            {
                return new ExpandedText(raw, null);
            }

            var value = new StringBuilder(raw.Length);
            var offsets = new List<int>(raw.Length + 1);
            int i = 0;

            while (i < raw.Length)
            {
                if (raw[i] == '&' && TryExpand(raw, i, out string? expansion, out int next))
                {
                    foreach (char c in expansion)
                    {
                        value.Append(c);
                        offsets.Add(i);
                    }

                    i = next;

                    continue;
                }

                value.Append(raw[i]);
                offsets.Add(i);
                i++;
            }

            offsets.Add(raw.Length);

            return new ExpandedText(value.ToString(), [.. offsets]);
        }

        /// <summary>Expands the reference at a position, when it is one XML defines.</summary>
        /// <remarks>
        /// A malformed reference is left as text. The lexer has already reported it, and a reader
        /// looking for type names has no better guess at what it meant.
        /// </remarks>
        private static bool TryExpand(
            string raw,
            int at,
            [NotNullWhen(true)] out string? expansion,
            out int next)
        {
            expansion = null;
            next = at;

            int semicolon = raw.IndexOf(';', at + 1);

            if (semicolon < 0)
            {
                return false;
            }

            string name = raw[(at + 1)..semicolon];

            expansion = name switch
            {
                "lt" => "<",
                "gt" => ">",
                "amp" => "&",
                "quot" => "\"",
                "apos" => "'",
                _ => CharacterReference(name),
            };

            next = semicolon + 1;

            return expansion is not null;
        }

        private static string? CharacterReference(string name)
        {
            if (name.Length < 2 || name[0] != '#')
            {
                return null;
            }

            bool parsed = name[1] is 'x' or 'X'
                ? int.TryParse(name.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code)
                : int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out code);

            return parsed && code is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF)
                ? char.ConvertFromUtf32(code)
                : null;
        }
    }
}
