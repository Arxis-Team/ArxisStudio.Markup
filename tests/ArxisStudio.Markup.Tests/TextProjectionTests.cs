using System;
using Xunit;

namespace ArxisStudio.Markup.Tests;

/// <summary>
/// Splicing one document into another moves everything after the splice. These cover the map
/// that undoes that, because without it a position reported against the projected text points
/// at the wrong line of the wrong file.
/// </summary>
public sealed class TextProjectionTests
{
    private static readonly Uri OuterUri = new("file:///Views/View.axaml");
    private static readonly Uri InnerUri = new("file:///Themes/Colors.axaml");
    private static readonly Uri DeepUri = new("file:///Themes/Palette.axaml");

    private const string Outer = "<A>\n  <Include />\n  <B />\n</A>\n";
    private const string Inner = "<Colors>\n  <Red />\n</Colors>\n";

    /// <summary>The span of <c>&lt;Include /&gt;</c> in <see cref="Outer"/>.</summary>
    private static readonly TextSpan IncludeSpan = new(6, 11);

    /// <summary>The span of the whole <c>&lt;Colors&gt;</c> element in <see cref="Inner"/>.</summary>
    private static readonly TextSpan ColorsSpan = new(0, 28);

    private static TextProjection Project()
    {
        var builder = new TextProjectionBuilder(SourceText.From(Outer), OuterUri);

        builder.Replace(IncludeSpan, TextProjection.Identity(SourceText.From(Inner), InnerUri), ColorsSpan);

        return builder.ToProjection();
    }

    [Fact]
    public void Identity_LeavesEveryPositionWhereItWas()
    {
        TextProjection projection = TextProjection.Identity(SourceText.From(Outer), OuterUri);

        Assert.True(projection.IsIdentity);
        Assert.Same(projection.Source, projection.Text);

        for (int offset = 0; offset <= Outer.Length; offset++)
        {
            TextProjectionPosition position = projection.Map(offset);

            Assert.Equal(offset, position.Offset);
            Assert.Equal(OuterUri, position.SourceUri);
            Assert.True(position.IsOriginal);
        }
    }

    [Fact]
    public void ToProjection_SplicesTheReplacementInPlace()
    {
        TextProjection projection = Project();

        Assert.False(projection.IsIdentity);
        Assert.Equal("<A>\n  <Colors>\n  <Red />\n</Colors>\n  <B />\n</A>\n", projection.Text.ToString());
    }

    [Fact]
    public void ToProjection_LeavesTheSourceAlone()
    {
        TextProjection projection = Project();

        Assert.Equal(Outer, projection.Source.ToString());
    }

    [Fact]
    public void Map_AttributesEachRunToTheDocumentItCameFrom()
    {
        TextProjection projection = Project();
        string text = projection.Text.ToString();

        // The '<' of <A>, which is the outer document's own first character.
        TextProjectionPosition start = projection.Map(0);

        Assert.True(start.IsOriginal);
        Assert.Equal(OuterUri, start.SourceUri);
        Assert.Equal(0, start.Offset);

        // The 'R' of <Red />, which only exists in the included file.
        int red = text.IndexOf("<Red", StringComparison.Ordinal);
        TextProjectionPosition inner = projection.Map(red);

        Assert.False(inner.IsOriginal);
        Assert.Equal(InnerUri, inner.SourceUri);
        Assert.Equal(Inner.IndexOf("<Red", StringComparison.Ordinal), inner.Offset);

        // <B />, which the splice pushed further down the projected text but which has not
        // moved a character in the document it is actually written in.
        int b = text.IndexOf("<B", StringComparison.Ordinal);
        TextProjectionPosition after = projection.Map(b);

        Assert.True(after.IsOriginal);
        Assert.Equal(OuterUri, after.SourceUri);
        Assert.Equal(Outer.IndexOf("<B", StringComparison.Ordinal), after.Offset);
    }

    [Fact]
    public void Map_TakesALineAndColumnOfTheProjectedText()
    {
        TextProjection projection = Project();

        // Line 2 of the projection is "  <Red />", which is line 1 of the included file.
        TextProjectionPosition position = projection.Map(new TextPosition(2, 2));

        Assert.False(position.IsOriginal);
        Assert.Equal(InnerUri, position.SourceUri);
        Assert.Equal(Inner.IndexOf("<Red", StringComparison.Ordinal), position.Offset);
    }

    [Fact]
    public void Map_ClampsPositionsBeyondTheProjectedText()
    {
        TextProjection projection = Project();

        Assert.Equal(projection.Map(projection.Text.Length), projection.Map(int.MaxValue));
        Assert.Equal(projection.Map(0), projection.Map(-5));
        Assert.Equal(projection.Map(new TextPosition(400, 0)), projection.Map(new TextPosition(400, 400)));
    }

    [Fact]
    public void Map_CoversEveryCharacterOfTheProjectedText()
    {
        TextProjection projection = Project();
        string text = projection.Text.ToString();

        for (int offset = 0; offset < text.Length; offset++)
        {
            TextProjectionPosition position = projection.Map(offset);
            string source = position.IsOriginal ? Outer : Inner;

            Assert.Equal(text[offset], source[position.Offset]);
        }
    }

    [Fact]
    public void Map_ReachesThroughANestedProjection()
    {
        const string Deep = "<Palette>\n  <Blue />\n</Palette>\n";
        const string Middle = "<Colors>\n  <Include />\n</Colors>\n";

        var inner = new TextProjectionBuilder(SourceText.From(Middle), InnerUri);

        inner.Replace(
            new TextSpan(11, 11),
            TextProjection.Identity(SourceText.From(Deep), DeepUri),
            new TextSpan(0, 31));

        var outer = new TextProjectionBuilder(SourceText.From(Outer), OuterUri);
        TextProjection middle = inner.ToProjection();

        outer.Replace(IncludeSpan, middle, middle.GetProjectedSpan(new TextSpan(0, Middle.Length - 1)));

        TextProjection projection = outer.ToProjection();
        string text = projection.Text.ToString();

        Assert.Contains("<Blue />", text, StringComparison.Ordinal);

        // Three documents, one flat map. Text that reached the outer projection by way of the
        // middle one is still attributed to the file it is really written in.
        TextProjectionPosition blue = projection.Map(text.IndexOf("<Blue", StringComparison.Ordinal));

        Assert.False(blue.IsOriginal);
        Assert.Equal(DeepUri, blue.SourceUri);
        Assert.Equal(Deep.IndexOf("<Blue", StringComparison.Ordinal), blue.Offset);

        TextProjectionPosition colors = projection.Map(text.IndexOf("<Colors", StringComparison.Ordinal));

        Assert.Equal(InnerUri, colors.SourceUri);
        Assert.Equal(0, colors.Offset);
    }

    [Fact]
    public void ToProjection_HandlesSeveralReplacementsOutOfOrder()
    {
        const string Source = "<A><P /><Q /></A>";

        var builder = new TextProjectionBuilder(SourceText.From(Source), OuterUri);

        builder.Replace(
            new TextSpan(8, 5), TextProjection.Identity(SourceText.From("<qq />"), DeepUri), new TextSpan(0, 6));
        builder.Replace(
            new TextSpan(3, 5), TextProjection.Identity(SourceText.From("<pp />"), InnerUri), new TextSpan(0, 6));

        TextProjection projection = builder.ToProjection();
        string text = projection.Text.ToString();

        Assert.Equal("<A><pp /><qq /></A>", text);
        Assert.Equal(InnerUri, projection.Map(text.IndexOf("<pp", StringComparison.Ordinal)).SourceUri);
        Assert.Equal(DeepUri, projection.Map(text.IndexOf("<qq", StringComparison.Ordinal)).SourceUri);
        Assert.True(projection.Map(text.IndexOf("</A>", StringComparison.Ordinal)).IsOriginal);
    }

    [Fact]
    public void GetProjectedSpan_CoversWhatWasSplicedIntoIt()
    {
        TextProjection projection = Project();

        // The whole of <A>...</A> in the source, which in the projection has to run from the
        // same start to an end pushed along by everything the include brought with it.
        TextSpan projected = projection.GetProjectedSpan(new TextSpan(0, Outer.Length - 1));

        Assert.Equal(0, projected.Start);
        Assert.Equal(projection.Text.Length - 1, projected.End);
        Assert.Equal("</A>", projection.Text.GetText(new TextSpan(projected.End - 4, 4)));
    }

    [Fact]
    public void GetProjectedOffset_PutsAReplacedRangeAtItsReplacement()
    {
        TextProjection projection = Project();

        // Nothing in the projection is the include element, so every offset inside it answers
        // with where the content that stands for it begins.
        Assert.Equal(IncludeSpan.Start, projection.GetProjectedOffset(IncludeSpan.Start));
        Assert.Equal(IncludeSpan.Start, projection.GetProjectedOffset(IncludeSpan.Start + 4));
        Assert.Equal(IncludeSpan.Start + ColorsSpan.Length, projection.GetProjectedOffset(IncludeSpan.End));
    }

    [Fact]
    public void Replace_WritesTextOfItsOwnAndMapsItToWhereItWentIn()
    {
        var builder = new TextProjectionBuilder(SourceText.From(Outer), OuterUri);

        builder.Replace(new TextSpan(2, 0), " k=\"v\"");

        TextProjection projection = builder.ToProjection();

        Assert.Equal("<A k=\"v\">\n  <Include />\n  <B />\n</A>\n", projection.Text.ToString());

        TextProjectionSegment written = Assert.Single(
            projection.Segments, static segment => segment.IsSynthesized);

        Assert.True(written.IsOriginal);
        Assert.Equal(OuterUri, written.SourceUri);

        // Text nobody wrote in a file has no character of one to point at, so every position
        // in it answers with the point it was put at rather than an offset it does not have.
        Assert.Equal(2, projection.Map(3).Offset);
        Assert.Equal(2, projection.Map(7).Offset);
        Assert.True(projection.Map(5).IsOriginal);

        // Everything after it still maps to where it really is.
        Assert.Equal(2, projection.Map(8).Offset);
        Assert.Equal(3, projection.Map(9).Offset);
    }

    [Fact]
    public void Replace_DeletesWithEmptyText()
    {
        var builder = new TextProjectionBuilder(SourceText.From(Outer), OuterUri);

        builder.Replace(IncludeSpan, string.Empty);

        TextProjection projection = builder.ToProjection();

        string text = projection.Text.ToString();

        Assert.Equal("<A>\n  \n  <B />\n</A>\n", text);
        Assert.DoesNotContain(projection.Segments, static segment => segment.IsSynthesized);
        Assert.Equal(
            Outer.IndexOf("<B", StringComparison.Ordinal),
            projection.Map(text.IndexOf("<B", StringComparison.Ordinal)).Offset);
    }

    [Fact]
    public void Replace_CarriesANestedProjectionsWrittenTextAcrossAsWritten()
    {
        var inner = new TextProjectionBuilder(SourceText.From(Inner), InnerUri);

        inner.Replace(new TextSpan(7, 0), " a=\"b\"");

        TextProjection middle = inner.ToProjection();
        var outer = new TextProjectionBuilder(SourceText.From(Outer), OuterUri);

        outer.Replace(IncludeSpan, middle, middle.GetProjectedSpan(ColorsSpan));

        TextProjection projection = outer.ToProjection();
        string text = projection.Text.ToString();
        int written = text.IndexOf("a=\"b\"", StringComparison.Ordinal);

        // Nesting must not turn a run that stood for a point into one claiming a stretch of the
        // included file it never covered.
        Assert.All(
            new[] { written, written + 1, written + 4 },
            offset => Assert.Equal(7, projection.Map(offset).Offset));

        Assert.Equal(InnerUri, projection.Map(written).SourceUri);
        Assert.False(projection.Map(written).IsOriginal);
    }

    [Fact]
    public void Replace_RejectsOverlappingReplacements()
    {
        var builder = new TextProjectionBuilder(SourceText.From(Outer), OuterUri);
        TextProjection inner = TextProjection.Identity(SourceText.From(Inner), InnerUri);

        builder.Replace(IncludeSpan, inner, ColorsSpan);

        Assert.Throws<ArgumentException>(
            () => builder.Replace(new TextSpan(IncludeSpan.Start + 2, 4), inner, ColorsSpan));
    }

    [Fact]
    public void Replace_RejectsAReplacementEitherWayRoundAnEmptyOne()
    {
        var builder = new TextProjectionBuilder(SourceText.From(Outer), OuterUri);

        builder.Replace(new TextSpan(IncludeSpan.Start + 2, 0), "XX");

        // An empty span overlaps nothing by the usual definition, so a replacement swallowing an
        // insertion point has to be caught deliberately. Left through, the build walks the
        // splices in an order it cannot make sense of.
        Assert.Throws<ArgumentException>(() => builder.Replace(IncludeSpan, "YY"));
    }

    [Fact]
    public void Replace_RejectsASpanBeyondTheTextItIndexes()
    {
        var builder = new TextProjectionBuilder(SourceText.From(Outer), OuterUri);
        TextProjection inner = TextProjection.Identity(SourceText.From(Inner), InnerUri);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.Replace(new TextSpan(Outer.Length, 4), inner, ColorsSpan));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.Replace(IncludeSpan, inner, new TextSpan(0, Inner.Length + 1)));
    }

    [Fact]
    public void ToProjection_IsAnIdentityProjectionWhenNothingWasSpliced()
    {
        var builder = new TextProjectionBuilder(SourceText.From(Outer), OuterUri);

        Assert.True(builder.IsEmpty);
        Assert.True(builder.ToProjection().IsIdentity);
    }
}
