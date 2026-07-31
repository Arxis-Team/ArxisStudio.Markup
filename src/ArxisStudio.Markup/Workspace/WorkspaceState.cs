using System;
using System.Collections.Immutable;

namespace ArxisStudio.Markup;

/// <summary>
/// The complete, immutable state of a <see cref="MarkupWorkspace"/>.
/// </summary>
/// <remarks>
/// <para>
/// Holding every map in one object is what makes a workspace mutation a single reference
/// write. A reader takes the current state once and then works entirely inside it, so it can
/// never observe a document present in one map and absent from another — the torn view that
/// separate fields would allow while a multi-document commit is in flight.
/// </para>
/// <para>
/// It is also what makes a transaction commit and an undo atomic: both are one swap.
/// </para>
/// </remarks>
/// <param name="Documents">The open documents, by identity.</param>
/// <param name="DocumentsByUri">The identity of the document open at each URI.</param>
/// <param name="VersionHighWater">
/// The highest version ever issued for each document, retained after the document is closed so
/// that a version is never handed out twice.
/// </param>
internal sealed record WorkspaceState(
    ImmutableDictionary<MarkupDocumentId, MarkupDocument> Documents,
    ImmutableDictionary<Uri, MarkupDocumentId> DocumentsByUri,
    ImmutableDictionary<MarkupDocumentId, DocumentVersion> VersionHighWater)
{
    public static WorkspaceState Empty { get; } = new(
        ImmutableDictionary<MarkupDocumentId, MarkupDocument>.Empty,
        ImmutableDictionary<Uri, MarkupDocumentId>.Empty,
        ImmutableDictionary<MarkupDocumentId, DocumentVersion>.Empty);

    /// <summary>Issues the next unused version for a document.</summary>
    public DocumentVersion NextVersion(MarkupDocumentId id) =>
        VersionHighWater.TryGetValue(id, out DocumentVersion highWater)
            ? highWater.Next()
            : DocumentVersion.Initial;

    /// <summary>Adds or replaces a document, recording the version it was issued.</summary>
    public WorkspaceState WithDocument(MarkupDocument document) => this with
    {
        Documents = Documents.SetItem(document.Id, document),
        DocumentsByUri = DocumentsByUri.SetItem(document.Uri, document.Id),
        VersionHighWater = VersionHighWater.SetItem(document.Id, document.Version),
    };

    /// <summary>
    /// Removes a document, keeping its version high-water mark so that reopening or undoing
    /// cannot reissue a version this document has already used.
    /// </summary>
    public WorkspaceState WithoutDocument(MarkupDocument document)
    {
        ImmutableDictionary<Uri, MarkupDocumentId> byUri =
            DocumentsByUri.TryGetValue(document.Uri, out MarkupDocumentId mapped) && mapped == document.Id
                ? DocumentsByUri.Remove(document.Uri)
                : DocumentsByUri;

        return this with
        {
            Documents = Documents.Remove(document.Id),
            DocumentsByUri = byUri,
        };
    }
}
