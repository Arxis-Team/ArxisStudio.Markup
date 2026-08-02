using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Diagnostics;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The half of a session that brings its objects in line with a document that has changed
/// underneath them.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here compiles anything. A document is text, the objects are built from text, and an
/// update is a comparison of two syntax trees followed by the smallest change that is certainly
/// enough — which is what makes editing XAML and seeing the result a thing that can happen
/// without a build.
/// </para>
/// <para>
/// An update that cannot be applied leaves the objects exactly as they were. The document that
/// was offered is kept as <see cref="PendingDocument"/> rather than dropped, because the usual
/// reason an update fails is that the author is halfway through typing it, and the next keystroke
/// is the correction.
/// </para>
/// </remarks>
public sealed partial class XamlLoadSession
{
    private readonly Dictionary<Uri, TextProjection> _fragments = new(XamlUri.Comparer);

    /// <summary>
    /// Gets the most recent document that was offered and not applied, if there is one.
    /// </summary>
    /// <remarks>
    /// A refused update is not a discarded one. This is what was refused, so a caller can show
    /// it, diff it, or hand back a corrected version of it; it is cleared as soon as an update
    /// lands.
    /// </remarks>
    public XamlDocument? PendingDocument { get; private set; }

    /// <summary>
    /// Brings the objects in line with a changed version of the session's document.
    /// </summary>
    /// <param name="updated">The document as it now reads.</param>
    /// <param name="cancellationToken">A token to observe while updating.</param>
    /// <returns>What the update did, and everything noticed on the way.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="updated"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public async ValueTask<XamlUpdateResult> ApplyDocumentUpdateAsync(
        XamlDocument updated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var diagnostics = new List<MarkupDiagnostic>();

        // A document that did not parse describes nothing to update towards, and the errors
        // saying why are more use than anything an attempt would produce.
        if (!updated.IsWellFormed)
        {
            diagnostics.AddRange(updated.GetDiagnostics().Where(static diagnostic => diagnostic.IsError));

            return Refuse(
                updated,
                XamlUpdateStrategy.None,
                [],
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateRejected,
                "The document offered does not parse, so the objects were left as they were.");
        }

        ImmutableArray<XamlDocumentChange> changes = XamlDocumentDiff.Compare(Document, updated);
        XamlUpdateStrategy strategy = XamlDocumentDiff.Largest(changes);

        if (strategy == XamlUpdateStrategy.RecreateSession)
        {
            return Refuse(
                updated,
                strategy,
                changes,
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateRequiresNewSession,
                "The root element or x:Class changed. The objects were left as they were; create a " +
                "new session from the new document.");
        }

        return await ApplyAsync(updated, strategy, changes, diagnostics, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Brings the objects in line with a resource file that has changed outside the document.
    /// </summary>
    /// <remarks>
    /// An included dictionary or style file is part of the text the objects were built from, so a
    /// change to one is a change to the load even though the document itself reads the same. The
    /// document is reprojected — which is what re-reads the file through the environment's
    /// resolvers — and the difference that makes decides what has to be rebuilt.
    /// </remarks>
    /// <param name="resourceUri">The resource that changed.</param>
    /// <param name="cancellationToken">A token to observe while updating.</param>
    /// <returns>What the update did, and everything noticed on the way.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resourceUri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public async ValueTask<XamlUpdateResult> ApplySourceUpdateAsync(
        Uri resourceUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var diagnostics = new List<MarkupDiagnostic>();

        TextProjection projection = await XamlDocumentProjector
            .ProjectAsync(Document, Environment, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        // Nothing the document reaches changed, whatever the caller was told about the file.
        if (string.Equals(projection.Text.ToString(), Projection.Text.ToString(), StringComparison.Ordinal))
        {
            return new XamlUpdateResult
            {
                Applied = true,
                Strategy = XamlUpdateStrategy.None,
                Changes = [],
                Diagnostics = [.. diagnostics],
            };
        }

        // The included content is not part of the document, so there is no element of it to set
        // a property on. What has to be built again is whatever element of the document the
        // include was expanded inside — every one of them, because which include's expansion
        // changed is not a question the projected text answers.
        ImmutableArray<XamlDocumentChange> changes =
        [
            .. Document.GetResourceReferences()
                .Select(reference => Host(reference.Element))
                .OfType<XamlElement>()
                .Distinct()
                .Select(static host => new XamlDocumentChange(
                    XamlUpdateStrategy.ReplaceResource, host, host, null) { ReplacesObject = true }),
        ];

        if (changes.IsEmpty)
        {
            return Refuse(
                Document,
                XamlUpdateStrategy.RecreateSession,
                [],
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateRequiresNewSession,
                $"'{XamlUri.ToDisplayString(resourceUri)}' is included straight into the root element, " +
                "which cannot be rebuilt in place. Create a new session from the same document.");
        }

        return await ApplyAsync(Document, XamlUpdateStrategy.ReplaceResource, changes, diagnostics, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the element an include was expanded inside, which is what has to be built again
    /// when the file it names changes.
    /// </summary>
    /// <remarks>
    /// The nearest ordinary element that produced an object of its own and is not the root: a
    /// property element is not something that can be loaded on its own, and the root has no slot
    /// to be put back into.
    /// </remarks>
    private XamlElement? Host(XamlElement include)
    {
        foreach (XamlElement ancestor in include.AncestorsAndSelf().OfType<XamlElement>().Skip(1))
        {
            if (ReferenceEquals(ancestor, Document.Root))
            {
                return null;
            }

            if (!ancestor.IsPropertyElementSyntax && Objects.GetObject(ancestor) is not null)
            {
                return ancestor;
            }
        }

        return null;
    }

    /// <summary>Applies an update that can be made on the objects that already exist.</summary>
    private async ValueTask<XamlUpdateResult> ApplyAsync(
        XamlDocument updated,
        XamlUpdateStrategy strategy,
        ImmutableArray<XamlDocumentChange> changes,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        // Reprojecting before anything is touched means a failure to resolve an include is a
        // refused update rather than a half-updated tree.
        TextProjection projection = await XamlDocumentProjector
            .ProjectAsync(updated, Environment, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        // Every fragment is projected and parsed before any object is touched, for the same
        // reason: a fragment that will not build is a refused update, not a half-rebuilt tree.
        var fragments = new List<(XamlDocumentChange Change, TextProjection Projection)>();

        foreach (XamlDocumentChange change in changes.Where(static change => Rebuilds(change.Strategy)))
        {
            if (change.NewElement is not { } element)
            {
                return Refuse(
                    updated,
                    strategy,
                    changes,
                    diagnostics,
                    XamlLoaderDiagnosticCodes.UpdateNotApplied,
                    $"{change} does not say which element to rebuild.");
            }

            fragments.Add((
                change,
                await XamlDocumentProjector
                    .ProjectAsync(updated, element, Environment, diagnostics, [], cancellationToken)
                    .ConfigureAwait(false)));
        }

        bool applied = await _dispatcher
            .InvokeAsync(() => Write(changes, fragments, diagnostics), cancellationToken)
            .ConfigureAwait(false);

        if (!applied)
        {
            return Refuse(
                updated,
                strategy,
                changes,
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateNotApplied,
                "The update could not be applied, and the objects were left as they were.");
        }

        // What survives the change keeps the object it already had, paired by where it sits.
        // Rebuilding that from Avalonia's recorded positions would read them against a document
        // they predate, and an update that added a line would break every one after it.
        IReadOnlyDictionary<XamlElement, object> carried = Objects.Carry(Document, updated);

        Document = updated;
        Projection = projection;
        Objects = XamlObjectMap.Build(updated, RootObject, projection, _fragments, carried);
        PendingDocument = null;

        // A fragment whose objects the walk never reached has been replaced by a later one, and
        // its projection describes text that is no longer anywhere in the tree.
        foreach (Uri stale in _fragments.Keys.Where(uri => !Objects.ObservedSources.Contains(uri)).ToArray())
        {
            _fragments.Remove(stale);
        }

        // Design values are re-applied from the document rather than patched one at a time.
        // Nothing re-evaluates on an update, so the document is the only place that says what
        // they are now, and applying all of them is the same walk a design-mode load does.
        if (Options.Mode == XamlLoadMode.Design)
        {
            await _dispatcher
                .InvokeAsync<object?>(
                    () =>
                    {
                        XamlDesignValues.Apply(updated, Objects, RootObject, diagnostics);

                        return null;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new XamlUpdateResult
        {
            Applied = true,
            Strategy = strategy,
            Changes = changes,
            Diagnostics = [.. diagnostics],
        };
    }

    /// <summary>
    /// Writes every change onto the objects, or writes none of them.
    /// </summary>
    /// <remarks>
    /// Each change is checked before any is made. A run that stopped halfway would leave the
    /// objects agreeing with neither document, and nothing would say where the boundary was.
    /// </remarks>
    private bool Write(
        ImmutableArray<XamlDocumentChange> changes,
        List<(XamlDocumentChange Change, TextProjection Projection)> fragments,
        List<MarkupDiagnostic> diagnostics)
    {
        var writes = new List<(object Target, XamlMemberDescriptor Member, object? Value)>();
        var rebuilds = new List<(XamlDocumentChange Change, object Previous, object Fresh, Uri? RuntimeUri)>();
        var reorders = new List<(XamlElement Parent, IReadOnlyList<XamlElement> Order)>();

        // Building every fragment first means a fragment that will not build refuses the update
        // rather than stopping halfway through a tree that is already part-way rebuilt.
        foreach ((XamlDocumentChange change, TextProjection fragment) in fragments)
        {
            if (change.OldElement is not { } element || Objects.GetObject(element) is not { } previous)
            {
                diagnostics.Add(MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.UpdateNotApplied,
                    $"{change} names an element that produced no object.",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri));

                return false;
            }

            (object? fresh, Uri? runtimeUri) = Build(fragment, diagnostics);

            if (fresh is null)
            {
                return false;
            }

            rebuilds.Add((change, previous, fresh, runtimeUri));
        }

        foreach (XamlDocumentChange change in changes)
        {
            if (Rebuilds(change.Strategy))
            {
                continue;
            }

            if (change.Strategy == XamlUpdateStrategy.UpdateDesignValue)
            {
                // Design values are reapplied wholesale once the document has advanced, because
                // that is the same walk a design-mode load does and there is only one of it.
                continue;
            }

            if (change.Strategy == XamlUpdateStrategy.ReorderChildren)
            {
                if (change.OldElement is not { } parent
                    || change.NewElement is not { } reordered
                    || XamlElementIdentity.Pair([.. parent.Elements], [.. reordered.Elements]) is not { } pairs)
                {
                    diagnostics.Add(MarkupDiagnostic.Synchronization(
                        XamlLoaderDiagnosticCodes.UpdateNotApplied,
                        $"{change} does not say which child went where.",
                        MarkupDiagnosticSeverity.Error,
                        Document.Uri));

                    return false;
                }

                // The elements of the loaded document, in the order the new one gives them: the
                // map is keyed by the document the objects were built from.
                reorders.Add((parent, [.. pairs.Select(static pair => pair.Before)]));

                continue;
            }

            if (change.OldElement is not { } element
                || change.NewElement is not { } updatedElement
                || change.MemberName is not { } name)
            {
                diagnostics.Add(MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.UpdateNotApplied,
                    $"{change} does not say which member of which element changed.",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri));

                return false;
            }

            if (Objects.GetObject(element) is not { } target)
            {
                diagnostics.Add(MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.UpdateNotApplied,
                    $"<{element.Name}> produced no object, so {name} has nothing to be set on.",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri,
                    element.NameSpan));

                return false;
            }

            XamlMemberDescriptor member = XamlMemberResolver.Instance.Resolve(target.GetType(), name);

            if (!member.IsResolved || member.IsReadOnly || !member.CanWrite)
            {
                diagnostics.Add(MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.UnresolvedMember,
                    $"{name} is not a writable member of {target.GetType().Name}.",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri,
                    element.NameSpan));

                return false;
            }

            string text = updatedElement.GetAttribute(XamlQualifiedName.Parse(name))?.GetValueText() ?? string.Empty;

            writes.Add((target, member, XamlValueConversion.Convert(member.ValueType, text, diagnostics)));
        }

        // Before anything is rebuilt: a reorder moves the objects that already exist, and a
        // rebuild that ran first would have taken one of them out of the collection to be moved.
        foreach ((XamlElement parent, IReadOnlyList<XamlElement> order) in reorders)
        {
            if (!XamlObjectReplacement.Reorder(Objects, parent, order, diagnostics))
            {
                return false;
            }
        }

        foreach ((XamlDocumentChange change, object previous, object fresh, Uri? runtimeUri) in rebuilds)
        {
            bool replaced = change.ReplacesObject
                ? XamlObjectReplacement.Replace(Objects, change.OldElement!, previous, fresh, diagnostics)
                : XamlObjectReplacement.ReplaceContent(previous, fresh, change.OldElement!, diagnostics);

            if (!replaced)
            {
                return false;
            }

            if (runtimeUri is not null)
            {
                _fragments[runtimeUri] = fragments.First(entry => entry.Change == change).Projection;
            }
        }

        foreach ((object target, XamlMemberDescriptor member, object? value) in writes)
        {
            XamlDesignValues.Write(target, member, value);
        }

        return true;
    }

    /// <summary>Builds the objects a projected fragment describes.</summary>
    /// <remarks>
    /// Through Avalonia's own runtime loader, like any other load. It names the text it is given,
    /// and that name is how the object map later tells objects built from this fragment from the
    /// ones built from the document — which is what keeps them traceable to their markup.
    /// </remarks>
    private (object? Fresh, Uri? RuntimeUri) Build(TextProjection fragment, List<MarkupDiagnostic> diagnostics)
    {
        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = Options.LocalAssembly,
            UseCompiledBindingsByDefault = Options.UseCompiledBindingsByDefault,
            DesignMode = Options.Mode == XamlLoadMode.Design,
            CreateSourceInfo = true,
            DiagnosticHandler = diagnostic =>
            {
                diagnostics.Add(Translate(diagnostic, Document, fragment));

                return diagnostic.Severity;
            },
        };

        try
        {
            object fresh = AvaloniaRuntimeXamlLoader.Load(
                new RuntimeXamlLoaderDocument(Document.BaseUri, fragment.Text.ToString()), configuration);

            return (fresh, XamlSourceInfo.GetXamlSourceInfo(fresh)?.SourceUri);
        }
        catch (Exception error)
        {
            diagnostics.Add(MarkupDiagnostic.Synchronization(
                XamlLoaderDiagnosticCodes.UpdateNotApplied,
                $"Rebuilding part of the document failed: {error.Message}",
                MarkupDiagnosticSeverity.Error,
                Document.Uri));

            return (null, null);
        }
    }

    /// <summary>Reports whether a strategy is one that builds markup again rather than setting a value.</summary>
    private static bool Rebuilds(XamlUpdateStrategy strategy) =>
        strategy is XamlUpdateStrategy.ReplaceResource
            or XamlUpdateStrategy.ReloadStyle
            or XamlUpdateStrategy.ReloadTheme
            or XamlUpdateStrategy.ReloadTemplate
            or XamlUpdateStrategy.ReloadSubtree;

    /// <summary>Records an update that was not applied, keeping the document it was offered.</summary>
    private XamlUpdateResult Refuse(
        XamlDocument updated,
        XamlUpdateStrategy strategy,
        ImmutableArray<XamlDocumentChange> changes,
        List<MarkupDiagnostic> diagnostics,
        string code,
        string message)
    {
        PendingDocument = updated;

        diagnostics.Add(MarkupDiagnostic.Synchronization(
            code,
            message,
            code == XamlLoaderDiagnosticCodes.UpdateRejected
                ? MarkupDiagnosticSeverity.Error
                : MarkupDiagnosticSeverity.Warning,
            updated.Uri));

        return new XamlUpdateResult
        {
            Applied = false,
            Strategy = strategy,
            Changes = changes,
            Diagnostics = [.. diagnostics],
        };
    }
}
