using System;
using System.Globalization;

namespace ArxisStudio.Markup;

/// <summary>
/// A zero-based line and column pair identifying a location in a <see cref="SourceText"/>.
/// </summary>
/// <remarks>
/// A column counts UTF-16 code units from the start of its line, not grapheme clusters or
/// display columns. Tabs count as one.
/// </remarks>
/// <param name="Line">The zero-based line number.</param>
/// <param name="Column">The zero-based column, in UTF-16 code units from the start of the line.</param>
public readonly record struct TextPosition(int Line, int Column) : IComparable<TextPosition>
{
    /// <summary>Gets the zero-based line number.</summary>
    public int Line { get; } = Line >= 0
        ? Line
        : throw new ArgumentOutOfRangeException(nameof(Line), Line, "A line number cannot be negative.");

    /// <summary>Gets the zero-based column.</summary>
    public int Column { get; } = Column >= 0
        ? Column
        : throw new ArgumentOutOfRangeException(nameof(Column), Column, "A column cannot be negative.");

    /// <summary>Compares two positions in reading order.</summary>
    /// <param name="other">The position to compare against.</param>
    /// <returns>A negative value, zero, or a positive value as this position precedes, equals or follows <paramref name="other"/>.</returns>
    public int CompareTo(TextPosition other)
    {
        int lines = Line.CompareTo(other.Line);

        return lines != 0 ? lines : Column.CompareTo(other.Column);
    }

    /// <summary>Determines whether the left position precedes the right one.</summary>
    /// <param name="left">The left position.</param>
    /// <param name="right">The right position.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes <paramref name="right"/>.</returns>
    public static bool operator <(TextPosition left, TextPosition right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether the left position precedes or equals the right one.</summary>
    /// <param name="left">The left position.</param>
    /// <param name="right">The right position.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> does not follow <paramref name="right"/>.</returns>
    public static bool operator <=(TextPosition left, TextPosition right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether the left position follows the right one.</summary>
    /// <param name="left">The left position.</param>
    /// <param name="right">The right position.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows <paramref name="right"/>.</returns>
    public static bool operator >(TextPosition left, TextPosition right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether the left position follows or equals the right one.</summary>
    /// <param name="left">The left position.</param>
    /// <param name="right">The right position.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> does not precede <paramref name="right"/>.</returns>
    public static bool operator >=(TextPosition left, TextPosition right) => left.CompareTo(right) >= 0;

    /// <summary>Returns a string of the form <c>line:column</c>, using zero-based values.</summary>
    /// <returns>A readable representation of the position.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Line}:{Column}");
}
