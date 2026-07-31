using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup;

/// <summary>
/// Owns the set of open documents and their current snapshots.
/// </summary>
/// <remarks>
/// <para>
/// The workspace holds one immutable map of identity to snapshot. Mutations take a lock and
/// publish a replacement map; readers take the current map without any lock at all. That is
/// what makes concurrent reads of snapshots safe while an edit is in flight — a reader either
/// sees the state before the edit or the state after it, never a half-applied one.
/// </para>
/// <para>
/// Transactions, batched notifications and undo/redo build on this in milestone 2.
/// </para>
/// </remarks>
public sealed class MarkupWorkspace
{
    private readonly IMarkupSourceProvider _sourceProvider;
    private readonly Lock _gate = new();

    private ImmutableDictionary<MarkupDocumentId, MarkupDocument> _documents =
        ImmutableDictionary<MarkupDocumentId, MarkupDocument>.Empty;

    private ImmutableDictionary<Uri, MarkupDocumentId> _documentsByUri =
        ImmutableDictionary<Uri, MarkupDocumentId>.Empty;

    /// <summary>Creates a workspace that resolves documents through a provider.</summary>
    /// <param name="sourceProvider">The provider used to open documents.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sourceProvider"/> is <see langword="null"/>.</exception>
    public MarkupWorkspace(IMarkupSourceProvider sourceProvider)
    {
        ArgumentNullException.ThrowIfNull(sourceProvider);

        _sourceProvider = sourceProvider;
    }

    /// <summary>Raised after a document is opened, changed or closed.</summary>
    /// <remarks>
    /// Raised outside the workspace lock, so a handler may read the workspace without
    /// deadlocking. A handler that throws will propagate to the caller that caused the change.
    /// </remarks>
    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;

    /// <summary>Gets the provider documents are opened through.</summary>
    public IMarkupSourceProvider SourceProvider => _sourceProvider;

    /// <summary>Gets a snapshot of the currently open documents.</summary>
    /// <remarks>The returned collection is a point-in-time view and is not affected by later changes.</remarks>
    public IReadOnlyCollection<MarkupDocument> Documents => Volatile.Read(ref _documents).Values.ToImmutableArray();

    /// <summary>Gets the number of currently open documents.</summary>
    public int Count => Volatile.Read(ref _documents).Count;

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

        MarkupSource source = await _sourceProvider.TryGetSourceAsync(uri, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No source provider could resolve '{uri}'. Supply a provider that knows this URI.");

        SourceText text = await source.GetTextAsync(cancellationToken).ConfigureAwait(false);

        return AddDocument(new MarkupDocument(MarkupDocumentId.New(), uri, text));
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

        return AddDocument(new MarkupDocument(MarkupDocumentId.New(), uri, text));
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
        Volatile.Read(ref _documents).TryGetValue(id, out document);

    /// <summary>Attempts to get the snapshot of the open document at a URI.</summary>
    /// <param name="uri">The document's URI.</param>
    /// <param name="document">The document's current snapshot, when one is open at that URI.</param>
    /// <returns><see langword="true"/> if a document is open at that URI.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public bool TryGetDocumentByUri(Uri uri, [NotNullWhen(true)] out MarkupDocument? document)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (Volatile.Read(ref _documentsByUri).TryGetValue(uri, out MarkupDocumentId id))
        {
            return TryGetDocument(id, out document);
        }

        document = null;

        return false;
    }

    /// <summary>Replaces an open document's text, advancing its version.</summary>
    /// <param name="id">The document's identity.</param>
    /// <param name="text">The new text.</param>
    /// <returns>
    /// The new snapshot, or the existing one when the text is unchanged, so that a no-op
    /// update raises no event and consumes no version.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The document is not open.</exception>
    public MarkupDocument UpdateDocument(MarkupDocumentId id, SourceText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        MarkupDocument oldDocument;
        MarkupDocument newDocument;

        lock (_gate)
        {
            if (!_documents.TryGetValue(id, out MarkupDocument? current))
            {
                throw new InvalidOperationException($"Document '{id}' is not open in this workspace.");
            }

            oldDocument = current;
            newDocument = current.WithText(text);

            if (ReferenceEquals(newDocument, current))
            {
                return current;
            }

            _documents = _documents.SetItem(id, newDocument);
        }

        RaiseDocumentChanged(DocumentChangeKind.Changed, oldDocument, newDocument);

        return newDocument;
    }

    /// <summary>Applies changes to an open document's text, advancing its version.</summary>
    /// <param name="id">The document's identity.</param>
    /// <param name="changes">The changes to apply, ordered and non-overlapping.</param>
    /// <returns>The new snapshot, or the existing one when <paramref name="changes"/> is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The document is not open.</exception>
    public MarkupDocument ApplyChanges(MarkupDocumentId id, IReadOnlyList<TextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        MarkupDocument current = GetDocument(id);

        return UpdateDocument(id, current.Text.WithChanges(changes));
    }

    /// <summary>Removes a document from the workspace.</summary>
    /// <param name="id">The document's identity.</param>
    /// <returns><see langword="true"/> if the document was open.</returns>
    public bool CloseDocument(MarkupDocumentId id)
    {
        MarkupDocument closed;

        lock (_gate)
        {
            if (!_documents.TryGetValue(id, out MarkupDocument? current))
            {
                return false;
            }

            closed = current;
            _documents = _documents.Remove(id);

            // Only drop the URI mapping if it still points at this document, so that a
            // document reopened at the same URI is not unmapped by a late close.
            if (_documentsByUri.TryGetValue(current.Uri, out MarkupDocumentId mapped) && mapped == id)
            {
                _documentsByUri = _documentsByUri.Remove(current.Uri);
            }
        }

        RaiseDocumentChanged(DocumentChangeKind.Closed, closed, newDocument: null);

        return true;
    }

    private MarkupDocument AddDocument(MarkupDocument document)
    {
        lock (_gate)
        {
            if (_documentsByUri.ContainsKey(document.Uri))
            {
                throw new InvalidOperationException(
                    $"A document is already open at '{document.Uri}'. Close it before opening another.");
            }

            _documents = _documents.Add(document.Id, document);
            _documentsByUri = _documentsByUri.Add(document.Uri, document.Id);
        }

        RaiseDocumentChanged(DocumentChangeKind.Opened, oldDocument: null, document);

        return document;
    }

    private void RaiseDocumentChanged(
        DocumentChangeKind kind,
        MarkupDocument? oldDocument,
        MarkupDocument? newDocument) =>
        DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(kind, oldDocument, newDocument));
}
