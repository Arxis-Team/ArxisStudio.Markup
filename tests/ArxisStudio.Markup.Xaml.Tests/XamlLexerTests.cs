using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

public sealed class XamlLexerTests
{
    private static ImmutableArray<XamlToken> Lex(string source) => XamlDocument.Parse(source).Tokens;

    private static string[] KindsOf(string source) =>
        [.. Lex(source).Select(static token => token.Kind.ToString())];

    [Fact]
    public void ASimpleTagProducesOneTokenPerPieceOfSyntax()
    {
        Assert.Equal(
            ["LessThan", "Name", "Whitespace", "Name", "Equals", "Quote", "AttributeValueText", "Quote", "Whitespace", "SlashGreaterThan", "EndOfFile"],
            KindsOf("<Button Width=\"320\" />"));
    }

    [Fact]
    public void APrefixAndLocalNameAreSeparateTokens()
    {
        // The prefix has to be addressable on its own: it is what namespace resolution and
        // prefix-preserving edits work with.
        Assert.Equal(
            ["LessThan", "Name", "Colon", "Name", "SlashGreaterThan", "EndOfFile"],
            KindsOf("<x:Button/>"));
    }

    [Theory]
    [InlineData("<!-- hi -->", XamlTokenKind.Comment)]
    [InlineData("<![CDATA[hi]]>", XamlTokenKind.CData)]
    [InlineData("<?pi hi?>", XamlTokenKind.ProcessingInstruction)]
    [InlineData("<?xml version=\"1.0\"?>", XamlTokenKind.XmlDeclaration)]
    [InlineData("<!DOCTYPE a>", XamlTokenKind.DocumentType)]
    public void PrologAndAsideConstructsAreSingleTokens(string source, XamlTokenKind expected)
    {
        XamlToken token = Lex(source)[0];

        Assert.Equal(expected, token.Kind);
        Assert.Equal(new TextSpan(0, source.Length), token.Span);
    }

    [Fact]
    public void AProcessingInstructionIsNotMistakenForTheDeclaration()
    {
        // "<?xml-stylesheet" starts with "<?xml" but is an ordinary instruction.
        Assert.Equal(XamlTokenKind.ProcessingInstruction, Lex("<?xml-stylesheet href=\"a\"?>")[0].Kind);
    }

    [Fact]
    public void WhitespaceAndNewLinesAreDistinctAndKeptSeparate()
    {
        Assert.Equal(
            ["Whitespace", "NewLine", "Whitespace", "EndOfFile"],
            KindsOf("  \n\t"));
    }

    [Fact]
    public void CarriageReturnLineFeedIsOneNewLine()
    {
        ImmutableArray<XamlToken> tokens = Lex("\r\n");

        Assert.Equal(XamlTokenKind.NewLine, tokens[0].Kind);
        Assert.Equal(2, tokens[0].Span.Length);
    }

    [Fact]
    public void EntityReferencesAreRecognisedButNotExpanded()
    {
        ImmutableArray<XamlToken> tokens = Lex("a&amp;b&#65;c&#x42;d");

        Assert.Equal(
            ["Text", "EntityReference", "Text", "EntityReference", "Text", "EntityReference", "Text", "EndOfFile"],
            tokens.Select(static token => token.Kind.ToString()));
    }

    [Fact]
    public void EntityReferencesInsideAttributeValuesAreAlsoRecognised()
    {
        Assert.Equal(
            ["LessThan", "Name", "Whitespace", "Name", "Equals", "Quote", "AttributeValueText", "EntityReference", "AttributeValueText", "Quote", "SlashGreaterThan", "EndOfFile"],
            KindsOf("<a b=\"x&amp;y\"/>"));
    }

    [Fact]
    public void ABareAmpersandIsSkippedRatherThanTreatedAsAReference()
    {
        ImmutableArray<XamlToken> tokens = Lex("a & b");

        Assert.Contains(tokens, static token => token.Kind == XamlTokenKind.Skipped);
        Assert.Contains(
            XamlDocument.Parse("a & b").Diagnostics,
            static diagnostic => diagnostic.Code == XamlDiagnosticCodes.InvalidEntityReference);
    }

    [Fact]
    public void AStrayAngleBracketInContentIsSkippedRatherThanStartingATag()
    {
        Assert.Contains(Lex("a < b"), static token => token.Kind == XamlTokenKind.Skipped);
    }

    [Fact]
    public void BothQuoteStylesDelimitAttributeValues()
    {
        Assert.Equal(KindsOf("<a b='x'/>"), KindsOf("<a b=\"x\"/>"));
    }

    [Fact]
    public void AQuoteOfTheOtherStyleInsideAValueIsJustText()
    {
        XamlDocument document = XamlDocument.Parse("<a b=\"it's fine\"/>");

        Assert.Equal("it's fine", document.Root!.Attributes[0].GetValueText());
    }

    [Fact]
    public void AnUnterminatedConstructTakesTheRestOfTheDocument()
    {
        // Ending the token at the end of the input keeps every character accounted for, which
        // is what lets even a broken document be written back unchanged.
        XamlDocument document = XamlDocument.Parse("<a><!-- never closed");
        XamlToken comment = document.Tokens.Single(static token => token.Kind == XamlTokenKind.Comment);

        Assert.Equal(20, comment.Span.End);
        Assert.Contains(
            document.Diagnostics,
            static diagnostic => diagnostic.Code == XamlDiagnosticCodes.UnterminatedComment);
    }

    [Fact]
    public void AMissingClosingQuoteDoesNotSwallowTheRestOfTheDocument()
    {
        // Stopping at the next '<' bounds the damage to one attribute instead of consuming
        // every remaining element as attribute text.
        XamlDocument document = XamlDocument.Parse("<a b=\"unterminated <c /></a>");

        Assert.Contains(
            document.Diagnostics,
            static diagnostic => diagnostic.Code == XamlDiagnosticCodes.UnterminatedAttributeValue);
        Assert.Contains(document.Tokens, static token => token.Kind == XamlTokenKind.LessThan);
    }

    [Fact]
    public void DottedNamesLexAsASingleName()
    {
        // "Grid.Row" is one XML name. Splitting it into owner and member is a decision this
        // package has no metadata to make.
        Assert.Equal(
            ["LessThan", "Name", "Whitespace", "Name", "Equals", "Quote", "AttributeValueText", "Quote", "SlashGreaterThan", "EndOfFile"],
            KindsOf("<a Grid.Row=\"1\"/>"));
    }
}
