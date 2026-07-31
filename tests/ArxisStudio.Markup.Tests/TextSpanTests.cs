using System;
using Xunit;

namespace ArxisStudio.Markup.Tests;

public sealed class TextSpanTests
{
    [Fact]
    public void End_IsStartPlusLength()
    {
        var span = new TextSpan(5, 3);

        Assert.Equal(8, span.End);
        Assert.False(span.IsEmpty);
    }

    [Fact]
    public void EmptySpan_HasZeroLengthAndCoincidentBounds()
    {
        var span = new TextSpan(7, 0);

        Assert.True(span.IsEmpty);
        Assert.Equal(7, span.Start);
        Assert.Equal(7, span.End);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void NegativeBounds_AreRejected(int start, int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(start, length));
    }

    [Fact]
    public void FromBounds_ProducesTheSpanBetweenThem()
    {
        TextSpan span = TextSpan.FromBounds(4, 10);

        Assert.Equal(4, span.Start);
        Assert.Equal(6, span.Length);
    }

    [Fact]
    public void FromBounds_RejectsAnEndBeforeItsStart()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextSpan.FromBounds(10, 4));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public void Contains_ExcludesTheEndOffset(int position, bool expected)
    {
        Assert.Equal(expected, new TextSpan(5, 3).Contains(position));
    }

    [Fact]
    public void Contains_OnAnEmptySpan_IsAlwaysFalse()
    {
        // An empty span occupies no character, so it can contain no position.
        Assert.False(new TextSpan(5, 0).Contains(5));
    }

    [Fact]
    public void Contains_RecognisesAFullyEnclosedSpan()
    {
        var outer = new TextSpan(2, 10);

        Assert.True(outer.Contains(new TextSpan(4, 3)));
        Assert.True(outer.Contains(outer));
        Assert.False(outer.Contains(new TextSpan(4, 20)));
    }

    [Fact]
    public void OverlapsWith_RequiresASharedCharacter()
    {
        var span = new TextSpan(5, 5);

        Assert.True(span.OverlapsWith(new TextSpan(7, 5)));
        Assert.False(span.OverlapsWith(new TextSpan(10, 5)));
        Assert.False(span.OverlapsWith(new TextSpan(0, 5)));
    }

    [Fact]
    public void IntersectsWith_AcceptsTouchingSpans()
    {
        var span = new TextSpan(5, 5);

        // Touching end to start is an intersection but not an overlap. Insertion points
        // depend on the difference: an empty change at an element's edge belongs to it.
        Assert.True(span.IntersectsWith(new TextSpan(10, 5)));
        Assert.True(span.IntersectsWith(new TextSpan(0, 5)));
        Assert.False(span.IntersectsWith(new TextSpan(11, 5)));
    }

    [Fact]
    public void Overlap_ReturnsTheSharedPortionOrNothing()
    {
        var span = new TextSpan(5, 5);

        Assert.Equal(new TextSpan(7, 3), span.Overlap(new TextSpan(7, 5)));
        Assert.Null(span.Overlap(new TextSpan(10, 5)));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(new TextSpan(3, 4), new TextSpan(3, 4));
        Assert.NotEqual(new TextSpan(3, 4), new TextSpan(3, 5));
        Assert.Equal(new TextSpan(3, 4).GetHashCode(), new TextSpan(3, 4).GetHashCode());
    }

    [Fact]
    public void ToString_ShowsHalfOpenBounds()
    {
        Assert.Equal("[5..8)", new TextSpan(5, 3).ToString());
    }
}
