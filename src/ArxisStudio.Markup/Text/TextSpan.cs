using System;
using System.Globalization;

namespace ArxisStudio.Markup;

/// <summary>
/// A contiguous range of characters within a <see cref="SourceText"/>, expressed as a
/// zero-based start offset and a length.
/// </summary>
/// <remarks>
/// A span is a position in text, not a reference to it. It carries no association with a
/// particular snapshot, so a span taken from one snapshot is only meaningful against another
/// once the intervening changes have been accounted for.
/// </remarks>
/// <param name="Start">The zero-based offset of the first character in the span.</param>
/// <param name="Length">The number of characters in the span.</param>
public readonly record struct TextSpan(int Start, int Length)
{
    /// <summary>Gets the zero-based offset of the first character in the span.</summary>
    public int Start { get; } = Start >= 0
        ? Start
        : throw new ArgumentOutOfRangeException(nameof(Start), Start, "A span start cannot be negative.");

    /// <summary>Gets the number of characters in the span.</summary>
    public int Length { get; } = Length >= 0
        ? Length
        : throw new ArgumentOutOfRangeException(nameof(Length), Length, "A span length cannot be negative.");

    /// <summary>Gets the offset one past the last character in the span.</summary>
    public int End => Start + Length;

    /// <summary>Gets a value indicating whether the span contains no characters.</summary>
    public bool IsEmpty => Length == 0;

    /// <summary>Creates a span from a start offset and an exclusive end offset.</summary>
    /// <param name="start">The zero-based offset of the first character.</param>
    /// <param name="end">The offset one past the last character.</param>
    /// <returns>The span covering <paramref name="start"/> up to but excluding <paramref name="end"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="end"/> precedes <paramref name="start"/>.</exception>
    public static TextSpan FromBounds(int start, int end)
    {
        if (end < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end), end, "A span end cannot precede its start.");
        }

        return new TextSpan(start, end - start);
    }

    /// <summary>
    /// Determines whether the span contains the given position. The end offset is excluded,
    /// so an empty span contains no position.
    /// </summary>
    /// <param name="position">The zero-based offset to test.</param>
    /// <returns><see langword="true"/> if the position falls inside the span.</returns>
    public bool Contains(int position) => unchecked((uint)(position - Start) < (uint)Length);

    /// <summary>Determines whether this span fully contains another span.</summary>
    /// <param name="span">The span to test.</param>
    /// <returns><see langword="true"/> if <paramref name="span"/> lies entirely within this one.</returns>
    public bool Contains(TextSpan span) => span.Start >= Start && span.End <= End;

    /// <summary>
    /// Determines whether the two spans share at least one character. Spans that merely touch
    /// end to start do not overlap.
    /// </summary>
    /// <param name="span">The span to test.</param>
    /// <returns><see langword="true"/> if the spans share at least one character.</returns>
    public bool OverlapsWith(TextSpan span) => Math.Max(Start, span.Start) < Math.Min(End, span.End);

    /// <summary>
    /// Determines whether the two spans overlap or touch. Unlike <see cref="OverlapsWith"/>,
    /// an empty span at the boundary of another intersects it.
    /// </summary>
    /// <param name="span">The span to test.</param>
    /// <returns><see langword="true"/> if the spans overlap or touch.</returns>
    public bool IntersectsWith(TextSpan span) => span.Start <= End && span.End >= Start;

    /// <summary>Returns the overlapping portion of the two spans, if any.</summary>
    /// <param name="span">The span to intersect with.</param>
    /// <returns>The shared portion, or <see langword="null"/> when the spans do not overlap.</returns>
    public TextSpan? Overlap(TextSpan span)
    {
        int start = Math.Max(Start, span.Start);
        int end = Math.Min(End, span.End);

        return start < end ? FromBounds(start, end) : null;
    }

    /// <summary>Returns a string of the form <c>[start..end)</c>.</summary>
    /// <returns>A readable representation of the span's bounds.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"[{Start}..{End})");
}
