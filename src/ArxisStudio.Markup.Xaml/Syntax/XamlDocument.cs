using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// A parsed XAML document: the snapshot it came from, the tree built over it, and everything
/// the parse noticed along the way.
/// </summary>
/// <remarks>
/// <para>
/// The document is the source of truth, not a view over one. It keeps the exact snapshot it
/// was parsed from, and every node points into that snapshot, so nothing about the original
/// text is ever inferred or reconstructed.
/// </para>
/// <para>
/// Parsing never throws for malformed input. A document that fails to parse cleanly still has
/// a tree, still covers all of its text, and still writes back byte for byte; what it also has
/// is <see cref="XamlSyntaxNode.Diagnostics"/> saying what was wrong.
/// </para>
/// </remarks>
public sealed class XamlDocument : XamlSyntaxNode
{
    private XamlDocument(
        SourceText sourceText,
        Uri? uri,
        ImmutableArray<XamlToken> tokens,
        ImmutableArray<XamlSyntaxNode> nodes,
        ImmutableArray<MarkupDiagnostic> diagnostics)
        : base(new TextSpan(0, sourceText.Length), diagnostics)
    {
        SourceText = sourceText;
        Uri = uri;
        Tokens = tokens;

        AttachChildren(nodes);

        Root = nodes.OfType<XamlElement>().FirstOrDefault();
    }

    /// <summary>Gets the snapshot this document was parsed from.</summary>
    public SourceText SourceText { get; }

    /// <summary>Gets the document's location, when one was supplied.</summary>
    public Uri? Uri { get; }

    /// <summary>
    /// Gets the root element, or <see langword="null"/> when the document has none.
    /// </summary>
    /// <remarks>
    /// A document with no root is malformed but still parseable, so this is nullable rather
    /// than a reason to have refused the parse.
    /// </remarks>
    public XamlElement? Root { get; }

    /// <summary>Gets the token stream, which accounts for every character of the snapshot.</summary>
    public ImmutableArray<XamlToken> Tokens { get; }

    /// <summary>Parses a snapshot with the default options.</summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed document, with diagnostics for anything malformed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XamlDocument Parse(SourceText text) => Parse(text, null);

    /// <summary>Parses a string with the default options.</summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed document, with diagnostics for anything malformed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XamlDocument Parse(string text) => Parse(text, null);

    /// <summary>Parses a snapshot.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="options">Parse options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The parsed document, with diagnostics for anything malformed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XamlDocument Parse(SourceText text, XamlParseOptions? options)
    {
        ArgumentNullException.ThrowIfNull(text);

        options ??= XamlParseOptions.Default;

        (ImmutableArray<XamlToken> tokens, ImmutableArray<MarkupDiagnostic> lexical) =
            XamlLexer.Lex(text, options.DocumentUri);

        (ImmutableArray<XamlSyntaxNode> nodes, ImmutableArray<MarkupDiagnostic> syntactic) =
            XamlParser.Parse(text, options.DocumentUri, tokens);

        ImmutableArray<MarkupDiagnostic> diagnostics =
            [.. lexical, .. syntactic, .. Validate(nodes, options.DocumentUri)];

        return new XamlDocument(text, options.DocumentUri, tokens, nodes, diagnostics);
    }

    /// <summary>Parses a string.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="options">Parse options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The parsed document, with diagnostics for anything malformed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XamlDocument Parse(string text, XamlParseOptions? options)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Parse(SourceText.From(text), options);
    }

    /// <summary>Gets every diagnostic the parse produced, in source order.</summary>
    /// <returns>The document's diagnostics.</returns>
    public IEnumerable<MarkupDiagnostic> GetDiagnostics() => Diagnostics;

    /// <summary>Gets a value indicating whether the document parsed without errors.</summary>
    public bool IsWellFormed => !Diagnostics.Any(static diagnostic => diagnostic.IsError);

    /// <summary>Checks the document-level rules that only make sense once the tree exists.</summary>
    private static ImmutableArray<MarkupDiagnostic> Validate(
        ImmutableArray<XamlSyntaxNode> nodes,
        Uri? documentUri)
    {
        XamlElement[] roots = [.. nodes.OfType<XamlElement>()];

        if (roots.Length == 0)
        {
            // An empty or comment-only file. Reported, but the document is still usable.
            return
            [
                MarkupDiagnostic.Parse(
                    XamlDiagnosticCodes.MissingRootElement,
                    "The document has no root element.",
                    MarkupDiagnosticSeverity.Error,
                    documentUri,
                    new TextSpan(0, 0)),
            ];
        }

        if (roots.Length == 1)
        {
            return [];
        }

        return
        [
            .. roots.Skip(1).Select(extra => MarkupDiagnostic.Parse(
                XamlDiagnosticCodes.MultipleRootElements,
                $"The document has more than one root element; '{extra.Name}' is an extra one.",
                MarkupDiagnosticSeverity.Error,
                documentUri,
                extra.NameSpan)),
        ];
    }
}
