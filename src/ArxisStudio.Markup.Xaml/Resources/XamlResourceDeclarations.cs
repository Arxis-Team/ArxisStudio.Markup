using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Finds the keyed resources a document writes, and those an element can reach by key.
/// </summary>
/// <remarks>
/// <para>
/// Discovery is syntactic, like the includes (<see cref="XamlResourceAnalyzer"/>). A dictionary is a
/// <c>Resources</c> property element or an element called <c>ResourceDictionary</c> — a subclass under another name
/// is not recognised, because recognising one would take a type resolved. A key on an element anywhere else is not
/// an entry.
/// </para>
/// <para>
/// <b>Reach is the element and its ancestors</b> — and the styles they write — whatever the order of the text.
/// That is what Avalonia's compiled markup does: a <c>StaticResource</c> finds the element's own resources, an
/// ancestor's written after the use, and a key written later in the same dictionary, and does not find a
/// sibling's. A resource a theme dictionary alone holds is in reach too, but a <c>StaticResource</c> reads it only
/// where a theme is already known when the element is built; that is the loader's to decide, not the text's.
/// </para>
/// </remarks>
public static class XamlResourceDeclarations
{
    private const string Dictionary = "ResourceDictionary";
    private const string ResourcesMember = "Resources";
    private const string MergedMember = "MergedDictionaries";
    private const string ThemeMember = "ThemeDictionaries";
    private const string StylesMember = "Styles";

    /// <summary>Finds every keyed resource a document writes, in source order.</summary>
    /// <param name="document">The document to scan.</param>
    /// <returns>The entries, each with the element whose resources it belongs to.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static ImmutableArray<XamlResourceDeclaration> Discover(XamlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        ImmutableArray<XamlResourceDeclaration>.Builder declarations = ImmutableArray.CreateBuilder<XamlResourceDeclaration>();

        foreach (XamlElement element in document.DescendantElements())
        {
            if (TryRead(element, out XamlResourceDeclaration? declaration))
            {
                declarations.Add(declaration);
            }
        }

        return declarations.ToImmutable();
    }

    /// <summary>
    /// Finds the resources an element can reach by key in its own document: its own, then each ancestor's, nearest
    /// first, with the resources of the styles each of them writes.
    /// </summary>
    /// <param name="element">The element whose reach is asked about.</param>
    /// <returns>The entries in reach, nearest owner first and in source order within one owner.</returns>
    /// <remarks>
    /// The same key can be in reach twice — an ancestor redefining what the application declares, a theme
    /// dictionary per variant. The first one for a key and theme is the one a lookup finds.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static ImmutableArray<XamlResourceDeclaration> InScope(XamlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        ImmutableArray<XamlResourceDeclaration> all = Discover(element.Document);

        if (all.IsEmpty)
        {
            return [];
        }

        ImmutableArray<XamlResourceDeclaration>.Builder reached = ImmutableArray.CreateBuilder<XamlResourceDeclaration>();

        for (XamlElement? at = element.IsPropertyElementSyntax ? OwnerOf(element) : element; at is not null; at = OwnerOf(at))
        {
            foreach (XamlResourceDeclaration declaration in all)
            {
                if (ReferenceEquals(declaration.Scope, at))
                {
                    reached.Add(declaration);
                }
            }
        }

        return reached.ToImmutable();
    }

    /// <summary>Reads an element as a dictionary entry: an <c>x:Key</c> inside a dictionary.</summary>
    private static bool TryRead(XamlElement element, [NotNullWhen(true)] out XamlResourceDeclaration? declaration)
    {
        declaration = null;

        if (element.IsPropertyElementSyntax || element.GetDirectiveAttribute(XamlDirectives.Key) is not { } key)
        {
            return false;
        }

        // A dictionary merged in or standing for a theme is not an entry: its key, where it has one, is a variant.
        if (element.Parent is XamlElement { IsPropertyElementSyntax: true, MemberName: MergedMember or ThemeMember })
        {
            return false;
        }

        string? theme = null;
        XamlElement current = element;

        while (current.Parent is XamlElement parent)
        {
            if (parent.IsPropertyElementSyntax)
            {
                switch (parent.MemberName)
                {
                    case ResourcesMember when parent.Parent is XamlElement owner:
                        declaration = new XamlResourceDeclaration(element, key, owner, ScopeOf(owner), theme);

                        return true;

                    // Only a dictionary sits in these, and never the entry itself (left out above): the walk came up
                    // through one.
                    case MergedMember or ThemeMember when IsDictionary(current):
                        current = parent;

                        continue;

                    default:
                        return false;
                }
            }

            if (!IsDictionary(parent))
            {
                return false;
            }

            // A theme dictionary's key names the variant its entries are for; the nearest one decides.
            if (theme is null
                && parent.Parent is XamlElement { IsPropertyElementSyntax: true, MemberName: ThemeMember }
                && parent.GetDirectiveAttribute(XamlDirectives.Key) is { } variant)
            {
                theme = variant.GetValue() is XamlLiteralValue literal ? literal.Text : variant.GetValueText();
            }

            current = parent;
        }

        // Up to the root through dictionaries alone: the entries of a dictionary document.
        if (!ReferenceEquals(current, element) && IsDictionary(current))
        {
            declaration = new XamlResourceDeclaration(element, key, current, ScopeOf(current), theme);

            return true;
        }

        return false;
    }

    /// <summary>
    /// The element whose reach an owner's entries are in: the owner itself, or for a style its host — unless the style
    /// is the document's root, which everything in the document is inside.
    /// </summary>
    private static XamlElement? ScopeOf(XamlElement owner) =>
        IsStyle(owner) && owner.Parent is XamlElement ? StyleHost(owner) : owner;

    /// <summary>
    /// The element whose <c>Styles</c> a style is written in — through the styles it is nested in — or
    /// <see langword="null"/> for an owner that is no style, or one written anywhere else.
    /// </summary>
    private static XamlElement? StyleHost(XamlElement owner)
    {
        if (!IsStyle(owner))
        {
            return null;
        }

        for (XamlElement? at = owner.Parent as XamlElement; at is not null; at = at.Parent as XamlElement)
        {
            if (at.IsPropertyElementSyntax)
            {
                if (string.Equals(at.MemberName, StylesMember, StringComparison.Ordinal))
                {
                    return at.Parent as XamlElement;
                }

                // A style's own children go in Children; anything else is not where styles apply from.
                if (!string.Equals(at.MemberName, "Children", StringComparison.Ordinal))
                {
                    return null;
                }

                continue;
            }

            if (!IsStyle(at))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>The element an element belongs to: its parent, past a property element it is written in.</summary>
    private static XamlElement? OwnerOf(XamlElement element)
    {
        for (XamlSyntaxNode? at = element.Parent; at is not null; at = at.Parent)
        {
            if (at is XamlElement { IsPropertyElementSyntax: false } owner)
            {
                return owner;
            }
        }

        return null;
    }

    private static bool IsDictionary(XamlElement element) =>
        !element.IsPropertyElementSyntax && string.Equals(element.Name.LocalName, Dictionary, StringComparison.Ordinal);

    private static bool IsStyle(XamlElement element) =>
        !element.IsPropertyElementSyntax && element.Name.LocalName is "Style" or "Styles" or "ControlTheme";
}
