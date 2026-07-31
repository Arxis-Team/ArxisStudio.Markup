using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Builds a syntax tree from a token stream.
/// </summary>
/// <remarks>
/// <para>
/// Malformed input produces a best-effort tree and diagnostics, never an exception. A document
/// somebody is halfway through typing is the normal case for this library, not an error case,
/// and refusing to give a tree for it would make the whole thing useless in an editor.
/// </para>
/// <para>
/// Every token ends up inside exactly one node, and every node's children lie within it, in
/// order, without overlapping. That is what lets the writer reproduce the document by walking
/// the tree.
/// </para>
/// </remarks>
internal sealed class XamlParser
{
    private readonly SourceText _text;
    private readonly Uri? _documentUri;
    private readonly ImmutableArray<XamlToken> _tokens;
    private readonly List<MarkupDiagnostic> _diagnostics = [];

    private int _index;

    private XamlParser(SourceText text, Uri? documentUri, ImmutableArray<XamlToken> tokens)
    {
        _text = text;
        _documentUri = documentUri;
        _tokens = tokens;
    }

    /// <summary>Parses a token stream into the document's top-level nodes.</summary>
    /// <param name="text">The snapshot the tokens came from.</param>
    /// <param name="documentUri">The document's URI, attached to diagnostics.</param>
    /// <param name="tokens">The token stream.</param>
    /// <returns>The top-level nodes and any diagnostics the parser raised.</returns>
    public static (ImmutableArray<XamlSyntaxNode> Nodes, ImmutableArray<MarkupDiagnostic> Diagnostics) Parse(
        SourceText text,
        Uri? documentUri,
        ImmutableArray<XamlToken> tokens)
    {
        var parser = new XamlParser(text, documentUri, tokens);
        ImmutableArray<XamlSyntaxNode> nodes = parser.ParseContent(XamlNamespaceContext.Empty, enclosing: null);

        return (nodes, [.. parser._diagnostics]);
    }

    private XamlToken Current => _tokens[_index];

    private bool AtEnd => Current.Kind == XamlTokenKind.EndOfFile;

    /// <summary>
    /// Parses nodes until the end of the input or an end tag this level does not own.
    /// </summary>
    /// <param name="context">The namespace declarations in scope here.</param>
    /// <param name="enclosing">
    /// The name of the element being filled, or <see langword="null"/> at document level. An
    /// end tag that does not match it belongs to an ancestor, so parsing stops and lets the
    /// caller decide.
    /// </param>
    private ImmutableArray<XamlSyntaxNode> ParseContent(XamlNamespaceContext context, XamlQualifiedName? enclosing)
    {
        ImmutableArray<XamlSyntaxNode>.Builder nodes = ImmutableArray.CreateBuilder<XamlSyntaxNode>();

        while (!AtEnd)
        {
            XamlToken token = Current;

            if (token.Kind == XamlTokenKind.LessThanSlash)
            {
                if (enclosing is not null)
                {
                    // Whether this end tag closes the current element is the caller's call.
                    return nodes.ToImmutable();
                }

                // At document level there is nothing for it to close, so consume it as text
                // rather than spin here.
                Report(
                    XamlDiagnosticCodes.UnexpectedEndTag,
                    "There is no open element for this end tag to close.",
                    token.Span);

                nodes.Add(ConsumeTagAsTrivia());

                continue;
            }

            nodes.Add(token.Kind switch
            {
                XamlTokenKind.LessThan => ParseElement(context),
                XamlTokenKind.Comment => ParseComment(),
                XamlTokenKind.CData => ParseCData(),
                XamlTokenKind.XmlDeclaration => ParseProcessingInstruction(XamlProcessingInstructionKind.XmlDeclaration),
                XamlTokenKind.DocumentType => ParseProcessingInstruction(XamlProcessingInstructionKind.DocumentType),
                XamlTokenKind.ProcessingInstruction => ParseProcessingInstruction(XamlProcessingInstructionKind.ProcessingInstruction),
                XamlTokenKind.Whitespace => ParseTrivia(XamlTriviaKind.Whitespace),
                XamlTokenKind.NewLine => ParseTrivia(XamlTriviaKind.NewLine),
                XamlTokenKind.Text or XamlTokenKind.EntityReference => ParseText(),
                _ => ParseTrivia(XamlTriviaKind.Skipped),
            });
        }

        return nodes.ToImmutable();
    }

    private XamlElement ParseElement(XamlNamespaceContext parentContext)
    {
        int start = Current.Span.Start;

        Advance(); // '<'

        (XamlQualifiedName name, TextSpan nameSpan) = ParseName(XamlDiagnosticCodes.ExpectedElementName);
        (ImmutableArray<XamlAttribute> attributes, bool isEmpty, int startTagEnd) = ParseAttributes();

        XamlNamespaceContext context = parentContext.Push(CollectDeclarations(attributes));
        var startTagSpan = TextSpan.FromBounds(start, startTagEnd);

        if (isEmpty)
        {
            return new XamlElement(
                startTagSpan, name, nameSpan, startTagSpan, endTagSpan: null, endTagName: null,
                isEmpty: true, attributes, [], context, []);
        }

        ImmutableArray<XamlSyntaxNode> content = ParseContent(context, name);
        (TextSpan? endTagSpan, XamlQualifiedName? endTagName) = TryParseEndTag(name, start, nameSpan);

        int end = endTagSpan?.End ?? (content.Length > 0 ? content[^1].Span.End : startTagEnd);

        return new XamlElement(
            TextSpan.FromBounds(start, end), name, nameSpan, startTagSpan, endTagSpan, endTagName,
            isEmpty: false, attributes, content, context, []);
    }

    /// <summary>Consumes the end tag when it closes this element, and reports when it does not.</summary>
    private (TextSpan? Span, XamlQualifiedName? Name) TryParseEndTag(
        XamlQualifiedName expected,
        int elementStart,
        TextSpan nameSpan)
    {
        if (AtEnd)
        {
            Report(
                XamlDiagnosticCodes.UnclosedElement,
                $"Element '{expected}' is never closed.",
                nameSpan);

            return (null, null);
        }

        int probe = _index;
        int start = Current.Span.Start;

        Advance(); // '</'

        (XamlQualifiedName actual, _) = ParseName(XamlDiagnosticCodes.ExpectedElementName, report: false);

        if (actual != expected)
        {
            // It belongs to an ancestor. Rewinding lets the enclosing level claim it instead of
            // this one swallowing a tag that was never its own.
            _index = probe;
            Report(
                XamlDiagnosticCodes.UnclosedElement,
                $"Element '{expected}' is closed by '</{actual}>', which does not match.",
                TextSpan.FromBounds(elementStart, start));

            return (null, null);
        }

        while (!AtEnd && Current.Kind is XamlTokenKind.Whitespace or XamlTokenKind.NewLine or XamlTokenKind.Skipped)
        {
            Advance();
        }

        int end;

        if (!AtEnd && Current.Kind == XamlTokenKind.GreaterThan)
        {
            end = Current.Span.End;
            Advance();
        }
        else
        {
            end = _tokens[_index - 1].Span.End;
            Report(
                XamlDiagnosticCodes.UnterminatedTag,
                $"The end tag for '{expected}' is missing its closing '>'.",
                new TextSpan(end, 0));
        }

        return (TextSpan.FromBounds(start, end), actual);
    }

    /// <summary>Parses the attributes of a start tag and whatever closes it.</summary>
    private (ImmutableArray<XamlAttribute> Attributes, bool IsEmpty, int End) ParseAttributes()
    {
        ImmutableArray<XamlAttribute>.Builder attributes = ImmutableArray.CreateBuilder<XamlAttribute>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (!AtEnd)
        {
            switch (Current.Kind)
            {
                case XamlTokenKind.GreaterThan:
                    int openEnd = Current.Span.End;
                    Advance();

                    return (attributes.ToImmutable(), false, openEnd);

                case XamlTokenKind.SlashGreaterThan:
                    int emptyEnd = Current.Span.End;
                    Advance();

                    return (attributes.ToImmutable(), true, emptyEnd);

                case XamlTokenKind.Name:
                    XamlAttribute attribute = ParseAttribute();

                    if (!seen.Add(attribute.Name.ToString()))
                    {
                        Report(
                            XamlDiagnosticCodes.DuplicateAttribute,
                            $"Attribute '{attribute.Name}' is declared more than once on this element.",
                            attribute.NameSpan);
                    }

                    attributes.Add(attribute);

                    break;

                default:
                    // Whitespace, stray quotes and skipped text all belong to the tag rather
                    // than to any attribute, so they are stepped over and stay inside the
                    // start tag's span.
                    Advance();

                    break;
            }
        }

        int end = _tokens[_index].Span.Start;

        Report(
            XamlDiagnosticCodes.UnterminatedTag,
            "The tag is missing its closing '>'.",
            new TextSpan(end, 0));

        return (attributes.ToImmutable(), false, end);
    }

    private XamlAttribute ParseAttribute()
    {
        int start = Current.Span.Start;

        (XamlQualifiedName name, TextSpan nameSpan) = ParseName(XamlDiagnosticCodes.ExpectedElementName, report: false);

        int afterName = _tokens[_index - 1].Span.End;
        int scan = _index;

        while (scan < _tokens.Length && _tokens[scan].IsWhitespace)
        {
            scan++;
        }

        if (scan >= _tokens.Length || _tokens[scan].Kind != XamlTokenKind.Equals)
        {
            // A bare attribute name. HTML tolerates it; XML does not, and inventing a value
            // would put text in the document that the author never wrote.
            Report(
                XamlDiagnosticCodes.MissingAttributeValue,
                $"Attribute '{name}' has no '=' and value.",
                nameSpan);

            return Create(TextSpan.FromBounds(start, afterName), name, nameSpan, valueSpan: null, quote: null);
        }

        _index = scan + 1; // past '='

        while (!AtEnd && Current.IsWhitespace)
        {
            Advance();
        }

        if (AtEnd || Current.Kind != XamlTokenKind.Quote)
        {
            Report(
                XamlDiagnosticCodes.MissingAttributeValue,
                $"The value of attribute '{name}' is not quoted.",
                nameSpan);

            return Create(TextSpan.FromBounds(start, _tokens[_index - 1].Span.End), name, nameSpan, valueSpan: null, quote: null);
        }

        char quote = _text[Current.Span.Start];
        int valueStart = Current.Span.End;

        Advance(); // opening quote

        while (!AtEnd && Current.Kind is XamlTokenKind.AttributeValueText or XamlTokenKind.EntityReference)
        {
            Advance();
        }

        int valueEnd = _tokens[_index - 1].Span.End;
        int end;

        if (!AtEnd && Current.Kind == XamlTokenKind.Quote)
        {
            valueEnd = Current.Span.Start;
            end = Current.Span.End;
            Advance();
        }
        else
        {
            // The lexer already reported the unterminated value; the attribute still spans
            // what was written so the text survives.
            end = valueEnd;
        }

        return Create(TextSpan.FromBounds(start, end), name, nameSpan, TextSpan.FromBounds(valueStart, valueEnd), quote);

        static XamlAttribute Create(
            TextSpan span, XamlQualifiedName name, TextSpan nameSpan, TextSpan? valueSpan, char? quote)
        {
            // 'xmlns="..."' declares the default namespace; 'xmlns:p="..."' declares a prefix.
            if (name.IsUnprefixed("xmlns"))
            {
                return new XamlNamespaceDeclaration(span, name, nameSpan, valueSpan, quote, prefix: null, []);
            }

            return string.Equals(name.Prefix, "xmlns", StringComparison.Ordinal)
                ? new XamlNamespaceDeclaration(span, name, nameSpan, valueSpan, quote, name.LocalName, [])
                : new XamlAttribute(span, name, nameSpan, valueSpan, quote, []);
        }
    }

    /// <summary>Reads a possibly prefixed name from the token stream.</summary>
    private (XamlQualifiedName Name, TextSpan Span) ParseName(string missingCode, bool report = true)
    {
        if (AtEnd || Current.Kind != XamlTokenKind.Name)
        {
            if (report)
            {
                Report(missingCode, "A name was expected here.", new TextSpan(Current.Span.Start, 0));
            }

            return (new XamlQualifiedName(null, string.Empty), new TextSpan(Current.Span.Start, 0));
        }

        int start = Current.Span.Start;
        string first = TextOf(Current);

        Advance();

        if (!AtEnd && Current.Kind == XamlTokenKind.Colon)
        {
            Advance();

            if (!AtEnd && Current.Kind == XamlTokenKind.Name)
            {
                string local = TextOf(Current);
                int end = Current.Span.End;

                Advance();

                return (new XamlQualifiedName(first, local), TextSpan.FromBounds(start, end));
            }

            // A trailing colon with no local name. Keeping the prefix as the local name would
            // silently rename it, so the colon is simply not treated as a separator.
            return (new XamlQualifiedName(null, first), TextSpan.FromBounds(start, _tokens[_index - 1].Span.End));
        }

        return (new XamlQualifiedName(null, first), TextSpan.FromBounds(start, _tokens[_index - 1].Span.End));
    }

    private List<KeyValuePair<string?, string>> CollectDeclarations(ImmutableArray<XamlAttribute> attributes)
    {
        var declarations = new List<KeyValuePair<string?, string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (XamlAttribute attribute in attributes)
        {
            if (attribute is not XamlNamespaceDeclaration declaration)
            {
                continue;
            }

            if (!seen.Add(declaration.Prefix ?? string.Empty))
            {
                Report(
                    XamlDiagnosticCodes.DuplicateNamespacePrefix,
                    declaration.IsDefault
                        ? "The default namespace is declared more than once on this element."
                        : $"Prefix '{declaration.Prefix}' is declared more than once on this element.",
                    declaration.NameSpan);
            }

            declarations.Add(new KeyValuePair<string?, string>(declaration.Prefix, ValueOf(declaration)));
        }

        return declarations;
    }

    /// <summary>Reads a namespace declaration's value directly, before the node has a document.</summary>
    private string ValueOf(XamlNamespaceDeclaration declaration) =>
        declaration.ValueSpan is { } span ? _text.GetText(span) : string.Empty;

    private XamlComment ParseComment()
    {
        XamlToken token = Current;

        Advance();

        // "<!--" and "-->" are four and three characters. An unterminated comment has no
        // closing delimiter to exclude.
        int contentStart = Math.Min(token.Span.Start + 4, token.Span.End);
        int contentEnd = EndsWith(token, "-->") ? token.Span.End - 3 : token.Span.End;

        return new XamlComment(token.Span, TextSpan.FromBounds(contentStart, Math.Max(contentStart, contentEnd)), []);
    }

    private XamlCData ParseCData()
    {
        XamlToken token = Current;

        Advance();

        int contentStart = Math.Min(token.Span.Start + 9, token.Span.End);
        int contentEnd = EndsWith(token, "]]>") ? token.Span.End - 3 : token.Span.End;

        return new XamlCData(token.Span, TextSpan.FromBounds(contentStart, Math.Max(contentStart, contentEnd)), []);
    }

    private XamlProcessingInstruction ParseProcessingInstruction(XamlProcessingInstructionKind kind)
    {
        XamlToken token = Current;

        Advance();

        return new XamlProcessingInstruction(token.Span, kind, []);
    }

    private XamlTrivia ParseTrivia(XamlTriviaKind kind)
    {
        XamlToken token = Current;

        Advance();

        return new XamlTrivia(token.Span, kind, []);
    }

    /// <summary>Merges a run of text and entity references into one node.</summary>
    private XamlText ParseText()
    {
        int start = Current.Span.Start;
        int end = Current.Span.End;

        while (!AtEnd && Current.Kind is XamlTokenKind.Text or XamlTokenKind.EntityReference)
        {
            end = Current.Span.End;
            Advance();
        }

        return new XamlText(TextSpan.FromBounds(start, end), []);
    }

    /// <summary>Consumes a stray end tag as skipped text so its characters survive.</summary>
    private XamlTrivia ConsumeTagAsTrivia()
    {
        int start = Current.Span.Start;
        int end = Current.Span.End;

        Advance();

        while (!AtEnd && Current.Kind is not (XamlTokenKind.LessThan or XamlTokenKind.LessThanSlash))
        {
            end = Current.Span.End;

            bool closes = Current.Kind is XamlTokenKind.GreaterThan or XamlTokenKind.SlashGreaterThan;

            Advance();

            if (closes)
            {
                break;
            }
        }

        return new XamlTrivia(TextSpan.FromBounds(start, end), XamlTriviaKind.Skipped, []);
    }

    private bool EndsWith(XamlToken token, string suffix)
    {
        if (token.Span.Length < suffix.Length)
        {
            return false;
        }

        int start = token.Span.End - suffix.Length;

        for (var index = 0; index < suffix.Length; index++)
        {
            if (_text[start + index] != suffix[index])
            {
                return false;
            }
        }

        return true;
    }

    private string TextOf(XamlToken token) => _text.GetText(token.Span);

    private void Advance()
    {
        if (!AtEnd)
        {
            _index++;
        }
    }

    private void Report(string code, string message, TextSpan span) =>
        _diagnostics.Add(MarkupDiagnostic.Parse(
            code, message, MarkupDiagnosticSeverity.Error, _documentUri, span));
}
