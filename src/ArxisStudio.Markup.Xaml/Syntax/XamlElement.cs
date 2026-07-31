using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// An element, covering its start tag, its content and its end tag.
/// </summary>
/// <remarks>
/// <para>
/// The start tag, content and end tag are tracked as separate ranges so an edit can target one
/// without disturbing the others — changing an attribute must not reformat the children, and
/// inserting a child must not touch the tag.
/// </para>
/// <para>
/// An element whose end tag is missing still has a span and still writes back as what the
/// source said. Malformed input produces a best-effort tree and a diagnostic, never an
/// exception and never discarded text.
/// </para>
/// </remarks>
public sealed class XamlElement : XamlSyntaxNode
{
    private readonly ImmutableArray<XamlAttribute> _attributes;
    private readonly ImmutableArray<XamlSyntaxNode> _content;

    internal XamlElement(
        TextSpan span,
        XamlQualifiedName name,
        TextSpan nameSpan,
        TextSpan startTagSpan,
        TextSpan? endTagSpan,
        XamlQualifiedName? endTagName,
        bool isEmpty,
        ImmutableArray<XamlAttribute> attributes,
        ImmutableArray<XamlSyntaxNode> content,
        XamlNamespaceContext namespaceContext,
        ImmutableArray<MarkupDiagnostic> diagnostics)
        : base(span, diagnostics)
    {
        Name = name;
        NameSpan = nameSpan;
        StartTagSpan = startTagSpan;
        EndTagSpan = endTagSpan;
        EndTagName = endTagName;
        IsEmpty = isEmpty;
        NamespaceContext = namespaceContext;
        _attributes = attributes;
        _content = content;

        AttachChildren([.. attributes.CastArray<XamlSyntaxNode>(), .. content]);
    }

    /// <summary>Gets the element's name as written in the start tag, prefix included.</summary>
    public XamlQualifiedName Name { get; }

    /// <summary>Gets the range of the name in the start tag.</summary>
    public TextSpan NameSpan { get; }

    /// <summary>Gets the range of the start tag, including its delimiters.</summary>
    public TextSpan StartTagSpan { get; }

    /// <summary>
    /// Gets the range of the end tag, or <see langword="null"/> when the element is
    /// self-closing or its end tag is missing.
    /// </summary>
    public TextSpan? EndTagSpan { get; }

    /// <summary>
    /// Gets the name written in the end tag, which differs from <see cref="Name"/> only when
    /// the source is malformed.
    /// </summary>
    public XamlQualifiedName? EndTagName { get; }

    /// <summary>Gets a value indicating whether the element was written self-closing, as <c>&lt;X /&gt;</c>.</summary>
    public bool IsEmpty { get; }

    /// <summary>Gets a value indicating whether the element was opened but never closed.</summary>
    public bool IsUnclosed => !IsEmpty && EndTagSpan is null;

    /// <summary>Gets the element's attributes in source order, namespace declarations included.</summary>
    public ImmutableArray<XamlAttribute> Attributes => _attributes;

    /// <summary>Gets the element's content nodes in source order.</summary>
    public ImmutableArray<XamlSyntaxNode> Content => _content;

    /// <summary>Gets the namespace declarations in scope inside this element.</summary>
    public XamlNamespaceContext NamespaceContext { get; }

    /// <summary>Gets the namespace declarations this element makes, in source order.</summary>
    public IEnumerable<XamlNamespaceDeclaration> NamespaceDeclarations =>
        _attributes.OfType<XamlNamespaceDeclaration>();

    /// <summary>Gets the element's child elements in source order.</summary>
    public IEnumerable<XamlElement> Elements => _content.OfType<XamlElement>();

    /// <summary>
    /// Gets the namespace URI this element's name resolves to, or <see langword="null"/> when
    /// its prefix is not in scope.
    /// </summary>
    public string? NamespaceUri => NamespaceContext.LookupNamespace(Name.Prefix);

    /// <summary>
    /// Gets a value indicating whether the element's name has the <c>Owner.Member</c> shape,
    /// as <c>&lt;Grid.RowDefinitions&gt;</c> does.
    /// </summary>
    /// <remarks>
    /// The shape is all this package can see. Whether the member is a collection, a content
    /// property, an attached property or something else needs CLR metadata, which belongs to
    /// the loader.
    /// </remarks>
    public bool IsPropertyElementSyntax => Name.IsDotted;

    /// <summary>Gets the owner part of a property-element name, or <see langword="null"/>.</summary>
    public string? OwnerName => Name.OwnerName;

    /// <summary>Gets the member part of a property-element name, or <see langword="null"/>.</summary>
    public string? MemberName => Name.MemberName;

    /// <summary>Gets the element's XAML-language directives, such as <c>x:Name</c>.</summary>
    public IEnumerable<XamlAttribute> Directives =>
        _attributes.Where(static attribute => attribute.IsDirective);

    /// <summary>Gets the element's design-time attributes, such as <c>d:DesignWidth</c>.</summary>
    public IEnumerable<XamlAttribute> DesignTimeAttributes =>
        _attributes.Where(static attribute => attribute.IsDesignTime);

    /// <summary>
    /// Reads a XAML-language directive's value.
    /// </summary>
    /// <remarks>
    /// Matched by namespace rather than prefix, so this finds <c>x:Name</c> in a document that
    /// spells the XAML namespace with any prefix at all.
    /// </remarks>
    /// <param name="localName">The directive's local name, such as <c>Name</c> or <c>Key</c>.</param>
    /// <returns>The directive's raw text, or <see langword="null"/> when the element has no such directive.</returns>
    public string? GetDirective(string localName) => GetDirectiveAttribute(localName)?.GetValueText();

    /// <summary>Finds a XAML-language directive.</summary>
    /// <param name="localName">The directive's local name.</param>
    /// <returns>The attribute, or <see langword="null"/>.</returns>
    public XamlAttribute? GetDirectiveAttribute(string localName) =>
        _attributes.FirstOrDefault(attribute =>
            attribute.IsDirective && string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal));

    /// <summary>Reads a design-time attribute's value.</summary>
    /// <param name="localName">The attribute's local name, such as <c>DesignWidth</c>.</param>
    /// <returns>The raw text, or <see langword="null"/> when the element has no such attribute.</returns>
    public string? GetDesignTimeAttribute(string localName) =>
        _attributes.FirstOrDefault(attribute =>
            attribute.IsDesignTime && string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))
        ?.GetValueText();

    /// <summary>Finds an attribute by its written name.</summary>
    /// <param name="name">The name as written, prefix included.</param>
    /// <returns>The first attribute with that name, or <see langword="null"/>.</returns>
    public XamlAttribute? GetAttribute(XamlQualifiedName name) =>
        _attributes.FirstOrDefault(attribute => attribute.Name == name);

    /// <summary>Finds an unprefixed attribute by its local name.</summary>
    /// <param name="localName">The local name.</param>
    /// <returns>The first unprefixed attribute with that name, or <see langword="null"/>.</returns>
    public XamlAttribute? GetAttribute(string localName) =>
        _attributes.FirstOrDefault(attribute => attribute.Name.IsUnprefixed(localName));

    /// <summary>Returns the element's name and span.</summary>
    /// <returns>A readable description of the element.</returns>
    public override string ToString() => $"{nameof(XamlElement)} {Name}{Span}";
}
