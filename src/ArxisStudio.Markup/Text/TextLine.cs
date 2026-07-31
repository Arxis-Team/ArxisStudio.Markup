using System;
using System.Globalization;

namespace ArxisStudio.Markup;

/// <summary>
/// A single line within a <see cref="SourceText"/>, exposed both with and without its
/// trailing line break.
/// </summary>
/// <remarks>
/// The distinction matters to this library: the line break is part of the source text and must
/// survive a round trip untouched, so it is reported rather than trimmed away.
/// </remarks>
public sealed class TextLine
{
    private readonly SourceText _text;

    internal TextLine(SourceText text, int lineNumber, int start, int endIncludingLineBreak, int end)
    {
        _text = text;
        LineNumber = lineNumber;
        Start = start;
        End = end;
        EndIncludingLineBreak = endIncludingLineBreak;
    }

    /// <summary>Gets the zero-based line number.</summary>
    public int LineNumber { get; }

    /// <summary>Gets the offset of the first character of the line.</summary>
    public int Start { get; }

    /// <summary>Gets the offset one past the last character of the line, excluding the line break.</summary>
    public int End { get; }

    /// <summary>Gets the offset one past the line break that terminates the line.</summary>
    public int EndIncludingLineBreak { get; }

    /// <summary>Gets the span of the line without its trailing line break.</summary>
    public TextSpan Span => TextSpan.FromBounds(Start, End);

    /// <summary>Gets the span of the line including its trailing line break.</summary>
    public TextSpan SpanIncludingLineBreak => TextSpan.FromBounds(Start, EndIncludingLineBreak);

    /// <summary>
    /// Gets the length of the line break terminating this line, which is zero for the last line
    /// when the text does not end with one.
    /// </summary>
    public int LineBreakLength => EndIncludingLineBreak - End;

    /// <summary>Gets the text of the line, excluding its trailing line break.</summary>
    /// <returns>The line's text.</returns>
    public override string ToString() => _text.GetText(Span);

    /// <summary>Gets the text of the line including its trailing line break.</summary>
    /// <returns>The line's text with its line break, exactly as it appears in the source.</returns>
    public string ToStringIncludingLineBreak() => _text.GetText(SpanIncludingLineBreak);

    /// <summary>Gets the line break terminating this line, exactly as it appears in the source.</summary>
    /// <returns>The line break, or an empty string when the line is not terminated by one.</returns>
    public string GetLineBreak() =>
        _text.GetText(TextSpan.FromBounds(End, EndIncludingLineBreak));

    /// <summary>Returns a diagnostic string of the form <c>line N [start..end)</c>.</summary>
    /// <returns>A readable description of the line's position.</returns>
    public string ToDebugString() =>
        string.Create(CultureInfo.InvariantCulture, $"line {LineNumber} {Span}");
}
