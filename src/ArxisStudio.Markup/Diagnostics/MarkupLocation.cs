using System;

namespace ArxisStudio.Markup;

/// <summary>
/// A place in a document that a diagnostic refers to.
/// </summary>
/// <remarks>
/// Used for the secondary locations a diagnostic needs to be understandable: the other end of
/// a resource include cycle, or the earlier declaration a duplicate key collides with.
/// </remarks>
/// <param name="DocumentUri">The document the location is in.</param>
/// <param name="Span">The range within that document, when the location is more precise than the whole file.</param>
/// <param name="Message">An optional note explaining this location's role in the diagnostic.</param>
public readonly record struct MarkupLocation(Uri DocumentUri, TextSpan? Span = null, string? Message = null)
{
    /// <summary>Gets the document the location is in.</summary>
    public Uri DocumentUri { get; } = DocumentUri ?? throw new ArgumentNullException(nameof(DocumentUri));

    /// <summary>Returns the location in <c>uri[start..end)</c> form.</summary>
    /// <returns>A readable representation of the location.</returns>
    public override string ToString() =>
        Span is { } span ? $"{DocumentUri}{span}" : DocumentUri.ToString();
}
