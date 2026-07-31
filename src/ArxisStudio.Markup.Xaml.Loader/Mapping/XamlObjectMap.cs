using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
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

    private Uri? _documentSourceUri;

    private XamlObjectMap(TextProjection projection)
    {
        _projection = projection;
    }

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
    public static XamlObjectMap Build(XamlDocument document, object root, TextProjection projection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(projection);

        var map = new XamlObjectMap(projection)
        {
            // Avalonia names a runtime-loaded document with a synthetic URI of its own rather
            // than the base URI it was handed, so what the document "is" can only be learnt
            // from the root object. Anything reporting a different URI came from elsewhere --
            // a template or an included resource -- and must not be mapped into this document.
            _documentSourceUri = XamlSourceInfo.GetXamlSourceInfo(root)?.SourceUri,
        };

        map.Walk(document, root, XamlObjectOrigin.Document, new HashSet<object>(ReferenceEqualityComparer.Instance));

        return map;
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
        return runtimeObject is StyledElement { TemplatedParent: not null }
            ? XamlObjectOrigin.Template
            : XamlObjectOrigin.RuntimeGenerated;
    }

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

        XamlElement? element = declaration.Element;

        _objects.Add(current);
        _origins.AddOrUpdate(current, new OriginBox(origin));

        if (declaration.SourceUri is { } sourceUri)
        {
            _sourceUris.AddOrUpdate(current, sourceUri);
        }

        // Only a Document-origin object gets a two-way link. A template-generated child may
        // carry source information pointing at the template's own markup, and letting that
        // masquerade as the control's declaration is the specific mistake this map exists to
        // avoid.
        if (element is not null && origin == XamlObjectOrigin.Document)
        {
            _elementsByObject.AddOrUpdate(current, element);
            _objectsByElement.TryAdd(element, current);
        }

        foreach (object child in ChildrenOf(current))
        {
            Walk(document, child, origin == XamlObjectOrigin.Document ? XamlObjectOrigin.Document : origin, seen);
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

        // Source information from another file describes another document's markup. Mapping it
        // into this one would put an object under an unrelated element. Included documents are
        // not this case: they were spliced into the text Avalonia was handed, so they report
        // the same URI as the rest of it and the projection is what tells them apart.
        if (_documentSourceUri is not null
            && info.SourceUri is not null
            && !XamlUri.Comparer.Equals(info.SourceUri, _documentSourceUri))
        {
            return default;
        }

        TextProjectionPosition position = _projection.Map(
            new TextPosition(info.LineNumber - 1, Math.Max(0, info.LinePosition - 1)));

        if (!position.IsOriginal)
        {
            return new Declaration(null, position.SourceUri, IsFromIncludedDocument: true);
        }

        // The recorded position lands inside the start tag, so the innermost node there is a
        // part of the element rather than the element itself.
        XamlElement? element = document
            .FindNode(OffsetIn(document.SourceText, position.Offset))
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
    private int OffsetIn(SourceText current, int offsetInProjectedSource)
    {
        if (ReferenceEquals(current, _projection.Source))
        {
            return Math.Min(offsetInProjectedSource, Math.Max(0, current.Length - 1));
        }

        TextPosition position = _projection.Source.Lines.GetPosition(
            Math.Clamp(offsetInProjectedSource, 0, _projection.Source.Length));

        TextLineCollection lines = current.Lines;
        TextLine line = lines[Math.Min(position.Line, lines.Count - 1)];

        return Math.Min(
            line.Start + Math.Clamp(position.Column, 0, line.Span.Length),
            Math.Max(0, current.Length - 1));
    }

    /// <summary>Gets the objects reachable from one, without leaving the logical world.</summary>
    /// <remarks>
    /// The logical tree, resources and styles are walked; the visual tree is not. Visual
    /// children are produced by templates and measured layout, neither of which the document
    /// declared.
    /// </remarks>
    private static IEnumerable<object> ChildrenOf(object current)
    {
        if (current is ILogical logical)
        {
            foreach (ILogical child in logical.LogicalChildren)
            {
                yield return child;
            }
        }

        if (current is ContentControl { Content: { } content } && content is not string)
        {
            yield return content;
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
