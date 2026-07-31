using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

public sealed class XamlParserTests
{
    [Fact]
    public void TheRootElementIsFound()
    {
        XamlDocument document = XamlDocument.Parse("<!-- lead -->\n<Grid />\n");

        Assert.NotNull(document.Root);
        Assert.Equal("Grid", document.Root.Name.LocalName);
        Assert.True(document.IsWellFormed);
    }

    [Fact]
    public void ElementSpansCoverTheirWholeTagPair()
    {
        XamlDocument document = XamlDocument.Parse("<a>\n  <b />\n</a>");

        Assert.Equal("<a>\n  <b />\n</a>", document.Root!.GetSourceText());
        Assert.Equal("<a>", document.SourceText.GetText(document.Root.StartTagSpan));
        Assert.Equal("</a>", document.SourceText.GetText(document.Root.EndTagSpan!.Value));
    }

    [Fact]
    public void SelfClosingElementsHaveNoEndTag()
    {
        XamlElement element = XamlDocument.Parse("<a />").Root!;

        Assert.True(element.IsEmpty);
        Assert.Null(element.EndTagSpan);
        Assert.False(element.IsUnclosed);
    }

    [Fact]
    public void AttributesKeepTheirNameValueAndQuoteCharacter()
    {
        XamlElement element = XamlDocument.Parse("<a x:Key='k' Width=\"320\" />").Root!;

        Assert.Equal(2, element.Attributes.Length);

        XamlAttribute key = element.Attributes[0];
        Assert.Equal("x", key.Name.Prefix);
        Assert.Equal("Key", key.Name.LocalName);
        Assert.Equal("k", key.GetValueText());
        Assert.Equal('\'', key.Quote);

        XamlAttribute width = element.Attributes[1];
        Assert.Equal("320", width.GetValueText());
        Assert.Equal('"', width.Quote);
    }

    [Fact]
    public void AttributeValuesAreReturnedRawWithEntitiesUnexpanded()
    {
        XamlElement element = XamlDocument.Parse("<a b=\"x &amp; y &#65;\" />").Root!;

        Assert.Equal("x &amp; y &#65;", element.Attributes[0].GetValueText());
    }

    [Fact]
    public void MarkupExtensionsAreJustAttributeTextAtThisStage()
    {
        // This package does not parse them and certainly does not execute them. It records
        // what was written.
        XamlElement element = XamlDocument.Parse(
            "<a Text=\"{Binding Value, Converter={StaticResource C}}\" />").Root!;

        Assert.Equal("{Binding Value, Converter={StaticResource C}}", element.Attributes[0].GetValueText());
    }

    [Fact]
    public void PropertyElementSyntaxParsesAsAnOrdinaryElement()
    {
        // "Grid.RowDefinitions" is a name with a dot in it, nothing more. Classifying it as a
        // member belongs to the loader.
        XamlElement root = XamlDocument.Parse(
            "<Grid><Grid.RowDefinitions><RowDefinition /></Grid.RowDefinitions></Grid>").Root!;

        XamlElement property = root.Elements.Single();

        Assert.Equal("Grid.RowDefinitions", property.Name.LocalName);
        Assert.Null(property.Name.Prefix);
        Assert.Single(property.Elements);
    }

    [Fact]
    public void ContentNodesKeepTheirOrder()
    {
        XamlElement root = XamlDocument.Parse("<a>text<!--c--><b /><![CDATA[d]]></a>").Root!;

        Assert.Equal(
            [nameof(XamlText), nameof(XamlComment), nameof(XamlElement), nameof(XamlCData)],
            root.Content.Select(static node => node.GetType().Name));
    }

    [Fact]
    public void CommentsAndCDataExposeTheirContentWithoutDelimiters()
    {
        XamlElement root = XamlDocument.Parse("<a><!-- note --><![CDATA[<raw>]]></a>").Root!;

        Assert.Equal(" note ", root.Content.OfType<XamlComment>().Single().GetContent());
        Assert.Equal("<raw>", root.Content.OfType<XamlCData>().Single().GetContent());
    }

    [Fact]
    public void WhitespaceBetweenElementsBecomesTrivia()
    {
        XamlElement root = XamlDocument.Parse("<a>\n  <b />\n</a>").Root!;

        Assert.Equal(
            [XamlTriviaKind.NewLine, XamlTriviaKind.Whitespace, XamlTriviaKind.NewLine],
            root.Content.OfType<XamlTrivia>().Select(static trivia => trivia.Kind));
    }

    [Fact]
    public void PrologConstructsAreKeptAsDocumentChildren()
    {
        XamlDocument document = XamlDocument.Parse(
            "<?xml version=\"1.0\"?>\n<!DOCTYPE a>\n<?pi?>\n<a />");

        XamlProcessingInstruction[] prolog = [.. document.Children.OfType<XamlProcessingInstruction>()];

        Assert.Equal(
            [
                XamlProcessingInstructionKind.XmlDeclaration,
                XamlProcessingInstructionKind.DocumentType,
                XamlProcessingInstructionKind.ProcessingInstruction,
            ],
            prolog.Select(static node => node.Kind));

        Assert.True(prolog[0].IsXmlDeclaration);
    }

    [Fact]
    public void ParentAndDocumentLinksAreWiredUp()
    {
        XamlDocument document = XamlDocument.Parse("<a><b><c /></b></a>");
        XamlElement innermost = document.DescendantElements().Single(static e => e.Name.LocalName == "c");

        Assert.Same(document, innermost.Document);
        Assert.Equal(["c", "b", "a"], innermost.AncestorsAndSelf().OfType<XamlElement>().Select(static e => e.Name.LocalName));
        Assert.Null(document.Parent);
    }

    [Fact]
    public void FindNodeReturnsTheInnermostNodeAtAnOffset()
    {
        const string Source = "<a><b Width=\"320\" /></a>";
        XamlDocument document = XamlDocument.Parse(Source);

        XamlSyntaxNode? found = document.FindNode(Source.IndexOf("320", System.StringComparison.Ordinal));

        Assert.IsType<XamlAttribute>(found);
        Assert.Equal("Width", ((XamlAttribute)found).Name.LocalName);
    }

    [Fact]
    public void GetAttributeFindsByWrittenName()
    {
        XamlElement element = XamlDocument.Parse("<a x:Name=\"n\" Width=\"1\" />").Root!;

        Assert.NotNull(element.GetAttribute("Width"));
        Assert.NotNull(element.GetAttribute(new XamlQualifiedName("x", "Name")));

        // An unprefixed lookup must not match a prefixed attribute.
        Assert.Null(element.GetAttribute("Name"));
    }

    [Fact]
    public void ADocumentWithNoRootIsStillParseable()
    {
        XamlDocument document = XamlDocument.Parse("<!-- only a comment -->");

        Assert.Null(document.Root);
        Assert.Contains(
            document.Diagnostics,
            static diagnostic => diagnostic.Code == XamlDiagnosticCodes.MissingRootElement);
    }

    [Fact]
    public void ExtraRootElementsAreReportedButKept()
    {
        XamlDocument document = XamlDocument.Parse("<a /><b /><c />");

        Assert.Equal("a", document.Root!.Name.LocalName);
        Assert.Equal(3, document.Children.OfType<XamlElement>().Count());
        Assert.Equal(
            2,
            document.Diagnostics.Count(static d => d.Code == XamlDiagnosticCodes.MultipleRootElements));
    }
}
