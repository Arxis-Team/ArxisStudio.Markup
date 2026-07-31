using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace ArxisStudio.Markup;

/// <summary>
/// A group of changes to one or more documents that becomes visible all at once, or not at all.
/// </summary>
/// <remarks>
/// <para>
/// Changes are staged inside the transaction and published to the workspace only on
/// <see cref="Commit"/>. Nothing a transaction does is visible to any other reader until then,
/// which is what makes "rollback never leaves partial changes" a structural property rather
/// than something the tests have to catch: a rolled-back transaction has nothing to undo
/// because it never wrote anything.
/// </para>
/// <para>
/// Reads through the transaction see its own staged state, so an operation can update a
/// document and then read it back mid-edit.
/// </para>
/// <para>
/// Disposing an uncommitted transaction rolls it back. Wrapping one in <c>using</c> is
/// therefore the safe default: an exception between the first change and the commit discards
/// the partial work instead of leaving it published.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var transaction = workspace.BeginTransaction("Rename resource");
///
/// transaction.ApplyChanges(view, viewChanges);
/// transaction.ApplyChanges(theme, themeChanges);
///
/// transaction.Commit();
/// </code>
/// </example>
public sealed class MarkupTransaction : IDisposable
{
    private readonly MarkupWorkspace _workspace;
    private readonly Dictionary<MarkupDocumentId, DocumentTransition> _transitions = [];

    private WorkspaceState _pending;

    internal MarkupTransaction(MarkupWorkspace workspace, WorkspaceState original, string description)
    {
        _workspace = workspace;
        Original = original;
        _pending = original;
        Description = description;
    }

    /// <summary>Gets the human-readable description this transaction was opened with.</summary>
    /// <remarks>Surfaced by <see cref="MarkupWorkspace.UndoDescription"/> so a host can label its undo command.</remarks>
    public string Description { get; }

    /// <summary>Gets the transaction's lifecycle stage.</summary>
    public MarkupTransactionState State { get; private set; } = MarkupTransactionState.Active;

    /// <summary>Gets a value indicating whether the transaction has staged any change.</summary>
    public bool HasChanges => _transitions.Count > 0;

    /// <summary>Gets the number of documents this transaction has touched.</summary>
    public int ChangedDocumentCount => _transitions.Count;

    internal WorkspaceState Original { get; }

    internal WorkspaceState Pending => _pending;

    internal ImmutableArray<DocumentTransition> Transitions => [.. _transitions.Values];

    /// <summary>Gets a document as this transaction currently sees it.</summary>
    /// <param name="id">The document's identity.</param>
    /// <returns>The staged snapshot.</returns>
    /// <exception cref="InvalidOperationException">The transaction is no longer active, or the document is not open.</exception>
    public MarkupDocument GetDocument(MarkupDocumentId id) =>
        TryGetDocument(id, out MarkupDocument? document)
            ? document
            : throw new InvalidOperationException($"Document '{id}' is not open in this transaction.");

    /// <summary>Attempts to get a document as this transaction currently sees it.</summary>
    /// <param name="id">The document's identity.</param>
    /// <param name="document">The staged snapshot, when the document is open.</param>
    /// <returns><see langword="true"/> if the document is open.</returns>
    /// <exception cref="InvalidOperationException">The transaction is no longer active.</exception>
    public bool TryGetDocument(MarkupDocumentId id, [NotNullWhen(true)] out MarkupDocument? document)
    {
        ThrowIfNotActive();

        return _pending.Documents.TryGetValue(id, out document);
    }

    /// <summary>Stages the addition of a document.</summary>
    /// <param name="uri">The document's URI.</param>
    /// <param name="text">The document's initial text.</param>
    /// <returns>The staged snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The transaction is no longer active, or a document is already staged at that URI.</exception>
    public MarkupDocument AddDocument(Uri uri, SourceText text)
    {
        ThrowIfNotActive();
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(text);

        if (_pending.DocumentsByUri.ContainsKey(uri))
        {
            throw new InvalidOperationException(
                $"A document is already open at '{uri}'. Close it before opening another.");
        }

        MarkupDocumentId id = MarkupDocumentId.New();
        MarkupDocument document = MarkupDocument.Create(id, uri, text, _pending.NextVersion(id));

        _pending = _pending.WithDocument(document);
        Record(id, uri, before: null, after: text);

        return document;
    }

    /// <summary>Stages a replacement of a document's text.</summary>
    /// <param name="id">The document's identity.</param>
    /// <param name="text">The new text.</param>
    /// <returns>The staged snapshot, or the current one when the text is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The transaction is no longer active, or the document is not open.</exception>
    public MarkupDocument UpdateDocument(MarkupDocumentId id, SourceText text)
    {
        ThrowIfNotActive();
        ArgumentNullException.ThrowIfNull(text);

        MarkupDocument current = GetDocument(id);

        if (ReferenceEquals(current.Text, text))
        {
            return current;
        }

        MarkupDocument updated = MarkupDocument.Create(id, current.Uri, text, _pending.NextVersion(id));

        _pending = _pending.WithDocument(updated);
        Record(id, current.Uri, before: OriginalTextOf(id), after: text);

        return updated;
    }

    /// <summary>Stages changes against a document's current staged text.</summary>
    /// <param name="id">The document's identity.</param>
    /// <param name="changes">The changes to apply, ordered and non-overlapping.</param>
    /// <returns>The staged snapshot, or the current one when <paramref name="changes"/> is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The transaction is no longer active, or the document is not open.</exception>
    public MarkupDocument ApplyChanges(MarkupDocumentId id, IReadOnlyList<TextChange> changes)
    {
        ThrowIfNotActive();
        ArgumentNullException.ThrowIfNull(changes);

        MarkupDocument current = GetDocument(id);

        return UpdateDocument(id, current.Text.WithChanges(changes));
    }

    /// <summary>Stages the removal of a document.</summary>
    /// <param name="id">The document's identity.</param>
    /// <returns><see langword="true"/> if the document was open.</returns>
    /// <exception cref="InvalidOperationException">The transaction is no longer active.</exception>
    public bool RemoveDocument(MarkupDocumentId id)
    {
        ThrowIfNotActive();

        if (!_pending.Documents.TryGetValue(id, out MarkupDocument? current))
        {
            return false;
        }

        _pending = _pending.WithoutDocument(current);
        Record(id, current.Uri, before: OriginalTextOf(id), after: null);

        return true;
    }

    /// <summary>Publishes every staged change to the workspace as one atomic step.</summary>
    /// <remarks>
    /// The workspace must not have changed since this transaction was opened. If another
    /// transaction committed in the meantime, this one fails rather than overwriting it; the
    /// caller should recompute its changes against the current state and retry.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The transaction is no longer active, or the workspace changed after it was opened.
    /// </exception>
    public void Commit()
    {
        ThrowIfNotActive();

        _workspace.CommitTransaction(this);
        State = MarkupTransactionState.Committed;
    }

    /// <summary>Discards every staged change.</summary>
    /// <remarks>
    /// Nothing was published, so there is nothing to unwind. Rolling back an already
    /// rolled-back transaction is a no-op; rolling back a committed one is invalid — use
    /// <see cref="MarkupWorkspace.Undo"/> for that.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The transaction was already committed.</exception>
    public void Rollback()
    {
        if (State == MarkupTransactionState.Committed)
        {
            throw new InvalidOperationException(
                "This transaction was already committed. Use MarkupWorkspace.Undo to reverse a committed transaction.");
        }

        _pending = Original;
        _transitions.Clear();
        State = MarkupTransactionState.RolledBack;
    }

    /// <summary>Rolls the transaction back if it is still active.</summary>
    public void Dispose()
    {
        if (State == MarkupTransactionState.Active)
        {
            Rollback();
        }
    }

    /// <summary>
    /// Records a document's transition, keeping the text it had when this transaction opened.
    /// </summary>
    /// <remarks>
    /// Several changes to one document collapse into a single transition, so undoing the
    /// transaction returns straight to the pre-transaction text rather than stepping back
    /// through each intermediate edit.
    /// </remarks>
    private void Record(MarkupDocumentId id, Uri uri, SourceText? before, SourceText? after) =>
        _transitions[id] = new DocumentTransition(id, uri, before, after);

    private SourceText? OriginalTextOf(MarkupDocumentId id) =>
        _transitions.TryGetValue(id, out DocumentTransition? existing)
            ? existing.Before
            : Original.Documents.TryGetValue(id, out MarkupDocument? original)
                ? original.Text
                : null;

    private void ThrowIfNotActive()
    {
        if (State != MarkupTransactionState.Active)
        {
            throw new InvalidOperationException(
                $"This transaction is {State} and can no longer be used. Open a new one.");
        }
    }
}
