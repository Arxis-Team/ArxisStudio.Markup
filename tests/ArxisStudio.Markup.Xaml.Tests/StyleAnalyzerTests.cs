using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// Style declarations read from the syntax alone: whose style each one is, which setters it
/// carries, and what it is nested in — with no Avalonia and no type resolved.
/// </summary>
public sealed class StyleAnalyzerTests
{
    private const string Avalonia = "https://github.com/avaloniaui";
    private const string Local = "using:App.Controls";

    private static XamlDocument Styles(string body) => XamlDocument.Parse(
        $"<Styles xmlns=\"{Avalonia}\" xmlns:x=\"{XamlNamespaces.Xaml}\" xmlns:local=\"{Local}\">\n"
        + body
        + "\n</Styles>");

    private static XamlDocument Resources(string body) => XamlDocument.Parse(
        $"<ResourceDictionary xmlns=\"{Avalonia}\" xmlns:x=\"{XamlNamespaces.Xaml}\" xmlns:local=\"{Local}\">\n"
        + body
        + "\n</ResourceDictionary>");

    private static string TextOf(XamlDocument document, XamlTypeReference target) =>
        document.SourceText.GetText(target.Span);

    [Fact]
    public void TheTemplatedControlFileIsReadAsAStyleOfItsControl()
    {
        // The file a templated control is created with: its look, written as a style for it.
        XamlDocument document = XamlDocument.Parse(
            "<Styles xmlns=\"https://github.com/avaloniaui\"\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "        xmlns:controls=\"using:ProjectSystem.Ide.Views\">\n" +
            "  <Design.PreviewWith>\n" +
            "    <controls:TemplatedControl1 />\n" +
            "  </Design.PreviewWith>\n" +
            "\n" +
            "  <Style Selector=\"controls|TemplatedControl1\">\n" +
            "    <!-- Set Defaults -->\n" +
            "    <Setter Property=\"Template\">\n" +
            "      <ControlTemplate>\n" +
            "        <TextBlock Text=\"Templated Control\" />\n" +
            "      </ControlTemplate>\n" +
            "    </Setter>\n" +
            "  </Style>\n" +
            "</Styles>");

        XamlStyleDeclaration style = Assert.Single(XamlStyleAnalyzer.Discover(document));
        XamlTypeReference target = Assert.Single(style.Targets);

        Assert.Equal(XamlStyleKind.Style, style.Kind);
        Assert.Null(style.Parent);
        Assert.Equal("controls|TemplatedControl1", style.Selector);
        Assert.Equal(new XamlQualifiedName("controls", "TemplatedControl1"), target.Name);
        Assert.Equal("using:ProjectSystem.Ide.Views", target.NamespaceUri);
        Assert.Equal("controls|TemplatedControl1", TextOf(document, target));
        Assert.Equal("Template", Assert.Single(style.Setters).Property);
    }

    [Theory]
    [InlineData("Button", "Button", "Button", false)]
    [InlineData("StackPanel > Button.primary:pointerover", "Button", "Button", false)]
    [InlineData("StackPanel Button#Ok", "Button", "Button", false)]
    [InlineData("local|Badge /template/ Border#PART_Root", "Border", "Border", false)]
    [InlineData("local|Badge/template/Border", "Border", "Border", false)]
    [InlineData("Border>local|Badge[IsEnabled=True]", "Badge", "local|Badge", true)]
    [InlineData(":is(Button).accent", "Button", "Button", false)]
    [InlineData("Window :is( local|Badge )", "Badge", "local|Badge", true)]
    [InlineData("Button:not(.primary, TextBlock)", "Button", "Button", false)]
    [InlineData("Panel > Button:nth-child(2n+1)", "Button", "Button", false)]
    [InlineData("ListBox ListBoxItem[Tag=a b] /template/ ContentPresenter", "ContentPresenter", "ContentPresenter", false)]
    public void TheTargetIsTheTypeOfTheLastStep(string selector, string localName, string written, bool isLocal)
    {
        XamlDocument document = Styles($"  <Style Selector=\"{selector}\" />");

        XamlTypeReference target = Assert.Single(Assert.Single(XamlStyleAnalyzer.Discover(document)).Targets);

        Assert.Equal(localName, target.Name.LocalName);
        Assert.Equal(written, TextOf(document, target));
        Assert.Equal(isLocal ? Local : Avalonia, target.NamespaceUri);
    }

    [Fact]
    public void EachAlternativeContributesItsTarget()
    {
        XamlDocument document = Styles("  <Style Selector=\"Button, local|Badge , StackPanel > :is(TextBlock)\" />");

        ImmutableArray<XamlTypeReference> targets = Assert.Single(XamlStyleAnalyzer.Discover(document)).Targets;

        Assert.Equal(["Button", "local|Badge", "TextBlock"], targets.Select(target => TextOf(document, target)));
        Assert.Equal([Avalonia, Local, Avalonia], targets.Select(static target => target.NamespaceUri));
    }

    [Theory]
    [InlineData(".highlight")]
    [InlineData(":pointerover")]
    [InlineData("#Ok")]
    [InlineData(":not(Button)")]
    [InlineData("Button &gt;")]
    [InlineData("local|")]
    [InlineData("((")]
    [InlineData("]]")]
    [InlineData("")]
    [InlineData("^")]
    public void ASelectorThatNamesNoTypeTargetsNothing(string selector)
    {
        // "^" with no parent to stand for is a selector Avalonia refuses; here it is simply one that
        // names nothing, like the rest. Reading never throws on what it cannot follow.
        XamlDocument document = Styles($"  <Style Selector=\"{selector}\" />");

        Assert.Empty(Assert.Single(XamlStyleAnalyzer.Discover(document)).Targets);
    }

    [Fact]
    public void ANestedStyleTakesItsParentsTargets()
    {
        XamlDocument document = Resources(
            "  <ControlTheme x:Key=\"{x:Type local:Badge}\" TargetType=\"local:Badge\">\n" +
            "    <Setter Property=\"Template\">\n" +
            "      <ControlTemplate><Border Name=\"PART_Root\" /></ControlTemplate>\n" +
            "    </Setter>\n" +
            "    <Style Selector=\"^:pointerover\">\n" +
            "      <Setter Property=\"Opacity\" Value=\"0.8\" />\n" +
            "    </Style>\n" +
            "    <Style Selector=\"^ /template/ Border#PART_Root\">\n" +
            "      <Setter Property=\"Background\" Value=\"Red\" />\n" +
            "    </Style>\n" +
            "  </ControlTheme>");

        ImmutableArray<XamlStyleDeclaration> declarations = XamlStyleAnalyzer.Discover(document);

        Assert.Equal(3, declarations.Length);

        XamlStyleDeclaration theme = declarations[0];
        XamlStyleDeclaration state = declarations[1];
        XamlStyleDeclaration part = declarations[2];

        Assert.Equal(XamlStyleKind.ControlTheme, theme.Kind);
        Assert.Null(theme.Selector);
        Assert.Equal("local:Badge", TextOf(document, Assert.Single(theme.Targets)));

        // The theme's setters are its own; its states carry theirs.
        Assert.Equal(["Template"], theme.Setters.Select(static setter => setter.Property));
        Assert.Equal(["Opacity"], state.Setters.Select(static setter => setter.Property));

        Assert.Same(theme, state.Parent);
        Assert.Same(theme.Targets[0], Assert.Single(state.Targets));

        Assert.Same(theme, part.Parent);
        Assert.Equal(Avalonia, Assert.Single(part.Targets).NamespaceUri);
        Assert.Equal("Border", Assert.Single(part.Targets).Name.LocalName);
    }

    [Theory]
    [InlineData("local:Badge", "local:Badge")]
    [InlineData(" local:Badge ", "local:Badge")]
    [InlineData("{x:Type local:Badge}", "local:Badge")]
    [InlineData("{x:Type TypeName=local:Badge}", "local:Badge")]
    [InlineData("Button", "Button")]
    [InlineData("{x:Type Button}", "Button")]
    public void AControlThemeTargetTypeIsReadAsANameOrAsXType(string targetType, string written)
    {
        XamlDocument document = Resources($"  <ControlTheme x:Key=\"Theme\" TargetType=\"{targetType}\" />");

        XamlTypeReference target = Assert.Single(Assert.Single(XamlStyleAnalyzer.Discover(document)).Targets);

        Assert.Equal(written, TextOf(document, target));
        Assert.Equal(XamlQualifiedName.Parse(written), target.Name);
        Assert.Equal(written.StartsWith("local:", StringComparison.Ordinal) ? Local : Avalonia, target.NamespaceUri);
    }

    [Theory]
    [InlineData("{StaticResource BadgeType}")]
    [InlineData("")]
    public void AControlThemeTargetTypeThatNamesNoTypeTargetsNothing(string targetType)
    {
        XamlDocument document = Resources($"  <ControlTheme x:Key=\"Theme\" TargetType=\"{targetType}\" />");

        Assert.Empty(Assert.Single(XamlStyleAnalyzer.Discover(document)).Targets);
    }

    [Theory]
    [InlineData("StackPanel &gt; local|Badge")]
    [InlineData("StackPanel&#32;&#x3E;&#x20;local|Badge")]
    [InlineData("Border.a&amp;b local|Badge")]
    public void EntityReferencesInASelectorKeepTheirPositions(string selector)
    {
        // The attribute's text is raw; the span of the type is where the type is written in it.
        XamlDocument document = Styles($"  <Style Selector=\"{selector}\" />");

        XamlTypeReference target = Assert.Single(Assert.Single(XamlStyleAnalyzer.Discover(document)).Targets);

        Assert.Equal("local|Badge", TextOf(document, target));
        Assert.Equal(Local, target.NamespaceUri);
    }

    [Fact]
    public void SettersAreReadFromTheStyleAndFromItsSettersMember()
    {
        XamlDocument document = Styles(
            "  <Style Selector=\"Button\">\n" +
            "    <Setter Property=\"Background\" Value=\"Red\" />\n" +
            "    <Style.Setters>\n" +
            "      <Setter Property=\"(Grid.Row)\" Value=\"1\" />\n" +
            "    </Style.Setters>\n" +
            "    <Setter Value=\"Orphan\" />\n" +
            "  </Style>");

        ImmutableArray<XamlStyleSetter> setters = Assert.Single(XamlStyleAnalyzer.Discover(document)).Setters;

        Assert.Equal(["Background", "(Grid.Row)", null], setters.Select(static setter => setter.Property));
        Assert.All(setters, static setter => Assert.Equal("Setter", setter.Element.Name.LocalName));
    }

    [Fact]
    public void DeclarationsAreFoundWhereverTheyAppear()
    {
        XamlDocument document = XamlDocument.Parse(
            $"<UserControl xmlns=\"{Avalonia}\" xmlns:x=\"{XamlNamespaces.Xaml}\" xmlns:local=\"{Local}\">\n" +
            "  <UserControl.Styles>\n" +
            "    <Style Selector=\"TextBlock.title\" />\n" +
            "  </UserControl.Styles>\n" +
            "  <UserControl.Resources>\n" +
            "    <ResourceDictionary>\n" +
            "      <ResourceDictionary.MergedDictionaries>\n" +
            "        <ResourceDictionary>\n" +
            "          <ControlTheme x:Key=\"Badge\" TargetType=\"local:Badge\" />\n" +
            "        </ResourceDictionary>\n" +
            "      </ResourceDictionary.MergedDictionaries>\n" +
            "      <ResourceDictionary.ThemeDictionaries>\n" +
            "        <ResourceDictionary x:Key=\"Dark\">\n" +
            "          <ControlTheme x:Key=\"DarkBadge\" TargetType=\"local:Badge\" />\n" +
            "        </ResourceDictionary>\n" +
            "      </ResourceDictionary.ThemeDictionaries>\n" +
            "    </ResourceDictionary>\n" +
            "  </UserControl.Resources>\n" +
            "  <Border>\n" +
            "    <Border.Styles>\n" +
            "      <Style Selector=\"Button\"><Style Selector=\"^:pressed\" /></Style>\n" +
            "    </Border.Styles>\n" +
            "  </Border>\n" +
            "</UserControl>");

        ImmutableArray<XamlStyleDeclaration> declarations = XamlStyleAnalyzer.Discover(document);

        Assert.Equal(
            [XamlStyleKind.Style, XamlStyleKind.ControlTheme, XamlStyleKind.ControlTheme, XamlStyleKind.Style, XamlStyleKind.Style],
            declarations.Select(static declaration => declaration.Kind));
        Assert.Equal(
            ["TextBlock", "Badge", "Badge", "Button", "Button"],
            declarations.Select(static declaration => Assert.Single(declaration.Targets).Name.LocalName));
        Assert.Same(declarations[3], declarations[4].Parent);
    }

    [Fact]
    public void NestingIsTheChildrenAndNotTheResources()
    {
        XamlDocument document = Styles(
            "  <Style Selector=\"local|Badge\">\n" +
            "    <Style.Resources>\n" +
            "      <ControlTheme x:Key=\"Inner\" TargetType=\"Button\">\n" +
            "        <Setter Property=\"Template\">\n" +
            "          <ControlTemplate>\n" +
            "            <Border>\n" +
            "              <Border.Styles><Style Selector=\"^\" /></Border.Styles>\n" +
            "            </Border>\n" +
            "          </ControlTemplate>\n" +
            "        </Setter>\n" +
            "      </ControlTheme>\n" +
            "    </Style.Resources>\n" +
            "    <Style.Children>\n" +
            "      <Style Selector=\"^:pointerover\" />\n" +
            "    </Style.Children>\n" +
            "  </Style>");

        ImmutableArray<XamlStyleDeclaration> declarations = XamlStyleAnalyzer.Discover(document);

        XamlStyleDeclaration outer = declarations[0];
        XamlStyleDeclaration resource = declarations[1];
        XamlStyleDeclaration insideTemplate = declarations[2];
        XamlStyleDeclaration child = declarations[3];

        // A resource of the style, and a style inside a template it sets, are not nested in it.
        Assert.Null(resource.Parent);
        Assert.Null(insideTemplate.Parent);
        Assert.Empty(insideTemplate.Targets);

        Assert.Same(outer, child.Parent);
        Assert.Equal("local|Badge", TextOf(document, Assert.Single(child.Targets)));
    }

    [Fact]
    public void AnUndeclaredPrefixLeavesTheNamespaceUnknown()
    {
        XamlDocument document = Styles("  <Style Selector=\"nowhere|Badge\" />");

        XamlTypeReference target = Assert.Single(Assert.Single(XamlStyleAnalyzer.Discover(document)).Targets);

        Assert.Equal(new XamlQualifiedName("nowhere", "Badge"), target.Name);
        Assert.Null(target.NamespaceUri);
    }

    [Fact]
    public void ASelectorWrittenAsAMarkupExtensionIsKeptButTargetsNothing()
    {
        XamlDocument document = Styles("  <Style Selector=\"{Binding Path}\" />");

        XamlStyleDeclaration style = Assert.Single(XamlStyleAnalyzer.Discover(document));

        Assert.Equal("{Binding Path}", style.Selector);
        Assert.Empty(style.Targets);
    }

    [Fact]
    public void PropertyElementsAndOtherElementsAreNotDeclarations()
    {
        XamlDocument document = Styles(
            "  <StyleInclude Source=\"avares://App/Styles.axaml\" />\n" +
            "  <Styles>\n" +
            "    <Styles.Resources><x:String x:Key=\"Style\">Style</x:String></Styles.Resources>\n" +
            "  </Styles>");

        Assert.Empty(XamlStyleAnalyzer.Discover(document));
    }

    [Fact]
    public void DiscoveryRejectsANullDocument()
    {
        Assert.Throws<ArgumentNullException>(() => XamlStyleAnalyzer.Discover(null!));
    }
}
