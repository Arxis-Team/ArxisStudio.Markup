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
/// Elements are paired by the identity their author declared — <c>x:Name</c>, or <c>Name</c> —
/// and by position among their siblings where no name decides. See
/// <see cref="XamlElementIdentity"/> for exactly when a name is allowed to; where it is not, this
/// stays deliberately conservative, because being wrong about which element is which would leave
/// a control holding a value from the element that used to be in its place.
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
                .Select(Widened)

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

    /// <summary>Reports whether an element reads a key with <c>StaticResource</c>.</summary>
    private static bool ReadsStatically(XamlElement element, string key) =>
        KeysReadBy(element).Contains(key, StringComparer.Ordinal);

    /// <summary>
    /// Reports whether two elements standing in the same place describe different objects.
    /// </summary>
    /// <remarks>
    /// The same comparison as the one between documents, asked of one pair of elements: by tree
    /// rather than by text, so an element that was only reindented is the same element.
    /// </remarks>
    /// <param name="before">The element as the objects were built from it.</param>
    /// <param name="after">The element standing in its place now.</param>
    /// <returns><see langword="true"/> when anything that affects an object differs.</returns>
    internal static bool Differ(XamlElement before, XamlElement after)
    {
        var changes = ImmutableArray.CreateBuilder<XamlDocumentChange>();

        CompareElements(before, after, changes);

        return changes.Count > 0;
    }

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

        // A child added or removed changes what has to exist, not what a property holds.
        if (beforeChildren.Length != afterChildren.Length
            || !SignificantText(before).SequenceEqual(SignificantText(after), StringComparer.Ordinal))
        {
            // The element is still the same element; only what is inside it differs. Rebuilding
            // its content leaves the object itself, and everything holding a reference to it,
            // alone — and works at the root, where there is no slot to put a new object into.
            changes.Add(Structural(before, after, replacesObject: false));

            return;
        }

        XamlElementPairing? pairing = XamlElementIdentity.Pair(beforeChildren, afterChildren);

        // Nothing names these children, so the only thing that says which is which is where each
        // one sits. A move is then indistinguishable from two elements having swapped contents,
        // and the conservative reading is the one that cannot put a value on the wrong object.
        if (pairing is null)
        {
            for (int index = 0; index < beforeChildren.Length; index++)
            {
                CompareElements(beforeChildren[index], afterChildren[index], changes);
            }

            return;
        }

        if (XamlElementIdentity.Moved(beforeChildren, pairing))
        {
            changes.Add(Reorder(before, after));
        }

        foreach ((XamlElement child, XamlElement updated) in pairing.All)
        {
            CompareElements(child, updated, changes);
        }
    }

    /// <summary>Describes named siblings having changed places.</summary>
    /// <remarks>
    /// Inside a style, a theme or a template there is no collection of live objects to move
    /// anything around in — what is there is rebuilt whole and put back — so the container has
    /// the last word, exactly as it does for every other kind of change.
    /// </remarks>
    private static XamlDocumentChange Reorder(XamlElement before, XamlElement after)
    {
        if (Container(before) is { } container)
        {
            return new XamlDocumentChange(
                container.Strategy, container.Element, Ancestor(after, container.Depth), null)
            {
                ReplacesObject = true,
            };
        }

        return new XamlDocumentChange(XamlUpdateStrategy.ReorderChildren, before, after, null);
    }

    private static void CompareAttributes(
        XamlElement before,
        XamlElement after,
        ImmutableArray<XamlDocumentChange>.Builder changes)
    {
        foreach (XamlAttribute attribute in Meaningful(after))
        {
            XamlAttribute? previous = before.GetAttribute(attribute.Name);

            if (previous is not null
                && string.Equals(previous.GetValueText(), attribute.GetValueText(), StringComparison.Ordinal))
            {
                continue;
            }

            if (IsIgnorable(attribute) && OnlyGrows(previous, attribute, before, after))
            {
                continue;
            }

            changes.Add(Classify(before, after, attribute, isRemoval: false));
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

        // A value taken out is cleared where it stands — which a load can only express by building
        // the element without it — as long as the member is one with a local value of its own to
        // clear; the session finds that out, and rebuilds when it is not.
        if (isRemoval)
        {
            return new XamlDocumentChange(XamlUpdateStrategy.ClearProperty, before, after, name);
        }

        // An expression is resolved while objects are built — a static resource needs its
        // dictionary, a converter is a resource — and is rebuilt; the few a session can evaluate
        // with nothing to build are set where the property stands. Whether this one can is decided
        // against the object's member, which the syntax cannot see.
        return attribute.GetValue() switch
        {
            XamlLiteralValue => new XamlDocumentChange(XamlUpdateStrategy.SetProperty, before, after, name),
            XamlMarkupExtensionValue extension when XamlInPlaceValues.IsCandidate(extension, after) =>
                new XamlDocumentChange(XamlUpdateStrategy.SetExpression, before, after, name),
            _ => Structural(before, after),
        };
    }

    /// <summary>
    /// Describes what has to be rebuilt instead of a change that cannot be written where it stands.
    /// </summary>
    /// <remarks>
    /// The change a load would need for the attribute if nothing could be written in place: its
    /// element built again — or the smallest container around it — and at the root, which has no
    /// slot to be put back into, a new session.
    /// </remarks>
    /// <param name="change">A value change the session found it cannot write in place.</param>
    /// <param name="loadedRoot">The root element of the document the objects were built from.</param>
    /// <returns>The change that rebuilds instead.</returns>
    internal static XamlDocumentChange Escalate(XamlDocumentChange change, XamlElement? loadedRoot)
    {
        if (change.OldElement is not { } before || change.NewElement is not { } after)
        {
            return new XamlDocumentChange(XamlUpdateStrategy.RecreateSession, change.OldElement, change.NewElement, null);
        }

        XamlDocumentChange rebuilt = Widened(Structural(before, after));

        return rebuilt.ReplacesObject && ReferenceEquals(rebuilt.OldElement, loadedRoot)
            ? new XamlDocumentChange(XamlUpdateStrategy.RecreateSession, before, after, null)
            : rebuilt;
    }

    /// <summary>
    /// Moves a rebuild out to the element whose dictionary a static reference inside it reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A part rebuilt on its own has none of the dictionaries around it, and a static reference is
    /// read from the dictionaries in scope while the part is built: a text block whose foreground
    /// became <c>{StaticResource Accent}</c>, with the brush on the form's resources, was rebuilt
    /// with no brush at all. The same reference in markup the form loads whole read the brush.
    /// </para>
    /// <para>
    /// So a rebuild whose markup reads a key from outside itself is moved out to the element that
    /// answers it — the nearest one declaring the key, as the lookup goes — and what that element
    /// holds is rebuilt instead: its dictionary is then part of the text the rebuild is built from.
    /// A key no element declares may still come from a file an element includes, and which file
    /// says what is not something the syntax answers, so the rebuild is moved out past the
    /// outermost such element. A key neither declared nor possibly included is the application's
    /// or a theme's, which a part built on its own still finds.
    /// </para>
    /// </remarks>
    private static XamlDocumentChange Widened(XamlDocumentChange change)
    {
        if (change.Strategy != XamlUpdateStrategy.ReloadSubtree
            || change.OldElement is not { } before
            || change.NewElement is not { } after)
        {
            return change;
        }

        ImmutableArray<XamlElement> includes =
            [.. after.Document.GetResourceReferences().Select(static reference => reference.Element)];

        int widest = 0;

        foreach ((XamlElement reader, string key) in StaticReads(after))
        {
            widest = Math.Max(widest, Reach(reader, key, after, includes));
        }

        if (widest == 0
            || Ancestor(before, widest) is not { } outerBefore
            || Ancestor(after, widest) is not { } outerAfter)
        {
            return change;
        }

        return Structural(outerBefore, outerAfter, replacesObject: false);
    }

    /// <summary>
    /// Counts how many levels above a rebuilt part sits the element whose dictionaries answer a
    /// static reference inside it — none when the part answers it itself, or nothing in the
    /// document can.
    /// </summary>
    private static int Reach(XamlElement reader, string key, XamlElement part, ImmutableArray<XamlElement> includes)
    {
        int above = -1;
        int included = 0;

        foreach (XamlElement scope in reader.AncestorsAndSelf().OfType<XamlElement>())
        {
            if (ReferenceEquals(scope, part))
            {
                above = 0;
            }
            else if (above >= 0)
            {
                above++;
            }

            if (scope.IsPropertyElementSyntax)
            {
                continue;
            }

            // The nearest declaration is the one the lookup finds, and building from it builds
            // everything nearer — any include on the way included.
            if (Dictionaries(scope).Any(entry => string.Equals(entry.GetDirective("Key"), key, StringComparison.Ordinal)))
            {
                return Math.Max(above, 0);
            }

            if (above > 0 && Dictionaries(scope).Any(entry => includes.Contains(entry)))
            {
                included = above;
            }
        }

        return included;
    }

    /// <summary>
    /// Gets every element an element reads resources from: what it writes as its <c>Resources</c>,
    /// and its <c>Styles</c>, whose dictionaries a lookup reaches through the element too.
    /// </summary>
    private static IEnumerable<XamlElement> Dictionaries(XamlElement element) =>
        element.MemberElements
            .Where(member => XamlObjectReplacement.Owns(element, member)
                && member.MemberName is "Resources" or "Styles")
            .SelectMany(static member => member.DescendantElements());

    /// <summary>
    /// Gets every key read with <c>StaticResource</c> in an element and everything inside it, with
    /// the element it is written on.
    /// </summary>
    private static IEnumerable<(XamlElement Reader, string Key)> StaticReads(XamlElement part) =>
        part.DescendantElements()
            .Prepend(part)
            .SelectMany(static element => KeysReadBy(element).Select(key => (element, key)));

    /// <summary>Gets the literal keys one element reads with <c>StaticResource</c>.</summary>
    private static IEnumerable<string> KeysReadBy(XamlElement element)
    {
        // Written as an element — <StaticResource ResourceKey="Accent" /> in a property element.
        if (element.Name.LocalName is "StaticResource" or "StaticResourceExtension"
            && element.Attributes.FirstOrDefault(static attribute =>
                    string.Equals(attribute.Name.LocalName, "ResourceKey", StringComparison.Ordinal))
                ?.GetValueText() is { Length: > 0 } written)
        {
            yield return written;
        }

        foreach (XamlAttribute attribute in element.Attributes)
        {
            if (attribute.GetValue() is XamlMarkupExtensionValue extension)
            {
                foreach (string key in StaticKeys(extension))
                {
                    yield return key;
                }
            }
        }
    }

    /// <summary>Gets the literal keys an expression reads with <c>StaticResource</c>, nested ones too.</summary>
    private static IEnumerable<string> StaticKeys(XamlMarkupExtensionValue extension)
    {
        if (extension.TypeName.LocalName is "StaticResource" or "StaticResourceExtension"
            && extension.Arguments.FirstOrDefault()?.Value is XamlLiteralValue { Text.Length: > 0 } key)
        {
            yield return key.Text;
        }

        // A converter or a fallback written as a static reference inside another expression.
        foreach (XamlMarkupExtensionArgument argument in extension.Arguments)
        {
            if (argument.Value is XamlMarkupExtensionValue nested)
            {
                foreach (string inner in StaticKeys(nested))
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>Reports whether an attribute is <c>mc:Ignorable</c>.</summary>
    private static bool IsIgnorable(XamlAttribute attribute) =>
        attribute.IsMarkupCompatibility
        && string.Equals(attribute.Name.LocalName, "Ignorable", StringComparison.Ordinal);

    /// <summary>
    /// Reports whether <c>mc:Ignorable</c> only gained namespaces nothing in the loaded document used.
    /// </summary>
    /// <remarks>
    /// A namespace a reader ignores is one whose markup it proceeds without, so making one ignorable
    /// changes the objects only if markup in it was applied. A namespace nothing used — the design
    /// namespace a tool declares and lists for the first design value it writes, which comes with it
    /// as a change of its own — changes nothing, and rebuilding the root for it meant a new session,
    /// and a new instance of the author's class, for a design width. A namespace that is taken off
    /// the list, or that markup in the loaded document is written in, is structural as before.
    /// </remarks>
    private static bool OnlyGrows(XamlAttribute? previous, XamlAttribute current, XamlElement before, XamlElement after)
    {
        var was = Ignored(previous, before);
        var now = Ignored(current, after);

        if (!was.IsSubsetOf(now))
        {
            return false;
        }

        now.ExceptWith(was);

        return !before.Document.DescendantElements().Any(element => Uses(element, now));
    }

    /// <summary>Gets the namespaces an <c>mc:Ignorable</c> attribute names, where it is written.</summary>
    private static HashSet<string> Ignored(XamlAttribute? attribute, XamlElement element)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (string prefix in (attribute?.GetValueText() ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (element.NamespaceContext.LookupNamespace(prefix) is { } namespaceUri)
            {
                namespaces.Add(namespaceUri);
            }
        }

        return namespaces;
    }

    /// <summary>Reports whether an element, or any attribute on it, is written in one of the namespaces.</summary>
    private static bool Uses(XamlElement element, HashSet<string> namespaces) =>
        (element.NamespaceUri is { } own && namespaces.Contains(own))
        || element.Attributes.Any(attribute => attribute is not XamlNamespaceDeclaration
            && attribute.Name.Prefix is { } prefix
            && element.NamespaceContext.LookupNamespace(prefix) is { } namespaceUri
            && namespaces.Contains(namespaceUri));

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

    /// <summary>
    /// Describes a change to what an element is or holds, as the smallest thing that can be
    /// rebuilt on its own.
    /// </summary>
    /// <remarks>
    /// A style's setter, a theme's, a template's content and a keyed resource's value are all
    /// changes to an element, and none of them can be rebuilt without the thing that owns them.
    /// Attributes are classified the same way, for the same reason.
    /// </remarks>
    private static XamlDocumentChange Structural(
        XamlElement before,
        XamlElement after,
        bool replacesObject = true)
    {
        if (Container(before) is { } container)
        {
            return new XamlDocumentChange(
                container.Strategy, container.Element, Ancestor(after, container.Depth), null)
            {
                ReplacesObject = true,
            };
        }

        // A property element is a member of its parent rather than a thing of its own, so it
        // produced no object and there is nothing to rebuild it into. What changed is what the
        // element that owns it holds.
        int levels = 0;
        XamlElement target = before;

        while (target.IsPropertyElementSyntax && target.Parent is XamlElement owner)
        {
            target = owner;
            levels++;
        }

        return new XamlDocumentChange(
            XamlUpdateStrategy.ReloadSubtree,
            target,
            levels == 0 ? after : Ancestor(after, levels),
            null)
        {
            ReplacesObject = levels == 0 && replacesObject,
        };
    }

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
