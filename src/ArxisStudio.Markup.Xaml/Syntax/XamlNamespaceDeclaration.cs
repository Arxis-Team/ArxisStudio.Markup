using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// An <c>xmlns</c> attribute, which binds a prefix — or the default namespace — to a URI.
/// </summary>
/// <remarks>
/// A namespace declaration is an attribute, and is kept in the element's attribute list in
/// source order so that writing the tag back reproduces it. It is given its own type because
/// what it does is structural: everything else in the document is named relative to it.
/// </remarks>
public sealed class XamlNamespaceDeclaration : XamlAttribute
{
    internal XamlNamespaceDeclaration(
        TextSpan span,
        XamlQualifiedName name,
        TextSpan nameSpan,
        TextSpan? valueSpan,
        char? quote,
        string? prefix,
        ImmutableArray<MarkupDiagnostic> diagnostics)
        : base(span, name, nameSpan, valueSpan, quote, diagnostics) => Prefix = prefix;

    /// <summary>
    /// Gets the prefix this declaration binds, or <see langword="null"/> when it binds the
    /// default namespace.
    /// </summary>
    public string? Prefix { get; }

    /// <summary>Gets a value indicating whether this declaration binds the default namespace.</summary>
    public bool IsDefault => Prefix is null;

    /// <summary>Gets the namespace URI as written.</summary>
    /// <returns>
    /// The URI, or an empty string when the declaration undeclares the namespace or the source
    /// is malformed.
    /// </returns>
    public string GetNamespaceUri() => GetValueText();
}
