using System;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

public sealed class DirectiveAndFormatTests
{
    private const string Xaml =
        "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
        "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
        "             xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\"\n" +
        "             xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"\n" +
        "             x:Class=\"MyApplication.Views.CustomerView\"\n" +
        "             mc:Ignorable=\"d\"\n" +
        "             d:DesignWidth=\"800\">\n" +
        "  <TextBlock x:Name=\"Title\" Text=\"{Binding Name}\" d:Text=\"Design value\" Grid.Row=\"1\" />\n" +
        "</UserControl>\n";

    private static XamlDocument Document => XamlDocument.Parse(Xaml);

    private static XamlElement TextBlock => Document.DescendantElements().First(static e => e.Name.LocalName == "TextBlock");

    [Fact]
    public void DirectivesAreRecognisedByNamespace()
    {
        XamlElement root = Document.Root!;

        Assert.Equal("MyApplication.Views.CustomerView", root.GetDirective(XamlDirectives.Class));
        Assert.Equal("Title", TextBlock.GetDirective(XamlDirectives.Name));
        Assert.Null(root.GetDirective(XamlDirectives.Key));
    }

    [Fact]
    public void ADirectiveIsFoundWhateverPrefixTheDocumentUses()
    {
        // Nothing obliges a document to spell the XAML namespace "x".
        XamlDocument document = XamlDocument.Parse(
            $"<a xmlns:whatever=\"{XamlNamespaces.Xaml}\" whatever:Name=\"n\" />");

        Assert.Equal("n", document.Root!.GetDirective(XamlDirectives.Name));
    }

    [Fact]
    public void NamespaceDeclarationsAreNotDirectives()
    {
        Assert.DoesNotContain(Document.Root!.Directives, static a => a is XamlNamespaceDeclaration);
    }

    [Fact]
    public void AnUnknownDirectiveIsStillADirective()
    {
        XamlDocument document = XamlDocument.Parse(
            $"<a xmlns:x=\"{XamlNamespaces.Xaml}\" x:NotInventedYet=\"value\" />");

        Assert.Equal("value", document.Root!.GetDirective("NotInventedYet"));
        Assert.Single(document.Root.Directives);
    }

    [Fact]
    public void DesignTimeAndMarkupCompatibilityAttributesAreRecognised()
    {
        XamlElement root = Document.Root!;

        Assert.Equal("800", root.GetDesignTimeAttribute("DesignWidth"));
        Assert.Equal("Design value", TextBlock.GetDesignTimeAttribute("Text"));
        Assert.True(root.GetAttribute(new XamlQualifiedName("mc", "Ignorable"))!.IsMarkupCompatibility);
    }

    [Fact]
    public void ADesignTimeShadowAttributeSitsAlongsideTheRealOne()
    {
        // Applying either belongs to the loader; this package only has to keep both.
        Assert.Equal("{Binding Name}", TextBlock.GetAttribute("Text")!.GetValueText());
        Assert.Equal("Design value", TextBlock.GetDesignTimeAttribute("Text"));
    }

    [Fact]
    public void TheOwnerMemberShapeIsIdentifiedWithoutClassifyingTheMember()
    {
        XamlQualifiedName name = TextBlock.GetAttribute("Grid.Row")!.Name;

        Assert.True(name.IsDotted);
        Assert.Equal("Grid", name.OwnerName);
        Assert.Equal("Row", name.MemberName);
    }

    [Fact]
    public void APropertyElementIsIdentifiedByShapeAlone()
    {
        XamlDocument document = XamlDocument.Parse(
            "<Grid><Grid.RowDefinitions><RowDefinition /></Grid.RowDefinitions></Grid>");

        XamlElement property = document.Root!.Elements.Single();

        Assert.True(property.IsPropertyElementSyntax);
        Assert.Equal("Grid", property.OwnerName);
        Assert.Equal("RowDefinitions", property.MemberName);

        // An ordinary element is not one, however deeply nested.
        Assert.False(property.Elements.Single().IsPropertyElementSyntax);
    }

    [Fact]
    public void PreserveIsTheDefaultWriteMode()
    {
        Assert.Equal(Xaml, Document.GetText());
        Assert.Equal(Xaml, Document.GetText(XamlWriteMode.Preserve));
    }

    [Fact]
    public void FormatModeReflowsTheDocument()
    {
        var options = new XamlFormattingOptions { Indentation = "  ", NewLine = "\n" };

        string formatted = XamlDocument
            .Parse("<Grid>\n\t\t<Button   Width=\"320\" />\n</Grid>")
            .GetText(XamlWriteMode.Format, options);

        Assert.Equal("<Grid>\n  <Button Width=\"320\" />\n</Grid>\n", formatted);
    }

    [Fact]
    public void FormatModeNeverRunsUnlessItIsAskedForByName()
    {
        // The contract is explicit: formatting must never be implicitly enabled, and a save
        // must never need it.
        string source = Fixtures.Read("Whitespace.axaml").ToString();
        XamlDocument document = XamlDocument.Parse(source);

        Assert.Equal(source, document.GetText());
        Assert.NotEqual(source, document.GetText(XamlWriteMode.Format));
    }

    [Fact]
    public void FormatModeKeepsCommentsAndValues()
    {
        string formatted = XamlDocument
            .Parse("<Grid>\n<!-- keep -->\n<Button Text=\"{Binding Name}\" />\n</Grid>")
            .GetText(XamlWriteMode.Format, new XamlFormattingOptions { NewLine = "\n" });

        Assert.Contains("<!-- keep -->", formatted, StringComparison.Ordinal);
        Assert.Contains("{Binding Name}", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatModeCanPutAttributesOnSeparateLines()
    {
        string formatted = XamlDocument
            .Parse("<Grid a=\"1\" b=\"2\" />")
            .GetText(
                XamlWriteMode.Format,
                new XamlFormattingOptions { NewLine = "\n", Indentation = "  ", PutAttributesOnSeparateLines = true });

        Assert.Equal("<Grid\n  a=\"1\"\n  b=\"2\" />\n", formatted);
    }

    [Fact]
    public void FormatModeHonoursTheRequestedQuoteCharacter()
    {
        string formatted = XamlDocument
            .Parse("<Grid a=\"1\" />")
            .GetText(
                XamlWriteMode.Format,
                new XamlFormattingOptions { NewLine = "\n", AttributeQuote = '\'' });

        Assert.Contains("a='1'", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void AFormattedDocumentStillParsesToTheSameShape()
    {
        foreach (string name in Fixtures.Names.Where(static n => !n.StartsWith("Malformed", StringComparison.Ordinal)))
        {
            XamlDocument original = Fixtures.Parse(name);
            string formatted = original.GetText(XamlWriteMode.Format, new XamlFormattingOptions { NewLine = "\n" });
            XamlDocument reparsed = XamlDocument.Parse(formatted);

            Assert.Equal(
                original.DescendantElements().Select(static e => e.Name.ToString()),
                reparsed.DescendantElements().Select(static e => e.Name.ToString()));
        }
    }
}
