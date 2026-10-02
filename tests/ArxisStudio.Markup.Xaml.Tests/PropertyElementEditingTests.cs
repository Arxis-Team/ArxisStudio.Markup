using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// A member written as a property element is set or taken out as one edit: an existing one keeps
/// its tags and the comments around its value, a new one goes where members are written and is
/// laid out as the file is.
/// </summary>
public sealed class PropertyElementEditingTests
{
    private const string Avalonia = "https://github.com/avaloniaui";
    private const string ViewModels = "using:App.ViewModels";

    private static readonly XamlQualifiedName DesignDataContext = XamlQualifiedName.Unprefixed("Design.DataContext");

    private const string Template =
        "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
        "             xmlns:vm=\"using:App.ViewModels\">\n" +
        "  <Design.DataContext>\n" +
        "    <!-- This only sets the DataContext for the previewer in an IDE -->\n" +
        "    <vm:OldViewModel />\n" +
        "  </Design.DataContext>\n" +
        "  <StackPanel />\n" +
        "</UserControl>\n";

    private static XamlElement Element(XamlDocument document, string localName) =>
        document.DescendantElements().First(element => element.Name.LocalName == localName);

    [Fact]
    public void ANewMemberIsWrittenInFrontOfTheContentAndLaidOutAsItsSiblingsAre()
    {
        XamlDocument document = XamlDocument.Parse(
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:vm=\"using:App.ViewModels\">\n" +
            "  <StackPanel>\n" +
            "    <Button Content=\"Save\" />\n" +
            "  </StackPanel>\n" +
            "</UserControl>\n");

        string result = document
            .SetPropertyElement(document.Root!, DesignDataContext, "<vm:MainViewModel />")
            .GetText();

        Assert.Equal(
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:vm=\"using:App.ViewModels\">\n" +
            "  <Design.DataContext>\n" +
            "    <vm:MainViewModel />\n" +
            "  </Design.DataContext>\n" +
            "  <StackPanel>\n" +
            "    <Button Content=\"Save\" />\n" +
            "  </StackPanel>\n" +
            "</UserControl>\n",
            result);
    }

    [Fact]
    public void AnExistingMemberHasWhatItSaysReplacedAndKeepsTheCommentAboveIt()
    {
        XamlDocument document = XamlDocument.Parse(Template);
        XamlElement old = Element(document, "OldViewModel");

        ImmutableArray<TextChange> changes = document.Edit()
            .SetPropertyElement(document.Root!, DesignDataContext, "<vm:MainViewModel />")
            .GetTextChanges();

        // One change over the old value and nothing else: the tags, the comment Avalonia's template
        // writes above the value and the layout around it are not what the edit is about.
        TextChange change = Assert.Single(changes);

        Assert.Equal(old.Span, change.Span);
        Assert.Equal(
            Template.Replace("OldViewModel", "MainViewModel", StringComparison.Ordinal),
            document.SetPropertyElement(document.Root!, DesignDataContext, "<vm:MainViewModel />").GetText());
    }

    [Fact]
    public void AMemberIsFoundByTheNamespaceItsPrefixIsBoundTo()
    {
        string source =
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:av=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:vm=\"using:App.ViewModels\">\n" +
            "  <av:Design.DataContext>\n" +
            "    <vm:OldViewModel />\n" +
            "  </av:Design.DataContext>\n" +
            "  <StackPanel />\n" +
            "</UserControl>\n";

        XamlDocument document = XamlDocument.Parse(source);
        XamlDocument edited = document.SetPropertyElement(document.Root!, DesignDataContext, "<vm:MainViewModel />");

        // Unprefixed here and 'av:' there are one name to a reader, so it is the same member.
        Assert.Equal(source.Replace("OldViewModel", "MainViewModel", StringComparison.Ordinal), edited.GetText());
    }

    [Fact]
    public void AMemberOfTheSameNameInAnotherNamespaceIsAnotherMember()
    {
        XamlDocument document = XamlDocument.Parse(
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\">\n" +
            "  <d:Design.DataContext>\n" +
            "    <TextBlock />\n" +
            "  </d:Design.DataContext>\n" +
            "  <StackPanel />\n" +
            "</UserControl>\n");

        string result = document.SetPropertyElement(document.Root!, DesignDataContext, "<Border />").GetText();

        Assert.Equal(
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\">\n" +
            "  <d:Design.DataContext>\n" +
            "    <TextBlock />\n" +
            "  </d:Design.DataContext>\n" +
            "  <Design.DataContext>\n" +
            "    <Border />\n" +
            "  </Design.DataContext>\n" +
            "  <StackPanel />\n" +
            "</UserControl>\n",
            result);
    }

    [Fact]
    public void ASelfClosingElementIsOpenedOntoLinesForItsFirstMember()
    {
        XamlDocument document = XamlDocument.Parse(
            "<StackPanel xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <Button Content=\"Save\" />\n" +
            "</StackPanel>\n");

        string result = document
            .SetPropertyElement(Element(document, "Button"), XamlQualifiedName.Unprefixed("Button.Flyout"), "<Flyout />")
            .GetText();

        Assert.Equal(
            "<StackPanel xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <Button Content=\"Save\">\n" +
            "    <Button.Flyout>\n" +
            "      <Flyout />\n" +
            "    </Button.Flyout>\n" +
            "  </Button>\n" +
            "</StackPanel>\n",
            result);
    }

    [Fact]
    public void AnElementWithOnlyMembersGetsTheNewOneAfterThemInTheDocumentsLineBreaks()
    {
        XamlDocument document = XamlDocument.Parse(
            "<Grid xmlns=\"https://github.com/avaloniaui\">\r\n" +
            "  <Grid.RowDefinitions>\r\n" +
            "    <RowDefinition />\r\n" +
            "  </Grid.RowDefinitions>\r\n" +
            "</Grid>\r\n");

        // Written with line feeds, as a tool builds its markup, and arriving with the file's own.
        string result = document
            .SetPropertyElement(
                document.Root!,
                XamlQualifiedName.Unprefixed("Grid.ColumnDefinitions"),
                "<ColumnDefinition Width=\"*\" />\n<ColumnDefinition Width=\"Auto\" />")
            .GetText();

        Assert.Equal(
            "<Grid xmlns=\"https://github.com/avaloniaui\">\r\n" +
            "  <Grid.RowDefinitions>\r\n" +
            "    <RowDefinition />\r\n" +
            "  </Grid.RowDefinitions>\r\n" +
            "  <Grid.ColumnDefinitions>\r\n" +
            "    <ColumnDefinition Width=\"*\" />\r\n" +
            "    <ColumnDefinition Width=\"Auto\" />\r\n" +
            "  </Grid.ColumnDefinitions>\r\n" +
            "</Grid>\r\n",
            result);
    }

    [Fact]
    public void ALineBreakInsideAValueIsLeftAsItIs()
    {
        XamlDocument document = XamlDocument.Parse(
            "<StackPanel xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <TextBox />\n" +
            "</StackPanel>\n");

        string result = document
            .SetPropertyElement(Element(document, "TextBox"), XamlQualifiedName.Unprefixed("TextBox.Text"), "first line\nsecond line")
            .GetText();

        // The value starts where the member's content starts; its second line is part of what it
        // says, and indenting it would change the text.
        Assert.Equal(
            "<StackPanel xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <TextBox>\n" +
            "    <TextBox.Text>\n" +
            "      first line\n" +
            "second line\n" +
            "    </TextBox.Text>\n" +
            "  </TextBox>\n" +
            "</StackPanel>\n",
            result);
    }

    [Fact]
    public void AnElementWrittenOnOneLineGetsItsMemberOnThatLine()
    {
        XamlDocument document = XamlDocument.Parse(
            "<Border xmlns=\"https://github.com/avaloniaui\"><TextBlock /></Border>");

        string result = document
            .SetPropertyElement(document.Root!, XamlQualifiedName.Unprefixed("Border.Background"), "<SolidColorBrush Color=\"Red\" />")
            .GetText();

        Assert.Equal(
            "<Border xmlns=\"https://github.com/avaloniaui\">" +
            "<Border.Background><SolidColorBrush Color=\"Red\" /></Border.Background><TextBlock /></Border>",
            result);
    }

    [Fact]
    public void ANewMemberGoesInFrontOfTextContentRatherThanInsideIt()
    {
        XamlDocument document = XamlDocument.Parse(
            "<TextBlock xmlns=\"https://github.com/avaloniaui\">Hello <Run Text=\"there\" /></TextBlock>");

        string result = document
            .SetPropertyElement(document.Root!, XamlQualifiedName.Unprefixed("TextBlock.Foreground"), "Red")
            .GetText();

        // Content is written in one piece; a member between the text and the run would split it.
        Assert.Equal(
            "<TextBlock xmlns=\"https://github.com/avaloniaui\">" +
            "<TextBlock.Foreground>Red</TextBlock.Foreground>Hello <Run Text=\"there\" /></TextBlock>",
            result);
    }

    [Fact]
    public void AMemberThatSaysNothingGetsTheValueAfterItsComment()
    {
        XamlDocument document = XamlDocument.Parse(
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:vm=\"using:App.ViewModels\">\n" +
            "  <Design.DataContext>\n" +
            "    <!-- previewer only -->\n" +
            "  </Design.DataContext>\n" +
            "  <StackPanel />\n" +
            "</UserControl>\n");

        string result = document.SetPropertyElement(document.Root!, DesignDataContext, "<vm:MainViewModel />").GetText();

        Assert.Equal(
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:vm=\"using:App.ViewModels\">\n" +
            "  <Design.DataContext>\n" +
            "    <!-- previewer only -->\n" +
            "    <vm:MainViewModel />\n" +
            "  </Design.DataContext>\n" +
            "  <StackPanel />\n" +
            "</UserControl>\n",
            result);
    }

    [Fact]
    public void ADesignDataContextIsWrittenWithTheNamespaceItsValueNeeds()
    {
        XamlDocument document = XamlDocument.Parse(
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "             x:Class=\"App.Views.MainView\">\n" +
            "  <StackPanel />\n" +
            "</UserControl>\n");

        XamlElement root = document.Root!;
        XamlDocumentEditor editor = document.Edit();

        // What a tool writes: the member's name and the value's type, each in the namespace it is
        // in, and one edit for the declaration and the member together.
        XamlQualifiedName member = editor.Qualify(root, Avalonia, "Design.DataContext");
        XamlQualifiedName type = editor.Qualify(root, ViewModels, "MainViewModel", "vm");

        string result = editor.SetPropertyElement(root, member, $"<{type} />").Apply().GetText();

        Assert.Equal(
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "             xmlns:vm=\"using:App.ViewModels\"\n" +
            "             x:Class=\"App.Views.MainView\">\n" +
            "  <Design.DataContext>\n" +
            "    <vm:MainViewModel />\n" +
            "  </Design.DataContext>\n" +
            "  <StackPanel />\n" +
            "</UserControl>\n",
            result);
    }

    [Fact]
    public void TakingAMemberOutTakesTheLinesItWasWrittenOn()
    {
        XamlDocument document = XamlDocument.Parse(Template);

        string result = document.RemovePropertyElement(document.Root!, DesignDataContext).GetText();

        Assert.Equal(
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:vm=\"using:App.ViewModels\">\n" +
            "  <StackPanel />\n" +
            "</UserControl>\n",
            result);
    }

    [Fact]
    public void TakingOutAMemberTheElementDoesNotWriteChangesNothing()
    {
        XamlDocument document = XamlDocument.Parse(Template);
        XamlElement stackPanel = Element(document, "StackPanel");

        XamlDocumentEditor editor = document.Edit()
            .RemovePropertyElement(stackPanel, XamlQualifiedName.Unprefixed("StackPanel.Resources"))
            .RemovePropertyElement(document.Root!, XamlQualifiedName.Unprefixed("UserControl.DataContext"));

        Assert.False(editor.HasChanges);
        Assert.Same(document, document.RemovePropertyElement(stackPanel, XamlQualifiedName.Unprefixed("StackPanel.Resources")));
    }

    [Fact]
    public void WhatIsNotAMemberIsRefused()
    {
        XamlDocument document = XamlDocument.Parse(Template);
        XamlElement root = document.Root!;
        XamlElement member = root.MemberElements.Single();
        XamlDocumentEditor editor = document.Edit();

        Assert.Throws<ArgumentException>(() => editor.SetPropertyElement(root, XamlQualifiedName.Unprefixed("DataContext"), "<Border />"));
        Assert.Throws<ArgumentException>(() => editor.SetPropertyElement(root, XamlQualifiedName.Unprefixed(".DataContext"), "<Border />"));
        Assert.Throws<ArgumentException>(() => editor.SetPropertyElement(root, XamlQualifiedName.Unprefixed("Design."), "<Border />"));
        Assert.Throws<ArgumentException>(() => editor.SetPropertyElement(root, default, "<Border />"));
        Assert.Throws<ArgumentException>(() => editor.RemovePropertyElement(root, XamlQualifiedName.Unprefixed("DataContext")));

        // A member that says nothing is a member taken out, and that edit has its own name.
        Assert.Throws<ArgumentException>(() => editor.SetPropertyElement(root, DesignDataContext, " \n "));
        Assert.Throws<ArgumentNullException>(() => editor.SetPropertyElement(root, DesignDataContext, null!));

        // A property element is a member, and has none of its own.
        Assert.Throws<InvalidOperationException>(() =>
            editor.SetPropertyElement(member, XamlQualifiedName.Unprefixed("Design.Width"), "10"));

        Assert.False(editor.HasChanges);
    }
}
