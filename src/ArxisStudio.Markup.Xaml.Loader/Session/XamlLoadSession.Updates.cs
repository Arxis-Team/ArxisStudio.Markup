using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
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
/// What cannot be checked in advance is user code. Once a setter, an accessor or a collection has
/// been invoked on a live object and thrown, nothing here can prove what it did first — a setter
/// is free to assign its field, touch a second property and then throw. An update that gets that
/// far and then stops is <see cref="XamlUpdateOutcome.RequiresNewSession"/>: the objects agree
/// with neither document, nothing can undo arbitrary side effects, and the session refuses every
/// later mutation rather than compounding the disagreement.
/// </para>
/// <para>
/// Every one of those failures keeps the document it was moving towards as
/// <see cref="PendingDocument"/>, cancellation included, because building a new session from it is
/// the documented way out and a caller told to do that must be given something to do it with.
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
    /// <remarks>
    /// Mutations of one session are serialised: an update called while another is running waits
    /// for it rather than interleaving with it. See <see cref="XamlLoadSession"/> for the whole
    /// policy, including what the synchronous editing methods do instead of waiting.
    /// </remarks>
    /// <param name="updated">The document as it now reads.</param>
    /// <param name="cancellationToken">A token to observe while waiting and while updating.</param>
    /// <returns>What the update did, and everything noticed on the way.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="updated"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async ValueTask<XamlUpdateResult> ApplyDocumentUpdateAsync(
        XamlDocument updated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updated);

        using XamlMutationGate.XamlMutationLease lease =
            await _mutation.EnterAsync(cancellationToken).ConfigureAwait(false);

        // Inside the gate, where the answer cannot change under the caller. Reading either of
        // these before taking a turn is reading a session somebody else may be part-way through.
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (Unusable() is { } unusable)
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

        (ImmutableArray<XamlDocumentChange> changes, Dictionary<XamlDocumentChange, XamlInPlaceWrite> inPlace) =
            await SettleAsync(XamlDocumentDiff.Compare(Document, updated), cancellationToken).ConfigureAwait(false);

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
                "The root element or x:Class changed, or the root takes a value only a new root can. " +
                "Nothing was written to the objects, and this session goes on describing the document it " +
                "loaded; create a new session to load the new one.");
        }

        return await ApplyAsync(updated, strategy, changes, inPlace, diagnostics, cancellationToken)
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
    /// <param name="cancellationToken">A token to observe while waiting and while updating.</param>
    /// <returns>What the update did, and everything noticed on the way.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resourceUri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async ValueTask<XamlUpdateResult> ApplySourceUpdateAsync(
        Uri resourceUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);

        using XamlMutationGate.XamlMutationLease lease =
            await _mutation.EnterAsync(cancellationToken).ConfigureAwait(false);

        // Inside the gate, where the answer cannot change under the caller. Reading either of
        // these before taking a turn is reading a session somebody else may be part-way through.
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // No document is offered to a source update, so there is none to remember.
        if (Unusable() is { } unusable)
        {
            return unusable;
        }

        var diagnostics = new List<MarkupDiagnostic>();

        // Projected the way the load projected it, so that the comparison below compares includes
        // and nothing else: text the load withheld and this did not would read as a change.
        TextProjection projection = await XamlDocumentProjector
            .ProjectAsync(
                Document,
                fragment: null,
                Environment,
                diagnostics,
                (await FindingsAsync(Document, diagnostics, cancellationToken).ConfigureAwait(false)).Withheld,
                cancellationToken)
            .ConfigureAwait(false);

        // Nothing the document reaches changed, whatever the caller was told about the file. Asked
        // of what the projections took from the includes rather than of the whole text: the
        // document's own text moves under a synchronous edit, which writes the session's document
        // and leaves the projection the objects were built from as it was — and read whole, the two
        // differed after any SetValue, and an include nobody touched rebuilt what it reaches.
        if (Spliced(projection).SequenceEqual(Spliced(Projection)))
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
                Document, XamlUpdateStrategy.ReplaceResource, changes, [], diagnostics, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds elements of the document again, as they stand — for a control whose own markup
    /// changed while the document that places it did not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A control written with <c>x:Class</c> is populated from its own markup when it is
    /// constructed, and an instance already in the tree goes on showing what that markup said
    /// then. A host that has given the class a newer document to populate from
    /// (<see cref="XamlLivePopulation"/>) asks for the elements that place it to be built again,
    /// and they come back populated from that document.
    /// </para>
    /// <para>
    /// Each element is rebuilt the way a change to it would be: in place of the object it produced,
    /// or with the smallest container it sits in — a style, a theme, a template, a keyed resource —
    /// and together with whatever around it a static reference inside it reads. An element inside
    /// another one asked for is built by the outer one. The root is not an element this rebuilds:
    /// the session is built around it, so asking for it is refused with nothing written and
    /// <see cref="XamlUpdateStrategy.RecreateSession"/> — a new session from the same document is
    /// what builds the root again.
    /// </para>
    /// <para>
    /// The elements are elements of <see cref="Document"/> as it stands when this update's turn
    /// comes. One of a document an earlier update has replaced since is refused with nothing
    /// written; find it again in the session's document and ask again.
    /// </para>
    /// </remarks>
    /// <param name="elements">The elements to build again.</param>
    /// <param name="cancellationToken">A token to observe while waiting and while updating.</param>
    /// <returns>What the update did, and everything noticed on the way.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="elements"/> is <see langword="null"/>, or holds a <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async ValueTask<XamlUpdateResult> ApplyRebuildAsync(
        IEnumerable<XamlElement> elements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(elements);

        XamlElement[] requested = [.. elements];

        if (Array.Exists(requested, static element => element is null))
        {
            throw new ArgumentNullException(nameof(elements), "An element to rebuild cannot be null.");
        }

        using XamlMutationGate.XamlMutationLease lease =
            await _mutation.EnterAsync(cancellationToken).ConfigureAwait(false);

        // Inside the gate, where the answer cannot change under the caller. Reading either of
        // these before taking a turn is reading a session somebody else may be part-way through.
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (Unusable() is { } unusable)
        {
            return unusable;
        }

        var diagnostics = new List<MarkupDiagnostic>();

        // Asked here rather than of the caller: an update queued ahead of this one replaces the
        // document while this one waits, and an element found before it is then an element of
        // nothing the objects describe.
        if (Array.Find(requested, element => !ReferenceEquals(element.Document, Document)) is { } stale)
        {
            return Refuse(
                Document,
                XamlUpdateStrategy.None,
                [],
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateNotApplied,
                $"<{stale.Name}> is not an element of the document this session describes now; an update " +
                "has replaced that document since. Nothing was written to the objects — find the element " +
                "in the session's document and ask again.");
        }

        (ImmutableArray<XamlDocumentChange> changes, Dictionary<XamlDocumentChange, XamlInPlaceWrite> inPlace) =
            await SettleAsync(
                    [
                        .. requested.Select(element => XamlDocumentDiff.Escalate(
                            new XamlDocumentChange(XamlUpdateStrategy.ReloadSubtree, element, element, null),
                            Document.Root)),
                    ],
                    cancellationToken)
                .ConfigureAwait(false);

        XamlUpdateStrategy strategy = XamlDocumentDiff.Largest(changes);

        if (strategy == XamlUpdateStrategy.None)
        {
            return new XamlUpdateResult
            {
                Outcome = XamlUpdateOutcome.Applied,
                Strategy = strategy,
                Changes = [],
                Diagnostics = [],
            };
        }

        if (strategy == XamlUpdateStrategy.RecreateSession)
        {
            return Refuse(
                Document,
                strategy,
                changes,
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateRequiresNewSession,
                "The root element is among the elements to build again, and the session is built around " +
                "the root. Nothing was written to the objects; create a new session from the same document " +
                "to build the root again.");
        }

        return await ApplyAsync(Document, strategy, changes, inPlace, diagnostics, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Decides, for every value change that might be written where it stands, whether it can be —
    /// and has the element rebuilt instead where it cannot.
    /// </summary>
    /// <remarks>
    /// Before anything is projected or written, because a change rebuilt instead needs a fragment
    /// of its own, and a change found out to be unwritable once writing had begun would be a broken
    /// session rather than a rebuilt element. The answers are kept: what a binding binds and what a
    /// static member holds were worked out here, through the environment, and the writing turn is no
    /// place to resolve a type.
    /// </remarks>
    private async ValueTask<(ImmutableArray<XamlDocumentChange> Changes, Dictionary<XamlDocumentChange, XamlInPlaceWrite> InPlace)>
        SettleAsync(ImmutableArray<XamlDocumentChange> changes, CancellationToken cancellationToken)
    {
        var inPlace = new Dictionary<XamlDocumentChange, XamlInPlaceWrite>();
        var settled = new List<XamlDocumentChange>(changes.Length);

        foreach (XamlDocumentChange change in changes)
        {
            // A handler is not a value: it is hooked up when its element is built, so a handler
            // written, renamed or moved is its element built again — with the handlers a rebuilt
            // part gets — and at the root, whose own handlers were hooked up by its load, a new session.
            if (change.Strategy == XamlUpdateStrategy.SetProperty && NamesAnEvent(change))
            {
                settled.Add(XamlDocumentDiff.Escalate(change, Document.Root));

                continue;
            }

            if (change.Strategy is not (XamlUpdateStrategy.ClearProperty or XamlUpdateStrategy.SetExpression))
            {
                settled.Add(change);

                continue;
            }

            if (await InPlaceAsync(change, cancellationToken).ConfigureAwait(false) is { } write)
            {
                inPlace[change] = write;
                settled.Add(change);
            }
            else
            {
                settled.Add(XamlDocumentDiff.Escalate(change, Document.Root));
            }
        }

        // An element rebuilt for several of its attributes is one rebuild, and is reported once.
        var rebuilt = new HashSet<(XamlUpdateStrategy, XamlElement?, bool)>();

        return (
            [
                .. settled
                    .Where(change => !Rebuilds(change.Strategy)
                        || rebuilt.Add((change.Strategy, change.OldElement, change.ReplacesObject)))
                    .OrderBy(static change => change.Strategy),
            ],
            inPlace);
    }

    /// <summary>Reports whether the attribute a change names is an event of the object its element produced.</summary>
    private bool NamesAnEvent(XamlDocumentChange change) =>
        change.OldElement is { } element
        && change.MemberName is { } name
        && Objects.GetObject(element) is { } target
        && Environment.MemberResolver.Resolve(target.GetType(), name).Kind == XamlMemberKind.Event;

    /// <summary>Works out what a value change writes where it stands, or that it cannot be written there.</summary>
    private async ValueTask<XamlInPlaceWrite?> InPlaceAsync(XamlDocumentChange change, CancellationToken cancellationToken)
    {
        if (change.OldElement is not { } element
            || change.MemberName is not { } name
            || Objects.GetObject(element) is not { } target)
        {
            return null;
        }

        XamlMemberDescriptor member = Environment.MemberResolver.Resolve(target.GetType(), name);

        // A member nobody knows is a rebuild's to report, in Avalonia's words.
        if (!member.IsResolved || member.IsReadOnly)
        {
            return null;
        }

        if (change.Strategy == XamlUpdateStrategy.ClearProperty)
        {
            // Only an Avalonia property has a local value of its own to take out. A CLR property
            // holds what its type's constructor gave it, which only building the object again says.
            return member.AvaloniaProperty is not null && target is AvaloniaObject ? XamlInPlaceWrite.Clear : null;
        }

        return change.NewElement is { } written
            && AttributeOf(written, name)?.GetValue() is XamlMarkupExtensionValue extension
                ? await XamlInPlaceValues
                    .EvaluateAsync(
                        extension, written, member, Environment, Options.UseCompiledBindingsByDefault, cancellationToken)
                    .ConfigureAwait(false)
                : null;
    }

    /// <summary>Finds the attribute a change names, whatever prefix it is written under.</summary>
    /// <remarks>
    /// A change carries the member's local name, and an attached property of another namespace is
    /// written <c>prefix:Owner.Member</c> — looking it up unprefixed found nothing, and the value read
    /// as empty.
    /// </remarks>
    private static XamlAttribute? AttributeOf(XamlElement element, string localName) =>
        element.Attributes.FirstOrDefault(attribute => attribute is not XamlNamespaceDeclaration
            && !attribute.IsDirective
            && !attribute.IsDesignTime
            && string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal));

    /// <summary>
    /// Gets what a projection took from anywhere but the document's own text, run by run.
    /// </summary>
    /// <remarks>
    /// The included files' text, and what was written in for them — the declarations an include
    /// needed hoisted onto the root, the attributes taken out. That is what a source update can
    /// change; the document's own text is the session's to change, and it is not this comparison's.
    /// </remarks>
    private static IEnumerable<(Uri? Source, string Text)> Spliced(TextProjection projection) =>
        projection.Segments
            .Where(static segment => !segment.IsOriginal || segment.IsSynthesized)
            .Select(segment => (segment.SourceUri, projection.Text.GetText(segment.ProjectedSpan)));

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

    /// <summary>
    /// Works out what a projection of a version of the document leaves out, by the rule the load
    /// used for the first, and which of its handlers the class answers.
    /// </summary>
    /// <remarks>
    /// The rule is <see cref="XamlAttributeChecks"/>, asked about the class this session populated:
    /// the handlers it does not answer, and the <c>x:Class</c> directive when there was no class to
    /// use. What it notices on the way is reported with the update, because it is as true of the
    /// document being offered as it was of the one loaded.
    /// </remarks>
    private ValueTask<XamlAttributeFindings> FindingsAsync(
        XamlDocument document,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken) =>
        XamlAttributeChecks.RunAsync(document, _rootClass, Options.ClassUse, Environment, diagnostics, cancellationToken);

    /// <summary>
    /// Works out what the projection of a part that is about to be rebuilt leaves out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What every projection of the session leaves out, and then two things only a rebuilt part
    /// does. Every handler in it, because a part is built on its own, with no instance for Avalonia
    /// to hook a handler up to — it refused the whole part over one, so a panel holding a button
    /// the class handles could not be rebuilt at all. The handlers the class answers are hooked up
    /// once the part's objects exist (<see cref="HookHandlers"/>).
    /// </para>
    /// <para>
    /// And, when the part is the root, what makes the root the class: <c>x:Class</c> and the
    /// directives that go with it. The root's content is rebuilt from a copy of the root, and a copy
    /// built with the class is the class constructed a second time — the author's constructor run
    /// again, and for a window a second window. Without them the copy is the element the root is
    /// written as, which is all a copy is for.
    /// </para>
    /// </remarks>
    private static HashSet<TextSpan> LeftOutOf(XamlElement part, XamlDocument document, XamlAttributeFindings findings)
    {
        var spans = new HashSet<TextSpan>(findings.Withheld);

        foreach (XamlHandlerAttribute handler in findings.Handlers)
        {
            if (part.Span.Contains(handler.Attribute.Span))
            {
                spans.Add(handler.Attribute.Span);
            }
        }

        if (ReferenceEquals(part, document.Root))
        {
            foreach (string directive in RootOnlyDirectives)
            {
                if (part.GetDirectiveAttribute(directive) is { } written)
                {
                    spans.Add(written.Span);
                }
            }
        }

        return spans;
    }

    /// <summary>The directives that make a root the class it names, and mean nothing anywhere else.</summary>
    private static readonly string[] RootOnlyDirectives =
        [XamlDirectives.Class, XamlDirectives.ClassModifier, "Subclass"];

    /// <summary>Applies an update that can be made on the objects that already exist.</summary>
    private async ValueTask<XamlUpdateResult> ApplyAsync(
        XamlDocument updated,
        XamlUpdateStrategy strategy,
        ImmutableArray<XamlDocumentChange> changes,
        Dictionary<XamlDocumentChange, XamlInPlaceWrite> inPlace,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        // What the load could not hand Avalonia, no part of this update hands it either — a class
        // that was never usable, a handler with nothing to hook up to. Worked out once for the
        // document, because every fragment below is a projection of the same text.
        XamlAttributeFindings findings =
            await FindingsAsync(updated, diagnostics, cancellationToken).ConfigureAwait(false);

        // Reprojecting before anything is touched means a failure to resolve an include is a
        // refused update rather than a half-updated tree.
        TextProjection projection = await XamlDocumentProjector
            .ProjectAsync(updated, fragment: null, Environment, diagnostics, findings.Withheld, cancellationToken)
            .ConfigureAwait(false);

        // Every fragment is projected and parsed before any object is touched, for the same
        // reason: a fragment that will not build is a refused update, not a half-rebuilt tree.
        var fragments = new List<(XamlDocumentChange Change, TextProjection Projection, Type? Placed)>();

        foreach (XamlDocumentChange change in Outermost(changes.Where(static change => Rebuilds(change.Strategy))))
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
                    .ProjectAsync(
                        updated, element, Environment, diagnostics, LeftOutOf(element, updated, findings), cancellationToken)
                    .ConfigureAwait(false),
                await PlacedClassAsync(element, updated, cancellationToken).ConfigureAwait(false)));
        }

        var rootMustBeRebuilt = false;

        // One turn of the dispatcher for everything that touches the objects: the writes, the map
        // rebuilt over them, the design values applied again and the handlers of the rebuilt parts
        // hooked up. A host that has borrowed parts of the root gives them back for exactly this
        // turn, so nothing renders a root that is half given back, and the map is never walked over
        // a window whose content is somewhere else.
        XamlMutationOutcome written = await _dispatcher
            .InvokeAsync(
                () =>
                {
                    using IDisposable lent = LendRoot();

                    var kept = new List<XamlElement>();
                    XamlMutationOutcome outcome = Write(changes, inPlace, fragments, diagnostics, kept, out bool root);

                    rootMustBeRebuilt = root;

                    if (outcome == XamlMutationOutcome.Applied)
                    {
                        // Past here the objects have moved. Anything that goes wrong from now on leaves
                        // them describing something no document says, so the session is marked, and the
                        // document it was moving towards is kept, before the failure is allowed out —
                        // building a new session from PendingDocument is the documented way out, and a
                        // caller told to do that has to be given something to do it with.
                        try
                        {
                            Finish(updated, projection, fragments, findings, kept, diagnostics);
                        }
                        catch (Exception)
                        {
                            PendingDocument = updated;

                            RequireRecreation();

                            throw;
                        }
                    }

                    return outcome;
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (written == XamlMutationOutcome.Refused && rootMustBeRebuilt)
        {
            // The same answer a changed root element gets, for the same reason: nothing was
            // written, this session is as usable as it was, and the new document is out of its
            // reach because reaching it means a new root object.
            return Refuse(
                updated,
                XamlUpdateStrategy.RecreateSession,
                changes,
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateRequiresNewSession,
                "A value the root element writes as a property element changed, and a single value "
                    + "cannot be moved onto the root from a rebuilt copy of it. Nothing was written to the "
                    + "objects, and this session goes on describing the document it loaded; create a new "
                    + "session to load the new one.");
        }

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

        return new XamlUpdateResult
        {
            Outcome = XamlUpdateOutcome.Applied,
            Strategy = strategy,
            Changes = changes,
            Diagnostics = [.. diagnostics],
        };
    }

    /// <summary>Adopts the new document once its changes are on the objects.</summary>
    /// <remarks>
    /// On the owning thread, all of it, and in the same turn as the writes. Rebuilding the map reads
    /// where Avalonia recorded that it built each object, and that is read off the objects themselves
    /// — which have the same thread affinity as everything else about them; and it walks the root's
    /// children, which a host that borrows them has given back only for this turn.
    /// </remarks>
    private void Finish(
        XamlDocument updated,
        TextProjection projection,
        List<(XamlDocumentChange Change, TextProjection Projection, Type? Placed)> fragments,
        XamlAttributeFindings findings,
        List<XamlElement> kept,
        List<MarkupDiagnostic> diagnostics)
    {
        Adopt(updated, projection);

        // Design values are re-applied from the document rather than patched one at a time.
        // Nothing re-evaluates on an update, so the document is the only place that says what they
        // are now, and applying all of them is the same walk a design-mode load does.
        if (Options.Mode == XamlLoadMode.Design)
        {
            XamlDesignValues.Apply(updated, Objects, RootObject, Environment.MemberResolver, diagnostics);
        }

        HookHandlers(fragments, findings, kept, diagnostics);
    }

    /// <summary>
    /// Hooks the handlers of every rebuilt part up to the session's root, now that the part's objects
    /// exist and the map knows them.
    /// </summary>
    /// <remarks>
    /// Every handler the class answers that sits in a rebuilt part, except on an element whose object
    /// was kept — the root, whose content alone was rebuilt, or any element rebuilt that way. Its
    /// handlers were hooked up when it was built, and hooking them up again would run each one twice.
    /// </remarks>
    private void HookHandlers(
        List<(XamlDocumentChange Change, TextProjection Projection, Type? Placed)> fragments,
        XamlAttributeFindings findings,
        List<XamlElement> kept,
        List<MarkupDiagnostic> diagnostics)
    {
        foreach ((XamlDocumentChange change, _, _) in fragments)
        {
            if (change.NewElement is not { } part)
            {
                continue;
            }

            foreach (XamlHandlerAttribute handler in findings.Handlers)
            {
                if (!part.Span.Contains(handler.Element.Span)
                    || kept.Exists(element => ReferenceEquals(element, handler.Element))
                    || Objects.GetObject(handler.Element) is not { } target)
                {
                    continue;
                }

                XamlHandlers.Hook(target, handler, RootObject, Environment.MemberResolver, diagnostics, Document.Uri);
            }
        }
    }

    /// <summary>Asks a host that has borrowed parts of the root to give them back for one write.</summary>
    private IDisposable LendRoot() => Options.RootAccess?.Lend(RootObject) ?? NothingLent.Instance;

    /// <summary>The lease of a root nobody borrowed from.</summary>
    private sealed class NothingLent : IDisposable
    {
        public static NothingLent Instance { get; } = new();

        public void Dispose()
        {
        }
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
    /// <para>
    /// Every part is built before anything is written, so an update that refuses — a root that needs
    /// a new session, a later part that will not build, a member that cannot be written — has built
    /// objects that go nowhere, and so has one that stops part-way. A copy of a top level is a top
    /// level, with a platform window of its own that nothing else will close and through which the
    /// copy, and every type it was built from, stays. So whatever way the run ends, every object it
    /// built and did not leave standing in the tree is retired on the way out: the copies whose
    /// content was moved across, and the parts of an update that never got there.
    /// </para>
    /// </remarks>
    private XamlMutationOutcome Write(
        ImmutableArray<XamlDocumentChange> changes,
        Dictionary<XamlDocumentChange, XamlInPlaceWrite> inPlace,
        List<(XamlDocumentChange Change, TextProjection Projection, Type? Placed)> fragments,
        List<MarkupDiagnostic> diagnostics,
        List<XamlElement> kept,
        out bool rootMustBeRebuilt)
    {
        var unplaced = new HashSet<object>(ReferenceEqualityComparer.Instance);

        try
        {
            return WriteParts(changes, inPlace, fragments, diagnostics, kept, unplaced, out rootMustBeRebuilt);
        }
        finally
        {
            foreach (object built in unplaced)
            {
                Retire(built, diagnostics);
            }
        }
    }

    /// <summary>
    /// Does what <see cref="Write"/> describes, putting every object it builds into
    /// <paramref name="unplaced"/> and taking out each one it leaves standing in the tree.
    /// </summary>
    private XamlMutationOutcome WriteParts(
        ImmutableArray<XamlDocumentChange> changes,
        Dictionary<XamlDocumentChange, XamlInPlaceWrite> inPlace,
        List<(XamlDocumentChange Change, TextProjection Projection, Type? Placed)> fragments,
        List<MarkupDiagnostic> diagnostics,
        List<XamlElement> kept,
        HashSet<object> unplaced,
        out bool rootMustBeRebuilt)
    {
        var writes = new List<(object Target, XamlMemberDescriptor Member, XamlInPlaceWrite Write)>();
        var rebuilds = new List<(
            XamlDocumentChange Change, object Previous, object Fresh, Uri? RuntimeUri, bool ReplacesObject)>();
        var reorders = new List<(XamlElement Parent, IReadOnlyList<XamlElement> Order)>();

        rootMustBeRebuilt = false;

        // Cleared here rather than after a successful apply: an attempt that gets part-way and
        // then refuses leaves pairs behind, keyed by elements of a document this session never
        // adopted and pointing at objects it threw away. Carrying those into the next update
        // would answer a question about this document with an object from a rejected one.
        _rebuilt.Clear();

        // Building every fragment first means a fragment that will not build refuses the update
        // rather than stopping halfway through a tree that is already part-way rebuilt.
        foreach ((XamlDocumentChange change, TextProjection fragment, Type? placed) in fragments)
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

            (object? fresh, Uri? runtimeUri) = Build(fragment, placed, diagnostics);

            if (fresh is null)
            {
                return XamlMutationOutcome.Refused;
            }

            unplaced.Add(fresh);

            bool replacesObject = change.ReplacesObject;

            // Rebuilding what an element holds leaves the object alone, which is only possible
            // while everything that changed can be moved onto it from the rebuilt copy: its
            // content, and the dictionaries and lists it writes as property elements. A single
            // value written that way cannot — so the object is put in instead, which is the
            // larger of the two and always enough. The comparison cannot decide this, because it
            // reads syntax and the difference is what the member is.
            if (!replacesObject
                && !XamlObjectReplacement.CanReplaceContent(
                    previous, fresh, element, change.NewElement, Environment.MemberResolver))
            {
                // Except at the root, which has no slot to be put into. Found out here, with
                // nothing touched, so it is still a refusal.
                if (ReferenceEquals(element, Document.Root))
                {
                    rootMustBeRebuilt = true;

                    return XamlMutationOutcome.Refused;
                }

                replacesObject = true;
            }

            rebuilds.Add((change, previous, fresh, runtimeUri, replacesObject));
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

            if (inPlace.TryGetValue(change, out XamlInPlaceWrite? settled))
            {
                // Worked out before the turn began, against the object's own member; all that is
                // left is that the object is still there to write it on.
                if (change.OldElement is not { } holder
                    || change.MemberName is not { } memberName
                    || Objects.GetObject(holder) is not { } owner)
                {
                    diagnostics.Add(MarkupDiagnostic.Synchronization(
                        XamlLoaderDiagnosticCodes.UpdateNotApplied,
                        $"{change} names an element that produced no object.",
                        MarkupDiagnosticSeverity.Error,
                        Document.Uri));

                    return XamlMutationOutcome.Refused;
                }

                writes.Add((owner, Environment.MemberResolver.Resolve(owner.GetType(), memberName), settled));

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

            XamlAttribute? written = AttributeOf(updatedElement, name);
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

            writes.Add((target, member, new XamlInPlaceWrite.Setting(value.Value)));
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

        foreach ((XamlDocumentChange change, object previous, object fresh, Uri? runtimeUri, bool replacesObject)
            in rebuilds)
        {
            XamlMutationOutcome replaced = replacesObject
                ? XamlObjectReplacement.Replace(
                    Objects, change.OldElement!, previous, fresh, Environment.MemberResolver, diagnostics)
                : XamlObjectReplacement.ReplaceContent(
                    previous, fresh, change.OldElement!, change.NewElement, Environment.MemberResolver, diagnostics);

            if (replaced != XamlMutationOutcome.Applied)
            {
                return Worsen(replaced, mutated, diagnostics, Document.Uri);
            }

            mutated = true;

            if (replacesObject)
            {
                // Standing in the tree now, where the object it replaced stood.
                unplaced.Remove(fresh);
            }
            else if (change.NewElement is { } holder)
            {
                // The copy only carried the content across, and is finished with: it stays among
                // what is retired on the way out.
                kept.Add(holder);
            }

            if (runtimeUri is not null)
            {
                _fragments[runtimeUri] = fragments.First(entry => entry.Change == change).Projection;
            }

            // What is now in the tree is the fresh object when it replaced the old one, and the
            // old one carrying the fresh one's content when only the content was rebuilt.
            if (change.NewElement is { } rebuilt)
            {
                Pair(rebuilt, replacesObject ? fresh : previous, _rebuilt);
            }
        }

        foreach ((object target, XamlMemberDescriptor member, XamlInPlaceWrite write) in writes)
        {
            try
            {
                // Each write ends whatever binding the document had written on the property first:
                // a binding runs at local-value priority, which a new local value does not end, and
                // the next change of its source would write over what the document now says.
                write.Apply(target, member);
            }
            catch (Exception error) when (error is InvalidCastException
                or ArgumentException
                or InvalidOperationException
                or TargetInvocationException)
            {
                // The setter ran and threw. What it did before throwing is not knowable from here
                // — assigning the field and then failing a cross-check is a thing controls do, and
                // so is setting a second property on the way — so this is never reported as
                // though the object were untouched, not even when it is the first write of the
                // update. Everything that could be checked without running it was checked above:
                // the member exists, it can be written, and the text converts to something it
                // holds. That is where a clean refusal comes from, and it has already passed.
                //
                // TargetInvocationException among them because a CLR property and an attached
                // accessor pair are written by reflection, which wraps whatever the setter threw.
                Exception refusal = (error as TargetInvocationException)?.InnerException ?? error;

                diagnostics.Add(MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.IncompatibleValue,
                    $"{target.GetType().Name}.{member.Name} threw while being written: {refusal.Message}",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri));

                return XamlMutationOutcome.Inconsistent;
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

    /// <summary>Closes a copy that carried a root's content across, when it is a window.</summary>
    /// <remarks>
    /// A window has a platform window from the moment it is constructed, never shown or not, and the
    /// platform holds it — and everything it was built from — until it is closed. The copy is this
    /// session's own object and nobody else knows it exists, so nobody else would close it.
    /// </remarks>
    private void Retire(object copy, List<MarkupDiagnostic> diagnostics)
    {
        if (copy is not Window window)
        {
            return;
        }

        try
        {
            window.Close();
        }
        catch (Exception error) when (error is InvalidOperationException or NullReferenceException)
        {
            // The update has been written and stands; what the platform still holds is reported,
            // because it is held until the process ends and is the kind of thing nobody looks for.
            diagnostics.Add(MarkupDiagnostic.Synchronization(
                XamlLoaderDiagnosticCodes.TopLevelCopyNotClosed,
                $"The copy of the {window.GetType().Name} the update built to carry its content could not " +
                $"be closed: {error.Message} Its platform window stays open until the process ends.",
                MarkupDiagnosticSeverity.Warning,
                Document.Uri));
        }
    }

    /// <summary>
    /// Finds the class a rebuilt part places when the class loads markup of its own, which is when
    /// the part's root cannot be left to Avalonia to construct.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Avalonia's runtime loader treats the root of what it is given as the thing the text
    /// defines. For an <c>x:Class</c> control it installs the text as the populate the control's
    /// constructor runs — so a placed control rebuilt as the root of its part was populated from
    /// the part, markup that places it rather than defines it, and came back empty. And it writes
    /// <see langword="null"/> into the hook afterwards, which ended a live registration for the
    /// class (ADR 0015). Such a root is constructed by the session instead, the way the document's
    /// load constructed it — its own constructor populating it from its compiled markup or its live
    /// document — and the part is loaded onto it.
    /// </para>
    /// <para>
    /// Not the document's own root, whose content is rebuilt from a copy: that text is the root's
    /// definition, and the copy is built from it as the load built the original.
    /// </para>
    /// </remarks>
    private async ValueTask<Type?> PlacedClassAsync(XamlElement part, XamlDocument document, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(part, document.Root) || part.IsPropertyElementSyntax || part.NamespaceUri is not { } namespaceUri)
        {
            return null;
        }

        XamlTypeResolution resolved = await Environment.TypeResolver
            .ResolveAsync(new XamlTypeName(namespaceUri, part.Name.LocalName), part.NamespaceContext, cancellationToken)
            .ConfigureAwait(false);

        return resolved.Success && XamlPopulateHook.Find(resolved.Type) is not null ? resolved.Type : null;
    }

    /// <summary>Builds the objects a projected fragment describes.</summary>
    /// <remarks>
    /// Through Avalonia's own runtime loader, like any other load. It names the text it is given,
    /// and that name is how the object map later tells objects built from this fragment from the
    /// ones built from the document — which is what keeps them traceable to their markup. A part
    /// whose root places an <c>x:Class</c> control is loaded onto an instance constructed here
    /// (<see cref="PlacedClassAsync"/>).
    /// </remarks>
    private (object? Fresh, Uri? RuntimeUri) Build(TextProjection fragment, Type? placed, List<MarkupDiagnostic> diagnostics)
    {
        Uri name = FragmentUri();

        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = LocalAssembly,
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
            object fresh;

            // An update compiles markup exactly as a load does, so it enters the same scope.
            using (Environment.CompilationScope?.Enter())
            {
                fresh = AvaloniaRuntimeXamlLoader.Load(
                    placed is null
                        ? new RuntimeXamlLoaderDocument(name, fragment.Text.ToString())
                        : new RuntimeXamlLoaderDocument(name, Activator.CreateInstance(placed), fragment.Text.ToString()),
                    configuration);
            }

            // What Avalonia recorded, when it recorded anything: the name given above is the
            // fallback, so a fragment is never keyed by nothing and never keyed by the
            // document's own name.
            return (fresh, XamlSourceInfo.GetXamlSourceInfo(fresh)?.SourceUri ?? name);
        }
        catch (Exception error)
        {
            // A constructor that throws arrives wrapped by the reflection that ran it.
            Exception reported = error is TargetInvocationException { InnerException: { } inner } ? inner : error;

            diagnostics.Add(MarkupDiagnostic.Synchronization(
                XamlLoaderDiagnosticCodes.UpdateNotApplied,
                $"Rebuilding part of the document failed: {reported.Message}",
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
    /// <para>
    /// The same conservative rule the rest of the update path uses: where the two sides stop
    /// having the same shape, the walk stops descending rather than guessing which child is
    /// which. A property element contributes what is inside it — a resource dictionary, a
    /// template — and those are not logical children, so a mismatch there simply ends the
    /// descent, and what is below keeps whatever the map can work out for itself.
    /// </para>
    /// <para>
    /// A resource is the exception, because it has something better than a position: its key.
    /// The entries of a dictionary the element holds are paired with the elements that declare
    /// them, by the key each is written under — see <see cref="PairResources"/>.
    /// </para>
    /// <para>
    /// The objects on the other side are what the element's content went into (<see cref="ContentOf"/>),
    /// not the object's logical children. Those are whatever the control parents, and a window in
    /// Avalonia 12 parents a host of its own beside its content — so the walk stopped at every
    /// window, a window's rebuilt content was left for the map to place by the positions Avalonia
    /// recorded against the copy's text rather than the document's, and the handlers in it had no
    /// object to be hooked up to.
    /// </para>
    /// </remarks>
    private void Pair(XamlElement element, object target, Dictionary<XamlElement, object> into)
    {
        into[element] = target;

        PairResources(element, target, into);

        XamlElement[] children = [.. element.ContentElements];
        object[] objects = ContentOf(target);

        if (children.Length != objects.Length)
        {
            return;
        }

        for (int index = 0; index < children.Length; index++)
        {
            Pair(children[index], objects[index], into);
        }
    }

    /// <summary>What an object holds where the elements written as its content went, in order.</summary>
    /// <remarks>
    /// The member the type calls its content: the one control a content control or a decorator
    /// holds, the controls of a panel's children or an items control's items. Only controls,
    /// which is all the logical children ever offered — what else a list holds was never paired
    /// by position, and a count that differs stops the walk rather than guess.
    /// </remarks>
    private object[] ContentOf(object target)
    {
        object? held = Environment.MemberResolver.FindContent(target.GetType()) is { CanRead: true } content
            ? XamlObjectReplacement.Held(target, content.Name, Environment.MemberResolver)
            : null;

        return held switch
        {
            ILogical one => [one],
            IEnumerable many and not string => [.. many.OfType<ILogical>()],
            _ => [],
        };
    }

    /// <summary>
    /// Pairs the resources an element declares with what its dictionaries now hold under their
    /// keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rebuilding what an element holds refills its dictionaries from the rebuilt copy, so every
    /// entry afterwards is a new object. An element still paired with the entry that used to be
    /// under its key sends the next edit of it — a changed colour, say — to an object the tree no
    /// longer holds; the update then reports success and nothing on screen moves.
    /// </para>
    /// <para>
    /// The key is what says which entry an element is, and it says so exactly, which a position
    /// among siblings never could. A key written as an expression — <c>{x:Type Button}</c> — is
    /// not text this can look up, and is left to whatever the map works out for itself.
    /// </para>
    /// </remarks>
    private void PairResources(XamlElement element, object target, Dictionary<XamlElement, object> into)
    {
        if (target is IResourceDictionary own)
        {
            PairEntries(element, own, into);

            return;
        }

        foreach (XamlElement member in element.MemberElements)
        {
            if (XamlObjectReplacement.Owns(element, member)
                && member.MemberName is { } name
                && XamlObjectReplacement.Held(target, name, Environment.MemberResolver) is IResourceDictionary held)
            {
                PairEntries(member, held, into);
            }
        }
    }

    /// <summary>Pairs the keyed elements written inside a dictionary with its entries.</summary>
    private void PairEntries(XamlElement holder, IResourceDictionary dictionary, Dictionary<XamlElement, object> into)
    {
        foreach (XamlElement entry in holder.ContentElements)
        {
            if (entry.GetDirective("Key") is not { Length: > 0 } key)
            {
                // A dictionary written out in full inside the property element is the dictionary
                // the member holds, and its entries are one level further down.
                if (holder.IsPropertyElementSyntax
                    && string.Equals(entry.Name.LocalName, nameof(ResourceDictionary), StringComparison.Ordinal))
                {
                    into[entry] = dictionary;

                    PairEntries(entry, dictionary, into);
                }

                continue;
            }

            if (key.StartsWith('{') || !dictionary.ContainsKey(key) || dictionary[key] is not { } value)
            {
                continue;
            }

            Pair(entry, value, into);
        }
    }

    /// <summary>Reports whether a strategy is one that builds markup again rather than setting a value.</summary>
    private static bool Rebuilds(XamlUpdateStrategy strategy) =>
        strategy is XamlUpdateStrategy.ReplaceResource
            or XamlUpdateStrategy.ReloadStyle
            or XamlUpdateStrategy.ReloadTheme
            or XamlUpdateStrategy.ReloadTemplate
            or XamlUpdateStrategy.ReloadSubtree;

    /// <summary>Leaves out every rebuild that another rebuild of the same update already does.</summary>
    /// <remarks>
    /// A rebuild inside another is built by the outer one, as the document says. Applied after it,
    /// the inner one put its copy into a tree the outer one had already replaced, and the map named
    /// the copy; applied before it, its copy was thrown away — and either way the handlers in the
    /// part were hooked up twice, once for each. Of two rebuilds of one element, the one that
    /// replaces the object does everything the one that rebuilds its content would.
    /// </remarks>
    private static IEnumerable<XamlDocumentChange> Outermost(IEnumerable<XamlDocumentChange> rebuilds)
    {
        XamlDocumentChange[] all = [.. rebuilds];
        var kept = new List<XamlDocumentChange>(all.Length);

        foreach (XamlDocumentChange change in all
            .OrderBy(static change => change.OldElement?.AncestorsAndSelf().Count() ?? 0)
            .ThenByDescending(static change => change.ReplacesObject))
        {
            if (!kept.Exists(outer => Covers(outer, change)))
            {
                kept.Add(change);
            }
        }

        // In the order they were asked for, which is smallest first.
        return all.Where(change => kept.Exists(outer => ReferenceEquals(outer, change)));
    }

    /// <summary>Reports whether one rebuild builds everything another would.</summary>
    private static bool Covers(XamlDocumentChange outer, XamlDocumentChange change) =>
        outer.OldElement is { } around
        && change.OldElement is { } element
        && (ReferenceEquals(around, element)
            ? outer.ReplacesObject || !change.ReplacesObject
            : element.AncestorsAndSelf().Skip(1).Contains(around));

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
    private XamlUpdateResult? Unusable()
    {
        if (State == XamlSessionState.Usable)
        {
            return null;
        }

        // PendingDocument is deliberately not touched. It was set by whatever broke the session,
        // and it is the document a replacement session must be built from; a later attempt that
        // arrives and is refused has no claim to replace it.
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
