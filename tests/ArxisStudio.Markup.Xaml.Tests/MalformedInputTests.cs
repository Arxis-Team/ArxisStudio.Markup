using System;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// Malformed input is the normal case in an editor, not an error case. It must produce a tree,
/// diagnostics with stable codes, and text that still writes back unchanged.
/// </summary>
public sealed class MalformedInputTests
{
    private static string[] CodesOf(string source) =>
        [.. XamlDocument.Parse(source).Diagnostics.Select(static diagnostic => diagnostic.Code)];

    [Fact]
    public void ParsingNeverThrowsForBrokenInput()
    {
        string[] broken =
        [
            "<", "<>", "</>", "<a", "<a b", "<a b=", "<a b=\"", "<!--", "<![CDATA[", "<?",
            "<a></b>", "</a>", "<a><b></a></b>", "&", "&#;", "<a b='c\"/>", "<a::b/>", "<:a/>",
        ];

        foreach (string source in broken)
        {
            XamlDocument document = XamlDocument.Parse(source);

            Assert.Equal(source, document.GetText());
        }
    }

    [Fact]
    public void AnUnclosedElementIsReportedAndStillHasASpan()
    {
        XamlDocument document = XamlDocument.Parse("<a><b></a>");

        Assert.Contains(XamlDiagnosticCodes.UnclosedElement, CodesOf("<a><b></a>"));

        XamlElement inner = document.DescendantElements().Single(static e => e.Name.LocalName == "b");

        Assert.True(inner.IsUnclosed);
        Assert.Null(inner.EndTagSpan);
    }

    [Fact]
    public void AnEndTagBelongingToAnAncestorIsLeftForIt()
    {
        // "</a>" must close 'a', not be swallowed by the unclosed 'b' inside it.
        XamlDocument document = XamlDocument.Parse("<a><b></a>");

        Assert.NotNull(document.Root!.EndTagSpan);
        Assert.Equal("</a>", document.SourceText.GetText(document.Root.EndTagSpan!.Value));
    }

    [Fact]
    public void AStrayEndTagIsReportedAndKept()
    {
        Assert.Contains(XamlDiagnosticCodes.UnexpectedEndTag, CodesOf("<a /></b>"));
        Assert.Equal("<a /></b>", XamlDocument.Parse("<a /></b>").GetText());
    }

    [Fact]
    public void AnAttributeWithNoValueIsReportedWithoutInventingOne()
    {
        // Writing a value the author never typed would put text in their file that they did
        // not write.
        XamlDocument document = XamlDocument.Parse("<a bare Width=\"1\" />");
        XamlAttribute bare = document.Root!.Attributes[0];

        Assert.Contains(XamlDiagnosticCodes.MissingAttributeValue, CodesOf("<a bare Width=\"1\" />"));
        Assert.False(bare.HasValue);
        Assert.Null(bare.Quote);
        Assert.Equal(string.Empty, bare.GetValueText());

        // Parsing continues: the well-formed attribute after it is still found.
        Assert.Equal("1", document.Root.GetAttribute("Width")!.GetValueText());
    }

    [Fact]
    public void AnUnquotedAttributeValueIsReported()
    {
        Assert.Contains(XamlDiagnosticCodes.MissingAttributeValue, CodesOf("<a b=c />"));
    }

    [Fact]
    public void ARepeatedAttributeIsReportedAndBothAreKept()
    {
        XamlDocument document = XamlDocument.Parse("<a b=\"1\" b=\"2\" />");

        Assert.Contains(XamlDiagnosticCodes.DuplicateAttribute, CodesOf("<a b=\"1\" b=\"2\" />"));
        Assert.Equal(2, document.Root!.Attributes.Length);
        Assert.Equal("<a b=\"1\" b=\"2\" />", document.GetText());
    }

    [Fact]
    public void DiagnosticsCarryStableCodesAndSourceSpans()
    {
        var uri = new Uri("file:///Views/Broken.axaml");
        XamlDocument document = XamlDocument.Parse(
            "<a><b></a>", new XamlParseOptions { DocumentUri = uri });

        MarkupDiagnostic diagnostic = document.Diagnostics.First(
            static d => d.Code == XamlDiagnosticCodes.UnclosedElement);

        Assert.Equal(MarkupDiagnosticCategory.Parse, diagnostic.Category);
        Assert.Equal(MarkupDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(uri, diagnostic.DocumentUri);
        Assert.NotNull(diagnostic.Span);
        Assert.True(diagnostic.IsError);
    }

    [Fact]
    public void DiagnosticSpansStayInsideTheDocument()
    {
        foreach (string name in Fixtures.Names)
        {
            XamlDocument document = Fixtures.Parse(name);

            foreach (MarkupDiagnostic diagnostic in document.Diagnostics)
            {
                if (diagnostic.Span is { } span)
                {
                    Assert.True(
                        span.End <= document.SourceText.Length,
                        $"{name}: {diagnostic.Code} points past the end of the document.");
                }
            }
        }
    }

    [Fact]
    public void AWellFormedDocumentHasNoDiagnostics()
    {
        XamlDocument document = Fixtures.Parse("Simple.axaml");

        Assert.True(document.IsWellFormed);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("MalformedUnclosed.axaml")]
    [InlineData("MalformedStray.axaml")]
    [InlineData("MalformedUnterminated.axaml")]
    public void MalformedFixturesAreReportedYetStillRoundTrip(string name)
    {
        XamlDocument document = Fixtures.Parse(name);

        Assert.False(document.IsWellFormed);
        Assert.Equal(Fixtures.Read(name).ToString(), document.GetText());
    }
}
