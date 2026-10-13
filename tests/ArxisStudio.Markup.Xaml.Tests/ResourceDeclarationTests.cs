using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// The keyed resources a document writes, read from the syntax alone, and the ones an element reaches by key.
/// </summary>
public sealed class ResourceDeclarationTests
{
    private const string Namespaces =
        "xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    [Fact]
    public void EntriesAreFoundInEveryKindOfDictionaryWithTheirOwnerAndTheme()
    {
        XamlDocument document = XamlDocument.Parse($$"""
            <Window {{Namespaces}}>
              <Window.Resources>
                <ResourceDictionary>
                  <ResourceDictionary.MergedDictionaries>
                    <ResourceDictionary>
                      <SolidColorBrush x:Key="Merged" Color="Red" />
                    </ResourceDictionary>
                    <ResourceInclude Source="/Colors.axaml" />
                  </ResourceDictionary.MergedDictionaries>
                  <ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="Dark">
                      <SolidColorBrush x:Key="Themed" Color="Black" />
                    </ResourceDictionary>
                  </ResourceDictionary.ThemeDictionaries>
                  <SolidColorBrush x:Key="Accent" Color="Blue" />
                </ResourceDictionary>
              </Window.Resources>
              <StackPanel>
                <StackPanel.Resources>
                  <x:Double x:Key="Gap">8</x:Double>
                  <ControlTheme x:Key="{x:Type Button}" TargetType="Button" />
                </StackPanel.Resources>
                <Button Content="{StaticResource Accent}" />
              </StackPanel>
            </Window>
            """);

        ImmutableArray<XamlResourceDeclaration> declarations = document.GetResourceDeclarations();

        Assert.Equal(["Merged", "Themed", "Accent", "Gap", "{x:Type Button}"], declarations.Select(static entry => entry.Key));
        Assert.Equal([null, "Dark", null, null, null], declarations.Select(static entry => entry.ThemeVariant));
        Assert.Equal(
            ["Window", "Window", "Window", "StackPanel", "StackPanel"],
            declarations.Select(static entry => entry.Owner.Name.LocalName));
        Assert.Equal("Accent", document.SourceText.GetText(declarations[2].KeySpan));
        Assert.Equal("SolidColorBrush", declarations[2].Element.Name.LocalName);
        Assert.True(declarations[3].IsTextKey);
        Assert.False(declarations[4].IsTextKey);

        // The button reaches its panel's entries first, then the window's — the merged, the themed and its own.
        XamlElement button = document.DescendantElements().Single(static element => element.Name.LocalName == "Button");

        Assert.Equal(
            ["Gap", "{x:Type Button}", "Merged", "Themed", "Accent"],
            XamlResourceDeclarations.InScope(button).Select(static entry => entry.Key));
    }

    [Fact]
    public void ADictionaryDocumentOwnsItsEntriesAndAKeyOutsideADictionaryIsNoEntry()
    {
        XamlDocument dictionary = XamlDocument.Parse($$"""
            <ResourceDictionary {{Namespaces}}>
              <SolidColorBrush x:Key="Accent" Color="Blue" />
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <SolidColorBrush x:Key="Text" Color="Black" />
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);

        ImmutableArray<XamlResourceDeclaration> entries = dictionary.GetResourceDeclarations();

        Assert.Equal([("Accent", (string?)null), ("Text", "Light")], entries.Select(static entry => (entry.Key, entry.ThemeVariant)));
        Assert.All(entries, entry => Assert.Same(dictionary.Root, entry.Owner));

        // A key no dictionary holds names nothing a lookup could find: a template in a template collection, a stray.
        XamlDocument stray = XamlDocument.Parse($$"""
            <UserControl {{Namespaces}}>
              <UserControl.DataTemplates>
                <DataTemplate x:Key="Card" />
              </UserControl.DataTemplates>
              <Border x:Key="Lost" />
            </UserControl>
            """);

        Assert.Empty(stray.GetResourceDeclarations());
    }

    [Fact]
    public void AnElementReachesItsOwnAndItsAncestorsResourcesInAnyOrderButNotASiblings()
    {
        XamlDocument document = XamlDocument.Parse($$"""
            <UserControl {{Namespaces}}>
              <UserControl.Styles>
                <Style Selector="Border">
                  <Style.Resources>
                    <SolidColorBrush x:Key="FromStyle" Color="Red" />
                  </Style.Resources>
                </Style>
              </UserControl.Styles>
              <StackPanel>
                <Border>
                  <Border.Resources>
                    <SolidColorBrush x:Key="Sibling" Color="Red" />
                  </Border.Resources>
                </Border>
                <Border Background="{StaticResource Later}">
                  <Border.Resources>
                    <SolidColorBrush x:Key="Own" Color="Red" />
                  </Border.Resources>
                </Border>
                <StackPanel.Resources>
                  <SolidColorBrush x:Key="Later" Color="Red" />
                </StackPanel.Resources>
              </StackPanel>
              <UserControl.Resources>
                <SolidColorBrush x:Key="Root" Color="Red" />
              </UserControl.Resources>
            </UserControl>
            """);

        XamlElement used = XamlElementPath.Parse("/0/1").Resolve(document)!;

        Assert.Equal(["Own", "Later", "FromStyle", "Root"], XamlResourceDeclarations.InScope(used).Select(static entry => entry.Key));
    }

    [Fact]
    public void AnElementInAPropertyElementReachesTheResourcesOfTheElementThatWritesIt()
    {
        XamlDocument document = XamlDocument.Parse($$"""
            <Button {{Namespaces}}>
              <Button.Resources>
                <x:String x:Key="Hint">Сохранить</x:String>
              </Button.Resources>
              <Button.Flyout>
                <Flyout>
                  <TextBlock Text="{StaticResource Hint}" />
                </Flyout>
              </Button.Flyout>
            </Button>
            """);

        XamlElement text = document.DescendantElements().Single(static element => element.Name.LocalName == "TextBlock");

        Assert.Equal(["Hint"], XamlResourceDeclarations.InScope(text).Select(static entry => entry.Key));
    }
}
