using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// Bringing a loaded tree in line with a document that changed under it, without compiling
/// anything and without recreating what did not have to be recreated.
/// </summary>
public sealed class UpdateTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string DesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";

    private static readonly Uri ViewUri = new("file:///Views/View.axaml");
    private static readonly Uri ColorsUri = new("file:///Themes/Colors.axaml");

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri });

    private static ValueTask<XamlLoadSession> Load(string xaml, XamlLoadMode mode = XamlLoadMode.Runtime) =>
        XamlLoadSession.CreateAsync(
            Parse(xaml),
            XamlLoadEnvironment.CreateDefault([typeof(CustomBadge).Assembly]),
            new XamlLoadOptions { Mode = mode },
            TestContext.Current.CancellationToken);

    private static ValueTask<XamlUpdateResult> Update(XamlLoadSession session, string xaml) =>
        session.ApplyDocumentUpdateAsync(Parse(xaml), TestContext.Current.CancellationToken);

    private static string View(string attributes) =>
        $"<Border xmlns=\"{AvaloniaNamespace}\"\n" +
        "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
        $"        xmlns:d=\"{DesignNamespace}\">\n" +
        $"  <TextBlock {attributes} />\n" +
        "</Border>";

    [AvaloniaFact]
    public async Task ALiteralPropertyIsSetOnTheObjectThatAlreadyExists()
    {
        await using XamlLoadSession session = await Load(View("Text=\"before\""));

        var border = session.GetRoot<Border>();
        var text = (TextBlock)border.Child!;

        XamlUpdateResult result = await Update(session, View("Text=\"after\""));

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.SetProperty, result.Strategy);

        // The same object, not a new one: a caller holding it, or a selection pointing at it,
        // survives the update.
        Assert.Same(text, border.Child);
        Assert.Equal("after", text.Text);
    }

    [AvaloniaFact]
    public async Task ThePropertyIsConvertedToTheTypeTheMemberHolds()
    {
        await using XamlLoadSession session = await Load(View("Width=\"10\""));

        XamlUpdateResult result = await Update(session, View("Width=\"320\""));

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(320d, ((TextBlock)session.GetRoot<Border>().Child!).Width);
    }

    [AvaloniaFact]
    public async Task TheSessionsDocumentAdvancesWithTheObjects()
    {
        await using XamlLoadSession session = await Load(View("Text=\"before\""));

        string updated = View("Text=\"after\"");

        Assert.True((await Update(session, updated)).Applied);

        Assert.Equal(updated, session.Document.GetText());
        Assert.Null(session.PendingDocument);

        // And the map advanced with it, so the next edit reaches the element it names.
        XamlElement element = Assert.IsType<XamlElement>(session.GetElement(session.GetRoot<Border>().Child!));

        Assert.Contains("after", element.GetSourceText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ReformattingIsNotAChange()
    {
        await using XamlLoadSession session = await Load(View("Text=\"same\""));

        // A comment, a blank line and a reflowed attribute move every offset in the file and
        // change nothing about the objects. Comparing trees rather than text is what sees that.
        XamlUpdateResult result = await Update(
            session,
            $"<Border xmlns=\"{AvaloniaNamespace}\"\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            $"        xmlns:d=\"{DesignNamespace}\">\n" +
            "  <!-- the label -->\n" +
            "\n" +
            "  <TextBlock\n" +
            "      Text=\"same\" />\n" +
            "</Border>");

        Assert.True(result.Applied);
        Assert.Equal(XamlUpdateStrategy.None, result.Strategy);
        Assert.Empty(result.Changes);
    }

    [AvaloniaFact]
    public async Task ADesignValueIsUpdatedInDesignMode()
    {
        await using XamlLoadSession session = await Load(
            View("d:Text=\"design before\" Text=\"real\""), XamlLoadMode.Design);

        var text = (TextBlock)session.GetRoot<Border>().Child!;

        Assert.Equal("design before", text.Text);

        XamlUpdateResult result = await Update(session, View("d:Text=\"design after\" Text=\"real\""));

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.UpdateDesignValue, result.Strategy);
        Assert.Equal("design after", text.Text);
    }

    [AvaloniaFact]
    public async Task AChangedDesignSizeReachesTheRoot()
    {
        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            Parse($"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:d=\"{DesignNamespace}\" d:DesignWidth=\"100\" />"),
            XamlLoadEnvironment.CreateDefault(),
            new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

        Assert.Equal(100d, session.GetRoot<Border>().Width);

        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            Parse($"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:d=\"{DesignNamespace}\" d:DesignWidth=\"640\" />"),
            TestContext.Current.CancellationToken);

        // Avalonia evaluated the original into Design.Width and nothing re-evaluates on an
        // update, so reading the attached property alone would keep answering 100.
        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(640d, session.GetRoot<Border>().Width);
    }

    [AvaloniaFact]
    public async Task ADesignValueIsNotAppliedInRunMode()
    {
        await using XamlLoadSession session = await Load(View("d:Text=\"design\" Text=\"real\""));

        var text = (TextBlock)session.GetRoot<Border>().Child!;

        XamlUpdateResult result = await Update(session, View("d:Text=\"design changed\" Text=\"real\""));

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal("real", text.Text);
    }

    [AvaloniaFact]
    public async Task AChangedRootNeedsANewSession()
    {
        await using XamlLoadSession session = await Load(View("Text=\"x\""));

        var border = session.GetRoot<Border>();

        XamlUpdateResult result = await Update(
            session, $"<StackPanel xmlns=\"{AvaloniaNamespace}\" />");

        Assert.False(result.Applied);
        Assert.Equal(XamlUpdateStrategy.RecreateSession, result.Strategy);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.UpdateRequiresNewSession);

        // The last tree that worked is still the tree.
        Assert.Same(border, session.RootObject);
    }

    [AvaloniaFact]
    public async Task AStructuralChangeIsReportedAsNeedingASubtreeReload()
    {
        await using XamlLoadSession session = await Load(View("Text=\"x\""));

        XamlUpdateResult result = await Update(
            session,
            $"<Border xmlns=\"{AvaloniaNamespace}\"\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            $"        xmlns:d=\"{DesignNamespace}\">\n" +
            "  <StackPanel><TextBlock Text=\"x\" /></StackPanel>\n" +
            "</Border>");

        Assert.Equal(XamlUpdateStrategy.ReloadSubtree, result.Strategy);
    }

    [AvaloniaFact]
    public async Task AChangedSetterIsReportedAgainstTheStyleThatOwnsIt()
    {
        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <Border.Styles>\n" +
            "    <Style Selector=\"TextBlock\"><Setter Property=\"Width\" Value=\"10\" /></Style>\n" +
            "  </Border.Styles>\n" +
            "  <TextBlock />\n" +
            "</Border>");

        XamlUpdateResult result = await Update(
            session,
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <Border.Styles>\n" +
            "    <Style Selector=\"TextBlock\"><Setter Property=\"Width\" Value=\"20\" /></Style>\n" +
            "  </Border.Styles>\n" +
            "  <TextBlock />\n" +
            "</Border>");

        // Setting Value on the Setter object would not restyle anything; the style is the
        // smallest thing that can actually be rebuilt.
        Assert.Equal(XamlUpdateStrategy.ReloadStyle, result.Strategy);
        Assert.Equal("Style", Assert.Single(result.Changes).OldElement?.Name.LocalName);
    }

    [AvaloniaFact]
    public async Task AChangedResourceIsReportedAgainstTheKeyedElement()
    {
        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <Border.Resources>\n" +
            "    <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
            "  </Border.Resources>\n" +
            "</Border>");

        XamlUpdateResult result = await Update(
            session,
            $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <Border.Resources>\n" +
            "    <SolidColorBrush x:Key=\"Accent\" Color=\"Blue\" />\n" +
            "  </Border.Resources>\n" +
            "</Border>");

        Assert.Equal(XamlUpdateStrategy.ReplaceResource, result.Strategy);
        Assert.Equal("SolidColorBrush", Assert.Single(result.Changes).OldElement?.Name.LocalName);
    }

    [AvaloniaFact]
    public async Task AChangedTemplateIsReportedAgainstTheTemplate()
    {
        await using XamlLoadSession session = await Load(
            $"<Button xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <Button.Template><ControlTemplate><Border Width=\"10\" /></ControlTemplate></Button.Template>\n" +
            "</Button>");

        XamlUpdateResult result = await Update(
            session,
            $"<Button xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <Button.Template><ControlTemplate><Border Width=\"20\" /></ControlTemplate></Button.Template>\n" +
            "</Button>");

        Assert.Equal(XamlUpdateStrategy.ReloadTemplate, result.Strategy);
    }

    [AvaloniaFact]
    public async Task ATemplateInsideAStyleIsReportedAgainstTheStyle()
    {
        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <Border.Styles>\n" +
            "    <Style Selector=\"Button\">\n" +
            "      <Setter Property=\"Template\">\n" +
            "        <ControlTemplate><Border Width=\"10\" /></ControlTemplate>\n" +
            "      </Setter>\n" +
            "    </Style>\n" +
            "  </Border.Styles>\n" +
            "</Border>");

        XamlUpdateResult result = await Update(
            session,
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <Border.Styles>\n" +
            "    <Style Selector=\"Button\">\n" +
            "      <Setter Property=\"Template\">\n" +
            "        <ControlTemplate><Border Width=\"20\" /></ControlTemplate>\n" +
            "      </Setter>\n" +
            "    </Style>\n" +
            "  </Border.Styles>\n" +
            "</Border>");

        // The template belongs to the style and cannot be replaced without it, so the outermost
        // container is the one that has to be rebuilt.
        Assert.Equal(XamlUpdateStrategy.ReloadStyle, result.Strategy);
    }

    [AvaloniaFact]
    public async Task ADocumentThatDoesNotParseIsRefusedAndKept()
    {
        await using XamlLoadSession session = await Load(View("Text=\"good\""));

        var text = (TextBlock)session.GetRoot<Border>().Child!;
        XamlDocument before = session.Document;

        XamlDocument broken = Parse($"<Border xmlns=\"{AvaloniaNamespace}\"><TextBlock Text=\"half");
        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            broken, TestContext.Current.CancellationToken);

        Assert.False(result.Applied);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.UpdateRejected);

        // The last tree that worked survives, and what was offered is kept rather than dropped.
        Assert.Equal("good", text.Text);
        Assert.Same(before, session.Document);
        Assert.Same(broken, session.PendingDocument);
    }

    [AvaloniaFact]
    public async Task ACorrectedUpdateLandsAfterARefusedOne()
    {
        await using XamlLoadSession session = await Load(View("Text=\"good\""));

        var text = (TextBlock)session.GetRoot<Border>().Child!;

        await session.ApplyDocumentUpdateAsync(
            Parse($"<Border xmlns=\"{AvaloniaNamespace}\"><TextBlock Text=\"half"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(session.PendingDocument);

        // The usual reason an update is refused is that the author is halfway through typing it.
        XamlUpdateResult result = await Update(session, View("Text=\"corrected\""));

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal("corrected", text.Text);
        Assert.Null(session.PendingDocument);
    }

    [AvaloniaFact]
    public async Task AnUnchangedIncludeMakesASourceUpdateANoOperation()
    {
        var resources = new InMemoryResourceResolver();
        XamlLoadEnvironment defaults = XamlLoadEnvironment.CreateDefault();

        resources.Update(
            ColorsUri,
            $"<ResourceDictionary xmlns=\"{AvaloniaNamespace}\"\n" +
            "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
            "</ResourceDictionary>");

        var environment = new XamlLoadEnvironment
        {
            SourceProvider = defaults.SourceProvider,
            AssemblyResolver = defaults.AssemblyResolver,
            TypeResolver = defaults.TypeResolver,
            ResourceResolver = new CompositeResourceResolver(resources, defaults.ResourceResolver),
        };

        string xaml =
            $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <Border.Resources>\n" +
            "    <ResourceDictionary>\n" +
            "      <ResourceDictionary.MergedDictionaries>\n" +
            "        <ResourceInclude Source=\"/Themes/Colors.axaml\" />\n" +
            "      </ResourceDictionary.MergedDictionaries>\n" +
            "    </ResourceDictionary>\n" +
            "  </Border.Resources>\n" +
            "</Border>";

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            Parse(xaml), environment, cancellationToken: TestContext.Current.CancellationToken);

        XamlUpdateResult unchanged = await session.ApplySourceUpdateAsync(
            ColorsUri, TestContext.Current.CancellationToken);

        // Being told a file changed is not evidence that anything the document reaches did.
        Assert.True(unchanged.Applied);
        Assert.Equal(XamlUpdateStrategy.None, unchanged.Strategy);

        resources.Update(
            ColorsUri,
            $"<ResourceDictionary xmlns=\"{AvaloniaNamespace}\"\n" +
            "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <SolidColorBrush x:Key=\"Accent\" Color=\"Blue\" />\n" +
            "</ResourceDictionary>");

        XamlUpdateResult changed = await session.ApplySourceUpdateAsync(
            ColorsUri, TestContext.Current.CancellationToken);

        Assert.Equal(XamlUpdateStrategy.ReplaceResource, changed.Strategy);
    }

    [AvaloniaFact]
    public async Task AReloadedStyleRestylesTheControlItTargets()
    {
        string Xaml(string width) =>
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <Border.Styles>\n" +
            $"    <Style Selector=\"Border\"><Setter Property=\"Width\" Value=\"{width}\" /></Style>\n" +
            "  </Border.Styles>\n" +
            "  <Border />\n" +
            "</Border>";

        await using XamlLoadSession session = await Load(Xaml("10"));

        var root = session.GetRoot<Border>();
        var inner = (Border)root.Child!;

        root.Measure(new Avalonia.Size(1000, 1000));

        Assert.Equal(10d, inner.Width);

        XamlUpdateResult result = await Update(session, Xaml("20"));

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.ReloadStyle, result.Strategy);

        root.Measure(new Avalonia.Size(1000, 1000));

        // The control was never rebuilt; the style it is matched by was.
        Assert.Same(inner, root.Child);
        Assert.Equal(20d, inner.Width);
    }

    [AvaloniaFact]
    public async Task AReplacedResourceReachesADynamicReference()
    {
        string Xaml(string colour) =>
            $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <Border.Resources>\n" +
            $"    <SolidColorBrush x:Key=\"Accent\" Color=\"{colour}\" />\n" +
            "  </Border.Resources>\n" +
            "  <Border Background=\"{DynamicResource Accent}\" />\n" +
            "</Border>";

        await using XamlLoadSession session = await Load(Xaml("Red"));

        var inner = (Border)session.GetRoot<Border>().Child!;

        Assert.Equal(Avalonia.Media.Colors.Red, ((Avalonia.Media.SolidColorBrush)inner.Background!).Color);

        XamlUpdateResult result = await Update(session, Xaml("Blue"));

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.ReplaceResource, result.Strategy);
        Assert.Equal(Avalonia.Media.Colors.Blue, ((Avalonia.Media.SolidColorBrush)inner.Background!).Color);
    }

    [AvaloniaFact]
    public async Task AStaticReferenceIsRebuiltWhenTheResourceItReadIsReplaced()
    {
        string Xaml(string colour) =>
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <StackPanel.Resources>\n" +
            $"    <SolidColorBrush x:Key=\"Accent\" Color=\"{colour}\" />\n" +
            "  </StackPanel.Resources>\n" +
            "  <Border Background=\"{StaticResource Accent}\" />\n" +
            "</StackPanel>";

        await using XamlLoadSession session = await Load(Xaml("Red"));

        var panel = session.GetRoot<StackPanel>();

        Assert.Equal(
            Avalonia.Media.Colors.Red,
            ((Avalonia.Media.SolidColorBrush)((Border)panel.Children[0]).Background!).Color);

        XamlUpdateResult result = await Update(session, Xaml("Blue"));

        // A static reference is resolved once, while the object is being built, so replacing the
        // dictionary entry alone would leave the border holding the brush it was given.
        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.ReloadSubtree, result.Strategy);
        Assert.Equal(
            Avalonia.Media.Colors.Blue,
            ((Avalonia.Media.SolidColorBrush)((Border)panel.Children[0]).Background!).Color);
    }

    [AvaloniaFact]
    public async Task AReloadedTemplateRecreatesTheContentItProduces()
    {
        string Xaml(string width) =>
            $"<Button xmlns=\"{AvaloniaNamespace}\" Width=\"200\" Height=\"50\">\n" +
            "  <Button.Template>\n" +
            $"    <ControlTemplate><Border Name=\"PART_Root\" Width=\"{width}\" /></ControlTemplate>\n" +
            "  </Button.Template>\n" +
            "</Button>";

        await using XamlLoadSession session = await Load(Xaml("10"));

        var button = session.GetRoot<Button>();

        button.ApplyTemplate();

        Assert.Equal(10d, button.GetVisualChildren().OfType<Border>().First().Width);

        XamlUpdateResult result = await Update(session, Xaml("20"));

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.ReloadTemplate, result.Strategy);

        button.ApplyTemplate();

        Assert.Equal(20d, button.GetVisualChildren().OfType<Border>().First().Width);
    }

    [AvaloniaFact]
    public async Task AChildAddedToANestedElementIsBuiltInPlace()
    {
        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <StackPanel><TextBlock Text=\"first\" /></StackPanel>\n" +
            "</Border>");

        var panel = (StackPanel)session.GetRoot<Border>().Child!;

        XamlUpdateResult result = await Update(
            session,
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <StackPanel><TextBlock Text=\"first\" /><TextBlock Text=\"second\" /></StackPanel>\n" +
            "</Border>");

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.ReloadSubtree, result.Strategy);

        // The panel is the same panel: only what is inside it was built again.
        Assert.Same(panel, session.GetRoot<Border>().Child);
        Assert.Equal(2, panel.Children.Count);
        Assert.Equal("second", ((TextBlock)panel.Children[1]).Text);
    }

    [AvaloniaFact]
    public async Task AChildAddedToTheRootIsBuiltInPlace()
    {
        await using XamlLoadSession session = await Load(
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\"><TextBlock Text=\"first\" /></StackPanel>");

        var panel = session.GetRoot<StackPanel>();

        XamlUpdateResult result = await Update(
            session,
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\">" +
            "<TextBlock Text=\"first\" /><TextBlock Text=\"second\" /></StackPanel>");

        // The root has no slot to be put back into, so its content is rebuilt inside it and the
        // session — which is built around that object — keeps working.
        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Same(panel, session.RootObject);
        Assert.Equal(2, panel.Children.Count);
    }

    [AvaloniaFact]
    public async Task ARebuiltObjectIsStillTraceableToItsMarkup()
    {
        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <StackPanel><TextBlock Text=\"first\" /></StackPanel>\n" +
            "</Border>");

        Assert.True((await Update(
            session,
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <StackPanel><TextBlock Text=\"first\" /><TextBlock Name=\"Added\" Text=\"second\" /></StackPanel>\n" +
            "</Border>")).Applied);

        var panel = (StackPanel)session.GetRoot<Border>().Child!;
        XamlElement element = Assert.IsType<XamlElement>(session.GetElement(panel.Children[1]));

        // The fragment Avalonia built it from is a text of its own, and the map has to know
        // which document that text was a projection of.
        Assert.Contains("Name=\"Added\"", element.GetSourceText(), StringComparison.Ordinal);
        Assert.Equal(XamlObjectOrigin.Document, session.GetOrigin(panel.Children[1]));
    }

    [AvaloniaFact]
    public async Task AFragmentThatWillNotBuildLeavesTheTreeAlone()
    {
        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <StackPanel><TextBlock Text=\"first\" /></StackPanel>\n" +
            "</Border>");

        var panel = (StackPanel)session.GetRoot<Border>().Child!;

        XamlUpdateResult result = await Update(
            session,
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <StackPanel><TextBlock Text=\"first\" /><NoSuchControl /></StackPanel>\n" +
            "</Border>");

        Assert.False(result.Applied);
        Assert.Single(panel.Children);
        Assert.NotNull(session.PendingDocument);
    }

    [AvaloniaFact]
    public async Task AChangedIncludeIsRebuiltWhereItWasExpanded()
    {
        var resources = new InMemoryResourceResolver();
        XamlLoadEnvironment defaults = XamlLoadEnvironment.CreateDefault();

        string Dictionary(string colour) =>
            $"<ResourceDictionary xmlns=\"{AvaloniaNamespace}\"\n" +
            "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            $"  <SolidColorBrush x:Key=\"Accent\" Color=\"{colour}\" />\n" +
            "</ResourceDictionary>";

        resources.Update(ColorsUri, Dictionary("Red"));

        var environment = new XamlLoadEnvironment
        {
            SourceProvider = defaults.SourceProvider,
            AssemblyResolver = defaults.AssemblyResolver,
            TypeResolver = defaults.TypeResolver,
            ResourceResolver = new CompositeResourceResolver(resources, defaults.ResourceResolver),
        };

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            Parse(
                $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                "  <Border.Resources>\n" +
                "    <ResourceDictionary>\n" +
                "      <ResourceDictionary.MergedDictionaries>\n" +
                "        <ResourceInclude Source=\"/Themes/Colors.axaml\" />\n" +
                "      </ResourceDictionary.MergedDictionaries>\n" +
                "    </ResourceDictionary>\n" +
                "  </Border.Resources>\n" +
                "  <Border Background=\"{DynamicResource Accent}\" />\n" +
                "</Border>"),
            environment,
            cancellationToken: TestContext.Current.CancellationToken);

        var inner = (Border)session.GetRoot<Border>().Child!;

        Assert.Equal(Avalonia.Media.Colors.Red, ((Avalonia.Media.SolidColorBrush)inner.Background!).Color);

        resources.Update(ColorsUri, Dictionary("Blue"));

        XamlUpdateResult result = await session.ApplySourceUpdateAsync(
            ColorsUri, TestContext.Current.CancellationToken);

        // The document reads the same; what changed is a file it pulls in, so what is rebuilt is
        // the element the include was expanded inside.
        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.ReplaceResource, result.Strategy);
        Assert.Equal(Avalonia.Media.Colors.Blue, ((Avalonia.Media.SolidColorBrush)inner.Background!).Color);
    }

    [AvaloniaFact]
    public async Task AnUpdateThatAddsALineDoesNotBreakTheNextOne()
    {
        await using XamlLoadSession session = await Load(View("Text=\"before\""));

        // A comment is trivia: the first update changes no object at all. What it does change is
        // every line number after it, and the map is rebuilt from positions recorded before it.
        XamlUpdateResult comment = await Update(
            session,
            $"<Border xmlns=\"{AvaloniaNamespace}\"\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            $"        xmlns:d=\"{DesignNamespace}\">\n" +
            "  <!-- a line that was not there before -->\n" +
            "  <TextBlock Text=\"before\" />\n" +
            "</Border>");

        Assert.True(comment.Applied);
        Assert.Equal(XamlUpdateStrategy.None, comment.Strategy);

        XamlUpdateResult second = await Update(
            session,
            $"<Border xmlns=\"{AvaloniaNamespace}\"\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            $"        xmlns:d=\"{DesignNamespace}\">\n" +
            "  <!-- a line that was not there before -->\n" +
            "  <TextBlock Text=\"after\" />\n" +
            "</Border>");

        Assert.True(second.Applied, string.Join(" | ", second.Diagnostics));
        Assert.Equal("after", ((TextBlock)session.GetRoot<Border>().Child!).Text);
    }

    private static string Panel(string children) =>
        $"<StackPanel xmlns=\"{AvaloniaNamespace}\"\n" +
        "            xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
        children +
        "\n</StackPanel>";

    [AvaloniaFact]
    public async Task NamedSiblingsThatChangePlacesAreMovedRatherThanRebuilt()
    {
        await using XamlLoadSession session = await Load(Panel(
            "  <TextBlock x:Name=\"Title\" Text=\"Title\" />\n" +
            "  <Button x:Name=\"Save\" Content=\"Save\" />"));

        var panel = session.GetRoot<StackPanel>();
        Control title = panel.Children[0];
        Control save = panel.Children[1];

        XamlUpdateResult result = await Update(session, Panel(
            "  <Button x:Name=\"Save\" Content=\"Save\" />\n" +
            "  <TextBlock x:Name=\"Title\" Text=\"Title\" />"));

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.ReorderChildren, result.Strategy);

        // The very objects that were there, in the order the document now gives them. A rebuild
        // would produce equal controls; these are the same ones, so everything they were holding
        // is still theirs.
        Assert.Same(save, panel.Children[0]);
        Assert.Same(title, panel.Children[1]);
    }

    [AvaloniaFact]
    public async Task AMovedElementKeepsItsPlaceInTheObjectMap()
    {
        await using XamlLoadSession session = await Load(Panel(
            "  <TextBlock x:Name=\"Title\" Text=\"before\" />\n" +
            "  <Button x:Name=\"Save\" Content=\"Save\" />"));

        var title = (TextBlock)session.GetRoot<StackPanel>().Children[0];

        await Update(session, Panel(
            "  <Button x:Name=\"Save\" Content=\"Save\" />\n" +
            "  <TextBlock x:Name=\"Title\" Text=\"before\" />"));

        // Setting a property after the move has to reach the object that moved, which it can only
        // do if the map followed the element to where it now is.
        XamlUpdateResult after = await Update(session, Panel(
            "  <Button x:Name=\"Save\" Content=\"Save\" />\n" +
            "  <TextBlock x:Name=\"Title\" Text=\"after\" />"));

        Assert.True(after.Applied, string.Join(" | ", after.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.SetProperty, after.Strategy);
        Assert.Equal("after", title.Text);
    }

    [AvaloniaFact]
    public async Task AMoveAndAValueChangeTogetherReachTheSameObject()
    {
        await using XamlLoadSession session = await Load(Panel(
            "  <TextBlock x:Name=\"Title\" Text=\"before\" />\n" +
            "  <Button x:Name=\"Save\" Content=\"Save\" />"));

        var panel = session.GetRoot<StackPanel>();
        var title = (TextBlock)panel.Children[0];

        XamlUpdateResult result = await Update(session, Panel(
            "  <Button x:Name=\"Save\" Content=\"Save\" />\n" +
            "  <TextBlock x:Name=\"Title\" Text=\"after\" />"));

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Same(title, panel.Children[1]);
        Assert.Equal("after", title.Text);
    }

    [AvaloniaFact]
    public async Task AnUnnamedSiblingMovesWithTheNamedOnesAroundIt()
    {
        await using XamlLoadSession session = await Load(Panel(
            "  <TextBlock x:Name=\"Title\" Text=\"Title\" />\n" +
            "  <Border Width=\"10\" />"));

        var panel = session.GetRoot<StackPanel>();
        Control title = panel.Children[0];
        Control border = panel.Children[1];

        XamlUpdateResult result = await Update(session, Panel(
            "  <Border Width=\"10\" />\n" +
            "  <TextBlock x:Name=\"Title\" Text=\"Title\" />"));

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.ReorderChildren, result.Strategy);
        Assert.Same(border, panel.Children[0]);
        Assert.Same(title, panel.Children[1]);
    }

    [AvaloniaFact]
    public async Task NameWorksAsAnIdentityWhereItMeansTheSameThing()
    {
        await using XamlLoadSession session = await Load(Panel(
            "  <TextBlock Name=\"Title\" Text=\"Title\" />\n" +
            "  <Button Name=\"Save\" Content=\"Save\" />"));

        var panel = session.GetRoot<StackPanel>();
        Control save = panel.Children[1];

        XamlUpdateResult result = await Update(session, Panel(
            "  <Button Name=\"Save\" Content=\"Save\" />\n" +
            "  <TextBlock Name=\"Title\" Text=\"Title\" />"));

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.ReorderChildren, result.Strategy);
        Assert.Same(save, panel.Children[0]);
    }

    [AvaloniaFact]
    public async Task NothingNamedFallsBackToComparingByPosition()
    {
        await using XamlLoadSession session = await Load(Panel(
            "  <TextBlock Text=\"first\" />\n" +
            "  <TextBlock Text=\"second\" />"));

        XamlUpdateResult result = await Update(session, Panel(
            "  <TextBlock Text=\"second\" />\n" +
            "  <TextBlock Text=\"first\" />"));

        // Nothing says which of the two is which, so this is two changed values rather than a
        // move — the conservative reading, and the one that cannot put a value on the wrong
        // object.
        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.SetProperty, result.Strategy);
    }

    [AvaloniaFact]
    public async Task ChildrenWrittenAsAPropertyElementAreReorderedToo()
    {
        await using XamlLoadSession session = await Load(Panel(
            "  <StackPanel.Children>\n" +
            "    <TextBlock x:Name=\"Title\" Text=\"Title\" />\n" +
            "    <Button x:Name=\"Save\" Content=\"Save\" />\n" +
            "  </StackPanel.Children>"));

        var panel = session.GetRoot<StackPanel>();
        Control save = panel.Children[1];

        XamlUpdateResult result = await Update(session, Panel(
            "  <StackPanel.Children>\n" +
            "    <Button x:Name=\"Save\" Content=\"Save\" />\n" +
            "    <TextBlock x:Name=\"Title\" Text=\"Title\" />\n" +
            "  </StackPanel.Children>"));

        // The parent here is a member rather than an object, so the collection to move things
        // around in is the one that member holds.
        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.ReorderChildren, result.Strategy);
        Assert.Same(save, panel.Children[0]);
    }

    [AvaloniaFact]
    public async Task AReorderLeavesTheDocumentExactlyAsItWasWritten()
    {
        await using XamlLoadSession session = await Load(Panel(
            "  <TextBlock x:Name=\"Title\" Text=\"Title\" />\n" +
            "  <Button x:Name=\"Save\" Content=\"Save\" />"));

        string moved = Panel(
            "  <Button x:Name=\"Save\"   Content=\"Save\" />\n" +
            "  <!-- moved above the title -->\n" +
            "  <TextBlock x:Name=\"Title\" Text=\"Title\" />");

        XamlUpdateResult result = await Update(session, moved);

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(moved, session.Document.GetText());
    }

    [AvaloniaFact]
    public async Task ARenamedElementIsNotTheSameElement()
    {
        await using XamlLoadSession session = await Load(Panel(
            "  <TextBlock x:Name=\"Title\" Text=\"Title\" />\n" +
            "  <Button x:Name=\"Save\" Content=\"Save\" />"));

        XamlUpdateResult result = await Update(session, Panel(
            "  <TextBlock x:Name=\"Heading\" Text=\"Title\" />\n" +
            "  <Button x:Name=\"Save\" Content=\"Save\" />"));

        // The set of names is not the same set, so nothing here is a move; a changed x:Name is
        // decided while the objects are built and is rebuilt for.
        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.ReloadSubtree, result.Strategy);
    }

    [AvaloniaFact]
    public async Task AnUpdateOnADisposedSessionThrows()
    {
        XamlLoadSession session = await Load(View("Text=\"x\""));

        await session.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await Update(session, View("Text=\"y\"")));
    }
}
