using System;
using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What a document is, how sure the answer is, and what it rests on.
/// </summary>
/// <remarks>
/// A result rather than an exception, like a load: a document naming a type nobody supplied is
/// ordinary, and the answer that can still be given is worth more than a refusal.
/// <see cref="IsResolved"/> says whether that answer rests on CLR types or only on names.
/// </remarks>
public sealed class XamlDocumentClassification
{
    internal XamlDocumentClassification(
        XamlDocumentKind kind,
        XamlElement? root,
        Type? rootType,
        bool isCustomRoot,
        ImmutableArray<XamlTemplatedType> templatedTypes,
        XamlElement? preview,
        bool isResolved,
        ImmutableArray<MarkupDiagnostic> diagnostics)
    {
        Kind = kind;
        Root = root;
        RootType = rootType;
        IsCustomRoot = isCustomRoot;
        TemplatedTypes = templatedTypes;
        Preview = preview;
        IsResolved = isResolved;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets what the document describes.</summary>
    public XamlDocumentKind Kind { get; }

    /// <summary>Gets the root element, or <see langword="null"/> when the document has none.</summary>
    public XamlElement? Root { get; }

    /// <summary>
    /// Gets the type of the root element, or <see langword="null"/> when it could not be
    /// resolved.
    /// </summary>
    /// <remarks>
    /// The element's type, not the <c>x:Class</c>: <c>&lt;Window x:Class="App.MainWindow"&gt;</c>
    /// has the root type <c>Window</c>, which is what decides the kind. The class derives from it
    /// and adds nothing a classification looks at.
    /// </remarks>
    public Type? RootType { get; }

    /// <summary>
    /// Gets a value indicating whether the root element is written outside Avalonia's own
    /// namespace — a control of the author's, or of a library's — rather than as one of the
    /// framework's types.
    /// </summary>
    /// <remarks>
    /// Independent of <see cref="Kind"/>: a root of the author's own base window is a
    /// <see cref="XamlDocumentKind.Window"/> with this set, and a root of the author's own control
    /// is a <see cref="XamlDocumentKind.Control"/> with this set. It is decided by the namespace
    /// the root is written in, so it is known whether or not the type resolved.
    /// </remarks>
    public bool IsCustomRoot { get; }

    /// <summary>
    /// Gets the controls written outside Avalonia's namespace whose <c>Template</c> the document
    /// sets — directly, or through a control theme in the document it is <c>BasedOn</c> — each
    /// once, in the order the document first does so. Empty unless the root is a set of styles or a
    /// resource dictionary.
    /// </summary>
    /// <remarks>
    /// Non-empty exactly when <see cref="Kind"/> is <see cref="XamlDocumentKind.TemplatedControl"/>.
    /// A template set inside <c>Design.PreviewWith</c> is not counted: the preview is a design-time
    /// stand-in, not part of what the document declares.
    /// </remarks>
    public ImmutableArray<XamlTemplatedType> TemplatedTypes { get; }

    /// <summary>
    /// Gets the element written inside the root's <c>Design.PreviewWith</c>, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// For a set of styles, a resource dictionary or a templated control's look, this is the one
    /// visual the document describes: its root has nothing to show by itself.
    /// </remarks>
    public XamlElement? Preview { get; }

    /// <summary>
    /// Gets a value indicating whether every type the kind depends on was resolved, so the kind
    /// rests on CLR types rather than on names.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> for <see cref="XamlDocumentKind.Unknown"/>, and for a templated
    /// control whose control the environment does not supply: the names say it is one, and the
    /// diagnostics say which type could not be checked.
    /// </remarks>
    public bool IsResolved { get; }

    /// <summary>
    /// Gets what stood in the way of a resolved answer: types and prefixes that did not resolve,
    /// located in the document.
    /// </summary>
    /// <remarks>
    /// Errors when the root could not be resolved and the kind is
    /// <see cref="XamlDocumentKind.Unknown"/>; warnings when the kind was still decided by names.
    /// </remarks>
    public ImmutableArray<MarkupDiagnostic> Diagnostics { get; }

    /// <summary>Returns the kind and the root type.</summary>
    /// <returns>A readable description of the classification.</returns>
    public override string ToString() =>
        RootType is null ? Kind.ToString() : $"{Kind} ({RootType.FullName})";
}
