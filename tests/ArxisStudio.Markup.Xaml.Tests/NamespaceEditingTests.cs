using System;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// Writing a name in a namespace the document may not have declared: under the prefix the
/// document gave it where it is in scope, declared on the root where it is not, and never as the
/// default namespace.
/// </summary>
public sealed class NamespaceEditingTests
{
    private const string Avalonia = "https://github.com/avaloniaui";
    private const string Controls = "using:App.Controls";

    private const string Form =
        "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
        "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
        "             x:Class=\"App.Views.Form\">\n" +
        "  <StackPanel>\n" +
        "    <Button Content=\"Save\" />\n" +
        "  </StackPanel>\n" +
        "</UserControl>\n";

    private static XamlElement Element(XamlDocument document, string localName) =>
        document.DescendantElements().First(element => element.Name.LocalName == localName);

    [Fact]
    public void ANamespaceInScopeIsWrittenWithThePrefixTheDocumentGaveIt()
    {
        XamlDocument document = XamlDocument.Parse(
            Form.Replace("x:Class", $"xmlns:controls=\"{Controls}\" x:Class", StringComparison.Ordinal));

        XamlDocumentEditor editor = document.Edit();

        // The document's own choice wins over the caller's wish: one namespace, one prefix.
        XamlQualifiedName name = editor.Qualify(Element(document, "StackPanel"), Controls, "Badge", "c");

        Assert.Equal("controls:Badge", name.ToString());
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void TheDefaultNamespaceGivesAnUnprefixedName()
    {
        var document = XamlDocument.Parse(Form);
        XamlDocumentEditor editor = document.Edit();

        Assert.Equal("TextBlock", editor.Qualify(Element(document, "StackPanel"), Avalonia, "TextBlock").ToString());

        // An attached property's owner is resolved as an element is, so Grid.Row stays Grid.Row.
        Assert.Equal("Grid.Row", editor.Qualify(Element(document, "Button"), Avalonia, "Grid.Row").ToString());
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void AMissingNamespaceIsDeclaredOnTheRootTheWayItsDeclarationsAreLaidOut()
    {
        var document = XamlDocument.Parse(Form);
        XamlDocumentEditor editor = document.Edit();
        XamlElement panel = Element(document, "StackPanel");

        XamlQualifiedName badge = editor.Qualify(panel, Controls, "Badge");

        string result = editor.InsertElement(panel, 1, $"<{badge} />").Apply().GetText();

        Assert.Equal(
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "             xmlns:controls=\"using:App.Controls\"\n" +
            "             x:Class=\"App.Views.Form\">\n" +
            "  <StackPanel>\n" +
            "    <Button Content=\"Save\" />\n" +
            "    <controls:Badge />\n" +
            "  </StackPanel>\n" +
            "</UserControl>\n",
            result);
    }

    [Fact]
    public void DeclarationsWrittenOnOneLineAreJoinedOnIt()
    {
        var document = XamlDocument.Parse(
            $"<UserControl xmlns=\"{Avalonia}\" xmlns:x=\"{XamlNamespaces.Xaml}\" Width=\"300\"><Border /></UserControl>");

        XamlDocumentEditor editor = document.Edit();

        editor.Qualify(Element(document, "Border"), Controls, "Badge");

        Assert.Equal(
            $"<UserControl xmlns=\"{Avalonia}\" xmlns:x=\"{XamlNamespaces.Xaml}\" xmlns:controls=\"{Controls}\" Width=\"300\"><Border /></UserControl>",
            editor.Apply().GetText());
    }

    [Fact]
    public void APrefixTheDocumentUsesForSomethingElseGetsANumber()
    {
        XamlDocument document = XamlDocument.Parse(
            Form.Replace("x:Class", "xmlns:controls=\"using:Other.Controls\" x:Class", StringComparison.Ordinal));

        XamlDocumentEditor editor = document.Edit();

        XamlQualifiedName name = editor.Qualify(Element(document, "StackPanel"), Controls, "Badge", "controls");

        Assert.Equal("controls1:Badge", name.ToString());
        Assert.Contains($"xmlns:controls1=\"{Controls}\"", editor.Apply().GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void APrefixWrittenWithoutADeclarationIsNotTakenEither()
    {
        // The document is broken, and declaring 'foo' would quietly change what its <foo:Thing>
        // means rather than say anything about the name being asked for.
        var document = XamlDocument.Parse($"<StackPanel xmlns=\"{Avalonia}\"><foo:Thing /></StackPanel>");
        XamlDocumentEditor editor = document.Edit();

        Assert.Equal("foo1:Gauge", editor.Qualify(document.Root!, "using:Foo", "Gauge", "foo").ToString());
    }

    [Fact]
    public void TwoNamesInOneNamespaceDeclareItOnce()
    {
        var document = XamlDocument.Parse(Form);
        XamlDocumentEditor editor = document.Edit();
        XamlElement panel = Element(document, "StackPanel");

        XamlQualifiedName first = editor.Qualify(panel, Controls, "Badge");
        XamlQualifiedName second = editor.Qualify(Element(document, "Button"), Controls, "Gauge");

        Assert.Equal(first.Prefix, second.Prefix);

        string result = editor.Apply().GetText();

        Assert.Equal(1, Occurrences(result, "xmlns:controls="));
    }

    [Fact]
    public void APrefixIsMadeUpFromTheLastPartOfAClrNamespace()
    {
        var document = XamlDocument.Parse(Form);
        XamlDocumentEditor editor = document.Edit();

        XamlQualifiedName name = editor.Qualify(
            Element(document, "StackPanel"), "clr-namespace:App.Shared.Widgets;assembly=App.Shared", "Gauge");

        Assert.Equal("widgets:Gauge", name.ToString());
    }

    [Fact]
    public void AnAttributeIsNeverWrittenInTheDefaultNamespace()
    {
        // Unprefixed, an attribute is in no namespace at all — so a namespace in scope only as the
        // default has to be declared again under a prefix to name an attribute in it.
        var document = XamlDocument.Parse(Form);
        XamlDocumentEditor editor = document.Edit();

        XamlQualifiedName name = editor.QualifyAttribute(Element(document, "Button"), Avalonia, "Tip", "av");

        Assert.Equal("av:Tip", name.ToString());

        string result = editor.Apply().GetText();

        Assert.Contains($"xmlns:av=\"{Avalonia}\"", result, StringComparison.Ordinal);
        Assert.Contains($"<UserControl xmlns=\"{Avalonia}\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDesignNamespaceIsDeclaredIgnorable()
    {
        var document = XamlDocument.Parse(Form);
        XamlDocumentEditor editor = document.Edit();
        XamlElement root = document.Root!;

        XamlQualifiedName width = editor.QualifyAttribute(root, XamlNamespaces.Design, "DesignWidth");

        XamlDocument result = editor.SetAttribute(root, width, "400").Apply();

        Assert.Equal(
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "             xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\"\n" +
            "             xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"\n" +
            "             mc:Ignorable=\"d\"\n" +
            "             x:Class=\"App.Views.Form\"\n" +
            "             d:DesignWidth=\"400\">\n" +
            "  <StackPanel>\n" +
            "    <Button Content=\"Save\" />\n" +
            "  </StackPanel>\n" +
            "</UserControl>\n",
            result.GetText());

        // And it is what it looks like: a design-time attribute, found by namespace.
        Assert.Equal("400", result.Root!.GetDesignTimeAttribute("DesignWidth"));
        Assert.True(result.IsWellFormed);
    }

    [Fact]
    public void AnExistingIgnorableListIsAddedToAndNothingElseOfItChanges()
    {
        var document = XamlDocument.Parse(
            $"<UserControl xmlns=\"{Avalonia}\" xmlns:d=\"{XamlNamespaces.Design}\"\n" +
            $"             xmlns:mc=\"{XamlNamespaces.MarkupCompatibility}\" mc:Ignorable='d'>\n" +
            "</UserControl>");

        XamlDocumentEditor editor = document.Edit().EnsureIgnorable("urn:sample", "s");

        // The quote the list was written with stays, and so does everything it already said. The
        // declaration goes on a line of its own, because the last two declarations are on theirs.
        Assert.Equal(
            $"<UserControl xmlns=\"{Avalonia}\" xmlns:d=\"{XamlNamespaces.Design}\"\n" +
            $"             xmlns:mc=\"{XamlNamespaces.MarkupCompatibility}\"\n" +
            "             xmlns:s=\"urn:sample\" mc:Ignorable='d s'>\n" +
            "</UserControl>",
            editor.Apply().GetText());
    }

    [Fact]
    public void TwoNamespacesMadeIgnorableInOneEditorShareOneValue()
    {
        var document = XamlDocument.Parse(
            $"<UserControl xmlns=\"{Avalonia}\" xmlns:d=\"{XamlNamespaces.Design}\" " +
            $"xmlns:mc=\"{XamlNamespaces.MarkupCompatibility}\" mc:Ignorable=\"d\" />");

        XamlDocumentEditor editor = document.Edit()
            .EnsureIgnorable("urn:a", "a")
            .EnsureIgnorable("urn:b", "b");

        // One change for the declarations and one for the list: two calls did not record two
        // rewrites of the same value, which would have overlapped.
        Assert.Equal(2, editor.GetTextChanges().Length);
        Assert.Contains("mc:Ignorable=\"d a b\"", editor.Apply().GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ANamespaceAlreadyListedLeavesTheDocumentAlone()
    {
        var document = XamlDocument.Parse(
            $"<UserControl xmlns=\"{Avalonia}\" xmlns:d=\"{XamlNamespaces.Design}\" " +
            $"xmlns:mc=\"{XamlNamespaces.MarkupCompatibility}\" mc:Ignorable=\"d\" />");

        Assert.False(document.Edit().EnsureIgnorable(XamlNamespaces.Design).HasChanges);
    }

    [Fact]
    public void AnIgnorableListWrittenWithoutItsNamespaceIsCompletedRatherThanDuplicated()
    {
        // mc:Ignorable with no mc declared: the declaration this records makes that attribute the
        // list, and a second mc:Ignorable beside it would not parse.
        var document = XamlDocument.Parse(
            $"<UserControl xmlns=\"{Avalonia}\" xmlns:d=\"{XamlNamespaces.Design}\" mc:Ignorable=\"d\" />");

        string result = document.Edit().EnsureIgnorable("urn:a", "a").Apply().GetText();

        Assert.Equal(1, Occurrences(result, "Ignorable="));
        Assert.Contains("mc:Ignorable=\"d a\"", result, StringComparison.Ordinal);
        Assert.Contains($"xmlns:mc=\"{XamlNamespaces.MarkupCompatibility}\"", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASelfClosingRootCanBeOpenedAndGiveANamespaceInOneEdit(bool declareFirst)
    {
        // The declaration goes after the root's last declaration, which is exactly where opening a
        // self-closing root begins its replacement — in whichever order the two were recorded.
        var document = XamlDocument.Parse($"<Border xmlns=\"{Avalonia}\"/>");
        XamlDocumentEditor editor = document.Edit();
        XamlElement root = document.Root!;

        if (declareFirst)
        {
            XamlQualifiedName badge = editor.Qualify(root, Controls, "Badge");

            editor.InsertElement(root, 0, $"<{badge} />");
        }
        else
        {
            editor.InsertElement(root, 0, "<controls:Badge />");
            editor.Qualify(root, Controls, "Badge");
        }

        Assert.Equal(
            $"<Border xmlns=\"{Avalonia}\" xmlns:controls=\"{Controls}\"><controls:Badge /></Border>",
            editor.Apply().GetText());
    }

    [Fact]
    public void AnElementOfAnotherDocumentIsRefused()
    {
        var document = XamlDocument.Parse(Form);
        var other = XamlDocument.Parse(Form);

        Assert.Throws<InvalidOperationException>(
            () => document.Edit().Qualify(Element(other, "StackPanel"), Controls, "Badge"));
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;

        for (int index = text.IndexOf(value, StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
