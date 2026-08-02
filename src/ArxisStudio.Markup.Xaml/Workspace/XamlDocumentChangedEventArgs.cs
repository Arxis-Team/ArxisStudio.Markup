using System;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Says what happened to a document in a <see cref="XamlWorkspace"/>.
/// </summary>
/// <remarks>
/// Raised for undo and redo as well as for an edit, because to anything drawing the document
/// those are the same event: the text is not what it was, and whatever was built from it is out
/// of date.
/// </remarks>
public sealed class XamlDocumentChangedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="kind">What happened.</param>
    /// <param name="documentId">Which document it happened to.</param>
    /// <param name="document">The document as it now reads, or <see langword="null"/> when it was closed.</param>
    public XamlDocumentChangedEventArgs(
        DocumentChangeKind kind,
        MarkupDocumentId documentId,
        XamlDocument? document)
    {
        Kind = kind;
        DocumentId = documentId;
        Document = document;
    }

    /// <summary>Gets what happened.</summary>
    public DocumentChangeKind Kind { get; }

    /// <summary>Gets which document it happened to.</summary>
    public MarkupDocumentId DocumentId { get; }

    /// <summary>Gets the document as it now reads, or <see langword="null"/> when it was closed.</summary>
    public XamlDocument? Document { get; }
}
