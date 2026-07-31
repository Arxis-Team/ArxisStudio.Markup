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
/// </remarks>
public sealed class XamlObjectMap
{
    private readonly Dictionary<XamlElement, object> _objectsByElement = [];
    private readonly ConditionalWeakTable<object, XamlElement> _elementsByObject = [];
    private readonly ConditionalWeakTable<object, OriginBox> _origins = [];
    private readonly List<object> _objects = [];

    private Uri? _documentSourceUri;

    private XamlObjectMap()
    {
    }

    /// <summary>Gets the objects the map knows about, in the order they were reached.</summary>
    public IReadOnlyList<object> Objects => _objects;

    /// <summary>Gets the elements that were matched to an object.</summary>
    public IReadOnlyCollection<XamlElement> MappedElements => _objectsByElement.Keys;

    /// <summary>Builds a map for a freshly loaded tree.</summary>
    /// <param name="document">The document the objects were created from.</param>
    /// <param name="root">The object the document produced.</param>
    /// <returns>The map.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="root"/> is <see langword="null"/>.</exception>
    public static XamlObjectMap Build(XamlDocument document, object root)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(root);

        var map = new XamlObjectMap
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

    /// <summary>Walks a tree, recording what each object came from.</summary>
    private void Walk(XamlDocument document, object current, XamlObjectOrigin inherited, HashSet<object> seen)
    {
        if (!seen.Add(current))
        {
            return;
        }

        XamlObjectOrigin origin = DetermineOrigin(current, inherited);
        XamlElement? element = FindDeclaration(document, current);

        _objects.Add(current);
        _origins.AddOrUpdate(current, new OriginBox(origin));

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

    /// <summary>Finds the element an object was created from, using Avalonia's source information.</summary>
    private XamlElement? FindDeclaration(XamlDocument document, object current)
    {
        XamlSourceInfo? info = XamlSourceInfo.GetXamlSourceInfo(current);

        if (info is null || info.LineNumber <= 0)
        {
            return null;
        }

        // Source information from another file describes another document's markup. Mapping it
        // into this one would put an object under an unrelated element.
        if (_documentSourceUri is not null
            && info.SourceUri is not null
            && !XamlUri.Comparer.Equals(info.SourceUri, _documentSourceUri))
        {
            return null;
        }

        TextLineCollection lines = document.SourceText.Lines;
        int line = Math.Min(info.LineNumber - 1, lines.Count - 1);

        if (line < 0)
        {
            return null;
        }

        TextLine textLine = lines[line];
        int column = Math.Clamp(info.LinePosition - 1, 0, Math.Max(0, textLine.Span.Length));
        int offset = Math.Min(textLine.Start + column, Math.Max(0, document.SourceText.Length - 1));

        // The recorded position lands inside the start tag, so the innermost node there is a
        // part of the element rather than the element itself.
        return document.FindNode(offset)?.AncestorsAndSelf().OfType<XamlElement>().FirstOrDefault();
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
    }

    /// <summary>A boxed origin, because a weak table needs a reference type.</summary>
    private sealed class OriginBox(XamlObjectOrigin value)
    {
        public XamlObjectOrigin Value { get; } = value;
    }
}
