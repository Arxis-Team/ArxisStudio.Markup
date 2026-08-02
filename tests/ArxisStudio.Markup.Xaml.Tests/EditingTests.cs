using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// The exit criterion of this milestone: a one-property edit leaves every unrelated character
/// of the document exactly where it was.
/// </summary>
public sealed class EditingTests
{
    private const string Source =
        "<UserControl xmlns=\"https://github.com/avaloniaui\"\r\n" +
        "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n" +
        "\r\n" +
        "  <!-- keep this comment exactly where it is -->\r\n" +
        "  <StackPanel>\r\n" +
        "\t<Button x:Name='SaveButton'   Width = \"320\"  Content=\"Save\" />\r\n" +
        "    <TextBlock Text=\"{Binding Customer.Name}\" />\r\n" +
        "  </StackPanel>\r\n" +
        "</UserControl>\r\n";

    private static XamlDocument Parse(string text = Source) => XamlDocument.Parse(text);

    private static XamlElement Element(XamlDocument document, string localName) =>
        document.DescendantElements().First(element => element.Name.LocalName == localName);

    private static XamlElement Named(XamlDocument document, string name) =>
        document.DescendantElements().Single(element => element.GetDirective(XamlDirectives.Name) == name);

    [Fact]
    public void SettingOneAttributeChangesOnlyThatValue()
    {
        XamlDocument document = Parse();
        XamlElement button = Named(document, "SaveButton");

        string result = document
            .SetAttribute(button, XamlQualifiedName.Parse("Width"), new XamlLiteralValue("480"))
            .GetText();

        Assert.Equal(Source.Replace("\"320\"", "\"480\"", StringComparison.Ordinal), result);
    }

    [Fact]
    public void AnEditPreservesEveryUnrelatedDetailOfTheSource()
    {
        // Each of these is source somebody wrote deliberately, and none of them is what the
        // edit was about.
        XamlDocument document = Parse();
        string result = document
            .SetAttribute(Named(document, "SaveButton"), XamlQualifiedName.Parse("Width"), "480")
            .GetText();

        Assert.Contains("<!-- keep this comment exactly where it is -->", result, StringComparison.Ordinal);
        Assert.Contains("x:Name='SaveButton'", result, StringComparison.Ordinal);   // single quotes
        Assert.Contains("Width = \"480\"", result, StringComparison.Ordinal);        // spacing around '='
        Assert.Contains("\t<Button", result, StringComparison.Ordinal);              // tab indentation
        Assert.Contains("\r\n\r\n", result, StringComparison.Ordinal);               // blank line
        Assert.Contains("{Binding Customer.Name}", result, StringComparison.Ordinal); // binding untouched

        // Every character outside the value is where it was: the edit is the diff.
        Assert.Equal(Source.Length, result.Length);
        Assert.Equal(Source.Replace("320", "480", StringComparison.Ordinal), result);
    }

    [Fact]
    public void TheEditIsExpressedAsASingleMinimalTextChange()
    {
        XamlDocument document = Parse();

        ImmutableArray<TextChange> changes = document.Edit()
            .SetAttribute(Named(document, "SaveButton"), XamlQualifiedName.Parse("Width"), "480")
            .GetTextChanges();

        TextChange change = Assert.Single(changes);

        // Three characters in, three characters out: the value and nothing around it.
        Assert.Equal(3, change.Span.Length);
        Assert.Equal("480", change.NewText);
        Assert.Equal("320", document.SourceText.GetText(change.Span));
    }

    [Fact]
    public void SettingABindingDoesNotConvertItToItsValue()
    {
        XamlDocument document = Parse();
        XamlElement text = Element(document, "TextBlock");

        XamlDocument edited = document.SetAttribute(
            text, XamlQualifiedName.Parse("Text"), XamlValue.Parse("{Binding Customer.Address}"));

        Assert.Contains("Text=\"{Binding Customer.Address}\"", edited.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddingAnAttributeAppendsItAfterTheLastOne()
    {
        XamlDocument document = Parse();

        XamlDocument edited = document.SetAttribute(
            Named(document, "SaveButton"), XamlQualifiedName.Parse("Grid.Row"), "1");

        Assert.Contains("Content=\"Save\" Grid.Row=\"1\" />", edited.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddingAnAttributeFollowsTheTagsExistingLayout()
    {
        // The root writes one attribute per line; a new one should read as part of the file
        // rather than as a machine's afterthought.
        XamlDocument document = Parse();

        XamlDocument edited = document.SetAttribute(
            document.Root!, XamlQualifiedName.Parse("xmlns:d"), "urn:d");

        Assert.Contains(
            "\r\n             xmlns:d=\"urn:d\">",
            edited.GetText(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingAnAttributeTakesItsLeadingWhitespaceWithIt()
    {
        XamlDocument document = Parse();

        XamlDocument edited = document.RemoveAttribute(
            Named(document, "SaveButton"), XamlQualifiedName.Parse("Width"));

        Assert.Contains("x:Name='SaveButton'  Content=\"Save\" />", edited.GetText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Width", edited.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingAnAbsentAttributeChangesNothing()
    {
        XamlDocument document = Parse();

        Assert.Equal(
            Source,
            document.RemoveAttribute(Named(document, "SaveButton"), XamlQualifiedName.Parse("Absent")).GetText());
    }

    [Fact]
    public void RemovingAnElementTakesTheLineItHadToItself()
    {
        XamlDocument document = Parse();

        string result = document.RemoveElement(Element(document, "TextBlock")).GetText();

        Assert.DoesNotContain("TextBlock", result, StringComparison.Ordinal);
        Assert.Contains("Content=\"Save\" />\r\n  </StackPanel>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void InsertingAnElementMatchesTheIndentationOfItsSiblings()
    {
        XamlDocument document = Parse();

        string result = document
            .InsertElement(Element(document, "StackPanel"), 0, "<Border />")
            .GetText();

        Assert.Contains("<Border />", result, StringComparison.Ordinal);
        Assert.Contains("<Border />\r\n\t<Button", result, StringComparison.Ordinal);
    }

    [Fact]
    public void InsertingAtTheEndAppendsAfterTheLastChild()
    {
        XamlDocument document = Parse();

        string result = document
            .InsertElement(Element(document, "StackPanel"), 99, "<Border />")
            .GetText();

        Assert.Contains("/>\r\n    <Border />\r\n  </StackPanel>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void MovingAnElementCarriesItsTextAcrossUnchanged()
    {
        const string Nested =
            "<Grid>\n  <Panel>\n    <Button Width=\"320\" Content=\"Save\" />\n  </Panel>\n  <Border>\n  </Border>\n</Grid>\n";

        XamlDocument document = XamlDocument.Parse(Nested);
        XamlElement button = Element(document, "Button");
        XamlElement border = Element(document, "Border");

        string result = document.MoveElement(button, border, 0).GetText();

        Assert.Contains("<Border>", result, StringComparison.Ordinal);
        Assert.Contains("<Button Width=\"320\" Content=\"Save\" />", result, StringComparison.Ordinal);
        Assert.Single(
            XamlDocument.Parse(result).DescendantElements(),
            static e => e.Name.LocalName == "Button");
    }

    [Fact]
    public void AnElementCannotBeMovedInsideItself()
    {
        XamlDocument document = Parse();
        XamlElement panel = Element(document, "StackPanel");
        XamlElement button = Named(document, "SaveButton");

        Assert.Throws<InvalidOperationException>(() => document.MoveElement(panel, button, 0));
        Assert.Throws<InvalidOperationException>(() => document.MoveElement(panel, panel, 0));
    }

    [Fact]
    public void SeveralEditsInOneOperationAreAppliedTogether()
    {
        XamlDocument document = Parse();

        string result = document.Edit()
            .SetAttribute(Named(document, "SaveButton"), XamlQualifiedName.Parse("Width"), "480")
            .SetAttribute(Element(document, "TextBlock"), XamlQualifiedName.Parse("Text"), "{Binding Other}")
            .Apply()
            .GetText();

        Assert.Contains("Width = \"480\"", result, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Other}\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void EditsThatChangeOverlappingRegionsAreRejected()
    {
        // There is no single correct interpretation, so guessing at one would corrupt the
        // document rather than merely surprise the caller.
        XamlDocument document = Parse();
        XamlElement button = Named(document, "SaveButton");

        XamlDocumentEditor editor = document.Edit()
            .SetAttribute(button, XamlQualifiedName.Parse("Width"), "480")
            .RemoveAttribute(button, XamlQualifiedName.Parse("Width"));

        Assert.Throws<InvalidOperationException>(editor.Apply);
    }

    [Fact]
    public void ANodeFromAnotherDocumentIsRejected()
    {
        // Its spans point into different text; using it would corrupt this document silently.
        XamlDocument first = Parse();
        XamlDocument second = Parse("<Other><Button /></Other>");

        Assert.Throws<InvalidOperationException>(
            () => first.SetAttribute(Element(second, "Button"), XamlQualifiedName.Parse("Width"), "1"));
    }

    [Fact]
    public void AStaleNodeFromAnEarlierVersionIsRejected()
    {
        // The contract requires an edit to validate that its target came from the version it
        // believes it is editing. After one edit, the old nodes describe text that has moved.
        XamlDocument document = Parse();
        XamlElement stale = Named(document, "SaveButton");

        XamlDocument edited = document.SetAttribute(stale, XamlQualifiedName.Parse("Width"), "480");

        Assert.Throws<InvalidOperationException>(
            () => edited.SetAttribute(stale, XamlQualifiedName.Parse("Content"), "Cancel"));
    }

    [Fact]
    public void AnEditedDocumentStillRoundTrips()
    {
        XamlDocument document = Parse();
        XamlDocument edited = document.SetAttribute(
            Named(document, "SaveButton"), XamlQualifiedName.Parse("Width"), "480");

        Assert.Equal(edited.SourceText.ToString(), edited.GetText());
    }

    [Fact]
    public void TheOriginalDocumentIsUnaffectedByAnEdit()
    {
        XamlDocument document = Parse();

        document.SetAttribute(Named(document, "SaveButton"), XamlQualifiedName.Parse("Width"), "480");

        Assert.Equal(Source, document.GetText());
    }

    [Fact]
    public void ReadingAValueAndWritingItBackChangesNothing()
    {
        // Round-tripping a value through the model must be a no-op. If reading unescaped and
        // writing re-escaped, "&amp;" would grow by one "amp;" on every save.
        const string WithEntities = "<Grid Text=\"a &amp; b &lt; c\" />";
        XamlDocument document = XamlDocument.Parse(WithEntities);
        XamlElement grid = document.Root!;

        XamlDocument edited = document.SetAttribute(
            grid, XamlQualifiedName.Parse("Text"), grid.GetAttribute("Text")!.GetValue());

        Assert.Equal(WithEntities, edited.GetText());
    }

    [Fact]
    public void PlainTextIsEscapedWhenTheCallerSaysItIsPlain()
    {
        XamlDocument document = XamlDocument.Parse("<Grid Text=\"x\" />");

        XamlDocument edited = document.SetAttribute(
            document.Root!,
            XamlQualifiedName.Parse("Text"),
            XamlLiteralValue.FromPlainText("a & b < c"));

        Assert.Contains("Text=\"a &amp; b &lt; c\"", edited.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AValueContainingTheDelimitingQuoteIsEscaped()
    {
        XamlDocument document = XamlDocument.Parse("<Grid Text=\"x\" />");

        XamlDocument edited = document.SetAttribute(
            document.Root!, XamlQualifiedName.Parse("Text"), "say \"hello\"");

        Assert.Contains("Text=\"say &quot;hello&quot;\"", edited.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownDirectivesAndNamespacesSurviveEditing()
    {
        const string WithUnknown =
            "<root xmlns:future=\"urn:not-invented-yet\" future:Directive=\"kept\">\n" +
            "  <Known Width=\"1\" future:Annotation=\"also kept\" />\n" +
            "</root>\n";

        XamlDocument document = XamlDocument.Parse(WithUnknown);

        string result = document
            .SetAttribute(Element(document, "Known"), XamlQualifiedName.Parse("Width"), "2")
            .GetText();

        Assert.Contains("future:Directive=\"kept\"", result, StringComparison.Ordinal);
        Assert.Contains("future:Annotation=\"also kept\"", result, StringComparison.Ordinal);
        Assert.Contains("Width=\"2\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void InsertingIntoASelfClosingElementIsRejectedRatherThanGuessed()
    {
        XamlDocument document = XamlDocument.Parse("<Grid><Button /></Grid>");

        Assert.Throws<InvalidOperationException>(
            () => document.InsertElement(Element(document, "Button"), 0, "<Border />"));
    }

    [Fact]
    public void ReplacingAnElementLeavesItsNeighboursAndItsLineAlone()
    {
        XamlDocument document = Parse();

        string result = document
            .ReplaceElement(Named(document, "SaveButton"), "<ToggleButton Content=\"Save\" />")
            .GetText();

        Assert.Equal(
            Source.Replace(
                "<Button x:Name='SaveButton'   Width = \"320\"  Content=\"Save\" />",
                "<ToggleButton Content=\"Save\" />",
                StringComparison.Ordinal),
            result);
    }

    [Fact]
    public void ReplacingIsOneChangeRatherThanARemovalAndAnInsertion()
    {
        XamlDocument document = Parse();

        ImmutableArray<TextChange> changes = document.Edit()
            .ReplaceElement(Named(document, "SaveButton"), "<ToggleButton />")
            .GetTextChanges();

        // Two changes would be a removal that takes the line and an insertion that puts one
        // back, and the element's position would then depend on the order they landed in.
        Assert.Single(changes);
    }

    [Fact]
    public void WrappingPutsTheElementInsideAndIndentsItOneLevel()
    {
        XamlDocument document = Parse();

        string result = document
            .WrapElement(Element(document, "TextBlock"), "<Border Padding=\"8\"></Border>")
            .GetText();

        Assert.Contains(
            "    <Border Padding=\"8\">\r\n" +
            "      <TextBlock Text=\"{Binding Customer.Name}\" />\r\n" +
            "    </Border>",
            result,
            StringComparison.Ordinal);

        // Both the step and the line ending came from the file rather than from a default: two
        // spaces because that is what sits between StackPanel and its children, CRLF because
        // that is what the document is written with.
        Assert.DoesNotContain("<Border Padding=\"8\">\n", result, StringComparison.Ordinal);
    }

    [Fact]
    public void WrappingCarriesEverythingTheElementWasWrittenWith()
    {
        XamlDocument document = Parse();

        string result = document
            .WrapElement(Named(document, "SaveButton"), "<Border></Border>")
            .GetText();

        Assert.Contains("x:Name='SaveButton'   Width = \"320\"  Content=\"Save\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AWrapperWithNowhereToPutContentIsRejectedRatherThanGuessed()
    {
        XamlDocument document = Parse();

        Assert.Throws<InvalidOperationException>(
            () => document.WrapElement(Element(document, "TextBlock"), "<Border />"));
    }

    [Fact]
    public void UnwrappingLiftsTheChildrenOutAndBringsThemBackALevel()
    {
        XamlDocument document = Parse();
        XamlDocument wrapped = document.WrapElement(Element(document, "TextBlock"), "<Border></Border>");

        XamlDocument unwrapped = wrapped.UnwrapElement(Element(wrapped, "Border"));

        // Wrapping and unwrapping is a round trip: the document is character for character what
        // it was, indentation included.
        Assert.Equal(Source, unwrapped.GetText());
    }

    [Fact]
    public void UnwrappingLeavesAPropertyElementWhereItBelongs()
    {
        XamlDocument document = XamlDocument.Parse(
            "<StackPanel>\n" +
            "  <Grid>\n" +
            "    <Grid.ColumnDefinitions>\n" +
            "      <ColumnDefinition Width=\"*\" />\n" +
            "    </Grid.ColumnDefinitions>\n" +
            "    <Button />\n" +
            "  </Grid>\n" +
            "</StackPanel>");

        string result = document.UnwrapElement(Element(document, "Grid")).GetText();

        // Promoting Grid.ColumnDefinitions to stand beside the button would put a member of a
        // grid where no grid is, which is markup that means nothing.
        Assert.DoesNotContain("Grid.ColumnDefinitions", result, StringComparison.Ordinal);
        Assert.Equal("<StackPanel>\n  <Button />\n</StackPanel>", result);
    }

    [Fact]
    public void WrappingDoesNotIndentTheTextAControlDisplays()
    {
        XamlDocument document = XamlDocument.Parse(
            "<StackPanel>\n" +
            "  <TextBox AcceptsReturn=\"True\">line one\nline two</TextBox>\n" +
            "</StackPanel>");

        string result = document
            .WrapElement(Element(document, "TextBox"), "<Border></Border>")
            .GetText();

        // The value is what the control displays. Indenting it would change what the user sees,
        // which is not what wrapping was asked to do.
        Assert.Contains(">line one\nline two<", result, StringComparison.Ordinal);
    }

    [Fact]
    public void WrappingTheRootKeepsTheDocumentsLineEndings()
    {
        XamlDocument document = XamlDocument.Parse("<Grid>\r\n  <Button />\r\n</Grid>\r\n");

        string result = document.WrapElement(document.Root!, "<Border></Border>").GetText();

        // The root has no line above it to learn from, and answering "line feed" would put a
        // second kind of line ending into a file that has one.
        Assert.DoesNotContain("<Border>\n", result, StringComparison.Ordinal);
        Assert.Contains("<Border>\r\n", result, StringComparison.Ordinal);
    }

    [Fact]
    public void UnwrappingAnElementWithNothingInItRemovesIt()
    {
        XamlDocument document = XamlDocument.Parse("<Grid>\n  <Border></Border>\n  <Button />\n</Grid>");

        string result = document.UnwrapElement(Element(document, "Border")).GetText();

        Assert.Equal("<Grid>\n  <Button />\n</Grid>", result);
    }

    [Fact]
    public void AnIndexCountsContentChildrenAndNotMembers()
    {
        XamlDocument document = XamlDocument.Parse(
            "<StackPanel>\n" +
            "  <StackPanel.Resources>\n" +
            "    <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
            "  </StackPanel.Resources>\n" +
            "  <Button />\n" +
            "</StackPanel>");

        string result = document
            .InsertElement(document.Root!, 0, "<TextBlock />")
            .GetText();

        // "First control" means after the resources, not before them: a property element is
        // where a member is written, not a thing standing beside the buttons.
        Assert.Contains("</StackPanel.Resources>\n  <TextBlock />\n  <Button />", result, StringComparison.Ordinal);
    }

    [Fact]
    public void InsertingIntoAParentThatOnlyDeclaresMembersLandsAfterThem()
    {
        XamlDocument document = XamlDocument.Parse(
            "<StackPanel>\n" +
            "  <StackPanel.Resources>\n" +
            "    <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
            "  </StackPanel.Resources>\n" +
            "</StackPanel>");

        string result = document.InsertElement(document.Root!, 0, "<Button />").GetText();

        Assert.Contains("</StackPanel.Resources>\n  <Button />", result, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicatingPutsAnAnonymousCopyStraightAfterTheOriginal()
    {
        XamlDocument document = Parse();

        string result = document.DuplicateElement(Named(document, "SaveButton")).GetText();

        // A copy that kept the name would have declared it twice, and a loader that enforces the
        // rule refuses the whole document.
        Assert.Equal(2, Occurrences(result, "<Button"));
        Assert.Equal(1, Occurrences(result, "x:Name='SaveButton'"));

        // Straight after the original and before what followed it, with everything else the
        // original was written with carried across.
        int original = result.IndexOf("<Button", StringComparison.Ordinal);
        int copy = result.IndexOf("<Button", original + 1, StringComparison.Ordinal);

        Assert.InRange(copy, original, result.IndexOf("<TextBlock", StringComparison.Ordinal));
        Assert.Contains("Width = \"320\"  Content=\"Save\"", result[copy..], StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicatingCanKeepTheNamesForACallerThatWillRenameThem()
    {
        XamlDocument document = Parse();

        string result = document
            .DuplicateElement(Named(document, "SaveButton"), XamlDuplicateNames.Keep)
            .GetText();

        Assert.Equal(2, Occurrences(result, "x:Name='SaveButton'"));
    }

    [Fact]
    public void DuplicatingTheRootIsRejectedRatherThanGuessed()
    {
        XamlDocument document = Parse();

        Assert.Throws<InvalidOperationException>(() => document.DuplicateElement(document.Root!));
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        var at = 0;

        while ((at = text.IndexOf(value, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += value.Length;
        }

        return count;
    }

    [Fact]
    public void AnEditorWithNothingRecordedReturnsTheSameDocument()
    {
        XamlDocument document = Parse();

        Assert.Same(document, document.Edit().Apply());
    }
}
