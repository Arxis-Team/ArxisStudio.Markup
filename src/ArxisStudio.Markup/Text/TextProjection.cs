using System;
using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup;

/// <summary>
/// Text assembled from one document plus runs spliced in from others, with a map back.
/// </summary>
/// <remarks>
/// <para>
/// A projection exists so that something which insists on being handed a single self-contained
/// text can be given one without the document itself being rewritten. The document stays the
/// source of truth; the projection is derived, never saved, and thrown away when the answer has
/// been read back out of it.
/// </para>
/// <para>
/// The map back is the point. Positions reported against the projected text — a line and column
/// in an error, or the position a downstream component recorded for an object it built — are
/// meaningless against the original, because splicing moves everything after each splice. The
/// segments say which document each run of projected text came from and where in that document
/// it started, so any such position can be turned back into a real one.
/// </para>
/// </remarks>
public sealed class TextProjection
{
    private readonly int[] _projectedStarts;

    internal TextProjection(
        SourceText source,
        Uri? sourceUri,
        SourceText text,
        ImmutableArray<TextProjectionSegment> segments,
        bool isIdentity)
    {
        Source = source;
        SourceUri = sourceUri;
        Text = text;
        Segments = segments;
        IsIdentity = isIdentity;
        _projectedStarts = [.. segments.Select(static segment => segment.ProjectedSpan.Start)];
    }

    /// <summary>Gets the text that was projected.</summary>
    public SourceText Source { get; }

    /// <summary>Gets the URI of the document that was projected, when it has one.</summary>
    public Uri? SourceUri { get; }

    /// <summary>Gets the projected text.</summary>
    public SourceText Text { get; }

    /// <summary>Gets the runs the projected text is made of, in order.</summary>
    public ImmutableArray<TextProjectionSegment> Segments { get; }

    /// <summary>
    /// Gets a value indicating whether nothing was spliced in, so the projected text is the
    /// original and every position maps to itself.
    /// </summary>
    public bool IsIdentity { get; }

    /// <summary>Creates a projection that changes nothing.</summary>
    /// <param name="source">The text to project.</param>
    /// <param name="sourceUri">The URI of the document the text belongs to, if it has one.</param>
    /// <returns>A projection whose text is <paramref name="source"/> itself.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static TextProjection Identity(SourceText source, Uri? sourceUri = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new TextProjection(
            source,
            sourceUri,
            source,
            [new TextProjectionSegment(new TextSpan(0, source.Length), 0, sourceUri, isOriginal: true)],
            isIdentity: true);
    }

    /// <summary>Maps an offset in the projected text back to the document it came from.</summary>
    /// <param name="projectedOffset">
    /// A zero-based offset into <see cref="Text"/>. Offsets outside the text are clamped to it,
    /// because callers routinely pass positions reported by something with its own idea of
    /// where the text ends.
    /// </param>
    /// <returns>The document and offset the character came from.</returns>
    public TextProjectionPosition Map(int projectedOffset)
    {
        int offset = Math.Clamp(projectedOffset, 0, Text.Length);

        if (Segments.IsEmpty)
        {
            return new TextProjectionPosition(SourceUri, 0, IsOriginal: true);
        }

        int index = Array.BinarySearch(_projectedStarts, offset);

        // A negative result is the complement of the first segment starting after the offset,
        // so the segment containing it is the one before that.
        index = index >= 0 ? index : Math.Max(0, ~index - 1);

        return Segments[index].Map(offset);
    }

    /// <summary>Maps a line and column in the projected text back to the document it came from.</summary>
    /// <param name="projectedPosition">
    /// A zero-based line and column in <see cref="Text"/>. Both are clamped, for the same reason
    /// <see cref="Map(int)"/> clamps.
    /// </param>
    /// <returns>The document and offset the character came from.</returns>
    public TextProjectionPosition Map(TextPosition projectedPosition)
    {
        TextLineCollection lines = Text.Lines;
        int number = Math.Clamp(projectedPosition.Line, 0, lines.Count - 1);
        TextLine line = lines[number];

        return Map(line.Start + Math.Clamp(projectedPosition.Column, 0, line.Span.Length));
    }

    /// <summary>
    /// Maps an offset in <see cref="Source"/> forward to the projected text.
    /// </summary>
    /// <remarks>
    /// An offset inside a range that was replaced has no character of its own in the projection;
    /// it maps to where the replacement begins, so that a span containing a replacement still
    /// comes back covering everything spliced in.
    /// </remarks>
    /// <param name="sourceOffset">A zero-based offset into <see cref="Source"/>, clamped to it.</param>
    /// <returns>The equivalent offset in <see cref="Text"/>.</returns>
    public int GetProjectedOffset(int sourceOffset)
    {
        int offset = Math.Clamp(sourceOffset, 0, Source.Length);

        if (IsIdentity)
        {
            return offset;
        }

        int afterLastOriginal = 0;

        foreach (TextProjectionSegment segment in Segments)
        {
            // Synthesized runs stand for a point rather than covering one, so they carry no
            // stretch of source to find an offset within.
            if (!segment.IsOriginal || segment.IsSynthesized)
            {
                continue;
            }

            if (offset < segment.SourceSpan.Start)
            {
                // The offset is inside a replaced range. Everything spliced in there stands for
                // it, so the projection's answer is where that content starts.
                break;
            }

            // The end of a run is included, so the offset just past a stretch of original text
            // lands before whatever replaced what followed it rather than after it.
            if (offset <= segment.SourceSpan.End)
            {
                return segment.ProjectedSpan.Start + (offset - segment.SourceSpan.Start);
            }

            afterLastOriginal = segment.ProjectedSpan.End;
        }

        return afterLastOriginal;
    }

    /// <summary>Maps a range of <see cref="Source"/> forward to the projected text.</summary>
    /// <param name="sourceSpan">A range of <see cref="Source"/>.</param>
    /// <returns>The range of <see cref="Text"/> that stands for it, splices included.</returns>
    public TextSpan GetProjectedSpan(TextSpan sourceSpan) => TextSpan.FromBounds(
        GetProjectedOffset(sourceSpan.Start),
        Math.Max(GetProjectedOffset(sourceSpan.Start), GetProjectedOffset(sourceSpan.End)));
}
