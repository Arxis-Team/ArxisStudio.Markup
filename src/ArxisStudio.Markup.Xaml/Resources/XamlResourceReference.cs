using System;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// One document's declared dependency on another, as written in the source.
/// </summary>
/// <remarks>
/// A reference records both what the document said and what that resolves to. The two are kept
/// apart deliberately: <see cref="SourceText"/> is what has to be written back if the document
/// is saved, while <see cref="ResolvedUri"/> is what the graph is keyed by. Rewriting a
/// relative include as the absolute URI it happens to resolve to would be a change nobody
/// asked for.
/// </remarks>
public sealed class XamlResourceReference
{
    internal XamlResourceReference(
        XamlElement element,
        XamlResourceReferenceKind kind,
        string sourceText,
        Uri? resolvedUri)
    {
        Element = element;
        Kind = kind;
        SourceText = sourceText;
        ResolvedUri = resolvedUri;
    }

    /// <summary>Gets the element that declares the dependency.</summary>
    public XamlElement Element { get; }

    /// <summary>Gets which kind of include it is.</summary>
    public XamlResourceReferenceKind Kind { get; }

    /// <summary>Gets the <c>Source</c> attribute's text exactly as written.</summary>
    public string SourceText { get; }

    /// <summary>
    /// Gets the URI the source resolves to, or <see langword="null"/> when it could not be
    /// resolved.
    /// </summary>
    /// <remarks>
    /// Resolution is URI arithmetic and nothing else. Whether anything exists at the result is
    /// a question for a source provider, and a reference to a document that has not been
    /// written yet is perfectly ordinary in an editor.
    /// </remarks>
    public Uri? ResolvedUri { get; }

    /// <summary>Gets the range of the <c>Source</c> attribute's value, for diagnostics.</summary>
    public TextSpan Span => Element.GetAttribute("Source")?.ValueSpan ?? Element.Span;

    /// <summary>Returns the kind and the source text.</summary>
    /// <returns>A readable description of the reference.</returns>
    public override string ToString() => $"{Kind} '{SourceText}'";
}
