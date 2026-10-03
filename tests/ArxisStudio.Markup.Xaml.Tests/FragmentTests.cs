using System;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// Markup crossing from one document to another: lifted with the namespaces it is written in, and
/// written in the receiving document's — declared where they are missing, renamed only where a
/// prefix means something else.
/// </summary>
public sealed class FragmentTests
{
    private const string Avalonia = "https://github.com/avaloniaui";
    private const string Xaml = XamlNamespaces.Xaml;

    private const string Card =
        "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
        "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
        "             xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\"\n" +
        "             xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"\n" +
        "             xmlns:local=\"using:App.Controls\"\n" +
        "             xmlns:unused=\"using:App.Unused\"\n" +
        "             mc:Ignorable=\"d\"\n" +
        "             x:Class=\"App.Controls.Card\">\n" +
        "  <StackPanel>\n" +
        "    <Border Padding=\"8\">\n" +
        "      <local:Badge x:Name=\"Status\" d:Text=\"sample\" />\n" +
        "    </Border>\n" +
        "  </StackPanel>\n" +
        "</UserControl>\n";

    private const string Main =
        "<Window xmlns=\"https://github.com/avaloniaui\"\n" +
        "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
        "        x:Class=\"App.Views.Main\">\n" +
        "  <Grid>\n" +
        "    <TextBlock Text=\"first\" />\n" +
        "  </Grid>\n" +
        "</Window>\n";

    private static readonly Uri CardUri = new("file:///App/Controls/Card.axaml");

    private static XamlElement Element(XamlDocument document, string localName) =>
        document.DescendantElements().First(element => element.Name.LocalName == localName);

    private static XamlFragment Border() =>
        XamlFragment.From(Element(XamlDocument.Parse(Card, new XamlParseOptions { DocumentUri = CardUri }), "Border"));

    [Fact]
    public void AFragmentCarriesTheDeclarationsItsTextUsesAndIsLeftAligned()
    {
        XamlFragment fragment = Border();

        // The default namespace always; x, d and local because the text names them; mc because d
        // was ignorable where it came from; unused because nothing in it says unused.
        Assert.Equal(
            "<Border xmlns=\"https://github.com/avaloniaui\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
            "xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\" " +
            "xmlns:local=\"using:App.Controls\" " +
            "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"d\" " +
            "Padding=\"8\">\n" +
            "  <local:Badge x:Name=\"Status\" d:Text=\"sample\" />\n" +
            "</Border>",
            fragment.ToXamlText());

        Assert.Equal(["", "d", "local", "mc", "x"], fragment.Namespaces.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([XamlNamespaces.Design], fragment.IgnorableNamespaces);
        Assert.Equal(CardUri, fragment.SourceUri);
        Assert.True(fragment.Document.IsWellFormed);
    }

    [Fact]
    public void AFragmentReadsBackFromItsOwnText()
    {
        XamlFragment written = Border();
        XamlFragment read = XamlFragment.Parse(written.ToXamlText());

        Assert.Equal(written.ToXamlText(), read.ToXamlText());
        Assert.Equal(written.Namespaces.OrderBy(static e => e.Key), read.Namespaces.OrderBy(static e => e.Key));
        Assert.Equal(written.IgnorableNamespaces, read.IgnorableNamespaces);
    }

    [Fact]
    public void TextCopiedWithItsIndentationIsLeftAligned()
    {
        XamlFragment fragment = XamlFragment.Parse(
            "\n    <Border>\n      <Button />\n    </Border>\n");

        Assert.Equal("<Border>\n  <Button />\n</Border>", fragment.ToXamlText());
    }

    [Fact]
    public void InsertingDeclaresWhatTheDocumentLacksAndIndentsToWhereItLands()
    {
        var target = XamlDocument.Parse(Main);
        XamlDocumentEditor editor = target.Edit();

        editor.InsertFragment(Element(target, "Grid"), 1, Border());

        Assert.Empty(editor.Diagnostics);
        Assert.Equal(
            "<Window xmlns=\"https://github.com/avaloniaui\"\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "        xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\"\n" +
            "        xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"\n" +
            "        xmlns:local=\"using:App.Controls\"\n" +
            "        mc:Ignorable=\"d\"\n" +
            "        x:Class=\"App.Views.Main\">\n" +
            "  <Grid>\n" +
            "    <TextBlock Text=\"first\" />\n" +
            "    <Border Padding=\"8\">\n" +
            "      <local:Badge x:Name=\"Status\" d:Text=\"sample\" />\n" +
            "    </Border>\n" +
            "  </Grid>\n" +
            "</Window>\n",
            editor.Apply().GetText());
    }

    /// <summary>
    /// Two fragments pasted into an empty panel by one editor arrive one under the other, each indented to
    /// where it lands.
    /// </summary>
    [Fact]
    public void TwoFragmentsPastedIntoAnEmptyPanelArriveInOrder()
    {
        var target = XamlDocument.Parse(
            "<Window xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <StackPanel />\n" +
            "</Window>\n");

        XamlDocumentEditor editor = target.Edit();
        XamlElement panel = Element(target, "StackPanel");

        editor.InsertFragment(panel, 0, XamlFragment.Parse($"<TextBlock xmlns=\"{Avalonia}\" Text=\"first\" />"));
        editor.InsertFragment(panel, 0, XamlFragment.Parse($"<Border xmlns=\"{Avalonia}\">\n  <Button />\n</Border>"));

        Assert.Empty(editor.Diagnostics);
        Assert.Equal(
            "<Window xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <StackPanel>\n" +
            "    <TextBlock Text=\"first\" />\n" +
            "    <Border>\n" +
            "      <Button />\n" +
            "    </Border>\n" +
            "  </StackPanel>\n" +
            "</Window>\n",
            editor.Apply().GetText());
    }

    [Fact]
    public void APrefixMeaningSomethingElseHereIsRenamedWhereverTheSyntaxNamesIt()
    {
        var source = XamlDocument.Parse(
            $"<UserControl xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" xmlns:local=\"using:App.Controls\">\n" +
            "  <Border local:Tip.Text=\"hint\" x:DataType=\"local:CardModel\">\n" +
            "    <Border.Styles>\n" +
            "      <Style Selector=\"local|Badge.accent\">\n" +
            "        <Setter Property=\"local:Tip.Text\" Value=\"{x:Static local:Texts.Accent}\" />\n" +
            "      </Style>\n" +
            "    </Border.Styles>\n" +
            "    <StackPanel>\n" +
            "      <local:Badge Kind=\"{local:Glyph Star}\" Tag=\"{x:Type local:Badge}\"></local:Badge>\n" +
            "      <TextBlock Text=\"{Binding (local:Tip.Text), ElementName=Card}\" />\n" +
            "      <TextBlock>local:Badge</TextBlock>\n" +
            "    </StackPanel>\n" +
            "  </Border>\n" +
            "</UserControl>");

        var target = XamlDocument.Parse(
            "<Window xmlns=\"https://github.com/avaloniaui\"\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "        xmlns:local=\"using:App.Views\">\n" +
            "  <Grid>\n" +
            "    <TextBlock />\n" +
            "  </Grid>\n" +
            "</Window>");

        XamlDocumentEditor editor = target.Edit();

        editor.InsertFragment(Element(target, "Grid"), 1, XamlFragment.From(Element(source, "Border")));

        // Every place the syntax says names App.Controls now says local1, and the text a control
        // displays still says what it said.
        Assert.Empty(editor.Diagnostics);
        Assert.Equal(
            "<Window xmlns=\"https://github.com/avaloniaui\"\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "        xmlns:local=\"using:App.Views\"\n" +
            "        xmlns:local1=\"using:App.Controls\">\n" +
            "  <Grid>\n" +
            "    <TextBlock />\n" +
            "    <Border local1:Tip.Text=\"hint\" x:DataType=\"local1:CardModel\">\n" +
            "      <Border.Styles>\n" +
            "        <Style Selector=\"local1|Badge.accent\">\n" +
            "          <Setter Property=\"local1:Tip.Text\" Value=\"{x:Static local1:Texts.Accent}\" />\n" +
            "        </Style>\n" +
            "      </Border.Styles>\n" +
            "      <StackPanel>\n" +
            "        <local1:Badge Kind=\"{local1:Glyph Star}\" Tag=\"{x:Type local1:Badge}\"></local1:Badge>\n" +
            "        <TextBlock Text=\"{Binding (local1:Tip.Text), ElementName=Card}\" />\n" +
            "        <TextBlock>local:Badge</TextBlock>\n" +
            "      </StackPanel>\n" +
            "    </Border>\n" +
            "  </Grid>\n" +
            "</Window>",
            editor.Apply().GetText());
    }

    [Fact]
    public void AMentionTheSyntaxDoesNotReadAsANameIsLeftAndReported()
    {
        XamlFragment fragment = XamlFragment.Parse(
            $"<TextBlock xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" xmlns:local=\"using:App.Controls\" " +
            "Text=\"see local:Badge\" Tag=\"{x:Type local:Badge}\" />");

        var target = XamlDocument.Parse(
            $"<StackPanel xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" xmlns:local=\"using:App.Views\" />");

        XamlDocumentEditor editor = target.Edit().InsertFragment(target.Root!, 0, fragment);

        string result = editor.Apply().GetText();

        Assert.Contains("Tag=\"{x:Type local1:Badge}\"", result, StringComparison.Ordinal);
        Assert.Contains("Text=\"see local:Badge\"", result, StringComparison.Ordinal);

        MarkupDiagnostic reported = Assert.Single(editor.Diagnostics);

        Assert.Equal(XamlDiagnosticCodes.FragmentPrefixLeftAsWritten, reported.Code);
        Assert.Equal(MarkupDiagnosticSeverity.Warning, reported.Severity);
        Assert.Contains("'local'", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APrefixMeaningSomethingElseTakesTheOneTheDocumentGivesItsNamespace()
    {
        XamlFragment fragment = XamlFragment.Parse(
            $"<StackPanel xmlns=\"{Avalonia}\" xmlns:local=\"using:App.Controls\"><local:Badge /></StackPanel>");

        var target = XamlDocument.Parse(
            $"<Grid xmlns=\"{Avalonia}\" xmlns:controls=\"using:App.Controls\" xmlns:local=\"using:App.Views\">" +
            "<Border /></Grid>");

        string result = target.Edit().InsertFragment(target.Root!, 1, fragment).Apply().GetText();

        Assert.Equal(
            $"<Grid xmlns=\"{Avalonia}\" xmlns:controls=\"using:App.Controls\" xmlns:local=\"using:App.Views\">" +
            "<Border /><StackPanel><controls:Badge /></StackPanel></Grid>",
            result);
    }

    [Fact]
    public void APrefixNothingHereUsesIsDeclaredAsTheFragmentWroteIt()
    {
        // The same namespace is already here under another prefix, and the fragment's own is free.
        // Declaring it again costs one attribute; renaming would rewrite the fragment, and is kept
        // for when it cannot be helped.
        XamlFragment fragment = XamlFragment.Parse(
            $"<StackPanel xmlns=\"{Avalonia}\" xmlns:c=\"using:App.Controls\"><c:Badge /></StackPanel>");

        var target = XamlDocument.Parse(
            $"<Grid xmlns=\"{Avalonia}\" xmlns:controls=\"using:App.Controls\"><Border /></Grid>");

        string result = target.Edit().InsertFragment(target.Root!, 1, fragment).Apply().GetText();

        Assert.Equal(
            $"<Grid xmlns=\"{Avalonia}\" xmlns:controls=\"using:App.Controls\" xmlns:c=\"using:App.Controls\">" +
            "<Border /><StackPanel><c:Badge /></StackPanel></Grid>",
            result);
    }

    [Fact]
    public void ANamespaceTheFragmentsSourceMarkedIgnorableArrivesIgnorable()
    {
        // Not the design namespace, which is made ignorable whenever it is declared, but one only
        // the fragment's source said a reader may skip.
        var source = XamlDocument.Parse(
            $"<StackPanel xmlns=\"{Avalonia}\" xmlns:s=\"urn:sample\" " +
            $"xmlns:mc=\"{XamlNamespaces.MarkupCompatibility}\" mc:Ignorable=\"s\">" +
            "<Button s:Note=\"later\" /></StackPanel>");

        var target = XamlDocument.Parse($"<Grid xmlns=\"{Avalonia}\"><Border /></Grid>");

        string result = target.Edit()
            .InsertFragment(target.Root!, 1, XamlFragment.From(Element(source, "Button")))
            .Apply()
            .GetText();

        Assert.Equal(
            $"<Grid xmlns=\"{Avalonia}\" xmlns:s=\"urn:sample\" " +
            $"xmlns:mc=\"{XamlNamespaces.MarkupCompatibility}\" mc:Ignorable=\"s\">" +
            "<Border /><Button s:Note=\"later\" /></Grid>",
            result);
    }

    [Fact]
    public void ANamespaceOnlyATakenOutNameUsedIsNotDeclared()
    {
        var target = XamlDocument.Parse($"<StackPanel xmlns=\"{Avalonia}\"><Button Name=\"Save\" /></StackPanel>");
        XamlFragment fragment = XamlFragment.Parse($"<Button xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" x:Name=\"Save\" />");

        string result = target.Edit().InsertFragment(target.Root!, 1, fragment).Apply().GetText();

        Assert.Equal($"<StackPanel xmlns=\"{Avalonia}\"><Button Name=\"Save\" /><Button /></StackPanel>", result);
    }

    [Fact]
    public void AnInnerDeclarationOfTheSamePrefixKeepsWhatItBinds()
    {
        // XML lets an element rebind a prefix for what it contains. What is inside it names that
        // other namespace, so a rename of the outer one does not reach it.
        XamlFragment fragment = XamlFragment.Parse(
            $"<StackPanel xmlns=\"{Avalonia}\" xmlns:local=\"using:App.Controls\">" +
            "<local:Badge />" +
            "<Border xmlns:local=\"using:App.Other\"><local:Gauge /></Border>" +
            "</StackPanel>");

        var target = XamlDocument.Parse($"<Grid xmlns=\"{Avalonia}\" xmlns:local=\"using:App.Views\" />");

        string result = target.Edit().InsertFragment(target.Root!, 0, fragment).Apply().GetText();

        Assert.Equal(
            $"<Grid xmlns=\"{Avalonia}\" xmlns:local=\"using:App.Views\" xmlns:local1=\"using:App.Controls\">" +
            "<StackPanel><local1:Badge /><Border xmlns:local=\"using:App.Other\"><local:Gauge /></Border></StackPanel>" +
            "</Grid>",
            result);
    }

    [Fact]
    public void AFragmentInAnotherDefaultNamespaceIsRefused()
    {
        var target = XamlDocument.Parse("<Root xmlns=\"urn:other\"><Child /></Root>");
        XamlDocumentEditor editor = target.Edit()
            .InsertFragment(target.Root!, 0, XamlFragment.Parse($"<Button xmlns=\"{Avalonia}\" />"));

        Assert.False(editor.HasChanges);
        Assert.Equal(XamlDiagnosticCodes.FragmentDefaultNamespaceConflict, Assert.Single(editor.Diagnostics).Code);
    }

    [Theory]
    [InlineData("<Border><Button></Border>")]
    [InlineData("just text")]
    public void AFragmentThatIsNotOneWellFormedElementIsRefused(string text)
    {
        var target = XamlDocument.Parse(Main);
        XamlDocumentEditor editor = target.Edit().InsertFragment(Element(target, "Grid"), 0, XamlFragment.Parse(text));

        Assert.False(editor.HasChanges);

        MarkupDiagnostic refused = Assert.Single(editor.Diagnostics);

        Assert.Equal(XamlDiagnosticCodes.MalformedFragment, refused.Code);
        Assert.True(refused.IsError);
    }

    [Fact]
    public void NamesTheDocumentAlreadyDeclaresAreTakenOutAndTheRestKept()
    {
        var target = XamlDocument.Parse(
            $"<StackPanel xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\"><Button x:Name=\"Save\" /></StackPanel>");

        XamlFragment fragment = XamlFragment.Parse(
            $"<WrapPanel xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\"><Button x:Name=\"Save\" /><Button Name=\"Cancel\" /></WrapPanel>");

        string result = target.Edit().InsertFragment(target.Root!, 1, fragment).Apply().GetText();

        Assert.Equal(
            $"<StackPanel xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\"><Button x:Name=\"Save\" />" +
            "<WrapPanel><Button /><Button Name=\"Cancel\" /></WrapPanel></StackPanel>",
            result);
    }

    [Theory]
    [InlineData(XamlDuplicateNames.Keep, "<Button x:Name=\"Save\" /><Button Name=\"Cancel\" />")]
    [InlineData(XamlDuplicateNames.Remove, "<Button /><Button />")]
    public void NamesCanBeKeptOrTakenOutWhole(XamlDuplicateNames names, string expected)
    {
        var target = XamlDocument.Parse(
            $"<StackPanel xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\"><Button x:Name=\"Save\" /></StackPanel>");

        XamlFragment fragment = XamlFragment.Parse(
            $"<WrapPanel xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\"><Button x:Name=\"Save\" /><Button Name=\"Cancel\" /></WrapPanel>");

        string result = target.Edit().InsertFragment(target.Root!, 1, fragment, names).Apply().GetText();

        Assert.Contains($"<WrapPanel>{expected}</WrapPanel>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameFragmentTwiceInOneEditorDoesNotDeclareANameTwice()
    {
        var target = XamlDocument.Parse($"<StackPanel xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\"><Border /></StackPanel>");
        XamlFragment fragment = XamlFragment.Parse($"<Button xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" x:Name=\"Extra\" />");

        string result = target.Edit()
            .InsertFragment(target.Root!, 1, fragment)
            .InsertFragment(target.Root!, 1, fragment)
            .Apply()
            .GetText();

        Assert.Equal(
            $"<StackPanel xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\"><Border /><Button x:Name=\"Extra\" /><Button /></StackPanel>",
            result);
    }

    [Fact]
    public void ADocumentsRootLeavesItsClassBehind()
    {
        // Only a document's root may say which class it populates, and inserted, it is not one.
        XamlFragment fragment = XamlFragment.From(XamlDocument.Parse(Card).Root!);
        var target = XamlDocument.Parse(Main);

        string result = target.Edit().InsertFragment(Element(target, "Grid"), 1, fragment).Apply().GetText();

        Assert.DoesNotContain("App.Controls.Card", result, StringComparison.Ordinal);
        Assert.Contains("x:Class=\"App.Views.Main\"", result, StringComparison.Ordinal);
        Assert.Contains("    <UserControl>\n", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TextAControlDisplaysIsNotReindented()
    {
        var source = XamlDocument.Parse(
            $"<StackPanel xmlns=\"{Avalonia}\">\n" +
            "  <TextBlock>first line\n" +
            "second line</TextBlock>\n" +
            "</StackPanel>");

        var target = XamlDocument.Parse(Main);

        string result = target.Edit()
            .InsertFragment(Element(target, "Grid"), 1, XamlFragment.From(Element(source, "TextBlock")))
            .Apply()
            .GetText();

        Assert.Contains("    <TextBlock>first line\nsecond line</TextBlock>\n", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDocumentsOwnLineBreaksAreUsed()
    {
        XamlFragment fragment = XamlFragment.Parse(
            $"<Border xmlns=\"{Avalonia}\">\r\n  <Button />\r\n</Border>");

        var target = XamlDocument.Parse(Main);

        string result = target.Edit().InsertFragment(Element(target, "Grid"), 1, fragment).Apply().GetText();

        Assert.DoesNotContain("\r", result, StringComparison.Ordinal);
        Assert.Contains("    <Border>\n      <Button />\n    </Border>\n", result, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkupFromTheSameDocumentNeedsNoDeclarationAndLosesItsNames()
    {
        var document = XamlDocument.Parse(Card);

        XamlDocumentEditor editor = document.Edit()
            .InsertFragment(Element(document, "StackPanel"), 0, XamlFragment.From(Element(document, "Badge")));

        string result = editor.Apply().GetText();

        Assert.Empty(editor.Diagnostics);
        Assert.Equal(Card.Split("xmlns").Length, result.Split("xmlns").Length);
        Assert.Contains("  <StackPanel>\n    <local:Badge d:Text=\"sample\" />\n    <Border", result, StringComparison.Ordinal);
    }
}
