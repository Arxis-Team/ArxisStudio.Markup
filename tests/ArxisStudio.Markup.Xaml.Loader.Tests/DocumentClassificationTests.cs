using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Styling;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// What a document is: a window, a user control, a templated control's look, a set of styles —
/// decided by types where the environment has them and by names where it does not.
/// </summary>
/// <remarks>
/// Plain facts, not <c>[AvaloniaFact]</c>: classifying resolves metadata and creates nothing, so
/// it needs no Avalonia thread, and every test here proves that by not having one.
/// </remarks>
public sealed class DocumentClassificationTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string Controls = "using:ArxisStudio.Markup.Xaml.Loader.TestControls";

    private static readonly Uri DocumentUri = new("file:///Project/Views/Document.axaml");

    private static readonly Assembly TestControls = typeof(CustomBadge).Assembly;

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault(assemblies: [TestControls]);

    private static XamlDocument Parse(string text) =>
        XamlDocument.Parse(text, new XamlParseOptions { DocumentUri = DocumentUri });

    private static ValueTask<XamlDocumentClassification> ClassifyAsync(string text) =>
        XamlDocumentClassifier.ClassifyAsync(Parse(text), Environment(), TestContext.Current.CancellationToken);

    private static string Look(string root, string declaration, string controls = Controls) =>
        $"<{root} xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\" xmlns:controls=\"{controls}\">\n"
        + declaration
        + $"\n</{root}>";

    [Fact]
    public async Task TheTemplatedControlFileIsATemplatedControl()
    {
        // The file Avalonia's "Templated Control" template creates, named for a control the
        // environment has. Its root is a set of styles; what it is for is the template it sets.
        XamlDocumentClassification classification = await ClassifyAsync(
            "<Styles xmlns=\"https://github.com/avaloniaui\"\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            $"        xmlns:controls=\"{Controls}\">\n" +
            "  <Design.PreviewWith>\n" +
            "    <controls:CustomBadge />\n" +
            "  </Design.PreviewWith>\n" +
            "\n" +
            "  <Style Selector=\"controls|CustomBadge\">\n" +
            "    <!-- Set Defaults -->\n" +
            "    <Setter Property=\"Template\">\n" +
            "      <ControlTemplate>\n" +
            "        <TextBlock Text=\"Templated Control\" />\n" +
            "      </ControlTemplate>\n" +
            "    </Setter>\n" +
            "  </Style>\n" +
            "</Styles>");

        Assert.Equal(XamlDocumentKind.TemplatedControl, classification.Kind);
        Assert.True(classification.IsResolved);
        Assert.Empty(classification.Diagnostics);
        Assert.Same(typeof(Styles), classification.RootType);
        Assert.False(classification.IsCustomRoot);

        XamlTemplatedType templated = Assert.Single(classification.TemplatedTypes);

        Assert.Same(typeof(CustomBadge), templated.Type);
        Assert.Equal("controls|CustomBadge", templated.Declaration.Selector);
        Assert.Equal("controls:CustomBadge", classification.Preview?.Name.ToString());
    }

    [Fact]
    public async Task AControlThemeOfTheControlIsATemplatedControlToo()
    {
        XamlDocumentClassification classification = await ClassifyAsync(Look(
            "ResourceDictionary",
            "  <Design.PreviewWith><StackPanel><controls:CustomBadge /></StackPanel></Design.PreviewWith>\n" +
            "  <ControlTheme x:Key=\"{x:Type controls:CustomBadge}\" TargetType=\"controls:CustomBadge\">\n" +
            "    <Setter Property=\"Template\">\n" +
            "      <ControlTemplate><TextBlock Text=\"Templated Control\" /></ControlTemplate>\n" +
            "    </Setter>\n" +
            "    <Style Selector=\"^:pointerover\"><Setter Property=\"Opacity\" Value=\"0.8\" /></Style>\n" +
            "  </ControlTheme>"));

        Assert.Equal(XamlDocumentKind.TemplatedControl, classification.Kind);
        Assert.True(classification.IsResolved);
        Assert.Same(typeof(ResourceDictionary), classification.RootType);
        Assert.Same(typeof(CustomBadge), Assert.Single(classification.TemplatedTypes).Type);
        Assert.Equal("StackPanel", classification.Preview?.Name.LocalName);
    }

    [Theory]
    [InlineData("Template")]
    [InlineData(" Template ")]
    [InlineData("TemplatedControl.Template")]
    [InlineData("(TemplatedControl.Template)")]
    public async Task ATemplateSetterIsRecognisedHoweverItIsWritten(string property)
    {
        XamlDocumentClassification classification = await ClassifyAsync(Look(
            "Styles",
            $"  <Style Selector=\"controls|CustomBadge\"><Setter Property=\"{property}\" Value=\"{{x:Null}}\" /></Style>"));

        Assert.Equal(XamlDocumentKind.TemplatedControl, classification.Kind);
    }

    [Theory]
    [InlineData("Styles", "  <Style Selector=\"controls|CustomBadge\"><Setter Property=\"Background\" Value=\"Red\" /></Style>", XamlDocumentKind.Styles)]
    [InlineData("Styles", "  <Style Selector=\"Button\"><Setter Property=\"Template\"><ControlTemplate><Border /></ControlTemplate></Setter></Style>", XamlDocumentKind.Styles)]
    [InlineData("ResourceDictionary", "  <ControlTheme x:Key=\"{x:Type Button}\" TargetType=\"Button\"><Setter Property=\"Template\"><ControlTemplate><Border /></ControlTemplate></Setter></ControlTheme>", XamlDocumentKind.ResourceDictionary)]
    [InlineData("Styles", "  <Style Selector=\"controls|SlotHost\"><Setter Property=\"Template\" Value=\"{x:Null}\" /></Style>", XamlDocumentKind.Styles)]
    [InlineData("Styles", "  <Style Selector=\"controls|CustomBadge /template/ Border\"><Setter Property=\"Template\" Value=\"{x:Null}\" /></Style>", XamlDocumentKind.Styles)]
    public async Task StylesThatDoNotTemplateAControlOfTheirOwnStayStyles(string root, string declaration, XamlDocumentKind kind)
    {
        // Restyling a control of one's own, re-templating Avalonia's Button, and "Template" set on
        // a control that has none are themes and mistakes, not a templated control's look.
        XamlDocumentClassification classification = await ClassifyAsync(Look(root, declaration));

        Assert.Equal(kind, classification.Kind);
        Assert.Empty(classification.TemplatedTypes);
        Assert.True(classification.IsResolved);
        Assert.Empty(classification.Diagnostics);
    }

    [Fact]
    public async Task ATemplatedControlNobodySuppliedIsJudgedByItsNames()
    {
        // The project has not been built, so the control is not in the environment. The names
        // still say what the file is; the result says it could not check.
        XamlDocument document = Parse(Look(
            "Styles",
            "  <Style Selector=\"controls|TemplatedControl1\">\n" +
            "    <Setter Property=\"Template\"><ControlTemplate><TextBlock /></ControlTemplate></Setter>\n" +
            "  </Style>",
            controls: "using:ProjectSystem.Ide.Views"));

        XamlDocumentClassification classification = await XamlDocumentClassifier.ClassifyAsync(
            document, Environment(), TestContext.Current.CancellationToken);

        Assert.Equal(XamlDocumentKind.TemplatedControl, classification.Kind);
        Assert.False(classification.IsResolved);

        XamlTemplatedType templated = Assert.Single(classification.TemplatedTypes);
        MarkupDiagnostic diagnostic = Assert.Single(classification.Diagnostics);

        Assert.False(templated.IsResolved);
        Assert.Equal("TemplatedControl1", templated.Reference.Name.LocalName);
        Assert.Equal(XamlLoaderDiagnosticCodes.UnresolvedType, diagnostic.Code);
        Assert.Equal(MarkupDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(DocumentUri, diagnostic.DocumentUri);
        Assert.Equal("controls|TemplatedControl1", document.SourceText.GetText(diagnostic.Span!.Value));
    }

    [Fact]
    public async Task AThemeBasedOnATemplateInTheSameDocumentTemplatesItsControl()
    {
        // How a theme library writes it: the template once, in a base theme for Avalonia's own
        // control, and a theme per control of its own that only says what it is based on. A key
        // written as {x:Type} is the type it names, whatever prefix spells it.
        XamlDocumentClassification classification = await ClassifyAsync(Look(
            "ResourceDictionary",
            "  <ControlTheme x:Key=\"BadgeBase\" TargetType=\"ContentControl\">\n" +
            "    <Setter Property=\"Template\"><ControlTemplate><ContentPresenter /></ControlTemplate></Setter>\n" +
            "  </ControlTheme>\n" +
            "  <ControlTheme x:Key=\"{x:Type controls:CustomBadge}\" TargetType=\"controls:CustomBadge\"\n" +
            "                BasedOn=\"{StaticResource BadgeBase}\" />\n" +
            "  <ControlTheme x:Key=\"CompactBadge\" TargetType=\"controls:CustomerView\"\n" +
            $"                xmlns:alias=\"{Controls}\" BasedOn=\"{{StaticResource {{x:Type alias:CustomBadge}}}}\" />"));

        Assert.Equal(XamlDocumentKind.TemplatedControl, classification.Kind);
        Assert.Equal(
            [typeof(CustomBadge), typeof(CustomerView)],
            classification.TemplatedTypes.Select(static templated => templated.Type));
        Assert.Equal(
            "{x:Type controls:CustomBadge}",
            classification.TemplatedTypes[0].Declaration.Element.GetDirective(XamlDirectives.Key));
    }

    [Theory]
    [InlineData(
        "  <ControlTheme x:Key=\"A\" TargetType=\"controls:CustomBadge\" BasedOn=\"{StaticResource B}\" />\n" +
        "  <ControlTheme x:Key=\"B\" TargetType=\"controls:CustomBadge\" BasedOn=\"{StaticResource A}\" />")]
    [InlineData(
        "  <ControlTheme x:Key=\"{x:Type controls:CustomBadge}\" TargetType=\"controls:CustomBadge\"\n" +
        "                BasedOn=\"{StaticResource {x:Type ContentControl}}\" />")]
    [InlineData(
        "  <ControlTheme x:Key=\"Base\" TargetType=\"ContentControl\">\n" +
        "    <Setter Property=\"Template\"><ControlTemplate><ContentPresenter /></ControlTemplate></Setter>\n" +
        "  </ControlTheme>\n" +
        "  <ControlTheme x:Key=\"Badge\" TargetType=\"controls:CustomBadge\" BasedOn=\"{DynamicResource Base}\" />")]
    public async Task ABaseThatGivesNoTemplateHereTemplatesNothing(string themes)
    {
        // A cycle ends; a base theme from another file supplies a template this one does not; and
        // BasedOn takes a static resource, so nothing else is followed.
        XamlDocumentClassification classification = await ClassifyAsync(Look("ResourceDictionary", themes));

        Assert.Equal(XamlDocumentKind.ResourceDictionary, classification.Kind);
        Assert.Empty(classification.TemplatedTypes);
    }

    [Fact]
    public async Task ATemplateForAnUndeclaredPrefixIsReportedAndNotCounted()
    {
        // A prefix nothing declares names no namespace, so not one outside Avalonia's either.
        XamlDocument document = Parse(Look(
            "Styles",
            "  <Style Selector=\"nowhere|Badge\"><Setter Property=\"Template\" Value=\"{x:Null}\" /></Style>"));

        XamlDocumentClassification classification = await XamlDocumentClassifier.ClassifyAsync(
            document, Environment(), TestContext.Current.CancellationToken);

        MarkupDiagnostic diagnostic = Assert.Single(classification.Diagnostics);

        Assert.Equal(XamlDocumentKind.Styles, classification.Kind);
        Assert.Empty(classification.TemplatedTypes);
        Assert.Equal(XamlLoaderDiagnosticCodes.UndeclaredPrefix, diagnostic.Code);
        Assert.Equal(MarkupDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("nowhere|Badge", document.SourceText.GetText(diagnostic.Span!.Value));
    }

    [Fact]
    public async Task AControlTemplatedTwiceIsListedOnce()
    {
        XamlDocumentClassification classification = await ClassifyAsync(Look(
            "Styles",
            "  <Style Selector=\"controls|CustomBadge\"><Setter Property=\"Template\" Value=\"{x:Null}\" /></Style>\n" +
            "  <Style Selector=\"controls|CustomBadge.compact\"><Setter Property=\"Template\" Value=\"{x:Null}\" /></Style>"));

        Assert.Same(typeof(CustomBadge), Assert.Single(classification.TemplatedTypes).Type);
    }

    [Fact]
    public async Task ATemplateSetInsideThePreviewDoesNotCount()
    {
        // The preview is a design-time stand-in. A template written there is not what the
        // document declares.
        XamlDocumentClassification classification = await ClassifyAsync(Look(
            "Styles",
            "  <Design.PreviewWith>\n" +
            "    <Border>\n" +
            "      <Border.Styles>\n" +
            "        <Style Selector=\"controls|CustomBadge\">\n" +
            "          <Setter Property=\"Template\"><ControlTemplate><TextBlock /></ControlTemplate></Setter>\n" +
            "        </Style>\n" +
            "      </Border.Styles>\n" +
            "      <controls:CustomBadge />\n" +
            "    </Border>\n" +
            "  </Design.PreviewWith>\n" +
            "  <Style Selector=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"14\" /></Style>"));

        Assert.Equal(XamlDocumentKind.Styles, classification.Kind);
        Assert.Empty(classification.TemplatedTypes);
        Assert.Equal("Border", classification.Preview?.Name.LocalName);
    }

    [Theory]
    [InlineData("<Window xmlns=\"https://github.com/avaloniaui\" />", XamlDocumentKind.Window, typeof(Window), false)]
    [InlineData("<c:ToolWindow xmlns=\"https://github.com/avaloniaui\" xmlns:c=\"using:ArxisStudio.Markup.Xaml.Loader.TestControls\" />", XamlDocumentKind.Window, typeof(ToolWindow), true)]
    [InlineData("<UserControl xmlns=\"https://github.com/avaloniaui\" />", XamlDocumentKind.UserControl, typeof(UserControl), false)]
    [InlineData("<c:CustomerView xmlns:c=\"https://arxis.studio/test-controls\" />", XamlDocumentKind.UserControl, typeof(CustomerView), true)]
    [InlineData("<Border xmlns=\"https://github.com/avaloniaui\" />", XamlDocumentKind.Control, typeof(Border), false)]
    [InlineData("<StackPanel xmlns=\"https://github.com/avaloniaui\" />", XamlDocumentKind.Control, typeof(StackPanel), false)]
    [InlineData("<c:CustomBadge xmlns:c=\"using:ArxisStudio.Markup.Xaml.Loader.TestControls\" />", XamlDocumentKind.Control, typeof(CustomBadge), true)]
    [InlineData("<c:SlotHost xmlns:c=\"using:ArxisStudio.Markup.Xaml.Loader.TestControls\" />", XamlDocumentKind.Control, typeof(SlotHost), true)]
    [InlineData("<Application xmlns=\"https://github.com/avaloniaui\" />", XamlDocumentKind.Application, typeof(global::Avalonia.Application), false)]
    [InlineData("<Styles xmlns=\"https://github.com/avaloniaui\" />", XamlDocumentKind.Styles, typeof(Styles), false)]
    [InlineData("<Style xmlns=\"https://github.com/avaloniaui\" Selector=\"Button\" />", XamlDocumentKind.Styles, typeof(Style), false)]
    [InlineData("<ControlTheme xmlns=\"https://github.com/avaloniaui\" TargetType=\"Button\" />", XamlDocumentKind.Styles, typeof(ControlTheme), false)]
    [InlineData("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\" />", XamlDocumentKind.ResourceDictionary, typeof(ResourceDictionary), false)]
    [InlineData("<SolidColorBrush xmlns=\"https://github.com/avaloniaui\" Color=\"Red\" />", XamlDocumentKind.Other, typeof(global::Avalonia.Media.SolidColorBrush), false)]
    public async Task TheRootsTypeDecidesTheKind(string text, XamlDocumentKind kind, Type rootType, bool isCustomRoot)
    {
        XamlDocumentClassification classification = await ClassifyAsync(text);

        Assert.Equal(kind, classification.Kind);
        Assert.Same(rootType, classification.RootType);
        Assert.Equal(isCustomRoot, classification.IsCustomRoot);
        Assert.True(classification.IsResolved);
        Assert.Empty(classification.Diagnostics);
        Assert.Empty(classification.TemplatedTypes);
    }

    [Fact]
    public async Task TheRootTypeIsTheElementsAndNotTheClass()
    {
        XamlDocumentClassification classification = await ClassifyAsync(
            $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\" " +
            $"x:Class=\"{typeof(CustomerView).FullName}\" />");

        Assert.Equal(XamlDocumentKind.UserControl, classification.Kind);
        Assert.Same(typeof(UserControl), classification.RootType);
        Assert.False(classification.IsCustomRoot);
    }

    [Fact]
    public async Task ARootNobodySuppliedIsUnknown()
    {
        // A root of somebody's own type, which the environment does not have, is not guessed at:
        // its name says nothing reliable about what it derives from.
        XamlDocument document = Parse($"<c:ToolWindowBase xmlns=\"{AvaloniaNamespace}\" xmlns:c=\"using:Nowhere.Views\" />");

        XamlDocumentClassification classification = await XamlDocumentClassifier.ClassifyAsync(
            document, Environment(), TestContext.Current.CancellationToken);

        MarkupDiagnostic diagnostic = Assert.Single(classification.Diagnostics);

        Assert.Equal(XamlDocumentKind.Unknown, classification.Kind);
        Assert.False(classification.IsResolved);
        Assert.Null(classification.RootType);
        Assert.True(classification.IsCustomRoot);
        Assert.Equal(XamlLoaderDiagnosticCodes.UnresolvedType, diagnostic.Code);
        Assert.Equal(MarkupDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(DocumentUri, diagnostic.DocumentUri);
        Assert.Equal(document.Root!.NameSpan, diagnostic.Span);
    }

    [Fact]
    public async Task ARootWithAnUndeclaredPrefixIsUnknown()
    {
        XamlDocument document = Parse($"<v:Window xmlns=\"{AvaloniaNamespace}\" />");

        XamlDocumentClassification classification = await XamlDocumentClassifier.ClassifyAsync(
            document, Environment(), TestContext.Current.CancellationToken);

        MarkupDiagnostic diagnostic = Assert.Single(classification.Diagnostics);

        Assert.Equal(XamlDocumentKind.Unknown, classification.Kind);
        Assert.False(classification.IsCustomRoot);
        Assert.Equal(XamlLoaderDiagnosticCodes.UndeclaredPrefix, diagnostic.Code);
        Assert.Equal(document.Root!.NameSpan, diagnostic.Span);
    }

    [Fact]
    public async Task ADocumentWithoutARootIsUnknownAndSaysNothingMore()
    {
        // The missing root is the document's own syntax diagnostic; repeating it here adds nothing.
        XamlDocumentClassification classification = await ClassifyAsync("<!-- nothing here -->");

        Assert.Equal(XamlDocumentKind.Unknown, classification.Kind);
        Assert.Null(classification.Root);
        Assert.Empty(classification.Diagnostics);
    }

    [Fact]
    public async Task ClassifyingRejectsNullArguments()
    {
        XamlDocument document = Parse($"<Border xmlns=\"{AvaloniaNamespace}\" />");

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await XamlDocumentClassifier.ClassifyAsync(null!, Environment(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await XamlDocumentClassifier.ClassifyAsync(document, null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClassifyingHonoursCancellationEvenWhenEveryTypeIsCached()
    {
        // Once the resolver has answered, nothing it does would notice a cancelled token, so the
        // classifier has to look for itself.
        XamlDocument document = Parse($"<Border xmlns=\"{AvaloniaNamespace}\" />");
        XamlLoadEnvironment environment = Environment();

        await XamlDocumentClassifier.ClassifyAsync(document, environment, TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await XamlDocumentClassifier.ClassifyAsync(document, environment, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task TemplatedTypesAreListedInTheOrderTheDocumentTemplatesThem()
    {
        XamlDocumentClassification classification = await ClassifyAsync(Look(
            "Styles",
            "  <Style Selector=\"controls|SheetHost, controls|CustomBadge\"><Setter Property=\"Template\" Value=\"{x:Null}\" /></Style>\n" +
            "  <Style Selector=\"controls|CustomerView\"><Setter Property=\"Template\" Value=\"{x:Null}\" /></Style>"));

        // SheetHost is a control without a template and is not listed; the other two are.
        Assert.Equal(
            [typeof(CustomBadge), typeof(CustomerView)],
            classification.TemplatedTypes.Select(static templated => templated.Type));
    }
}
