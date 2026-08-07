using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Diagnostics;
using Avalonia.Styling;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Which element of the document each loaded object came from, and the other way round.
/// </summary>
/// <remarks>
/// <para>
/// The map is built from the source information Avalonia records on the objects it creates,
/// not from walking the two trees side by side and hoping they line up. A structural walk
/// breaks the moment a control expands into more objects than the document mentions — which is
/// exactly what templates do — and the failure is silent.
/// </para>
/// <para>
/// Objects the document did not declare are recorded with the origin that explains them rather
/// than left out. A caller that picks a template-generated child on screen needs to be told
/// that it belongs to a template, not handed the control's own declaration as though editing
/// it were the same thing.
/// </para>
/// <para>
/// The positions Avalonia records are positions in the projected text, in which every include
/// has been replaced by the document it names. They are therefore read through the projection
/// rather than used directly, which is both what keeps the document's own elements at the right
/// offsets once a splice has moved everything after it, and what makes an object declared in an
/// included file identifiable as belonging to that file.
/// </para>
/// </remarks>
public sealed class XamlObjectMap
{
    private readonly Dictionary<XamlElement, object> _objectsByElement = [];
    private readonly ConditionalWeakTable<object, XamlElement> _elementsByObject = [];
    private readonly ConditionalWeakTable<object, OriginBox> _origins = [];
    private readonly ConditionalWeakTable<object, Uri> _sourceUris = [];
    private readonly List<object> _objects = [];

    private readonly TextProjection _projection;
    private readonly XamlMemberResolver _members;
    private readonly IReadOnlyDictionary<Uri, TextProjection> _fragments;
    private readonly HashSet<Uri> _observed = new(XamlUri.Comparer);
    private readonly HashSet<object> _carried = new(ReferenceEqualityComparer.Instance);

    private Uri? _documentSourceUri;

    /// <summary>
    /// The object the document produced, which is of the document whatever else is known about it.
    /// </summary>
    /// <remarks>
    /// Held because the rule below asks whether an object was declared, and the root of an
    /// <c>x:Class</c> document was not — it is created before the markup loads and handed over
    /// already made, so Avalonia records no position for it. Everything else with no declaration is
    /// something the run time produced; the root is the one exception, and it is the same exception
    /// <see cref="PairTheRoot"/> exists for.
    /// </remarks>
    private object? _root;

    private XamlObjectMap(
        TextProjection projection,
        IReadOnlyDictionary<Uri, TextProjection> fragments,
        XamlMemberResolver members)
    {
        _projection = projection;
        _fragments = fragments;
        _members = members;
    }

    /// <summary>
    /// Gets the names Avalonia gave the texts the objects in this map were actually built from.
    /// </summary>
    /// <remarks>
    /// A session prunes its record of separately loaded fragments to these, so the ones whose
    /// objects have since been replaced do not accumulate for the life of the session.
    /// </remarks>
    internal IReadOnlyCollection<Uri> ObservedSources => _observed;

    /// <summary>Gets the objects the map knows about, in the order they were reached.</summary>
    public IReadOnlyList<object> Objects => _objects;

    /// <summary>Gets the elements that were matched to an object.</summary>
    public IReadOnlyCollection<XamlElement> MappedElements => _objectsByElement.Keys;

    /// <summary>Builds a map for a tree loaded from a document with nothing spliced into it.</summary>
    /// <param name="document">The document the objects were created from.</param>
    /// <param name="root">The object the document produced.</param>
    /// <returns>The map.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="root"/> is <see langword="null"/>.</exception>
    public static XamlObjectMap Build(XamlDocument document, object root)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Build(document, root, TextProjection.Identity(document.SourceText, document.Uri));
    }

    /// <summary>Builds a map for a freshly loaded tree.</summary>
    /// <param name="document">The document the objects were created from.</param>
    /// <param name="root">The object the document produced.</param>
    /// <param name="projection">
    /// The text the objects were actually built from, which is the document with its includes
    /// resolved and spliced in.
    /// </param>
    /// <returns>The map.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static XamlObjectMap Build(XamlDocument document, object root, TextProjection projection) =>
        Build(document, root, projection, ImmutableDictionary<Uri, TextProjection>.Empty);

    /// <summary>
    /// Builds a map for a tree that has had part of it rebuilt from a fragment.
    /// </summary>
    /// <remarks>
    /// Avalonia names every text it is given, and gives a separately loaded fragment a name of
    /// its own, so an object's recorded name says which text built it. Each name is paired with
    /// the projection of that text, which is what lets an object rebuilt by an update still be
    /// traced to the markup in the document that describes it.
    /// </remarks>
    /// <param name="document">The document the objects were created from.</param>
    /// <param name="root">The object the document produced.</param>
    /// <param name="projection">The text the document as a whole was built from.</param>
    /// <param name="fragments">The projection behind each separately loaded fragment, by the name Avalonia gave it.</param>
    /// <returns>The map.</returns>
    internal static XamlObjectMap Build(
        XamlDocument document,
        object root,
        TextProjection projection,
        IReadOnlyDictionary<Uri, TextProjection> fragments) =>
        Build(document, root, projection, fragments, null, XamlMemberResolver.Instance);

    /// <summary>
    /// Builds a map for a tree after an update, carrying forward what an update already knows.
    /// </summary>
    /// <remarks>
    /// Avalonia records where it built an object, once, against the text it was given. An update
    /// that adds or removes a line leaves every later object recorded at a position that now
    /// describes something else, and no arithmetic recovers it — the objects were never rebuilt,
    /// so there is nothing newer to read. What an update does know is which element of the new
    /// document stands where each element of the old one stood, and that is what is carried.
    /// </remarks>
    /// <param name="document">The document the objects were created from.</param>
    /// <param name="root">The object the document produced.</param>
    /// <param name="projection">The text the document as a whole was built from.</param>
    /// <param name="fragments">The projection behind each separately loaded fragment, by the name Avalonia gave it.</param>
    /// <param name="carried">Objects already known to belong to elements of <paramref name="document"/>.</param>
    /// <param name="members">
    /// What decides which member a type calls its content. A session hands over its environment's,
    /// so that what is learnt about an assembly is discarded with the environment that resolved
    /// it; the public overloads, which have no environment to ask, use the shared resolver.
    /// </param>
    /// <returns>The map.</returns>
    internal static XamlObjectMap Build(
        XamlDocument document,
        object root,
        TextProjection projection,
        IReadOnlyDictionary<Uri, TextProjection> fragments,
        IReadOnlyDictionary<XamlElement, object>? carried,
        XamlMemberResolver members)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(projection);

        var map = new XamlObjectMap(projection, fragments, members)
        {
            // Avalonia names a runtime-loaded document with a synthetic URI of its own rather
            // than the base URI it was handed, so what the document "is" can only be learnt
            // from the root object. Anything reporting a different URI came from elsewhere --
            // a template or an included resource -- and must not be mapped into this document.
            _documentSourceUri = XamlSourceInfo.GetXamlSourceInfo(root)?.SourceUri,
        };

        foreach ((XamlElement element, object target) in
            carried ?? (IReadOnlyDictionary<XamlElement, object>)ImmutableDictionary<XamlElement, object>.Empty)
        {
            map._objectsByElement[element] = target;
            map._elementsByObject.AddOrUpdate(target, element);
            map._carried.Add(target);
        }

        map._root = root;

        map.Walk(document, root, XamlObjectOrigin.Document, new HashSet<object>(ReferenceEqualityComparer.Instance));
        map.PairTheRoot(document, root);

        return map;
    }

    /// <summary>
    /// Pairs the document's root element with the object it produced, when nothing else did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other pair is deduced: Avalonia records where it built an object, and the walk reads
    /// that position back into the document. The root of an <c>x:Class</c> document has no such
    /// position, and cannot — the instance is created before the markup is loaded and handed over
    /// already made, which is the whole purpose of the class. So the one element whose object is
    /// known without evidence was the one element with no entry, and a document of five elements
    /// mapped four.
    /// </para>
    /// <para>
    /// It is not deduced here either. <paramref name="root"/> is the object this document produced
    /// and <c>document.Root</c> is the element that describes it; that is what both arguments mean.
    /// Asserting it costs nothing and closes the case that matters most to a designer, where a click
    /// on a form's background found nothing and the form's own properties were unreachable.
    /// </para>
    /// <para>
    /// Only when the walk left both sides free. A root that already paired keeps what it deduced,
    /// and an element already claimed by another object is not taken from it — either would be this
    /// method overruling evidence with an assumption, which is the opposite of its purpose.
    /// </para>
    /// </remarks>
    private void PairTheRoot(XamlDocument document, object root)
    {
        if (document.Root is not { } element
            || _elementsByObject.TryGetValue(root, out _)
            || _objectsByElement.ContainsKey(element))
        {
            return;
        }

        _elementsByObject.AddOrUpdate(root, element);
        _objectsByElement[element] = root;
    }

    /// <summary>
    /// Pairs this map's objects with the elements that stand in the same places in a changed
    /// document.
    /// </summary>
    /// <remarks>
    /// Elements are paired the same way the difference between the two documents was worked out —
    /// by declared identity, and by position among their siblings where no name decides — and the
    /// walk stops descending where the two documents stop agreeing. Below that the objects were
    /// rebuilt, and the text they were rebuilt from is what says where they came from.
    /// </remarks>
    /// <param name="from">The document this map is keyed by.</param>
    /// <param name="to">The document it is being re-keyed to.</param>
    /// <returns>What each element of <paramref name="to"/> stands for.</returns>
    internal IReadOnlyDictionary<XamlElement, object> Carry(XamlDocument from, XamlDocument to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        var carried = new Dictionary<XamlElement, object>();

        if (from.Root is { } before && to.Root is { } after)
        {
            Pair(before, after, carried);
        }

        return carried;
    }

    private void Pair(XamlElement before, XamlElement after, Dictionary<XamlElement, object> carried)
    {
        if (before.Name != after.Name)
        {
            return;
        }

        if (_objectsByElement.TryGetValue(before, out object? target))
        {
            carried[after] = target;
        }

        XamlElement[] beforeChildren = [.. before.Elements];
        XamlElement[] afterChildren = [.. after.Elements];

        if (beforeChildren.Length != afterChildren.Length)
        {
            return;
        }

        if (XamlElementIdentity.Pair(beforeChildren, afterChildren) is { } pairing)
        {
            foreach ((XamlElement child, XamlElement updated) in pairing.All)
            {
                Pair(child, updated, carried);
            }

            return;
        }

        for (int index = 0; index < beforeChildren.Length; index++)
        {
            Pair(beforeChildren[index], afterChildren[index], carried);
        }
    }

    /// <summary>Gets the object an element produced.</summary>
    /// <param name="element">The element to look up.</param>
    /// <returns>The object, or <see langword="null"/> when the element produced none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public object? GetObject(XamlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return _objectsByElement.GetValueOrDefault(element);
    }

    /// <summary>Gets the element an object was declared by.</summary>
    /// <param name="runtimeObject">The object to look up.</param>
    /// <returns>
    /// The element, or <see langword="null"/> when the object has no declaration in this
    /// document — because a template or a style created it, or because nothing did.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtimeObject"/> is <see langword="null"/>.</exception>
    public XamlElement? GetElement(object runtimeObject)
    {
        ArgumentNullException.ThrowIfNull(runtimeObject);

        return _elementsByObject.TryGetValue(runtimeObject, out XamlElement? element) ? element : null;
    }

    /// <summary>Gets what an object came from.</summary>
    /// <param name="runtimeObject">The object to look up.</param>
    /// <returns>Its origin, or <see cref="XamlObjectOrigin.RuntimeGenerated"/> when the map has never seen it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtimeObject"/> is <see langword="null"/>.</exception>
    public XamlObjectOrigin GetOrigin(object runtimeObject)
    {
        ArgumentNullException.ThrowIfNull(runtimeObject);

        if (_origins.TryGetValue(runtimeObject, out OriginBox? origin))
        {
            return origin.Value;
        }

        // Templates are applied lazily, so their output routinely appears after the map was
        // built. Belonging to a template is a property of the object itself, not of when the
        // walk happened to run, and answering "run-time generated" here would let template
        // output be mistaken for something a caller may edit.
        return Undeclared(runtimeObject);
    }

    /// <summary>What an object is when this document did not declare it.</summary>
    /// <remarks>
    /// One rule, asked in two places — of an object the walk reached and could not find a
    /// declaration for, and of an object the walk never saw at all. Both are the same question, and
    /// answering it differently in the two places is how an object's provenance would come to
    /// depend on when it happened to be asked about.
    /// </remarks>
    private static XamlObjectOrigin Undeclared(object current) =>
        current is StyledElement { TemplatedParent: not null }
            ? XamlObjectOrigin.Template
            : XamlObjectOrigin.RuntimeGenerated;

    /// <summary>Gets the document an object was declared in.</summary>
    /// <param name="runtimeObject">The object to look up.</param>
    /// <returns>
    /// The URI of the file the object's markup lives in — this document, or an included one —
    /// or <see langword="null"/> when nothing declared it or the file has no URI.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtimeObject"/> is <see langword="null"/>.</exception>
    public Uri? GetSourceUri(object runtimeObject)
    {
        ArgumentNullException.ThrowIfNull(runtimeObject);

        return _sourceUris.TryGetValue(runtimeObject, out Uri? uri) ? uri : null;
    }

    /// <summary>Walks a tree, recording what each object came from.</summary>
    private void Walk(XamlDocument document, object current, XamlObjectOrigin inherited, HashSet<object> seen)
    {
        if (!seen.Add(current))
        {
            return;
        }

        Declaration declaration = Locate(document, current);

        // An object whose markup is in an included file is not part of this document however
        // deep in it the include sits, so it cannot inherit this document's origin: a style
        // pulled in by a StyleInclude has to read as a style, not as one written here.
        XamlObjectOrigin origin = DetermineOrigin(
            current,
            declaration.IsFromIncludedDocument ? XamlObjectOrigin.Resource : inherited);

        // Inheritance carries the document's origin down to children, and that is right for
        // children the document wrote. It is wrong for the rest, and a templated parent does not
        // catch them all: a presenter building an AccessText out of string content leaves that
        // property null, so a button's own label arrived here claiming to be of the document, with
        // no declaration anywhere to say what it was. A caller asking the map which controls the
        // user may edit got the label among them and had no way to tell.
        //
        // So the claim is only made where a declaration was found. Nothing else in the map changes:
        // an object with no element was never paired to one, and this only stops it answering a
        // question about provenance with somebody else's answer.
        if (origin is XamlObjectOrigin.Document
            && declaration is { Element: null, SourceUri: null }
            && !ReferenceEquals(current, _root))
        {
            origin = Undeclared(current);
        }

        XamlElement? element = declaration.Element;

        _objects.Add(current);
        _origins.AddOrUpdate(current, new OriginBox(origin));

        if (declaration.SourceUri is { } sourceUri)
        {
            _sourceUris.AddOrUpdate(current, sourceUri);
        }

        // A template-generated child carries source information pointing at the template's own
        // markup, and letting that masquerade as the control's declaration is the specific
        // mistake this map exists to avoid. A resource or a style is not that case: the element
        // that declares it is genuinely the element that declares it, and an update that
        // rebuilds one has to be able to find what it built.
        // An object carried across an update already has its element, and it is the one the
        // update paired it with rather than one derived from a position it predates.
        if (element is not null
            && !_carried.Contains(current)
            && origin is XamlObjectOrigin.Document or XamlObjectOrigin.Resource or XamlObjectOrigin.Style)
        {
            _elementsByObject.AddOrUpdate(current, element);
            _objectsByElement.TryAdd(element, current);
        }

        foreach (object child in ChildrenOf(current))
        {
            Walk(document, child, origin == XamlObjectOrigin.Document ? XamlObjectOrigin.Document : origin, seen);
        }
    }

    /// <summary>Reads what an object holds in its content member, without letting it throw.</summary>
    /// <remarks>
    /// The getter belongs to whoever wrote the control, and a map being built is no place for
    /// their exception to arrive: an object whose content cannot be read simply has none as far
    /// as this walk is concerned.
    /// </remarks>
    private static object? ContentOf(object current, XamlMemberDescriptor content)
    {
        try
        {
            return content switch
            {
                { AvaloniaProperty: { } property } when current is AvaloniaObject styled =>
                    styled.GetValue(property),
                { ClrProperty.CanRead: true } => content.ClrProperty!.GetValue(current),
                _ => null,
            };
        }
        catch (Exception error) when (error is InvalidOperationException
            or NotSupportedException
            or TargetInvocationException)
        {
            return null;
        }
    }

    /// <summary>Decides where an object came from, from what Avalonia can be asked about it.</summary>
    private static XamlObjectOrigin DetermineOrigin(object current, XamlObjectOrigin inherited)
    {
        // A templated parent is Avalonia's own record that this object was produced by applying
        // a template, whatever else it looks like.
        if (current is StyledElement { TemplatedParent: not null })
        {
            return XamlObjectOrigin.Template;
        }

        return current switch
        {
            IStyle or IStyleHost when inherited != XamlObjectOrigin.Document => XamlObjectOrigin.Style,
            IResourceDictionary => XamlObjectOrigin.Resource,
            _ => inherited,
        };
    }

    /// <summary>Finds where an object was declared, using Avalonia's source information.</summary>
    private Declaration Locate(XamlDocument document, object current)
    {
        XamlSourceInfo? info = XamlSourceInfo.GetXamlSourceInfo(current);

        if (info is null || info.LineNumber <= 0)
        {
            return default;
        }

        if (ProjectionFor(info.SourceUri) is not { } projection)
        {
            // Source information from a text this map knows nothing about describes somebody
            // else's markup — a template's, or a fragment loaded before the last update.
            // Mapping it into this document would put an object under an unrelated element.
            return default;
        }

        if (info.SourceUri is not null)
        {
            _observed.Add(info.SourceUri);
        }

        TextProjectionPosition position = projection.Map(
            new TextPosition(info.LineNumber - 1, Math.Max(0, info.LinePosition - 1)));

        if (!position.IsOriginal)
        {
            return new Declaration(null, position.SourceUri, IsFromIncludedDocument: true);
        }

        // The recorded position lands inside the start tag, so the innermost node there is a
        // part of the element rather than the element itself.
        XamlElement? element = document
            .FindNode(OffsetIn(document.SourceText, projection, position.Offset))
            ?.AncestorsAndSelf()
            .OfType<XamlElement>()
            .FirstOrDefault();

        return new Declaration(element, document.Uri, IsFromIncludedDocument: false);
    }

    /// <summary>
    /// Turns an offset into the text the objects were built from into one in the document as it
    /// stands now.
    /// </summary>
    /// <remarks>
    /// The two are the same text until an edit is applied, and editing reparses: the map is then
    /// rebuilt against a document whose text has moved on, from positions Avalonia recorded
    /// before it did. Going back through the line and column is what survives that — an edit
    /// that lengthens an earlier line moves every later offset and leaves the lines alone —
    /// which is the tolerance the map had before it read positions through a projection at all.
    /// </remarks>
    private static int OffsetIn(SourceText current, TextProjection projection, int offsetInProjectedSource)
    {
        if (ReferenceEquals(current, projection.Source))
        {
            return Math.Min(offsetInProjectedSource, Math.Max(0, current.Length - 1));
        }

        TextPosition position = projection.Source.Lines.GetPosition(
            Math.Clamp(offsetInProjectedSource, 0, projection.Source.Length));

        TextLineCollection lines = current.Lines;
        TextLine line = lines[Math.Min(position.Line, lines.Count - 1)];

        return Math.Min(
            line.Start + Math.Clamp(position.Column, 0, line.Span.Length),
            Math.Max(0, current.Length - 1));
    }

    /// <summary>Finds the projection of the text Avalonia built an object from.</summary>
    /// <remarks>
    /// The document's own text is identified by the name Avalonia gave it, which is learnt from
    /// the root object because a runtime-loaded document gets a synthetic one rather than the
    /// base URI it was handed. A fragment loaded by an update gets its own name and its own
    /// projection; anything else is markup this map has no claim on.
    /// </remarks>
    private TextProjection? ProjectionFor(Uri? runtimeSourceUri)
    {
        if (runtimeSourceUri is null
            || _documentSourceUri is null
            || XamlUri.Comparer.Equals(runtimeSourceUri, _documentSourceUri))
        {
            return _projection;
        }

        return _fragments.TryGetValue(runtimeSourceUri, out TextProjection? fragment) ? fragment : null;
    }

    /// <summary>Gets the objects reachable from one, without leaving the logical world.</summary>
    /// <remarks>
    /// The logical tree, resources and styles are walked; the visual tree is not. Visual
    /// children are produced by templates and measured layout, neither of which the document
    /// declared.
    /// </remarks>
    private IEnumerable<object> ChildrenOf(object current)
    {
        if (current is ILogical logical)
        {
            foreach (ILogical child in logical.LogicalChildren)
            {
                yield return child;
            }
        }

        // What the type calls its content, where holding it is all that has happened to it: a
        // ContentControl before its template has run keeps its content in a property and nowhere
        // else, and so does any control library's own control that says [Content] on one. Asking
        // the attribute covers both; naming ContentControl covers only the framework's.
        if (_members.FindContent(current.GetType()) is { CanRead: true } content
            && ContentOf(current, content) is { } held and not string)
        {
            // The collection itself, because markup can declare one — and its items, because a
            // control that keeps its children in a collection of its own puts nothing in the
            // logical tree until something parents them.
            yield return held;

            if (held is IEnumerable many and not IResourceProvider)
            {
                foreach (object? item in many)
                {
                    // Only what could have been declared. An items control's Items reflects
                    // whatever ItemsSource is bound to, and walking a view model's rows would
                    // record origins for objects no markup describes and hold them for the life
                    // of the session.
                    if (item is ILogical)
                    {
                        yield return item;
                    }
                }
            }
        }

        // Resources hang off the concrete hosts rather off a single interface, so the two that
        // can be a loaded root are named directly.
        if (current is StyledElement { Resources: { } elementResources })
        {
            yield return elementResources;
        }

        if (current is Application { Resources: { } applicationResources })
        {
            yield return applicationResources;
        }

        if (current is IResourceDictionary dictionary)
        {
            // Merged dictionaries are where an include's content ends up, so a walk that
            // skipped them would have nothing to say about the objects an include produced.
            foreach (IResourceProvider merged in dictionary.MergedDictionaries)
            {
                yield return merged;
            }

            foreach (object? value in dictionary.Values)
            {
                if (value is not null)
                {
                    yield return value;
                }
            }
        }

        // A control's template is a value of the control, not a child of it, so nothing in the
        // logical world reaches it — and an update that rebuilds one has to find what it built.
        if (current is TemplatedControl { Template: { } template })
        {
            yield return template;
        }

        if (current is IStyleHost styleHost)
        {
            foreach (IStyle style in styleHost.Styles)
            {
                yield return style;
            }
        }

        // A style include brings in a whole Styles collection, which sits inside the host's own
        // one rather than being flattened into it.
        if (current is Styles nested)
        {
            foreach (IStyle style in nested)
            {
                yield return style;
            }
        }
    }

    /// <summary>Where an object's markup was found, once the projection has been consulted.</summary>
    /// <param name="Element">
    /// The element that declared it, when that element is in the document being edited.
    /// </param>
    /// <param name="SourceUri">The file the markup is in, when that file has a URI.</param>
    /// <param name="IsFromIncludedDocument">Whether the markup came from an include rather than this document.</param>
    private readonly record struct Declaration(
        XamlElement? Element,
        Uri? SourceUri,
        bool IsFromIncludedDocument);

    /// <summary>A boxed origin, because a weak table needs a reference type.</summary>
    private sealed class OriginBox(XamlObjectOrigin value)
    {
        public XamlObjectOrigin Value { get; } = value;
    }
}
