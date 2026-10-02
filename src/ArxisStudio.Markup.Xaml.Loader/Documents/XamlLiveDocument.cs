using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// One document with its own history, kept in step with the session that shows it.
/// </summary>
/// <remarks>
/// <para>
/// A designer that shows a form beside an editor working on the same file has three things to keep
/// in step on every change: the text, the history that can take the change back, and the objects
/// built from the text. A change can come from the designer itself, from undo and redo, or from the
/// file being written by somebody else — and each of the three can refuse it in its own way: an
/// editor whose edits overlap, a session that cannot follow a change in place, a document that does
/// not load at all. This is the one place they are kept together, so that no route a change can take
/// leaves the history and the objects describing different text.
/// </para>
/// <para>
/// The text is the truth. Every change is recorded in it first and shown afterwards, and a change the
/// objects cannot follow is still recorded: the session that shows the document is updated in place
/// where it can be, rebuilt from the text where it cannot, and left showing the last text it could
/// where nothing shows the new one — which is <see cref="State"/>, with
/// <see cref="Diagnostics"/> saying why. A document halfway through being typed in another editor is
/// the ordinary case of that, not an error.
/// </para>
/// <para>
/// The history is the document's own, a <see cref="XamlWorkspace"/> of one document — see
/// <c>docs/adr/0025-a-live-document-owns-its-history-and-its-session.md</c>. Opening is not a step of
/// it, and text that arrives from outside is: a file written by another editor is something the
/// author can take back, and taking it back makes the document read as changed against the file.
/// </para>
/// <para>
/// Operations are taken one at a time, in the order they were asked for. Each runs against the text
/// as the operations before it left it, so an edit queued behind another is computed against the
/// other's result — and an element read from an earlier version is refused by the editor rather than
/// written somewhere it no longer is. Events are raised on the thread that owns the objects, once the
/// operation that caused them is over.
/// </para>
/// </remarks>
public sealed class XamlLiveDocument : IAsyncDisposable
{
    private readonly XamlMutationGate _gate = new();
    private readonly object _sync = new();
    private readonly IXamlDispatcher _dispatcher;
    private readonly MarkupWorkspace _markup;
    private readonly XamlWorkspace _workspace;
    private readonly MarkupDocumentId _id;

    private XamlDocument _document;
    private SourceText _savedText;
    private bool _dirty;
    private XamlLoadSession? _session;
    private XamlLiveDocumentState _state = XamlLiveDocumentState.Detached;
    private ImmutableArray<MarkupDiagnostic> _diagnostics = [];

    /// <summary>The environment the session is built in, while one is attached. Touched inside a turn only.</summary>
    private XamlLoadEnvironment? _environment;

    /// <summary>How the session is built, while an environment is attached. Touched inside a turn only.</summary>
    private XamlLoadOptions? _options;

    private int _disposal;

    private XamlLiveDocument(Uri uri, SourceText text, SourceText savedText, IXamlDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _markup = new MarkupWorkspace(new InMemoryMarkupSourceProvider());
        _workspace = new XamlWorkspace(_markup);
        _id = _markup.AddDocument(uri, text).Id;

        // Adding a document records a step, and undoing that step would close it. Opening a file is
        // not something the author did to it.
        _markup.ClearHistory();

        _document = _workspace.GetDocument(_id);
        _savedText = savedText;
        _dirty = !SameText(text, savedText);
    }

    /// <summary>
    /// Raised once an operation has moved the text, what is saved, the state or the URI.
    /// </summary>
    /// <remarks>On the thread that owns the objects, after the operation is over.</remarks>
    public event EventHandler<XamlLiveDocumentChangedEventArgs>? Changed;

    /// <summary>
    /// Raised when a different session, or none, takes the place of the session — while the previous
    /// one is still open, so its root can be taken off whatever shows it.
    /// </summary>
    /// <remarks>On the thread that owns the objects. The previous session is disposed once every handler has returned.</remarks>
    public event EventHandler<XamlSessionReplacedEventArgs>? SessionReplaced;

    /// <summary>Gets where the document lives.</summary>
    public Uri Uri => Document.Uri!;

    /// <summary>Gets the document as it reads now.</summary>
    public XamlDocument Document
    {
        get
        {
            lock (_sync)
            {
                return _document;
            }
        }
    }

    /// <summary>Gets the text as it was last saved, or as it was opened.</summary>
    public SourceText SavedText
    {
        get
        {
            lock (_sync)
            {
                return _savedText;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the document reads differently from what is saved.
    /// </summary>
    /// <remarks>
    /// Compared by text, not by history: undoing back to what was saved makes the document clean again,
    /// and a change that writes back what was there leaves it clean.
    /// </remarks>
    public bool IsDirty
    {
        get
        {
            lock (_sync)
            {
                return _dirty;
            }
        }
    }

    /// <summary>Gets the session that shows the document, or <see langword="null"/> when nothing does.</summary>
    public XamlLoadSession? Session
    {
        get
        {
            lock (_sync)
            {
                return _session;
            }
        }
    }

    /// <summary>Gets what the objects show of the text.</summary>
    public XamlLiveDocumentState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Gets what the last attempt to show the text found — why it is <see cref="XamlLiveDocumentState.Behind"/>
    /// or <see cref="XamlLiveDocumentState.Broken"/>, and the warnings of a text that does show.
    /// </summary>
    public ImmutableArray<MarkupDiagnostic> Diagnostics
    {
        get
        {
            lock (_sync)
            {
                return _diagnostics;
            }
        }
    }

    /// <summary>Gets a value indicating whether there is anything to undo.</summary>
    public bool CanUndo => _workspace.CanUndo;

    /// <summary>Gets a value indicating whether there is anything to redo.</summary>
    public bool CanRedo => _workspace.CanRedo;

    /// <summary>Gets the name of what undoing would undo.</summary>
    public string? UndoDescription => _workspace.UndoDescription;

    /// <summary>Gets the name of what redoing would redo.</summary>
    public string? RedoDescription => _workspace.RedoDescription;

    /// <summary>
    /// Opens a document with nothing showing it yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The history starts empty. A document restored with changes nobody saved — a session handed to
    /// the next copy of a tool — passes what the file holds as <paramref name="savedText"/>, and reads
    /// as changed from the start.
    /// </para>
    /// <para>
    /// Nothing is built until <see cref="AttachAsync"/> names an environment to build it in.
    /// </para>
    /// </remarks>
    /// <param name="uri">Where the document lives.</param>
    /// <param name="text">What it says.</param>
    /// <param name="savedText">
    /// What is saved where it lives, or <see langword="null"/> when that is <paramref name="text"/>.
    /// </param>
    /// <param name="dispatcher">
    /// The thread that owns the objects, which events are raised on — the same one the environment
    /// names. <see langword="null"/> for Avalonia's.
    /// </param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    public static XamlLiveDocument Open(
        Uri uri,
        SourceText text,
        SourceText? savedText = null,
        IXamlDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(text);

        return new XamlLiveDocument(uri, text, savedText ?? text, dispatcher ?? AvaloniaXamlDispatcher.Instance);
    }

    /// <summary>
    /// Records an edit as one step of the history and shows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The edit is recorded whatever the objects make of it. A session that follows it in place keeps
    /// every object the edit did not reach; one that cannot is rebuilt from the text; and a text that
    /// cannot be shown at all leaves the objects as they were, with the reason in
    /// <see cref="Diagnostics"/>.
    /// </para>
    /// <para>
    /// The callback runs inside the document's turn, against the document as the operations before it
    /// left it — not necessarily on the calling thread, so it should record edits and touch nothing
    /// else. Elements are found in <see cref="XamlDocumentEditor.Document"/>: one read from an earlier
    /// version belongs to a different parse, and the editor refuses it. An editor that records nothing
    /// changes nothing, and an exception from the callback or the editor leaves the document as it was.
    /// </para>
    /// <para>
    /// The token gives up waiting for the turn. Once the edit is recorded it is shown whatever the
    /// token says, because a step in the history that the session never heard of is the one state this
    /// type exists to prevent.
    /// </para>
    /// </remarks>
    /// <param name="edit">Records the edit.</param>
    /// <param name="description">What to call the step in the history — the action, not the mechanism.</param>
    /// <param name="cancellationToken">A token to give up waiting with.</param>
    /// <returns>What the edit did to the text and to what shows it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="edit"/> or <paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="description"/> is empty.</exception>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the turn came.</exception>
    public async ValueTask<XamlLiveEditResult> EditAsync(
        Action<XamlDocumentEditor> edit,
        string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        using XamlMutationGate.XamlMutationLease lease =
            await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        ThrowIfDisposed();

        XamlDocumentEditor editor = _workspace.GetDocument(_id).Edit();

        edit(editor);

        if (!editor.HasChanges)
        {
            return Unchanged();
        }

        _workspace.Apply(editor, description);

        return await ShowAsync(XamlLiveDocumentChanges.Text, rebuild: false, keepOnFailure: true).ConfigureAwait(false);
    }

    /// <summary>Takes back the last step of the history, and shows the text it leaves.</summary>
    /// <param name="cancellationToken">A token to give up waiting for the turn with.</param>
    /// <returns>What undoing did; nothing moved when there was nothing to undo.</returns>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the turn came.</exception>
    public ValueTask<XamlLiveEditResult> UndoAsync(CancellationToken cancellationToken = default) =>
        StepAsync(undoing: true, cancellationToken);

    /// <summary>Takes the last step back again, and shows the text it leaves.</summary>
    /// <param name="cancellationToken">A token to give up waiting for the turn with.</param>
    /// <returns>What redoing did; nothing moved when there was nothing to redo.</returns>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the turn came.</exception>
    public ValueTask<XamlLiveEditResult> RedoAsync(CancellationToken cancellationToken = default) =>
        StepAsync(undoing: false, cancellationToken);

    /// <summary>
    /// Takes text that arrived from outside — the file, written by another editor — as the policy says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Taken text is a step of the history under <paramref name="description"/>, so the author can take
    /// it back; it is also what is saved, because it came from where the document is saved. Text that
    /// is already the document's only becomes what is saved — the usual echo of the document's own save.
    /// </para>
    /// <para>
    /// Unsaved changes are never overwritten silently. <see cref="XamlExternalTextPolicy.ApplyIfClean"/>
    /// reports the conflict and changes nothing; the host asks the author and comes back with
    /// <see cref="XamlExternalTextPolicy.TakeTheirs"/> or <see cref="XamlExternalTextPolicy.KeepMine"/>.
    /// </para>
    /// </remarks>
    /// <param name="text">The text from outside.</param>
    /// <param name="description">What to call the step in the history, when the text is taken.</param>
    /// <param name="policy">What to do with unsaved changes.</param>
    /// <param name="cancellationToken">A token to give up waiting for the turn with.</param>
    /// <returns>What happened to the text, and what taking it did to what shows it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="description"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="policy"/> is not a policy.</exception>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the turn came.</exception>
    public async ValueTask<XamlExternalTextResult> AcceptExternalTextAsync(
        SourceText text,
        string description,
        XamlExternalTextPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (policy is not (XamlExternalTextPolicy.ApplyIfClean or XamlExternalTextPolicy.TakeTheirs
            or XamlExternalTextPolicy.KeepMine))
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Not a policy for text from outside.");
        }

        using XamlMutationGate.XamlMutationLease lease =
            await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        ThrowIfDisposed();

        if (SameText(text, _workspace.GetDocument(_id).SourceText))
        {
            await RaiseChangedAsync(MarkSaved(text)).ConfigureAwait(false);

            return new XamlExternalTextResult { Outcome = XamlExternalTextOutcome.AlreadyCurrent };
        }

        if (policy == XamlExternalTextPolicy.KeepMine)
        {
            await RaiseChangedAsync(MarkSaved(text)).ConfigureAwait(false);

            return new XamlExternalTextResult { Outcome = XamlExternalTextOutcome.KeptMine };
        }

        if (policy == XamlExternalTextPolicy.ApplyIfClean && IsDirty)
        {
            return new XamlExternalTextResult { Outcome = XamlExternalTextOutcome.Conflict };
        }

        using (MarkupTransaction transaction = _markup.BeginTransaction(description))
        {
            transaction.UpdateDocument(_id, text);
            transaction.Commit();
        }

        XamlLiveDocumentChanges saved = MarkSaved(text);
        XamlLiveEditResult shown = await ShowAsync(XamlLiveDocumentChanges.Text | saved, rebuild: false, keepOnFailure: true)
            .ConfigureAwait(false);

        return new XamlExternalTextResult { Outcome = XamlExternalTextOutcome.Taken, Edit = shown };
    }

    /// <summary>Records what was written where the document lives.</summary>
    /// <remarks>
    /// The host saves the text it read from <see cref="Document"/> and passes the same text here. An edit
    /// that landed in between is not what was written, and the document goes on reading as changed —
    /// which is the truth, and is why this takes the text rather than taking the document's word.
    /// </remarks>
    /// <param name="written">The text that was written.</param>
    /// <param name="cancellationToken">A token to give up waiting for the turn with.</param>
    /// <returns>A task that completes once the saved text is recorded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="written"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the turn came.</exception>
    public async ValueTask MarkSavedAsync(SourceText written, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(written);

        using XamlMutationGate.XamlMutationLease lease =
            await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        ThrowIfDisposed();

        await RaiseChangedAsync(MarkSaved(written)).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the document's objects in an environment, in place of whatever showed it before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The environment and the options are kept until <see cref="DetachAsync"/>, and every session the
    /// document builds from now on is built with them. A document already attached is attached again:
    /// the previous session goes whether or not the new one can be built, because it belongs to the
    /// previous environment, and keeping it would keep that alive.
    /// </para>
    /// <para>
    /// <see cref="SessionReplaced"/> is raised for the session that is built, as for any other.
    /// </para>
    /// </remarks>
    /// <param name="environment">Everything outside the document that loading needs.</param>
    /// <param name="options">How to load, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to give up waiting for the turn with.</param>
    /// <returns>What attaching did.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="environment"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the turn came.</exception>
    public async ValueTask<XamlLiveEditResult> AttachAsync(
        XamlLoadEnvironment environment,
        XamlLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);

        using XamlMutationGate.XamlMutationLease lease =
            await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        ThrowIfDisposed();

        _environment = environment;
        _options = options ?? XamlLoadOptions.Default;

        return await ShowAsync(XamlLiveDocumentChanges.None, rebuild: true, keepOnFailure: false).ConfigureAwait(false);
    }

    /// <summary>
    /// Lets go of the session, the environment and the options, keeping the text, the history and what
    /// is saved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a host does to a document nobody is looking at, and to every document before the code it
    /// was built from is replaced: nothing here holds a type, an assembly or an object of the
    /// environment afterwards, so a generation of a project's assemblies can go once its documents are
    /// detached. Edits, undo and text from outside go on landing in the text while it is detached, and
    /// <see cref="AttachAsync"/> shows whatever it then says.
    /// </para>
    /// <para>
    /// <see cref="SessionReplaced"/> is raised with no current session before the previous one is
    /// disposed.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">A token to give up waiting for the turn with.</param>
    /// <returns>A task that completes once the session is gone.</returns>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the turn came.</exception>
    public async ValueTask DetachAsync(CancellationToken cancellationToken = default)
    {
        using XamlMutationGate.XamlMutationLease lease =
            await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        ThrowIfDisposed();

        if (_environment is null && Session is null)
        {
            return;
        }

        _environment = null;
        _options = null;

        await ReplaceSessionAsync(null).ConfigureAwait(false);

        await RaiseChangedAsync(SetState(XamlLiveDocumentState.Detached, [])).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the session again from the text as it reads — because something the text names has
    /// changed rather than the text.
    /// </summary>
    /// <remarks>
    /// A text that does not build any more leaves the session that was there showing it, and the
    /// document <see cref="XamlLiveDocumentState.Behind"/> with the reason in <see cref="Diagnostics"/>.
    /// A detached document has nothing to build.
    /// </remarks>
    /// <param name="cancellationToken">A token to give up waiting for the turn with.</param>
    /// <returns>What rebuilding did.</returns>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the turn came.</exception>
    public async ValueTask<XamlLiveEditResult> RebuildAsync(CancellationToken cancellationToken = default)
    {
        using XamlMutationGate.XamlMutationLease lease =
            await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        ThrowIfDisposed();

        return await ShowAsync(XamlLiveDocumentChanges.None, rebuild: true, keepOnFailure: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves the document to where its file now lives — renamed or moved by somebody else — keeping its
    /// history.
    /// </summary>
    /// <remarks>
    /// The move is not a step of the history, and steps already in it come back at the new place. The
    /// session is built again, because what the document includes is found relative to where it lives.
    /// </remarks>
    /// <param name="uri">Where the document lives now.</param>
    /// <param name="cancellationToken">A token to give up waiting for the turn with.</param>
    /// <returns>What moving did; nothing moved when the document already lives there.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the turn came.</exception>
    public async ValueTask<XamlLiveEditResult> RetargetAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        using XamlMutationGate.XamlMutationLease lease =
            await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        ThrowIfDisposed();

        if (uri == Uri)
        {
            return Unchanged();
        }

        _markup.ChangeUri(_id, uri);

        return await ShowAsync(XamlLiveDocumentChanges.Uri, rebuild: true, keepOnFailure: true).ConfigureAwait(false);
    }

    /// <summary>Disposes the session and lets go of the history.</summary>
    /// <remarks>
    /// An operation in flight is waited for, and those queued behind this one are refused. No event is
    /// raised: the objects are the host's, and a host disposing the document is already taking them
    /// down.
    /// </remarks>
    /// <returns>A task that completes once the session is disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposal, 1) != 0)
        {
            return;
        }

        using XamlMutationGate.XamlMutationLease lease =
            await _gate.EnterAsync(CancellationToken.None).ConfigureAwait(false);

        XamlLoadSession? session;

        lock (_sync)
        {
            session = _session;
            _session = null;
        }

        _environment = null;
        _options = null;

        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        _workspace.Dispose();
    }

    /// <summary>Compares two texts by what they say.</summary>
    private static bool SameText(SourceText one, SourceText other) =>
        one.Length == other.Length && one.ToCharSpan().SequenceEqual(other.ToCharSpan());

    private async ValueTask<XamlLiveEditResult> StepAsync(bool undoing, CancellationToken cancellationToken)
    {
        using XamlMutationGate.XamlMutationLease lease =
            await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        ThrowIfDisposed();

        if (!(undoing ? _workspace.Undo() : _workspace.Redo()))
        {
            return Unchanged();
        }

        return await ShowAsync(XamlLiveDocumentChanges.Text, rebuild: false, keepOnFailure: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Publishes the text the workspace now holds, brings what shows it in line, and says what moved.
    /// </summary>
    /// <param name="changes">What the operation already moved.</param>
    /// <param name="rebuild">Whether to build a new session rather than update the one in place.</param>
    /// <param name="keepOnFailure">
    /// Whether a session that can still be believed stays when no new one can be built. Not when
    /// attaching: the session in place belongs to the environment being replaced.
    /// </param>
    private async ValueTask<XamlLiveEditResult> ShowAsync(
        XamlLiveDocumentChanges changes,
        bool rebuild,
        bool keepOnFailure)
    {
        XamlDocument document = _workspace.GetDocument(_id);

        lock (_sync)
        {
            bool wasDirty = _dirty;

            _document = document;
            _dirty = !SameText(document.SourceText, _savedText);

            if (_dirty != wasDirty)
            {
                changes |= XamlLiveDocumentChanges.Saved;
            }
        }

        (XamlLiveEditResult result, XamlLiveDocumentChanges shown) =
            await CatchUpAsync(document, (changes & XamlLiveDocumentChanges.Text) != 0, rebuild, keepOnFailure)
                .ConfigureAwait(false);

        await RaiseChangedAsync(changes | shown).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Brings what shows the document in line with a text: an update in place where the session can
    /// follow, a new session where it cannot, and the last one that can be believed where nothing new
    /// can be built.
    /// </summary>
    private async ValueTask<(XamlLiveEditResult Result, XamlLiveDocumentChanges Changes)> CatchUpAsync(
        XamlDocument target,
        bool textChanged,
        bool rebuild,
        bool keepOnFailure)
    {
        if (_environment is not { } environment)
        {
            return Settle(textChanged, replaced: false, XamlLiveDocumentState.Detached, [], update: null, load: null);
        }

        XamlLoadSession? session = Session;
        XamlUpdateResult? update = null;

        if (!rebuild && session is { State: XamlSessionState.Usable })
        {
            // Never cancelled: the text has already moved, and a session left between two texts is
            // worse than a slow one.
            update = await session.ApplyDocumentUpdateAsync(target, CancellationToken.None).ConfigureAwait(false);

            if (update.Applied)
            {
                return Settle(textChanged, replaced: false, XamlLiveDocumentState.Live, update.Diagnostics, update, load: null);
            }

            // A text that does not parse is somebody halfway through a sentence. The objects keep
            // showing the last text that did, and the next save is usually the correction — building
            // from a recovered parse would show something nobody wrote.
            if (update.Outcome == XamlUpdateOutcome.RejectedCleanly && !target.IsWellFormed)
            {
                return Settle(textChanged, replaced: false, XamlLiveDocumentState.Behind, update.Diagnostics, update, load: null);
            }
        }

        (XamlLoadSession? built, XamlLoadResult load) = await XamlLoadSession
            .TryCreateAsync(target, environment, _options, CancellationToken.None)
            .ConfigureAwait(false);

        if (built is not null)
        {
            await ReplaceSessionAsync(built).ConfigureAwait(false);

            return Settle(textChanged, replaced: true, XamlLiveDocumentState.Live, load.Diagnostics, update, load);
        }

        if (keepOnFailure && session is { State: XamlSessionState.Usable })
        {
            return Settle(textChanged, replaced: false, XamlLiveDocumentState.Behind, load.Diagnostics, update, load);
        }

        bool replaced = session is not null;

        await ReplaceSessionAsync(null).ConfigureAwait(false);

        return Settle(textChanged, replaced, XamlLiveDocumentState.Broken, load.Diagnostics, update, load);
    }

    /// <summary>Records where an operation left the document, and describes it.</summary>
    private (XamlLiveEditResult Result, XamlLiveDocumentChanges Changes) Settle(
        bool textChanged,
        bool replaced,
        XamlLiveDocumentState state,
        ImmutableArray<MarkupDiagnostic> diagnostics,
        XamlUpdateResult? update,
        XamlLoadResult? load)
    {
        XamlLiveDocumentChanges changes = SetState(state, diagnostics);

        if (replaced)
        {
            changes |= XamlLiveDocumentChanges.State;
        }

        var result = new XamlLiveEditResult
        {
            TextChanged = textChanged,
            SessionReplaced = replaced,
            State = state,
            Update = update,
            Load = load,
            Diagnostics = diagnostics,
        };

        return (result, changes);
    }

    /// <summary>Records a state and its diagnostics, and says whether either moved.</summary>
    private XamlLiveDocumentChanges SetState(XamlLiveDocumentState state, ImmutableArray<MarkupDiagnostic> diagnostics)
    {
        lock (_sync)
        {
            bool moved = _state != state || !_diagnostics.AsSpan().SequenceEqual(diagnostics.AsSpan());

            _state = state;
            _diagnostics = diagnostics;

            return moved ? XamlLiveDocumentChanges.State : XamlLiveDocumentChanges.None;
        }
    }

    /// <summary>Records a saved text, and says whether it or the document's difference from it moved.</summary>
    private XamlLiveDocumentChanges MarkSaved(SourceText written)
    {
        lock (_sync)
        {
            bool moved = !SameText(written, _savedText);
            bool wasDirty = _dirty;

            _savedText = written;
            _dirty = !SameText(_document.SourceText, written);

            return moved || _dirty != wasDirty ? XamlLiveDocumentChanges.Saved : XamlLiveDocumentChanges.None;
        }
    }

    /// <summary>
    /// Puts a session — or none — in place of the one there, telling the host while the previous one is
    /// still open and disposing it afterwards.
    /// </summary>
    private async ValueTask ReplaceSessionAsync(XamlLoadSession? replacement)
    {
        XamlLoadSession? previous;

        lock (_sync)
        {
            previous = _session;
            _session = replacement;
        }

        if (previous is null && replacement is null)
        {
            return;
        }

        try
        {
            if (SessionReplaced is not null)
            {
                var arguments = new XamlSessionReplacedEventArgs(previous, replacement);

                await _dispatcher.InvokeAsync(
                    () =>
                    {
                        SessionReplaced?.Invoke(this, arguments);

                        return true;
                    }).ConfigureAwait(false);
            }
        }
        finally
        {
            if (previous is not null)
            {
                await previous.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Raises <see cref="Changed"/> on the owning thread, when anything moved.</summary>
    private async ValueTask RaiseChangedAsync(XamlLiveDocumentChanges changes)
    {
        if (changes == XamlLiveDocumentChanges.None || Changed is null)
        {
            return;
        }

        var arguments = new XamlLiveDocumentChangedEventArgs(changes);

        await _dispatcher.InvokeAsync(
            () =>
            {
                Changed?.Invoke(this, arguments);

                return true;
            }).ConfigureAwait(false);
    }

    /// <summary>A result for an operation that moved nothing.</summary>
    private XamlLiveEditResult Unchanged()
    {
        lock (_sync)
        {
            return new XamlLiveEditResult
            {
                TextChanged = false,
                SessionReplaced = false,
                State = _state,
                Diagnostics = _diagnostics,
            };
        }
    }

    /// <summary>Refuses an operation that took its turn after the document was disposed.</summary>
    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Interlocked.CompareExchange(ref _disposal, 0, 0) != 0, this);
}
