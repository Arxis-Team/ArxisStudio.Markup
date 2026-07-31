using System;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Tests;

public sealed class TextLineCollectionTests
{
    [Fact]
    public void EmptyText_HasOneEmptyLine()
    {
        SourceText text = SourceText.From(string.Empty);

        Assert.Single(text.Lines);
        Assert.Equal(string.Empty, text.Lines[0].ToString());
    }

    [Fact]
    public void TextWithoutALineBreak_HasOneLine()
    {
        SourceText text = SourceText.From("<Button />");

        Assert.Single(text.Lines);
        Assert.Equal("<Button />", text.Lines[0].ToString());
        Assert.Equal(0, text.Lines[0].LineBreakLength);
    }

    [Theory]
    [InlineData("a\nb", "\n")]
    [InlineData("a\r\nb", "\r\n")]
    [InlineData("a\rb", "\r")]
    public void EachLineBreakStyle_IsRecognisedAndReportedVerbatim(string source, string expectedBreak)
    {
        SourceText text = SourceText.From(source);

        Assert.Equal(2, text.Lines.Count);
        Assert.Equal("a", text.Lines[0].ToString());
        Assert.Equal("b", text.Lines[1].ToString());
        Assert.Equal(expectedBreak, text.Lines[0].GetLineBreak());
    }

    [Fact]
    public void MixedLineBreaks_AreEachPreserved()
    {
        // A document edited on two platforms is the normal case, not a corner case. Rewriting
        // any of these breaks would destroy source the caller never touched.
        SourceText text = SourceText.From("a\r\nb\nc\rd");

        Assert.Equal(4, text.Lines.Count);
        Assert.Equal("\r\n", text.Lines[0].GetLineBreak());
        Assert.Equal("\n", text.Lines[1].GetLineBreak());
        Assert.Equal("\r", text.Lines[2].GetLineBreak());
        Assert.Equal(string.Empty, text.Lines[3].GetLineBreak());
    }

    [Fact]
    public void TrailingLineBreak_ProducesAFinalEmptyLine()
    {
        SourceText text = SourceText.From("a\n");

        Assert.Equal(2, text.Lines.Count);
        Assert.Equal(string.Empty, text.Lines[1].ToString());
    }

    [Fact]
    public void LineSpans_ExcludeAndIncludeTheBreak()
    {
        SourceText text = SourceText.From("ab\r\ncd");
        TextLine first = text.Lines[0];

        Assert.Equal(new TextSpan(0, 2), first.Span);
        Assert.Equal(new TextSpan(0, 4), first.SpanIncludingLineBreak);
        Assert.Equal("ab", first.ToString());
        Assert.Equal("ab\r\n", first.ToStringIncludingLineBreak());
    }

    [Fact]
    public void GetLineFromPosition_FindsTheContainingLine()
    {
        SourceText text = SourceText.From("abc\ndef\nghi");

        Assert.Equal(0, text.Lines.GetLineNumberFromPosition(0));
        Assert.Equal(0, text.Lines.GetLineNumberFromPosition(3));
        Assert.Equal(1, text.Lines.GetLineNumberFromPosition(4));
        Assert.Equal(2, text.Lines.GetLineNumberFromPosition(text.Length));
    }

    [Fact]
    public void PositionRoundTrip_HoldsForEveryOffset()
    {
        SourceText text = SourceText.From("<Grid>\r\n  <Button\n    Width=\"320\" />\r</Grid>");

        for (int offset = 0; offset <= text.Length; offset++)
        {
            TextPosition position = text.Lines.GetPosition(offset);

            Assert.Equal(offset, text.Lines.GetOffset(position));
        }
    }

    [Fact]
    public void GetPosition_ReportsZeroBasedLineAndColumn()
    {
        SourceText text = SourceText.From("abc\ndef");

        Assert.Equal(new TextPosition(0, 0), text.Lines.GetPosition(0));
        Assert.Equal(new TextPosition(1, 2), text.Lines.GetPosition(6));
    }

    [Fact]
    public void OffsetsOutsideTheText_AreRejected()
    {
        SourceText text = SourceText.From("abc");

        Assert.Throws<ArgumentOutOfRangeException>(() => text.Lines.GetLineNumberFromPosition(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => text.Lines.GetLineNumberFromPosition(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => text.Lines[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => text.Lines.GetOffset(new TextPosition(0, 99)));
    }

    [Fact]
    public void Enumeration_YieldsEveryLineInOrder()
    {
        SourceText text = SourceText.From("a\nb\nc");

        Assert.Equal(["a", "b", "c"], text.Lines.Select(static line => line.ToString()));
    }

    [Fact]
    public void LineLookup_ScalesToLargeDocuments()
    {
        // The contract targets documents up to several megabytes, so line lookup must not be
        // a linear scan. This asserts correctness at scale; the binary search is what makes
        // it fast enough to run at all.
        string source = string.Join('\n', Enumerable.Range(0, 50_000).Select(static i => $"line {i}"));
        SourceText text = SourceText.From(source);

        Assert.Equal(50_000, text.Lines.Count);
        Assert.Equal("line 49999", text.Lines[49_999].ToString());
        Assert.Equal(25_000, text.Lines.GetPosition(text.Lines[25_000].Start).Line);
    }
}
