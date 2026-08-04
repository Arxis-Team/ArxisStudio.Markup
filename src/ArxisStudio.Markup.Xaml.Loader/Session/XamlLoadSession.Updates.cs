using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.LogicalTree;
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
/// Almost every update that cannot be applied leaves the objects exactly as they were: the
/// document is compared, the includes are resolved, every fragment is built and every value
/// converted before the first live object is touched, so a refusal usually costs nothing. The
/// document that was offered is kept as <see cref="PendingDocument"/> rather than dropped,
/// because the usual reason an update fails is that the author is halfway through typing it, and
/// the next keystroke is the correction.
/// </para>
/// <para>
/// What cannot be checked in advance is user code. A validating setter may refuse a value of the
/// right type after an earlier write has landed, and a collection may refuse to take back what
/// was moved out of it. An update that gets that far and then stops is
/// <see cref="XamlUpdateOutcome.RequiresNewSession"/>: the objects agree with neither document,
/// nothing here can undo arbitrary side effects, and the session refuses every later mutation
/// rather than compounding the disagreement.
/// </para>
/// </remarks>
public sealed partial class XamlLoadSession
{
    private readonly Dictionary<Uri, TextProjection> _fragments = new(XamlUri.Comparer);

    /// <summary>How many fragments this session has built, so each gets a name of its own.</summary>
    private int _fragmentNumber;

    /// <summary>
    /// What each element of a rebuilt fragment produced, worked out while rebuilding it.
    /// </summary>
    /// <remarks>
    /// Avalonia records where it built the root of a separately loaded text and nothing below it,
    /// so the objects inside a rebuilt fragment have no recorded position of their own. Reading
    /// them as positions in the document is how a surviving control ends up attributed to
    /// whatever element sits at that line — and how its own element ends up with no object. What
    /// is known instead is the shape: the fragment was built from this element, so its children
    /// are that element's children, in order.
    /// </remarks>
    private readonly Dictionary<XamlElement, object> _rebuilt = [];

    /// <summary>
    /// Gets the most recent document that was offered and not applied, if there is one.
    /// </summary>
    /// <remarks>
    /// A refused update is not a discarded one. This is what was refused, so a caller can show
    /// it, diff it, or hand back a corrected version of it; it is cleared as soon as an update
    /// lands. After an update stopped part-way it is also the document to build the replacement
    /// session from, since it is the one the caller was trying to reach.
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
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async ValueTask<XamlUpdateResult> ApplyDocumentUpdateAsync(
        XamlDocument updated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Unusable(updated) is { } unusable)
        {
            return unusable;
        }

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
                "The document offered does not parse, so nothing was written to the objects.");
        }

        ImmutableArray<XamlDocumentChange> changes = XamlDocumentDiff.Compare(Document, updated);
        XamlUpdateStrategy strategy = XamlDocumentDiff.Largest(changes);

        if (strategy == XamlUpdateStrategy.RecreateSession)
        {
            // Nothing was written, so this session is as usable as it was — it is the new
            // document that cannot be reached from here, which Strategy is what says.
            return Refuse(
                updated,
                strategy,
                changes,
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateRequiresNewSession,
                "The root element or x:Class changed. Nothing was written to the objects, and this " +
                "session goes on describing the document it loaded; create a new session to load the " +
                "new one.");
        }

        return await ApplyAsync(updated, strategy, changes, diagnostics, cancellationToken)
            .ConfigureAwait(false);
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
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async ValueTask<XamlUpdateResult> ApplySourceUpdateAsync(
        Uri resourceUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Unusable() is { } unusable)
        {
            return unusable;
        }

        var diagnostics = new List<MarkupDiagnostic>();

        TextProjection projection = await XamlDocumentProjector
            .ProjectAsync(Document, Environment, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        // Nothing the document reaches changed, whatever the caller was told about the file.
        if (string.Equals(projection.Text.ToString(), Projection.Text.ToString(), StringComparison.Ordinal))
        {
            return new XamlUpdateResult
            {
                Outcome = XamlUpdateOutcome.Applied,
                Strategy = XamlUpdateStrategy.None,
                Changes = [],
                Diagnostics = [.. diagnostics],
            };
        }

        // The included content is not part of the document, so there is no element of it to
        // set a property on. What has to be built again is whatever element of the document
        // the include was expanded inside — every one of them, because which include's
        // expansion changed is not a question the projected text answers.
        var hosts = new List<XamlElement>();
        bool atRoot = false;

        foreach (XamlResourceReference reference in Document.GetResourceReferences())
        {
            if (Host(reference.Element) is { } host)
            {
                if (!hosts.Contains(host))
                {
                    hosts.Add(host);
                }
            }
            else
            {
                atRoot = true;
            }
        }

        var builder = ImmutableArray.CreateBuilder<XamlDocumentChange>();

        foreach (XamlElement host in hosts)
        {
            builder.Add(new XamlDocumentChange(XamlUpdateStrategy.ReplaceResource, host, host, null)
            {
                ReplacesObject = true,
            });
        }

        // An include with nothing between it and the root — a theme file is nothing else — has
        // no smaller element to rebuild. What is rebuilt is then the root's content rather than
        // the root: the object itself stays, because the session is built around it and the
        // caller holds it, and only what is inside it is built again.
        if (atRoot && Document.Root is { } root)
        {
            builder.Add(new XamlDocumentChange(XamlUpdateStrategy.ReplaceResource, root, root, null));
        }

        ImmutableArray<XamlDocumentChange> changes = builder.ToImmutable();

        if (changes.IsEmpty)
        {
            return Refuse(
                Document,
                XamlUpdateStrategy.RecreateSession,
                [],
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateRequiresNewSession,
                $"'{XamlUri.ToDisplayString(resourceUri)}' is reached by no element of this document " +
                "that can be rebuilt. Nothing was written to the objects; create a new session from " +
                "the same document to pick the file up.");
        }

        return await ApplyAsync(
                Document, XamlUpdateStrategy.ReplaceResource, changes, diagnostics, cancellationToken)
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

        XamlMutationOutcome written = await _dispatcher
            .InvokeAsync(() => Write(changes, fragments, diagnostics), cancellationToken)
            .ConfigureAwait(false);

        if (written == XamlMutationOutcome.Refused)
        {
            return Refuse(
                updated,
                strategy,
                changes,
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateNotApplied,
                "The update could not be applied. Nothing was written to the objects, so they still " +
                "describe the document this session loaded.");
        }

        if (written == XamlMutationOutcome.Inconsistent)
        {
            return Break(updated, strategy, changes, diagnostics);
        }

        // Past here the objects have moved. Anything that goes wrong from now on — including the
        // token being cancelled — leaves them describing something no document says, so the
        // session is marked before the failure is allowed out.
        try
        {
            return await FinishAsync(updated, strategy, changes, projection, diagnostics, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            RequireRecreation();

            throw;
        }
    }

    /// <summary>Adopts the new document once its changes are on the objects.</summary>
    private async ValueTask<XamlUpdateResult> FinishAsync(
        XamlDocument updated,
        XamlUpdateStrategy strategy,
        ImmutableArray<XamlDocumentChange> changes,
        TextProjection projection,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        Adopt(updated, projection);

        // Design values are re-applied from the document rather than patched one at a time.
        // Nothing re-evaluates on an update, so the document is the only place that says what
        // they are now, and applying all of them is the same walk a design-mode load does.
        if (Options.Mode == XamlLoadMode.Design)
        {
            await _dispatcher
                .InvokeAsync<object?>(
                    () =>
                    {
                        XamlDesignValues.Apply(
                            updated, Objects, RootObject, Environment.MemberResolver, diagnostics);

                        return null;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new XamlUpdateResult
        {
            Outcome = XamlUpdateOutcome.Applied,
            Strategy = strategy,
            Changes = changes,
            Diagnostics = [.. diagnostics],
        };
    }

    /// <summary>Moves the session onto the document its objects now describe.</summary>
    private void Adopt(XamlDocument updated, TextProjection projection)
    {
        // What survives the change keeps the object it already had, paired by where it sits.
        // Rebuilding that from Avalonia's recorded positions would read them against a document
        // they predate, and an update that added a line would break every one after it.
        var carried = new Dictionary<XamlElement, object>(Objects.Carry(Document, updated));

        // What a rebuild worked out wins over what the pairing carried: the carried object is the
        // one that used to stand there, and a rebuild has just replaced it.
        foreach ((XamlElement element, object target) in _rebuilt)
        {
            carried[element] = target;
        }

        _rebuilt.Clear();

        Document = updated;
        Projection = projection;
        Objects = XamlObjectMap.Build(
            updated, RootObject, projection, _fragments, carried, Environment.MemberResolver);
        PendingDocument = null;

        // A fragment whose objects the walk never reached has been replaced by a later one, and
        // its projection describes text that is no longer anywhere in the tree.
        foreach (Uri stale in _fragments.Keys.Where(uri => !Objects.ObservedSources.Contains(uri)).ToArray())
        {
            _fragments.Remove(stale);
        }
    }

    /// <summary>
    /// Writes every change onto the objects, or reports how far it got before it stopped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything that can be checked is checked before anything is written: that each element
    /// still has an object, that each member exists and can be written, and that the text converts
    /// to something the member can hold. A change refused at that point costs nothing, because
    /// nothing has been done yet, and the run reports
    /// <see cref="XamlMutationOutcome.Refused"/>.
    /// </para>
    /// <para>
    /// What cannot be checked in advance is user code: a setter that refuses a value of the right
    /// type, a collection that will not take back what was moved out of it. Once one step has
    /// written to a live object, a later step that merely refuses is no longer a refusal — the
    /// objects have moved and the document cannot follow them — so from that point every failure
    /// is <see cref="XamlMutationOutcome.Inconsistent"/> and the session must be replaced.
    /// </para>
    /// </remarks>
    private XamlMutationOutcome Write(
        ImmutableArray<XamlDocumentChange> changes,
        List<(XamlDocumentChange Change, TextProjection Projection)> fragments,
        List<MarkupDiagnostic> diagnostics)
    {
        var writes = new List<(object Target, XamlMemberDescriptor Member, object? Value)>();
        var rebuilds = new List<(XamlDocumentChange Change, object Previous, object Fresh, Uri? RuntimeUri)>();
        var reorders = new List<(XamlElement Parent, IReadOnlyList<XamlElement> Order)>();

        // Cleared here rather than after a successful apply: an attempt that gets part-way and
        // then refuses leaves pairs behind, keyed by elements of a document this session never
        // adopted and pointing at objects it threw away. Carrying those into the next update
        // would answer a question about this document with an object from a rejected one.
        _rebuilt.Clear();

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

                return XamlMutationOutcome.Refused;
            }

            (object? fresh, Uri? runtimeUri) = Build(fragment, diagnostics);

            if (fresh is null)
            {
                return XamlMutationOutcome.Refused;
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
                    || XamlElementIdentity.Pair([.. parent.Elements], [.. reordered.Elements]) is not { } pairing)
                {
                    diagnostics.Add(MarkupDiagnostic.Synchronization(
                        XamlLoaderDiagnosticCodes.UpdateNotApplied,
                        $"{change} does not say which child went where.",
                        MarkupDiagnosticSeverity.Error,
                        Document.Uri));

                    return XamlMutationOutcome.Refused;
                }

                // The elements of the loaded document, in the order the new one gives them, and
                // only the ones that produced an object: the map is keyed by the document the
                // objects were built from, and a property element is not one of them.
                reorders.Add((parent, [.. pairing.Content.Select(static pair => pair.Before)]));

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

                return XamlMutationOutcome.Refused;
            }

            if (Objects.GetObject(element) is not { } target)
            {
                diagnostics.Add(MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.UpdateNotApplied,
                    $"<{element.Name}> produced no object, so {name} has nothing to be set on.",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri,
                    element.NameSpan));

                return XamlMutationOutcome.Refused;
            }

            XamlMemberDescriptor member = Environment.MemberResolver.Resolve(target.GetType(), name);

            if (!member.IsResolved || member.IsReadOnly || !member.CanWrite)
            {
                diagnostics.Add(MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.UnresolvedMember,
                    $"{name} is not a writable member of {target.GetType().Name}.",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri,
                    element.NameSpan));

                return XamlMutationOutcome.Refused;
            }

            XamlAttribute? written = updatedElement.GetAttribute(XamlQualifiedName.Parse(name));
            XamlValueConversionResult value = member.ConvertFromText(written?.GetValueText() ?? string.Empty);

            // Asked before anything is written rather than found out by the setter throwing. Text
            // the member cannot hold is an ordinary user error — half a value, typed so far — and
            // refusing now is what keeps the objects and the document from disagreeing over it.
            if (!value.Succeeded)
            {
                diagnostics.Add(MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.IncompatibleValue,
                    $"{target.GetType().Name}.{name} cannot be set: {value.Error}",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri,
                    written?.Span ?? element.NameSpan));

                return XamlMutationOutcome.Refused;
            }

            writes.Add((target, member, value.Value));
        }

        // Nothing above this line has touched a live object; everything below it does. Once one
        // step has, a later step that merely refuses can no longer be reported as a refusal.
        var mutated = false;

        // Before anything is rebuilt: a reorder moves the objects that already exist, and a
        // rebuild that ran first would have taken one of them out of the collection to be moved.
        foreach ((XamlElement parent, IReadOnlyList<XamlElement> order) in reorders)
        {
            XamlMutationOutcome reordered = XamlObjectReplacement.Reorder(
                Objects, parent, order, Environment.MemberResolver, diagnostics);

            if (reordered != XamlMutationOutcome.Applied)
            {
                return Worsen(reordered, mutated, diagnostics, Document.Uri);
            }

            mutated = true;
        }

        foreach ((XamlDocumentChange change, object previous, object fresh, Uri? runtimeUri) in rebuilds)
        {
            XamlMutationOutcome replaced = change.ReplacesObject
                ? XamlObjectReplacement.Replace(
                    Objects, change.OldElement!, previous, fresh, Environment.MemberResolver, diagnostics)
                : XamlObjectReplacement.ReplaceContent(
                    previous, fresh, change.OldElement!, Environment.MemberResolver, diagnostics);

            if (replaced != XamlMutationOutcome.Applied)
            {
                return Worsen(replaced, mutated, diagnostics, Document.Uri);
            }

            mutated = true;

            if (runtimeUri is not null)
            {
                _fragments[runtimeUri] = fragments.First(entry => entry.Change == change).Projection;
            }

            // What is now in the tree is the fresh object when it replaced the old one, and the
            // old one carrying the fresh one's content when only the content was rebuilt.
            if (change.NewElement is { } rebuilt)
            {
                Pair(rebuilt, change.ReplacesObject ? fresh : previous, _rebuilt);
            }
        }

        foreach ((object target, XamlMemberDescriptor member, object? value) in writes)
        {
            try
            {
                XamlDesignValues.Write(target, member, value);
            }
            catch (Exception error) when (error is InvalidCastException
                or ArgumentException
                or InvalidOperationException
                or TargetInvocationException)
            {
                // A value of the right type the property still refuses — a validating setter, a
                // negative length. Reported like any other refused value; an exception out of an
                // update is for broken invariants, not for what a document says.
                //
                // TargetInvocationException among them because a CLR property and an attached
                // accessor pair are written by reflection, which wraps whatever the setter threw.
                Exception refusal = (error as TargetInvocationException)?.InnerException ?? error;

                diagnostics.Add(MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.IncompatibleValue,
                    $"{target.GetType().Name}.{member.Name} refused the value: {refusal.Message}",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri));

                // One property set, which either happened or did not: the object is as it was.
                // Whether that leaves the session usable depends on what ran before it.
                return Worsen(XamlMutationOutcome.Refused, mutated, diagnostics, Document.Uri);
            }

            mutated = true;
        }

        return XamlMutationOutcome.Applied;
    }

    /// <summary>
    /// Reads a step's own answer against how far the run had already got.
    /// </summary>
    /// <remarks>
    /// A step that refused touched nothing, which is only the same as the update having touched
    /// nothing while no earlier step has written. Once one has, the objects have moved and the
    /// document cannot follow them, so the honest answer is that this session describes neither.
    /// </remarks>
    private static XamlMutationOutcome Worsen(
        XamlMutationOutcome step,
        bool mutated,
        List<MarkupDiagnostic> diagnostics,
        Uri? documentUri)
    {
        if (step != XamlMutationOutcome.Refused || !mutated)
        {
            return step;
        }

        diagnostics.Add(MarkupDiagnostic.Synchronization(
            XamlLoaderDiagnosticCodes.SessionRequiresRecreation,
            "The change above was refused after earlier changes of the same update had already been " +
            "written, so the objects carry part of the new document and the session's document carries " +
            "none of it.",
            MarkupDiagnosticSeverity.Error,
            documentUri));

        return XamlMutationOutcome.Inconsistent;
    }

    /// <summary>
    /// Names a fragment so that the objects built from it can be told from every other object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A name of its own, and a different one each time. Handing a fragment the document's own
    /// base URI made its objects report the same name as the document's, and the map then read
    /// positions recorded against a few lines of fragment as positions in the whole document —
    /// which silently attributed a surviving control to whatever element happened to sit at that
    /// line, and left its own element with no object at all.
    /// </para>
    /// <para>
    /// Expressed as a query on the document's own URI, so that a relative include inside the
    /// fragment still resolves against the folder the document lives in.
    /// </para>
    /// </remarks>
    private Uri FragmentUri()
    {
        Uri anchor = Document.BaseUri ?? Document.Uri ?? new Uri("markup:///document");

        return new Uri(anchor, $"?fragment={++_fragmentNumber}");
    }

    /// <summary>Builds the objects a projected fragment describes.</summary>
    /// <remarks>
    /// Through Avalonia's own runtime loader, like any other load. It names the text it is given,
    /// and that name is how the object map later tells objects built from this fragment from the
    /// ones built from the document — which is what keeps them traceable to their markup.
    /// </remarks>
    private (object? Fresh, Uri? RuntimeUri) Build(TextProjection fragment, List<MarkupDiagnostic> diagnostics)
    {
        Uri name = FragmentUri();

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
                new RuntimeXamlLoaderDocument(name, fragment.Text.ToString()), configuration);

            // What Avalonia recorded, when it recorded anything: the name given above is the
            // fallback, so a fragment is never keyed by nothing and never keyed by the
            // document's own name.
            return (fresh, XamlSourceInfo.GetXamlSourceInfo(fresh)?.SourceUri ?? name);
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

    /// <summary>
    /// Pairs an element with the object it produced, and its children with that object's, by
    /// position.
    /// </summary>
    /// <remarks>
    /// The same conservative rule the rest of the update path uses: where the two sides stop
    /// having the same shape, the walk stops descending rather than guessing which child is
    /// which. A property element contributes what is inside it — a resource dictionary, a
    /// template — and those are not logical children, so a mismatch there simply ends the
    /// descent, and what is below keeps whatever the map can work out for itself.
    /// </remarks>
    private static void Pair(XamlElement element, object target, Dictionary<XamlElement, object> into)
    {
        into[element] = target;

        XamlElement[] children = [.. element.ContentElements];
        object[] objects = target is ILogical logical ? [.. logical.LogicalChildren] : [];

        if (children.Length != objects.Length)
        {
            return;
        }

        for (int index = 0; index < children.Length; index++)
        {
            Pair(children[index], objects[index], into);
        }
    }

    /// <summary>Reports whether a strategy is one that builds markup again rather than setting a value.</summary>
    private static bool Rebuilds(XamlUpdateStrategy strategy) =>
        strategy is XamlUpdateStrategy.ReplaceResource
            or XamlUpdateStrategy.ReloadStyle
            or XamlUpdateStrategy.ReloadTheme
            or XamlUpdateStrategy.ReloadTemplate
            or XamlUpdateStrategy.ReloadSubtree;

    /// <summary>
    /// Records an update that was refused before anything was written, keeping the document it
    /// was offered.
    /// </summary>
    /// <remarks>
    /// Only for failures that certainly touched no live object. Everything that reaches one goes
    /// through <see cref="Break"/> instead, because a refusal is a promise about the objects and
    /// this one cannot be made twice.
    /// </remarks>
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
            Outcome = XamlUpdateOutcome.RejectedCleanly,
            Strategy = strategy,
            Changes = changes,
            Diagnostics = [.. diagnostics],
        };
    }

    /// <summary>
    /// Records an update that stopped after it had begun writing, and closes the session to
    /// further changes.
    /// </summary>
    /// <remarks>
    /// The document is kept as <see cref="PendingDocument"/> because it is what a replacement
    /// session should be built from: it is the state the caller was trying to reach, and the one
    /// the objects are now part-way towards.
    /// </remarks>
    private XamlUpdateResult Break(
        XamlDocument updated,
        XamlUpdateStrategy strategy,
        ImmutableArray<XamlDocumentChange> changes,
        List<MarkupDiagnostic> diagnostics)
    {
        PendingDocument = updated;

        RequireRecreation();

        diagnostics.Add(MarkupDiagnostic.Synchronization(
            XamlLoaderDiagnosticCodes.SessionRequiresRecreation,
            "The update stopped after it had begun writing to the objects, so they describe neither " +
            "the document this session loaded nor the one offered, and no further change will be " +
            "accepted. Create a new session from PendingDocument and discard this one.",
            MarkupDiagnosticSeverity.Error,
            updated.Uri));

        return new XamlUpdateResult
        {
            Outcome = XamlUpdateOutcome.RequiresNewSession,
            Strategy = strategy,
            Changes = changes,
            Diagnostics = [.. diagnostics],
        };
    }

    /// <summary>
    /// Refuses a mutation outright when a previous one already left the session describing
    /// nothing, or <see langword="null"/> when the session is still worth using.
    /// </summary>
    /// <remarks>
    /// Deterministic on purpose: once the objects and the document disagree, every further change
    /// would be written onto a tree nobody can describe, and the second failure would be harder to
    /// explain than the first.
    /// </remarks>
    /// <param name="offered">
    /// The document this call was given, kept only when nothing else has been. A session left
    /// unusable by a cancelled update has no pending document of its own, and the next thing a
    /// caller offers is a better answer to "what should I load instead" than nothing.
    /// </param>
    private XamlUpdateResult? Unusable(XamlDocument? offered = null)
    {
        if (State == XamlSessionState.Usable)
        {
            return null;
        }

        // Not through Break: the state is already set, and a document offered after the fact must
        // not displace the one the session was actually left part-way towards.
        PendingDocument ??= offered;

        return new XamlUpdateResult
        {
            Outcome = XamlUpdateOutcome.RequiresNewSession,
            Strategy = XamlUpdateStrategy.RecreateSession,
            Changes = [],
            Diagnostics =
            [
                MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.SessionRequiresRecreation,
                    "An earlier update stopped after it had begun writing to the objects. This session " +
                    "accepts no further change; create a new session from the document you want.",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri),
            ],
        };
    }
}
