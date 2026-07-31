using System;

namespace ArxisStudio.Markup;

/// <summary>
/// An immutable snapshot of one document: its identity, its location, its text and the version
/// that text is at.
/// </summary>
/// <remarks>
/// Producing a new snapshot advances <see cref="Version"/> but never changes <see cref="Id"/>.
/// That separation is what lets callers hold a reference to a document across edits, and what
/// lets the workspace tell "the same document, later" apart from "a different document".
/// </remarks>
public sealed class MarkupDocument
{
    /// <summary>Creates the first snapshot of a document.</summary>
    /// <param name="id">The document's stable identity.</param>
    /// <param name="uri">The document's location, which need not name an existing file.</param>
    /// <param name="text">The document's text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    public MarkupDocument(MarkupDocumentId id, Uri uri, SourceText text)
        : this(id, uri, text, DocumentVersion.Initial)
    {
    }

    private MarkupDocument(MarkupDocumentId id, Uri uri, SourceText text, DocumentVersion version)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(text);

        Id = id;
        Uri = uri;
        Text = text;
        Version = version;
    }

    /// <summary>Gets the document's stable identity, which survives every edit.</summary>
    public MarkupDocumentId Id { get; }

    /// <summary>
    /// Gets the document's location. This is an identifier, not necessarily a filesystem path:
    /// an unsaved document may carry a URI no file corresponds to.
    /// </summary>
    public Uri Uri { get; }

    /// <summary>Gets the document's text at this version.</summary>
    public SourceText Text { get; }

    /// <summary>Gets the version of the text in this snapshot.</summary>
    public DocumentVersion Version { get; }

    /// <summary>
    /// Produces the next snapshot of this document with different text, keeping the identity
    /// and advancing the version.
    /// </summary>
    /// <param name="text">The new text.</param>
    /// <returns>
    /// The next snapshot, or this one when <paramref name="text"/> is the snapshot's current
    /// text, so that a no-op update does not consume a version.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public MarkupDocument WithText(SourceText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ReferenceEquals(text, Text)
            ? this
            : new MarkupDocument(Id, Uri, text, Version.Next());
    }

    /// <summary>Produces the next snapshot with the given changes applied to the text.</summary>
    /// <param name="changes">The changes to apply, ordered and non-overlapping.</param>
    /// <returns>The next snapshot, or this one when <paramref name="changes"/> is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public MarkupDocument WithChanges(System.Collections.Generic.IReadOnlyList<TextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return WithText(Text.WithChanges(changes));
    }

    /// <summary>Produces a snapshot of this document at a different location.</summary>
    /// <param name="uri">The new location.</param>
    /// <returns>The snapshot, or this one when the location is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public MarkupDocument WithUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri == Uri ? this : new MarkupDocument(Id, uri, Text, Version.Next());
    }

    /// <summary>Returns a string of the form <c>uri@version</c>.</summary>
    /// <returns>A readable description of the snapshot.</returns>
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Uri}@{Version}");
}
