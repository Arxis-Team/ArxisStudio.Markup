using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Finds the styles and control themes one document declares, and what each applies to.
/// </summary>
/// <remarks>
/// <para>
/// Discovery is syntactic, like <see cref="XamlResourceAnalyzer"/>'s. An element is a style
/// because of what it is called, and a type it applies to is a name in its <c>Selector</c> or its
/// <c>TargetType</c> — nothing here resolves a type, and nothing needs Avalonia. Which CLR types
/// those names mean, and what follows from them, is the loader's to say.
/// </para>
/// <para>
/// Declarations are found wherever they appear — in a <c>Styles</c> root, in a control's
/// <c>Styles</c>, in merged and theme dictionaries, nested in one another — because the search is
/// over every descendant rather than over a fixed set of expected positions. The namespace of an
/// element is not checked, for the reason <see cref="XamlResourceAnalyzer"/> gives.
/// </para>
/// </remarks>
public static class XamlStyleAnalyzer
{
    private const string SelectorAttribute = "Selector";
    private const string TargetTypeAttribute = "TargetType";
    private const string SetterElement = "Setter";
    private const string SettersMember = "Setters";
    private const string ChildrenMember = "Children";

    /// <summary>Finds every style and control theme a document declares, in source order.</summary>
    /// <remarks>
    /// A parent comes before the declarations nested in it, so a caller walking the result in
    /// order has always seen a declaration's <see cref="XamlStyleDeclaration.Parent"/> already.
    /// </remarks>
    /// <param name="document">The document to scan.</param>
    /// <returns>The declarations.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static ImmutableArray<XamlStyleDeclaration> Discover(XamlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        ImmutableArray<XamlStyleDeclaration>.Builder declarations =
            ImmutableArray.CreateBuilder<XamlStyleDeclaration>();

        var declared = new Dictionary<XamlElement, XamlStyleDeclaration>();

        // Depth-first in source order, so a parent is always declared before what it nests.
        foreach (XamlElement element in document.DescendantElements())
        {
            if (!TryGetKind(element, out XamlStyleKind kind))
            {
                continue;
            }

            XamlStyleDeclaration? parent = ParentOf(element, declared);
            XamlAttribute? selector = kind == XamlStyleKind.Style ? element.GetAttribute(SelectorAttribute) : null;

            ImmutableArray<XamlTypeReference> targets = kind == XamlStyleKind.ControlTheme
                ? TargetTypeOf(element)
                : SelectorTargetsOf(element, selector, parent);

            var declaration = new XamlStyleDeclaration(
                element,
                kind,
                parent,
                selector is { HasValue: true } ? selector.GetValueText() : null,
                targets,
                SettersOf(element));

            declared.Add(element, declaration);
            declarations.Add(declaration);
        }

        return declarations.ToImmutable();
    }

    /// <summary>Decides whether an element is a style declaration, by its local name alone.</summary>
    /// <remarks>
    /// A property element never matches: its local name is the whole <c>Style.Setters</c>.
    /// </remarks>
    private static bool TryGetKind(XamlElement element, out XamlStyleKind kind)
    {
        switch (element.Name.LocalName)
        {
            case "Style":
                kind = XamlStyleKind.Style;

                return true;

            case "ControlTheme":
                kind = XamlStyleKind.ControlTheme;

                return true;

            default:
                kind = default;

                return false;
        }
    }

    /// <summary>
    /// Finds the declaration an element is nested in: written directly inside it, or inside its
    /// <c>Children</c>. Anywhere else — its <c>Resources</c>, a template it sets — is not nesting.
    /// </summary>
    private static XamlStyleDeclaration? ParentOf(
        XamlElement element,
        Dictionary<XamlElement, XamlStyleDeclaration> declared)
    {
        XamlElement? container = element.Parent as XamlElement;

        if (container is { IsPropertyElementSyntax: true }
            && string.Equals(container.MemberName, ChildrenMember, StringComparison.Ordinal))
        {
            container = container.Parent as XamlElement;
        }

        return container is not null && declared.TryGetValue(container, out XamlStyleDeclaration? parent)
            ? parent
            : null;
    }

    private static ImmutableArray<XamlTypeReference> SelectorTargetsOf(
        XamlElement element,
        XamlAttribute? selector,
        XamlStyleDeclaration? parent)
    {
        // A selector computed by a markup extension is decided when the document runs, and its
        // text says nothing about what it will select.
        if (selector is not { ValueSpan: { } span } || selector.GetValue() is not XamlLiteralValue)
        {
            return [];
        }

        return XamlSelectorReader.ReadTargets(
            selector.GetValueText(),
            span.Start,
            element.NamespaceContext,
            parent?.Targets ?? []);
    }

    /// <summary>
    /// Reads a control theme's <c>TargetType</c>, written as a name or as <c>{x:Type name}</c>.
    /// </summary>
    private static ImmutableArray<XamlTypeReference> TargetTypeOf(XamlElement element)
    {
        if (element.GetAttribute(TargetTypeAttribute) is not { ValueSpan: { } span } attribute)
        {
            return [];
        }

        string raw = attribute.GetValueText();

        switch (attribute.GetValue())
        {
            case XamlLiteralValue:
                return TypeReferenceAt(element, raw, span.Start, 0, raw.Length);

            case XamlMarkupExtensionValue extension when IsTypeExtension(extension, element):
                if ((extension.PositionalArguments.FirstOrDefault() ?? extension.GetArgument("TypeName"))
                    ?.Value is not XamlLiteralValue name)
                {
                    return [];
                }

                // The arguments carry no positions of their own. The name is the first place its
                // text occurs after the extension's own name, which is where any argument starts.
                int after = raw.IndexOf(extension.TypeName.LocalName, StringComparison.Ordinal);
                int at = after < 0
                    ? -1
                    : raw.IndexOf(name.Text, after + extension.TypeName.LocalName.Length, StringComparison.Ordinal);

                return at < 0 ? [] : TypeReferenceAt(element, raw, span.Start, at, at + name.Text.Length);

            default:
                return [];
        }
    }

    private static bool IsTypeExtension(XamlMarkupExtensionValue extension, XamlElement element) =>
        extension.TypeName.LocalName is "Type" or "TypeExtension"
        && string.Equals(
            element.NamespaceContext.LookupNamespace(extension.TypeName.Prefix),
            XamlNamespaces.Xaml,
            StringComparison.Ordinal);

    /// <summary>Reads <c>prefix:Type</c> from a stretch of an attribute's raw text.</summary>
    private static ImmutableArray<XamlTypeReference> TypeReferenceAt(
        XamlElement element,
        string raw,
        int origin,
        int start,
        int end)
    {
        while (start < end && char.IsWhiteSpace(raw[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(raw[end - 1]))
        {
            end--;
        }

        var name = XamlQualifiedName.Parse(raw[start..end]);

        if (name.LocalName.Length == 0 || name.LocalName.Any(char.IsWhiteSpace))
        {
            return [];
        }

        return
        [
            new XamlTypeReference(
                name,
                element.NamespaceContext.LookupNamespace(name.Prefix),
                TextSpan.FromBounds(origin + start, origin + end)),
        ];
    }

    /// <summary>
    /// Collects the setters a declaration carries itself: those written directly inside it and
    /// those inside its <c>Setters</c>. A nested style's setters are that style's.
    /// </summary>
    private static ImmutableArray<XamlStyleSetter> SettersOf(XamlElement element)
    {
        ImmutableArray<XamlStyleSetter>.Builder setters = ImmutableArray.CreateBuilder<XamlStyleSetter>();

        foreach (XamlElement child in element.Elements)
        {
            if (IsSetter(child))
            {
                setters.Add(new XamlStyleSetter(child));
            }
            else if (child.IsPropertyElementSyntax
                && string.Equals(child.MemberName, SettersMember, StringComparison.Ordinal))
            {
                setters.AddRange(child.ContentElements.Where(IsSetter).Select(static setter => new XamlStyleSetter(setter)));
            }
        }

        return setters.ToImmutable();
    }

    private static bool IsSetter(XamlElement element) =>
        string.Equals(element.Name.LocalName, SetterElement, StringComparison.Ordinal);
}
