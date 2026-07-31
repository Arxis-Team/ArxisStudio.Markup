using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// The exit criterion of this milestone: an unchanged document comes back byte for byte.
/// </summary>
public sealed class RoundTripTests
{
    public static TheoryData<string> AllFixtures
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (string name in Fixtures.Names)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void EveryFixtureRoundTripsExactly(string name)
    {
        SourceText source = Fixtures.Read(name);
        XamlDocument document = XamlDocument.Parse(source);

        Assert.Equal(source.ToString(), document.GetText());
    }

    [Fact]
    public void ThereAreEnoughFixturesToBeWorthTrusting()
    {
        // A round-trip suite that silently stopped finding its fixtures would pass forever.
        Assert.True(Fixtures.Names.Count() >= 15, "The golden fixtures are missing.");
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void TheTokenStreamAccountsForEveryCharacter(string name)
    {
        // Losslessness rests on this: the tokens tile the document with no gaps and no
        // overlaps, so nothing can be silently dropped between them.
        SourceText source = Fixtures.Read(name);
        XamlDocument document = XamlDocument.Parse(source);
        var position = 0;

        foreach (XamlToken token in document.Tokens)
        {
            Assert.Equal(position, token.Span.Start);
            position = token.Span.End;
        }

        Assert.Equal(source.Length, position);
        Assert.Equal(XamlTokenKind.EndOfFile, document.Tokens[^1].Kind);
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void EveryNodesChildrenLieWithinItInOrderAndDoNotOverlap(string name)
    {
        XamlDocument document = Fixtures.Parse(name);

        Check(document);

        static void Check(XamlSyntaxNode node)
        {
            int previousEnd = node.Span.Start;

            foreach (XamlSyntaxNode child in node.Children)
            {
                Assert.True(
                    child.Span.Start >= previousEnd,
                    $"{child} starts before the end of its previous sibling in {node}.");

                Assert.True(
                    child.Span.End <= node.Span.End,
                    $"{child} extends beyond its parent {node}.");

                previousEnd = child.Span.End;

                Check(child);
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void EveryNodeWritesBackItsOwnSourceText(string name)
    {
        XamlDocument document = Fixtures.Parse(name);

        foreach (XamlSyntaxNode node in document.Descendants())
        {
            Assert.Equal(node.GetSourceText(), node.GetText());
        }
    }

    [Fact]
    public void AByteOrderMarkIsRecordedRatherThanSwallowed()
    {
        SourceText withMark = Fixtures.Read("Utf8Bom.axaml");
        SourceText without = Fixtures.Read("Simple.axaml");

        Assert.True(withMark.HasByteOrderMark);
        Assert.False(without.HasByteOrderMark);

        // The mark is metadata about the bytes, not a character of the document.
        Assert.Equal(without.ToString(), withMark.ToString());
        Assert.Equal(withMark.ToString(), XamlDocument.Parse(withMark).GetText());
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    public void LineEndingsAreNeverRewritten(string separator)
    {
        string source = $"<Grid>{separator}  <Button />{separator}</Grid>{separator}";

        Assert.Equal(source, XamlDocument.Parse(source).GetText());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    [InlineData("<")]
    [InlineData("<>")]
    [InlineData("&")]
    [InlineData("</>")]
    [InlineData("<!--")]
    [InlineData("<![CDATA[")]
    [InlineData("<?")]
    [InlineData("<a")]
    [InlineData("<a b")]
    [InlineData("<a b=")]
    [InlineData("<a b=\"")]
    [InlineData("<a></b>")]
    [InlineData("just text")]
    public void EvenDegenerateInputRoundTrips(string source)
    {
        // Nothing here is valid XAML. All of it is text somebody may be halfway through
        // typing, and none of it may be lost.
        Assert.Equal(source, XamlDocument.Parse(source).GetText());
    }
}
