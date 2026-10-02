namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// A type named inside an attribute value — a style's selector, a control theme's
/// <c>TargetType</c> — read for what it says and left unresolved.
/// </summary>
/// <remarks>
/// <para>
/// A selector writes a prefixed type as <c>prefix|Type</c> and an attribute writes it as
/// <c>prefix:Type</c>. Both arrive here as the same <see cref="Name"/>, so a caller compares what
/// was meant rather than how it was spelled; <see cref="Span"/> keeps where it was spelled.
/// </para>
/// <para>
/// Which CLR type the name means is a question for the loader, which has the assemblies. This
/// package resolves the prefix and stops there: <see cref="NamespaceUri"/> is what the prefix
/// means where the name is written, and together with the local name it is everything a type
/// resolver asks for.
/// </para>
/// </remarks>
public sealed class XamlTypeReference
{
    internal XamlTypeReference(XamlQualifiedName name, string? namespaceUri, TextSpan span)
    {
        Name = name;
        NamespaceUri = namespaceUri;
        Span = span;
    }

    /// <summary>Gets the prefix and local name, as written.</summary>
    public XamlQualifiedName Name { get; }

    /// <summary>
    /// Gets the namespace the name is in where it is written — the default namespace for an
    /// unprefixed name — or <see langword="null"/> when nothing in scope declares its prefix.
    /// </summary>
    public string? NamespaceUri { get; }

    /// <summary>Gets the range the name occupies in the document, prefix included.</summary>
    public TextSpan Span { get; }

    /// <summary>Returns the name in <c>prefix:Type</c> form.</summary>
    /// <returns>The name as an attribute would write it.</returns>
    public override string ToString() => Name.ToString();
}
