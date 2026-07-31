using System;
using System.Globalization;

namespace ArxisStudio.Markup;

/// <summary>
/// One unbroken run of a projection's text, and the document it was copied from.
/// </summary>
/// <remarks>
/// <para>
/// A projection mostly copies text rather than generating it, so the segments tile the projected
/// text end to end and mapping a position back is total: no position in a projection came from
/// nowhere.
/// </para>
/// <para>
/// A <see cref="IsSynthesized"/> segment is the exception, and it is why the map answers a
/// position rather than a range. Text a projection had to write for itself stands for a point in
/// the source rather than for a stretch of it, so every position inside it maps to that one
/// point instead of pretending to a character-by-character correspondence it does not have.
/// </para>
/// </remarks>
public sealed class TextProjectionSegment
{
    internal TextProjectionSegment(TextSpan projectedSpan, int sourceStart, Uri? sourceUri, bool isOriginal)
        : this(projectedSpan, new TextSpan(sourceStart, projectedSpan.Length), sourceUri, isOriginal, false)
    {
    }

    internal TextProjectionSegment(
        TextSpan projectedSpan,
        TextSpan sourceSpan,
        Uri? sourceUri,
        bool isOriginal,
        bool isSynthesized)
    {
        ProjectedSpan = projectedSpan;
        SourceSpan = sourceSpan;
        SourceUri = sourceUri;
        IsOriginal = isOriginal;
        IsSynthesized = isSynthesized;
    }

    /// <summary>Gets the range this run occupies in the projected text.</summary>
    public TextSpan ProjectedSpan { get; }

    /// <summary>Gets the range it was copied from, in the text of the document it came from.</summary>
    public TextSpan SourceSpan { get; }

    /// <summary>Gets the document it came from, when that document has a URI.</summary>
    public Uri? SourceUri { get; }

    /// <summary>
    /// Gets a value indicating whether it came from the text being projected rather than from
    /// something spliced into it.
    /// </summary>
    public bool IsOriginal { get; }

    /// <summary>
    /// Gets a value indicating whether the projection wrote this run itself rather than copying
    /// it, in which case <see cref="SourceSpan"/> is the point it stands for.
    /// </summary>
    public bool IsSynthesized { get; }

    /// <summary>Maps an offset inside this run back to the document it came from.</summary>
    /// <param name="projectedOffset">An offset within <see cref="ProjectedSpan"/>.</param>
    /// <returns>The position in the document this run was copied from.</returns>
    internal TextProjectionPosition Map(int projectedOffset) => new(
        SourceUri,
        IsSynthesized ? SourceSpan.Start : SourceSpan.Start + (projectedOffset - ProjectedSpan.Start),
        IsOriginal);

    /// <summary>Returns the projected range, the source range and the document.</summary>
    /// <returns>A readable description of the segment.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{ProjectedSpan} <- {SourceSpan} of {(SourceUri is null ? "<unnamed>" : SourceUri.OriginalString)}");
}
