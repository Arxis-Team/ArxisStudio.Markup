using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Works out what changed between two versions of a document, and how much each change costs.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is over the syntax tree rather than the text, which is the point of having a
/// lossless one: reindenting a file, adding a comment or reflowing an attribute across two lines
/// changes every offset in it and changes nothing about the objects it describes.
/// </para>
/// <para>
/// It is deliberately conservative. Elements are paired by position among their siblings and
/// nothing is matched across a move, so a reordering reads as a structural change rather than as
/// a clever minimal edit — being wrong about that would leave a control holding a value from the
/// element that used to be in its place.
/// </para>
/// </remarks>
internal static class XamlDocumentDiff
{
    /// <summary>Element names that decide how a change inside them has to be applied.</summary>
    private static readonly (string Name, XamlUpdateStrategy Strategy)[] Containers =
    [
        ("Style", XamlUpdateStrategy.ReloadStyle),
        ("Styles", XamlUpdateStrategy.ReloadStyle),
        ("ControlTheme", XamlUpdateStrategy.ReloadTheme),
        ("ControlTemplate", XamlUpdateStrategy.ReloadTemplate),
        ("DataTemplate", XamlUpdateStrategy.ReloadTemplate),
    ];

    /// <summary>Compares the document a session loaded with the one it is being given.</summary>
    /// <param name="loaded">The document the objects were built from.</param>
    /// <param name="updated">The document to bring them in line with.</param>
    /// <returns>The changes, in document order.</returns>
    internal static ImmutableArray<XamlDocumentChange> Compare(XamlDocument loaded, XamlDocument updated)
    {
        var changes = ImmutableArray.CreateBuilder<XamlDocumentChange>();

        if (loaded.Root is not { } before || updated.Root is not { } after)
        {
            // A document with no root produced no tree, and one that has gained a root has to
            // build one. Neither is an in-place update of anything.
            changes.Add(new XamlDocumentChange(XamlUpdateStrategy.RecreateSession, loaded.Root, updated.Root, null));

            return changes.ToImmutable();
        }

        // x:Class decides which object the document populates, and the session was handed one
        // before the load began. Changing it is a different session, not a different value.
        if (before.Name != after.Name
            || !string.Equals(before.GetDirective("Class"), after.GetDirective("Class"), StringComparison.Ordinal))
        {
            changes.Add(new XamlDocumentChange(XamlUpdateStrategy.RecreateSession, before, after, null));

            return changes.ToImmutable();
        }

        CompareElements(before, after, changes);

        return
        [
            .. changes
                .SelectMany(change => Consumers(change, loaded, updated))

                // Rebuilding the root object would leave nowhere to put it: the caller holds it,
                // and a session is built around it. That is the one case a new session is for.
                .Select(change => change.ReplacesObject && ReferenceEquals(change.OldElement, before)
                    ? new XamlDocumentChange(XamlUpdateStrategy.RecreateSession, before, after, null)
                    : change)

                // Smallest first, so a resource is in place before anything that reads it is
                // rebuilt from a document that expects the new value.
                .OrderBy(static change => change.Strategy),
        ];
    }

    /// <summary>
    /// Adds the elements that read a changed resource with <c>StaticResource</c> to the changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A static reference is resolved once, while the object is being built, so replacing the
    /// dictionary entry does nothing for anything already holding the old value. Whoever reads it
    /// has to be built again — which is the contract's separate row for a static resource, and
    /// the difference between an update that visibly works and one that quietly does not.
    /// </para>
    /// <para>
    /// What is rebuilt is the element that owns the dictionary, not the reader. A reader built on
    /// its own has no dictionary to read: a static reference is resolved against the resources in
    /// scope where the markup sits, and the smallest piece of markup that still has them is the
    /// one that declares them.
    /// </para>
    /// </remarks>
    private static IEnumerable<XamlDocumentChange> Consumers(
        XamlDocumentChange change,
        XamlDocument loaded,
        XamlDocument updated)
    {
        yield return change;

        if (change.Strategy != XamlUpdateStrategy.ReplaceResource
            || change.OldElement is not { } keyed
            || keyed.GetDirective("Key") is not { } key
            || !loaded.DescendantElements().Any(element => ReadsStatically(element, key)))
        {
            yield break;
        }

        int depth = 0;

        foreach (XamlElement ancestor in keyed.AncestorsAndSelf().OfType<XamlElement>())
        {
            // Past the dictionary and the Resources it hangs off, to whatever declares them.
            if (depth > 0
                && !ancestor.IsPropertyElementSyntax
                && !string.Equals(ancestor.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal))
            {
                if (Ancestor(change.NewElement ?? keyed, depth) is { } after)
                {
                    yield return Structural(ancestor, after, replacesObject: false);
                }

                yield break;
            }

            depth++;
        }
    }

    /// <summary>Reports whether any of an element's attributes reads a key with <c>StaticResource</c>.</summary>
    private static bool ReadsStatically(XamlElement element, string key) =>
        element.Attributes.Any(attribute =>
            attribute.GetValue() is XamlMarkupExtensionValue extension
            && string.Equals(extension.TypeName.LocalName, "StaticResource", StringComparison.Ordinal)
            && extension.PositionalArguments.Any(argument =>
                string.Equals(argument.Value.ToXamlText(), key, StringComparison.Ordinal)));

    /// <summary>Gets the largest strategy a set of changes calls for.</summary>
    internal static XamlUpdateStrategy Largest(ImmutableArray<XamlDocumentChange> changes) =>
        changes.IsEmpty
            ? XamlUpdateStrategy.None
            : changes.Max(static change => change.Strategy);

    private static void CompareElements(
        XamlElement before,
        XamlElement after,
        ImmutableArray<XamlDocumentChange>.Builder changes)
    {
        if (before.Name != after.Name)
        {
            changes.Add(Structural(before, after));

            return;
        }

        CompareAttributes(before, after, changes);

        XamlElement[] beforeChildren = [.. before.Elements];
        XamlElement[] afterChildren = [.. after.Elements];

        // A child added, removed or reordered changes what has to exist, not what a property
        // holds. Pairing survivors across such a change is exactly the cleverness that gets a
        // control the value of whatever used to sit in its place.
        if (beforeChildren.Length != afterChildren.Length
            || !SignificantText(before).SequenceEqual(SignificantText(after), StringComparer.Ordinal))
        {
            // The element is still the same element; only what is inside it differs. Rebuilding
            // its content leaves the object itself, and everything holding a reference to it,
            // alone — and works at the root, where there is no slot to put a new object into.
            changes.Add(Structural(before, after, replacesObject: false));

            return;
        }

        for (int index = 0; index < beforeChildren.Length; index++)
        {
            CompareElements(beforeChildren[index], afterChildren[index], changes);
        }
    }

    private static void CompareAttributes(
        XamlElement before,
        XamlElement after,
        ImmutableArray<XamlDocumentChange>.Builder changes)
    {
        foreach (XamlAttribute attribute in Meaningful(after))
        {
            XamlAttribute? previous = before.GetAttribute(attribute.Name);

            if (previous is null)
            {
                changes.Add(Classify(before, after, attribute, isRemoval: false));
            }
            else if (!string.Equals(previous.GetValueText(), attribute.GetValueText(), StringComparison.Ordinal))
            {
                changes.Add(Classify(before, after, attribute, isRemoval: false));
            }
        }

        foreach (XamlAttribute attribute in Meaningful(before))
        {
            if (after.GetAttribute(attribute.Name) is null)
            {
                // Removing an attribute is not the same as setting its default: a style, a
                // theme or an inherited value may be what it was covering up.
                changes.Add(Classify(before, after, attribute, isRemoval: true));
            }
        }
    }

    /// <summary>Decides what applying one attribute's change takes.</summary>
    private static XamlDocumentChange Classify(
        XamlElement before,
        XamlElement after,
        XamlAttribute attribute,
        bool isRemoval)
    {
        string name = attribute.Name.LocalName;

        if (attribute.IsDesignTime)
        {
            // A design value written as an expression was evaluated by the load, and a removed
            // one has to be undone rather than overwritten. Neither is something re-applying
            // the document's design values can do, so both are worth rebuilding for.
            return isRemoval || attribute.GetValue() is not XamlLiteralValue
                ? Structural(before, after)
                : new XamlDocumentChange(XamlUpdateStrategy.UpdateDesignValue, before, after, name);
        }

        // A directive is not a value on an object. x:Key decides where a resource lives and
        // x:Name what the tree calls it; both are decided while the objects are being built.
        if (attribute.IsDirective || attribute.IsMarkupCompatibility)
        {
            return Structural(before, after);
        }

        if (Container(before) is { } container)
        {
            // A style, a theme, a template or a resource is rebuilt whole and put back in the
            // collection it came from; there is no "content" of one that means anything alone.
            return new XamlDocumentChange(
                container.Strategy,
                container.Element,
                Ancestor(after, container.Depth),
                null)
            {
                ReplacesObject = true,
            };
        }

        // An expression is resolved while objects are built — a binding needs its source, a
        // static resource its dictionary — so there is nothing to set and the element is rebuilt.
        return isRemoval || attribute.GetValue() is not XamlLiteralValue
            ? Structural(before, after)
            : new XamlDocumentChange(XamlUpdateStrategy.SetProperty, before, after, name);
    }

    /// <summary>
    /// Finds the outermost style, theme, template or keyed resource the element sits in.
    /// </summary>
    /// <remarks>
    /// Outermost rather than innermost, because a template written inside a style's setter is
    /// part of that style and cannot be replaced without it. The outermost container is the
    /// smallest thing that can actually be rebuilt on its own.
    /// </remarks>
    private static (XamlElement Element, XamlUpdateStrategy Strategy, int Depth)? Container(XamlElement element)
    {
        (XamlElement Element, XamlUpdateStrategy Strategy, int Depth)? found = null;
        int depth = 0;

        foreach (XamlElement ancestor in element.AncestorsAndSelf().OfType<XamlElement>())
        {
            XamlUpdateStrategy? strategy = null;

            foreach ((string name, XamlUpdateStrategy candidate) in Containers)
            {
                if (string.Equals(ancestor.Name.LocalName, name, StringComparison.Ordinal))
                {
                    strategy = candidate;
                }
            }

            // A keyed element is an entry of a dictionary, so it can be replaced by its key —
            // unless it is one of the above, which say something more specific about it.
            strategy ??= ancestor.GetDirective("Key") is not null
                ? XamlUpdateStrategy.ReplaceResource
                : null;

            if (strategy is { } value)
            {
                found = (ancestor, value, depth);
            }

            depth++;
        }

        return found;
    }

    /// <summary>
    /// Climbs the same number of levels in the new tree as the container sat above the change.
    /// </summary>
    /// <remarks>
    /// Sound precisely because the comparison stops at the first structural difference: as far
    /// as it got, the two trees have the same shape, so the same number of steps upward reaches
    /// the element standing in the same place.
    /// </remarks>
    private static XamlElement? Ancestor(XamlElement element, int levels)
    {
        XamlSyntaxNode? node = element;

        for (int step = 0; step < levels && node is not null; step++)
        {
            node = node.Parent;
        }

        return node as XamlElement;
    }

    private static XamlDocumentChange Structural(
        XamlElement before,
        XamlElement after,
        bool replacesObject = true) =>
        new(XamlUpdateStrategy.ReloadSubtree, before, after, null) { ReplacesObject = replacesObject };

    /// <summary>Gets the attributes that can make a difference to an object.</summary>
    private static IEnumerable<XamlAttribute> Meaningful(XamlElement element) =>
        element.Attributes.Where(static attribute => attribute is not XamlNamespaceDeclaration);

    /// <summary>
    /// Gets the element's own text content, with whitespace-only runs left out.
    /// </summary>
    /// <remarks>
    /// Indentation between child elements is text as far as the tree is concerned and means
    /// nothing to the objects. Text that is not only whitespace is a value, and a changed one is
    /// a changed object.
    /// </remarks>
    private static IEnumerable<string> SignificantText(XamlElement element) =>
        element.Content
            .Where(static node => node is XamlText or XamlCData)
            .Select(static node => node.GetSourceText().Trim())
            .Where(static text => text.Length > 0);
}
