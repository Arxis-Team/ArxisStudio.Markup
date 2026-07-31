using System;

namespace ArxisStudio.Markup;

/// <summary>
/// Describes a document transition within a <see cref="MarkupWorkspace"/>.
/// </summary>
/// <remarks>
/// Both snapshots are carried so a handler can compute what actually changed without racing
/// the workspace for the current state. Because snapshots are immutable, the ones reported
/// here stay valid however long the handler holds them.
/// </remarks>
public sealed class DocumentChangedEventArgs : EventArgs
{
    /// <summary>Creates the event arguments.</summary>
    /// <param name="kind">What happened to the document.</param>
    /// <param name="oldDocument">The snapshot before the change, or <see langword="null"/> when the document was opened.</param>
    /// <param name="newDocument">The snapshot after the change, or <see langword="null"/> when the document was closed.</param>
    public DocumentChangedEventArgs(
        DocumentChangeKind kind,
        MarkupDocument? oldDocument,
        MarkupDocument? newDocument)
    {
        Kind = kind;
        OldDocument = oldDocument;
        NewDocument = newDocument;
    }

    /// <summary>Gets what happened to the document.</summary>
    public DocumentChangeKind Kind { get; }

    /// <summary>Gets the snapshot before the change, or <see langword="null"/> when the document was opened.</summary>
    public MarkupDocument? OldDocument { get; }

    /// <summary>Gets the snapshot after the change, or <see langword="null"/> when the document was closed.</summary>
    public MarkupDocument? NewDocument { get; }

    /// <summary>Gets the identity of the document the change concerns.</summary>
    public MarkupDocumentId DocumentId => (NewDocument ?? OldDocument)!.Id;
}
