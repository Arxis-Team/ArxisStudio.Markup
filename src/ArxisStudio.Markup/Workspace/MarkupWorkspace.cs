using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup;

/// <summary>
/// Owns the set of open documents, their current snapshots, and the history of committed
/// transactions.
/// </summary>
/// <remarks>
/// <para>
/// All of the workspace's mutable state lives in one immutable <see cref="WorkspaceState"/>.
/// Mutations take a lock, build a replacement and publish it with a single reference write;
/// readers take the current state without any lock at all. A reader therefore observes the
/// state either wholly before or wholly after a change, even when that change spans several
/// documents.
/// </para>
/// <para>
/// Every mutation goes through a transaction, including the single-document convenience
/// methods, which open and commit one implicitly. That keeps the undo history a complete and
/// coherent record of everything that has happened to the workspace.
/// </para>
/// </remarks>
public sealed class MarkupWorkspace
{
    private readonly Lock _gate = new();

    private WorkspaceState _state = WorkspaceState.Empty;
    private ImmutableStack<MarkupHistoryEntry> _undo = ImmutableStack<MarkupHistoryEntry>.Empty;
    private ImmutableStack<MarkupHistoryEntry> _redo = ImmutableStack<MarkupHistoryEntry>.Empty;

    /// <summary>Creates a workspace that resolves documents through a provider.</summary>
    /// <param name="sourceProvider">The provider used to open documents.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sourceProvider"/> is <see langword="null"/>.</exception>
    public MarkupWorkspace(IMarkupSourceProvider sourceProvider)
    {
        ArgumentNullException.ThrowIfNull(sourceProvider);

        SourceProvider = sourceProvider;
    }

    /// <summary>Raised once per affected document after a change is published.</summary>
    /// <remarks>
    /// A transaction that touches several documents raises the event once for each, but only
    /// after every one of them has been published — a handler that reads the workspace always
    /// sees the whole committed change, never part of it.
    /// <para>
    /// Raised outside the workspace lock, so a handler may read the workspace freely. A
    /// handler that throws propagates to whoever caused the change.
    /// </para>
    /// </remarks>
    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;

    /// <summary>Gets the provider documents are opened through.</summary>
    public IMarkupSourceProvider SourceProvider { get; }

    /// <summary>Gets a snapshot of the currently open documents.</summary>
    public IReadOnlyCollection<MarkupDocument> Documents => [.. Volatile.Read(ref _state).Documents.Values];

    /// <summary>Gets the number of currently open documents.</summary>
    public int Count => Volatile.Read(ref _state).Documents.Count;

    /// <summary>Gets a value indicating whether there is a committed transaction to undo.</summary>
    public bool CanUndo => !Volatile.Read(ref _undo).IsEmpty;

    /// <summary>Gets a value indicating whether there is an undone transaction to redo.</summary>
    public bool CanRedo => !Volatile.Read(ref _redo).IsEmpty;

    /// <summary>Gets the description of the transaction <see cref="Undo"/> would reverse.</summary>
    public string? UndoDescription => Peek(Volatile.Read(ref _undo));

    /// <summary>Gets the description of the transaction <see cref="Redo"/> would re-apply.</summary>
    public string? RedoDescription => Peek(Volatile.Read(ref _redo));

    /// <summary>Opens a transaction that groups changes to one or more documents.</summary>
    /// <param name="description">
    /// A human-readable description of the operation, surfaced through
    /// <see cref="UndoDescription"/> so a host can label its undo command.
    /// </param>
    /// <returns>The transaction. Dispose it to roll back anything not committed.</returns>
    /// <exception cref="ArgumentException"><paramref name="description"/> is <see langword="null"/> or blank.</exception>
    public MarkupTransaction BeginTransaction(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new MarkupTransaction(this, Volatile.Read(ref _state), description);
    }

    /// <summary>
    /// Opens the document at a URI, or returns the already open document for that URI.
    /// </summary>
    /// <param name="uri">The URI to open.</param>
    /// <param name="cancellationToken">A token to observe while resolving and reading.</param>
    /// <returns>The document's current snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No provider knows the URI.</exception>
    public async ValueTask<MarkupDocument> OpenDocumentAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (TryGetDocumentByUri(uri, out MarkupDocument? existing))
        {
            return existing;
        }

        MarkupSource source = await SourceProvider.TryGetSourceAsync(uri, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No source provider could resolve '{uri}'. Supply a provider that knows this URI.");

        SourceText text = await source.GetTextAsync(cancellationToken).ConfigureAwait(false);

        return AddDocument(uri, text);
    }

    /// <summary>Adds a document that was constructed without going through a provider.</summary>
    /// <param name="uri">The document's URI.</param>
    /// <param name="text">The document's initial text.</param>
    /// <returns>The document's first snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A document with that URI is already open.</exception>
    public MarkupDocument AddDocument(Uri uri, SourceText text)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(text);

        using MarkupTransaction transaction = BeginTransaction($"Add {uri}");

        MarkupDocument document = transaction.AddDocument(uri, text);
        transaction.Commit();

        return document;
    }

    /// <summary>Gets the snapshot of an open document.</summary>
    /// <param name="id">The document's identity.</param>
    /// <returns>The document's current snapshot.</returns>
    /// <exception cref="InvalidOperationException">The document is not open.</exception>
    public MarkupDocument GetDocument(MarkupDocumentId id) =>
        TryGetDocument(id, out MarkupDocument? document)
            ? document
            : throw new InvalidOperationException($"Document '{id}' is not open in this workspace.");

    /// <summary>Attempts to get the snapshot of an open document.</summary>
    /// <param name="id">The document's identity.</param>
    /// <param name="document">The document's current snapshot, when it is open.</param>
    /// <returns><see langword="true"/> if the document is open.</returns>
    public bool TryGetDocument(MarkupDocumentId id, [NotNullWhen(true)] out MarkupDocument? document) =>
        Volatile.Read(ref _state).Documents.TryGetValue(id, out document);

    /// <summary>Attempts to get the snapshot of the open document at a URI.</summary>
    /// <param name="uri">The document's URI.</param>
    /// <param name="document">The document's current snapshot, when one is open at that URI.</param>
    /// <returns><see langword="true"/> if a document is open at that URI.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public bool TryGetDocumentByUri(Uri uri, [NotNullWhen(true)] out MarkupDocument? document)
    {
        ArgumentNullException.ThrowIfNull(uri);

        // One read of the state, so the two lookups cannot disagree with each other.
        WorkspaceState state = Volatile.Read(ref _state);

        if (state.DocumentsByUri.TryGetValue(uri, out MarkupDocumentId id))
        {
            return state.Documents.TryGetValue(id, out document);
        }

        document = null;

        return false;
    }

    /// <summary>Replaces an open document's text in a transaction of its own.</summary>
    /// <param name="id">The document's identity.</param>
    /// <param name="text">The new text.</param>
    /// <returns>The new snapshot, or the existing one when the text is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The document is not open.</exception>
    public MarkupDocument UpdateDocument(MarkupDocumentId id, SourceText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        using MarkupTransaction transaction = BeginTransaction("Update document");

        MarkupDocument updated = transaction.UpdateDocument(id, text);

        if (!transaction.HasChanges)
        {
            return updated;
        }

        transaction.Commit();

        return updated;
    }

    /// <summary>Applies changes to an open document's text in a transaction of its own.</summary>
    /// <param name="id">The document's identity.</param>
    /// <param name="changes">The changes to apply, ordered and non-overlapping.</param>
    /// <returns>The new snapshot, or the existing one when <paramref name="changes"/> is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The document is not open.</exception>
    public MarkupDocument ApplyChanges(MarkupDocumentId id, IReadOnlyList<TextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return UpdateDocument(id, GetDocument(id).Text.WithChanges(changes));
    }

    /// <summary>Removes a document from the workspace in a transaction of its own.</summary>
    /// <param name="id">The document's identity.</param>
    /// <returns><see langword="true"/> if the document was open.</returns>
    public bool CloseDocument(MarkupDocumentId id)
    {
        using MarkupTransaction transaction = BeginTransaction("Close document");

        if (!transaction.RemoveDocument(id))
        {
            return false;
        }

        transaction.Commit();

        return true;
    }

    /// <summary>Reverses the most recently committed transaction.</summary>
    /// <remarks>
    /// The earlier text is restored as a new snapshot at a version that has never been used.
    /// Undo therefore returns the workspace to an earlier <em>state</em>, but not to earlier
    /// version numbers — see <see cref="DocumentTransition"/> for why reusing one would be
    /// unsafe.
    /// </remarks>
    /// <returns><see langword="true"/> if a transaction was undone.</returns>
    public bool Undo() => Step(undoing: true);

    /// <summary>Re-applies the most recently undone transaction.</summary>
    /// <returns><see langword="true"/> if a transaction was redone.</returns>
    public bool Redo() => Step(undoing: false);

    /// <summary>Discards the undo and redo history, leaving the current documents untouched.</summary>
    public void ClearHistory()
    {
        lock (_gate)
        {
            _undo = ImmutableStack<MarkupHistoryEntry>.Empty;
            _redo = ImmutableStack<MarkupHistoryEntry>.Empty;
        }
    }

    /// <summary>Publishes a transaction's staged state, or fails without changing anything.</summary>
    internal void CommitTransaction(MarkupTransaction transaction)
    {
        List<DocumentChangedEventArgs> notifications;

        lock (_gate)
        {
            // Optimistic concurrency: the transaction computed its changes against the state
            // it opened on, so committing over a state somebody else has since published
            // would silently discard their work.
            if (!ReferenceEquals(_state, transaction.Original))
            {
                throw new InvalidOperationException(
                    $"The workspace changed after the transaction '{transaction.Description}' was opened. " +
                    "Recompute the changes against the current state and retry.");
            }

            if (!transaction.HasChanges)
            {
                return;
            }

            notifications = DescribeChanges(transaction.Original, transaction.Pending, transaction.Transitions);

            _state = transaction.Pending;
            _undo = _undo.Push(new MarkupHistoryEntry(transaction.Description, transaction.Transitions));

            // A new committed change makes the redone future unreachable, which is the
            // universal convention for undo stacks.
            _redo = ImmutableStack<MarkupHistoryEntry>.Empty;
        }

        Raise(notifications);
    }

    private bool Step(bool undoing)
    {
        List<DocumentChangedEventArgs> notifications;

        lock (_gate)
        {
            ImmutableStack<MarkupHistoryEntry> source = undoing ? _undo : _redo;

            if (source.IsEmpty)
            {
                return false;
            }

            MarkupHistoryEntry entry = source.Peek();
            WorkspaceState before = _state;
            WorkspaceState after = before;

            foreach (DocumentTransition transition in entry.Transitions)
            {
                SourceText? target = undoing ? transition.Before : transition.After;

                after = target is null
                    ? Remove(after, transition.Id)
                    : Restore(after, transition, target);
            }

            notifications = DescribeChanges(before, after, entry.Transitions);
            _state = after;

            if (undoing)
            {
                _undo = _undo.Pop();
                _redo = _redo.Push(entry);
            }
            else
            {
                _redo = _redo.Pop();
                _undo = _undo.Push(entry);
            }
        }

        Raise(notifications);

        return true;
    }

    private static WorkspaceState Remove(WorkspaceState state, MarkupDocumentId id) =>
        state.Documents.TryGetValue(id, out MarkupDocument? current)
            ? state.WithoutDocument(current)
            : state;

    private static WorkspaceState Restore(WorkspaceState state, DocumentTransition transition, SourceText text) =>
        state.WithDocument(
            MarkupDocument.Create(transition.Id, transition.Uri, text, state.NextVersion(transition.Id)));

    private static List<DocumentChangedEventArgs> DescribeChanges(
        WorkspaceState before,
        WorkspaceState after,
        ImmutableArray<DocumentTransition> transitions)
    {
        var notifications = new List<DocumentChangedEventArgs>(transitions.Length);

        foreach (DocumentTransition transition in transitions)
        {
            before.Documents.TryGetValue(transition.Id, out MarkupDocument? old);
            after.Documents.TryGetValue(transition.Id, out MarkupDocument? current);

            DocumentChangeKind kind = (old, current) switch
            {
                (null, not null) => DocumentChangeKind.Opened,
                (not null, null) => DocumentChangeKind.Closed,
                _ => DocumentChangeKind.Changed,
            };

            if (old is not null || current is not null)
            {
                notifications.Add(new DocumentChangedEventArgs(kind, old, current));
            }
        }

        return notifications;
    }

    private static string? Peek(ImmutableStack<MarkupHistoryEntry> stack) =>
        stack.IsEmpty ? null : stack.Peek().Description;

    private void Raise(List<DocumentChangedEventArgs> notifications)
    {
        EventHandler<DocumentChangedEventArgs>? handler = DocumentChanged;

        if (handler is null)
        {
            return;
        }

        foreach (DocumentChangedEventArgs notification in notifications)
        {
            handler(this, notification);
        }
    }
}
