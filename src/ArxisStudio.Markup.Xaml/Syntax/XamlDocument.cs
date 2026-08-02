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
    /// Gets the URI relative includes are resolved against, which is the document's own
    /// location unless the parse options said otherwise.
    /// </summary>
    public Uri? BaseUri { get; private init; }

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

        return new XamlDocument(text, options.DocumentUri, tokens, nodes, diagnostics)
        {
            BaseUri = options.BaseUri ?? options.DocumentUri,
        };
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

    /// <summary>
    /// Opens an editor for making several changes as one operation.
    /// </summary>
    /// <remarks>
    /// Batching matters: each change is computed against this document's spans, so applying
    /// them together is the only way for several to be expressed in terms of the same text.
    /// The single-edit methods on this type each open and apply an editor of their own.
    /// </remarks>
    /// <returns>The editor. This document is unaffected until <see cref="XamlDocumentEditor.Apply"/>.</returns>
    public XamlDocumentEditor Edit() => new(this);

    /// <summary>Sets an attribute's value, adding the attribute if the element does not have it.</summary>
    /// <param name="element">The element to change.</param>
    /// <param name="name">The attribute name as it should be written.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>A new document with the change applied.</returns>
    public XamlDocument SetAttribute(XamlElement element, XamlQualifiedName name, XamlValue value) =>
        Edit().SetAttribute(element, name, value).Apply();

    /// <summary>Sets an attribute to raw attribute text.</summary>
    /// <param name="element">The element to change.</param>
    /// <param name="name">The attribute name as it should be written.</param>
    /// <param name="text">The raw value text.</param>
    /// <returns>A new document with the change applied.</returns>
    public XamlDocument SetAttribute(XamlElement element, XamlQualifiedName name, string text) =>
        Edit().SetAttribute(element, name, text).Apply();

    /// <summary>Removes an attribute.</summary>
    /// <param name="element">The element to change.</param>
    /// <param name="name">The attribute name as written.</param>
    /// <returns>A new document with the change applied, or this one when the attribute is absent.</returns>
    public XamlDocument RemoveAttribute(XamlElement element, XamlQualifiedName name) =>
        Edit().RemoveAttribute(element, name).Apply();

    /// <summary>Inserts XAML as a child of an element.</summary>
    /// <param name="parent">The element to insert into.</param>
    /// <param name="index">The position among the parent's child elements.</param>
    /// <param name="xaml">The XAML text to insert.</param>
    /// <returns>A new document with the change applied.</returns>
    public XamlDocument InsertElement(XamlElement parent, int index, string xaml) =>
        Edit().InsertElement(parent, index, xaml).Apply();

    /// <summary>Removes an element.</summary>
    /// <param name="element">The element to remove.</param>
    /// <returns>A new document with the change applied.</returns>
    public XamlDocument RemoveElement(XamlElement element) =>
        Edit().RemoveElement(element).Apply();

    /// <summary>Replaces an element with other XAML, in place.</summary>
    /// <param name="element">The element to replace.</param>
    /// <param name="xaml">The XAML to put in its place.</param>
    /// <returns>A new document with the change applied.</returns>
    public XamlDocument ReplaceElement(XamlElement element, string xaml) =>
        Edit().ReplaceElement(element, xaml).Apply();

    /// <summary>Puts an element inside a new one, written where it was.</summary>
    /// <param name="element">The element to wrap.</param>
    /// <param name="wrapperXaml">The wrapper, as markup with somewhere to put content.</param>
    /// <returns>A new document with the change applied.</returns>
    public XamlDocument WrapElement(XamlElement element, string wrapperXaml) =>
        Edit().WrapElement(element, wrapperXaml).Apply();

    /// <summary>Replaces an element with what it contains.</summary>
    /// <param name="element">The element to unwrap.</param>
    /// <returns>A new document with the change applied.</returns>
    public XamlDocument UnwrapElement(XamlElement element) =>
        Edit().UnwrapElement(element).Apply();

    /// <summary>Moves an element to a position under a new parent.</summary>
    /// <param name="element">The element to move.</param>
    /// <param name="newParent">The element to move it under.</param>
    /// <param name="index">The position among the new parent's child elements.</param>
    /// <returns>A new document with the change applied.</returns>
    public XamlDocument MoveElement(XamlElement element, XamlElement newParent, int index) =>
        Edit().MoveElement(element, newParent, index).Apply();

    /// <summary>
    /// Writes the document out in the requested mode.
    /// </summary>
    /// <remarks>
    /// <see cref="XamlWriteMode.Preserve"/> is the default everywhere and reproduces an
    /// unchanged document byte for byte. <see cref="XamlWriteMode.Format"/> reflows the whole
    /// document and is never reached by accident — saving never needs it.
    /// </remarks>
    /// <param name="mode">How to write the document.</param>
    /// <param name="options">Formatting options, used only in <see cref="XamlWriteMode.Format"/>.</param>
    /// <returns>The document's text.</returns>
    public string GetText(XamlWriteMode mode, XamlFormattingOptions? options = null) =>
        mode == XamlWriteMode.Format
            ? XamlFormatter.Format(this, options ?? XamlFormattingOptions.Default)
            : GetText();

    /// <summary>Finds the resource and style includes this document declares, in source order.</summary>
    /// <returns>The references, with their sources resolved against <see cref="BaseUri"/>.</returns>
    public ImmutableArray<XamlResourceReference> GetResourceReferences() =>
        XamlResourceAnalyzer.Discover(this);

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
