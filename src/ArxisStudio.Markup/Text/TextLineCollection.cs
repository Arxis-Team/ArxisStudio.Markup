using System;
using System.Collections;
using System.Collections.Generic;

namespace ArxisStudio.Markup;

/// <summary>
/// The lines of a <see cref="SourceText"/>, with offset-to-position lookup in logarithmic time.
/// </summary>
/// <remarks>
/// Built once per snapshot from a precomputed table of line starts. Because snapshots are
/// immutable the table never has to be invalidated, and reads are safe from any thread.
/// </remarks>
public sealed class TextLineCollection : IReadOnlyList<TextLine>
{
    private readonly SourceText _text;

    /// <summary>Offsets of the first character of each line. Always contains at least one entry.</summary>
    private readonly int[] _lineStarts;

    internal TextLineCollection(SourceText text, int[] lineStarts)
    {
        _text = text;
        _lineStarts = lineStarts;
    }

    /// <summary>Gets the number of lines. Text that ends with a line break has a final empty line.</summary>
    public int Count => _lineStarts.Length;

    /// <summary>Gets the line with the given zero-based number.</summary>
    /// <param name="index">The zero-based line number.</param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The line number is outside the text.</exception>
    public TextLine this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_lineStarts.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, "The line number is outside the text.");
            }

            int start = _lineStarts[index];
            int endIncludingLineBreak = index + 1 < _lineStarts.Length
                ? _lineStarts[index + 1]
                : _text.Length;

            int end = endIncludingLineBreak;

            // Walk back over the line break so the line can be reported both ways. Only the
            // final line of the text can be unterminated.
            if (end > start && end <= _text.Length)
            {
                if (end > start + 1 && _text[end - 2] == '\r' && _text[end - 1] == '\n')
                {
                    end -= 2;
                }
                else if (LineBreaks.IsLineBreak(_text[end - 1]))
                {
                    end -= 1;
                }
            }

            return new TextLine(_text, index, start, endIncludingLineBreak, end);
        }
    }

    /// <summary>Gets the line containing the given offset.</summary>
    /// <param name="position">The zero-based offset.</param>
    /// <returns>The line containing the offset.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The offset is outside the text.</exception>
    public TextLine GetLineFromPosition(int position) => this[GetLineNumberFromPosition(position)];

    /// <summary>Gets the zero-based number of the line containing the given offset.</summary>
    /// <param name="position">The zero-based offset.</param>
    /// <returns>The line number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The offset is outside the text.</exception>
    public int GetLineNumberFromPosition(int position)
    {
        if ((uint)position > (uint)_text.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position), position, "The offset is outside the text.");
        }

        int index = Array.BinarySearch(_lineStarts, position);

        // A negative result is the bitwise complement of the first line starting after the
        // offset, so the line containing it is the one before that.
        return index >= 0 ? index : ~index - 1;
    }

    /// <summary>Converts an offset into a line and column position.</summary>
    /// <param name="position">The zero-based offset.</param>
    /// <returns>The equivalent line and column.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The offset is outside the text.</exception>
    public TextPosition GetPosition(int position)
    {
        int line = GetLineNumberFromPosition(position);

        return new TextPosition(line, position - _lineStarts[line]);
    }

    /// <summary>Converts a line and column position into an offset.</summary>
    /// <param name="position">The line and column to convert.</param>
    /// <returns>The equivalent zero-based offset.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The line or column is outside the text.</exception>
    public int GetOffset(TextPosition position)
    {
        if (position.Line >= _lineStarts.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position), position, "The line is outside the text.");
        }

        TextLine line = this[position.Line];
        int offset = line.Start + position.Column;

        if (offset > line.EndIncludingLineBreak)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position), position, "The column is beyond the end of the line.");
        }

        return offset;
    }

    /// <summary>Returns an enumerator over the lines.</summary>
    /// <returns>An enumerator over the lines, in order.</returns>
    public IEnumerator<TextLine> GetEnumerator()
    {
        for (int index = 0; index < _lineStarts.Length; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
