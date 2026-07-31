using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// The exit criteria of this milestone: external resource files reach loaded controls, custom
/// control templates work, and nested dependencies resolve from in-memory providers alone.
/// </summary>
public sealed class ResourcesAndTemplatesTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string TestControlsNamespace = "https://arxis.studio/test-controls";

    private static readonly Uri ViewUri = new("file:///Views/View.axaml");
    private static readonly Uri ColorsUri = new("file:///Themes/Colors.axaml");
    private static readonly Uri PaletteUri = new("file:///Themes/Palette.axaml");
    private static readonly Uri StylesUri = new("file:///Themes/Styles.axaml");

    private static (XamlLoadEnvironment Environment, InMemoryResourceResolver Resources) Setup()
    {
        var resources = new InMemoryResourceResolver();
        XamlLoadEnvironment defaults = XamlLoadEnvironment.CreateDefault(
            [typeof(CustomBadge).Assembly], new InMemoryMarkupSourceProvider());

        return (
            new XamlLoadEnvironment
            {
                SourceProvider = defaults.SourceProvider,
                AssemblyResolver = defaults.AssemblyResolver,
                TypeResolver = defaults.TypeResolver,

                // In-memory first, so an unsaved edit shadows whatever is on disk.
                ResourceResolver = new CompositeResourceResolver(resources, defaults.ResourceResolver),
            },
            resources);
    }

    private static ValueTask<XamlLoadSession> Load(string xaml, XamlLoadEnvironment environment) =>
        XamlLoadSession.CreateAsync(
            XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri }),
            environment,
            cancellationToken: TestContext.Current.CancellationToken);

    /// <summary>Gets the child that uses the resources its parent declares.</summary>
    private static Border Inner(Border outer) => (Border)outer.Child!;

    private static string Dictionary(string key, string colour) =>
        $"<ResourceDictionary xmlns=\"{AvaloniaNamespace}\"\n" +
        "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
        $"  <SolidColorBrush x:Key=\"{key}\" Color=\"{colour}\" />\n" +
        "</ResourceDictionary>";

    /// <summary>A dictionary whose whole content is an include of another one.</summary>
    private static string MergingDictionary(string source) =>
        $"<ResourceDictionary xmlns=\"{AvaloniaNamespace}\">\n" +
        "  <ResourceDictionary.MergedDictionaries>\n" +
        $"    <ResourceInclude Source=\"{source}\" />\n" +
        "  </ResourceDictionary.MergedDictionaries>\n" +
        "</ResourceDictionary>";

    /// <summary>
    /// A view that gets its <c>Accent</c> brush from an include and nothing else, with the
    /// element that uses it written after the include so a splice moves it.
    /// </summary>
    private static string ViewIncluding(string source) =>
        $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
        "  <Border.Resources>\n" +
        "    <ResourceDictionary>\n" +
        "      <ResourceDictionary.MergedDictionaries>\n" +
        $"        <ResourceInclude Source=\"{source}\" />\n" +
        "      </ResourceDictionary.MergedDictionaries>\n" +
        "    </ResourceDictionary>\n" +
        "  </Border.Resources>\n" +
        "  <Border Name=\"Uses\" Background=\"{StaticResource Accent}\" />\n" +
        "</Border>";

    [AvaloniaFact]
    public async Task AnInlineResourceReachesTheControlThatUsesIt()
    {
        (XamlLoadEnvironment environment, _) = Setup();

        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <Border.Resources>\n" +
            "    <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
            "  </Border.Resources>\n" +
            "  <Border Background=\"{StaticResource Accent}\" />\n" +
            "</Border>",
            environment);

        var border = session.GetRoot<Border>();

        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(Inner(border).Background).Color);
    }

    [AvaloniaFact]
    public async Task AResourceIncludeIsResolvedThroughTheEnvironmentsResolver()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        // Nothing on disk and nothing embedded in an assembly: only the caller's resolver knows
        // this file exists, which is the whole point of the include going through it.
        resources.Update(ColorsUri, Dictionary("Accent", "Red"));

        await using XamlLoadSession session = await Load(ViewIncluding("/Themes/Colors.axaml"), environment);

        var border = session.GetRoot<Border>();

        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(Inner(border).Background).Color);
    }

    [AvaloniaFact]
    public async Task ANestedIncludeResolvesFromInMemoryProvidersAlone()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        // Colors.axaml includes Palette.axaml by a relative source, which only resolves if the
        // included document is resolved from where it lives rather than from the view.
        resources.Update(ColorsUri, MergingDictionary("Palette.axaml"));
        resources.Update(PaletteUri, Dictionary("Accent", "Lime"));

        await using XamlLoadSession session = await Load(ViewIncluding("/Themes/Colors.axaml"), environment);

        var border = session.GetRoot<Border>();

        Assert.Equal(Colors.Lime, Assert.IsType<SolidColorBrush>(Inner(border).Background).Color);
    }

    [AvaloniaFact]
    public async Task AnEditedIncludeIsWhatTheDocumentSees()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        resources.Update(ColorsUri, Dictionary("Accent", "Red"));

        await using (XamlLoadSession first = await Load(ViewIncluding("/Themes/Colors.axaml"), environment))
        {
            Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(Inner(first.GetRoot<Border>()).Background).Color);
        }

        // An unsaved edit to the included file, held in memory. Loading again has to see it.
        resources.Update(ColorsUri, Dictionary("Accent", "Blue"));

        await using XamlLoadSession second = await Load(ViewIncluding("/Themes/Colors.axaml"), environment);

        Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(Inner(second.GetRoot<Border>()).Background).Color);
    }

    [AvaloniaFact]
    public async Task AStyleIncludeIsResolvedThroughTheEnvironmentsResolver()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        resources.Update(
            StylesUri,
            $"<Styles xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <Style Selector=\"Border.wide\">\n" +
            "    <Setter Property=\"Width\" Value=\"321\" />\n" +
            "  </Style>\n" +
            "</Styles>");

        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <Border.Styles>\n" +
            "    <StyleInclude Source=\"/Themes/Styles.axaml\" />\n" +
            "  </Border.Styles>\n" +
            "  <Border Classes=\"wide\" />\n" +
            "</Border>",
            environment);

        var border = session.GetRoot<Border>();
        IStyle included = Assert.Single(border.Styles);

        // The include is gone: what sits in the collection is the styles the file declares,
        // built by Avalonia from text the environment's resolver supplied.
        var style = Assert.IsType<Style>(Assert.Single(Assert.IsType<Styles>(included)));

        Assert.Equal(XamlObjectOrigin.Style, session.GetOrigin(style));
        Assert.Equal(StylesUri, session.GetSourceUri(style));
        Assert.Null(session.GetElement(style));
    }

    [AvaloniaFact]
    public async Task AnIncludedFilesOwnPrefixesReachTheRootOfWhatIsLoaded()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        // The view knows nothing of this prefix. Avalonia accepts xmlns only on the root
        // element, so splicing the dictionary in as written would be rejected outright, and
        // dropping its declarations would leave local:CustomBadge naming nothing.
        resources.Update(
            ColorsUri,
            $"<ResourceDictionary xmlns=\"{AvaloniaNamespace}\"\n" +
            "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            $"                    xmlns:local=\"{TestControlsNamespace}\">\n" +
            "  <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
            "  <local:CustomBadge x:Key=\"Badge\" />\n" +
            "</ResourceDictionary>");

        await using XamlLoadSession session = await Load(ViewIncluding("/Themes/Colors.axaml"), environment);

        var border = session.GetRoot<Border>();

        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(Inner(border).Background).Color);
        Assert.True(border.Resources.TryGetResource("Badge", null, out object? badge));
        Assert.IsType<CustomBadge>(badge);

        // The prefix was moved to the root of the text Avalonia was given, which is the only
        // place it is allowed to be.
        Assert.Contains(
            $"xmlns:local=\"{TestControlsNamespace}\"",
            session.Projection.Text.Lines[0].ToString(),
            StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AnIncludedFilesOwnAssemblyIsNamedWhenItsPrefixOnlyImpliedIt()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        string assembly = typeof(CustomBadge).Assembly.GetName().Name!;
        var themeUri = new Uri($"avares://{assembly}/Themes/Colors.axaml");

        // 'using:' means "in the assembly this file lives in". That file is in the control
        // library; the view is not, so hoisting the prefix as written would repoint it at the
        // view's assembly and CustomBadge would resolve to nothing.
        resources.Update(
            themeUri,
            $"<ResourceDictionary xmlns=\"{AvaloniaNamespace}\"\n" +
            "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            $"                    xmlns:owned=\"using:{typeof(CustomBadge).Namespace}\">\n" +
            "  <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
            "  <owned:CustomBadge x:Key=\"Badge\" />\n" +
            "</ResourceDictionary>");

        await using XamlLoadSession session = await Load(ViewIncluding(themeUri.OriginalString), environment);

        var border = session.GetRoot<Border>();

        Assert.True(border.Resources.TryGetResource("Badge", null, out object? badge));
        Assert.IsType<CustomBadge>(badge);

        Assert.Contains(
            $"clr-namespace:{typeof(CustomBadge).Namespace};assembly={assembly}",
            session.Projection.Text.Lines[0].ToString(),
            StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AnIncludeThatRebindsAPrefixIsLeftAsWritten()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        resources.Update(
            ColorsUri,
            $"<ResourceDictionary xmlns=\"{AvaloniaNamespace}\"\n" +
            "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "                    xmlns:local=\"https://example.invalid/somewhere-else\">\n" +
            "  <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
            "</ResourceDictionary>");

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(
                $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:local=\"{TestControlsNamespace}\">\n" +
                "  <Border.Resources>\n" +
                "    <ResourceDictionary>\n" +
                "      <ResourceDictionary.MergedDictionaries>\n" +
                "        <ResourceInclude Source=\"/Themes/Colors.axaml\" />\n" +
                "      </ResourceDictionary.MergedDictionaries>\n" +
                "    </ResourceDictionary>\n" +
                "  </Border.Resources>\n" +
                "</Border>",
                new XamlParseOptions { DocumentUri = ViewUri }),
            environment,
            cancellationToken: TestContext.Current.CancellationToken);

        await using (session)
        {
            // Two files that are each correct alone and cannot both keep 'local' once merged.
            // Guessing which one to rename would be worse than saying so.
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.IncludeNamespaceConflict);
        }
    }

    [AvaloniaFact]
    public async Task AnIncludedObjectIsAttributedToTheFileItIsWrittenIn()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        resources.Update(ColorsUri, Dictionary("Accent", "Red"));

        await using XamlLoadSession session = await Load(ViewIncluding("/Themes/Colors.axaml"), environment);

        var border = session.GetRoot<Border>();
        IResourceProvider merged = Assert.Single(border.Resources.MergedDictionaries);

        // The dictionary was declared in Colors.axaml. Handing back an element of this document
        // for it would offer the caller an edit that writes into the wrong file.
        Assert.Equal(ColorsUri, session.GetSourceUri(merged));
        Assert.Equal(XamlObjectOrigin.Resource, session.GetOrigin(merged));
        Assert.Null(session.GetElement(merged));
    }

    [AvaloniaFact]
    public async Task TheLinesAnIncludeAddsDoNotMoveTheDocumentsOwnElements()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        // Three lines of dictionary replacing one line of include, so every element written
        // after it sits two lines further down in the text Avalonia is given than in the file.
        resources.Update(ColorsUri, Dictionary("Accent", "Red"));

        string xaml = ViewIncluding("/Themes/Colors.axaml");

        await using XamlLoadSession session = await Load(xaml, environment);

        Assert.False(session.Projection.IsIdentity);

        XamlElement element = Assert.IsType<XamlElement>(session.GetElement(Inner(session.GetRoot<Border>())));

        Assert.Contains(
            "Name=\"Uses\"",
            session.Document.SourceText.GetText(element.Span),
            StringComparison.Ordinal);
        Assert.Equal(ViewUri, session.GetSourceUri(Inner(session.GetRoot<Border>())));
    }

    [AvaloniaFact]
    public async Task ProjectingAnIncludeLeavesTheDocumentItself()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        resources.Update(ColorsUri, Dictionary("Accent", "Red"));

        string xaml = ViewIncluding("/Themes/Colors.axaml");

        await using XamlLoadSession session = await Load(xaml, environment);

        // Rule 1 and rule 2 of the contract. The projection is what Avalonia was handed; the
        // document is what a save would write, and it still says what the author wrote.
        Assert.Equal(xaml, session.Document.GetText());
        Assert.Equal(xaml, session.Projection.Source.ToString());
        Assert.Contains("<ResourceInclude", session.Document.GetText(), StringComparison.Ordinal);
        Assert.DoesNotContain("<ResourceInclude", session.Projection.Text.ToString(), StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush", session.Projection.Text.ToString(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AnIncludeCycleIsReportedRatherThanExpandedForEver()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        resources.Update(ColorsUri, MergingDictionary("Palette.axaml"));
        resources.Update(PaletteUri, MergingDictionary("Colors.axaml"));

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(ViewIncluding("/Themes/Colors.axaml"), new XamlParseOptions { DocumentUri = ViewUri }),
            environment,
            cancellationToken: TestContext.Current.CancellationToken);

        await using (session)
        {
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.IncludeCycle);
        }
    }

    [AvaloniaFact]
    public async Task AnIncludeWrittenInsideAnotherIsNotSplicedTwice()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        resources.Update(ColorsUri, Dictionary("Accent", "Red"));
        resources.Update(PaletteUri, Dictionary("Accent", "Lime"));

        // Odd markup, but a document is entitled to contain it, and discovery reports every
        // include anywhere. The outer one takes the inner one with it; describing two texts for
        // one place is a mistake this must not make on the caller's behalf.
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(
                $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                "  <Border.Resources>\n" +
                "    <ResourceDictionary>\n" +
                "      <ResourceDictionary.MergedDictionaries>\n" +
                "        <ResourceInclude Source=\"/Themes/Colors.axaml\">\n" +
                "          <ResourceInclude Source=\"/Themes/Palette.axaml\" />\n" +
                "        </ResourceInclude>\n" +
                "      </ResourceDictionary.MergedDictionaries>\n" +
                "    </ResourceDictionary>\n" +
                "  </Border.Resources>\n" +
                "  <Border Background=\"{StaticResource Accent}\" />\n" +
                "</Border>",
                new XamlParseOptions { DocumentUri = ViewUri }),
            environment,
            cancellationToken: TestContext.Current.CancellationToken);

        await using (session)
        {
            Assert.NotNull(session);
            Assert.Equal(
                Colors.Red,
                Assert.IsType<SolidColorBrush>(Inner(session.GetRoot<Border>()).Background).Color);
            Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.IsError);
        }
    }

    [AvaloniaFact]
    public async Task ARelativeIncludeLeftAsWrittenStillPointsWhereItWasWritten()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        // Colors.axaml is spliced into a view in another folder, and the include it carries is
        // one no resolver knows. Left verbatim it would be resolved against the view's folder,
        // which is not where the author wrote it.
        resources.Update(ColorsUri, MergingDictionary("Palette.axaml"));

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(ViewIncluding("/Themes/Colors.axaml"), new XamlParseOptions { DocumentUri = ViewUri }),
            environment,
            cancellationToken: TestContext.Current.CancellationToken);

        await using (session)
        {
            // Avalonia's own asset loader cannot open a file: URI at all, so this particular
            // load still fails. What matters is which document it went looking for.
            Assert.Contains(
                result.Diagnostics,
                static diagnostic => diagnostic.Message.Contains(
                    "file:///Themes/Palette.axaml", StringComparison.Ordinal));

            Assert.DoesNotContain(
                result.Diagnostics,
                static diagnostic => diagnostic.Message.Contains(
                    "file:///Views/Palette.axaml", StringComparison.Ordinal));
        }
    }

    [AvaloniaFact]
    public async Task AnIncludeNoResolverKnowsIsLeftForAvalonia()
    {
        (XamlLoadEnvironment environment, _) = Setup();

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(
                ViewIncluding("avares://NoSuchAssembly/Themes/Colors.axaml"),
                new XamlParseOptions { DocumentUri = ViewUri }),
            environment,
            cancellationToken: TestContext.Current.CancellationToken);

        await using (session)
        {
            // Not an error here: Avalonia's own asset loader reaches resources this library was
            // never told about, and it is the one that gets to say the URI is wrong.
            MarkupDiagnostic left = Assert.Single(
                result.Diagnostics,
                diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.IncludeNotProjected);

            Assert.Equal(MarkupDiagnosticSeverity.Info, left.Severity);
        }
    }

    [AvaloniaFact]
    public async Task AMalformedIncludeIsReportedAgainstTheFileItIsIn()
    {
        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = Setup();

        resources.Update(ColorsUri, $"<ResourceDictionary xmlns=\"{AvaloniaNamespace}\">");

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(ViewIncluding("/Themes/Colors.axaml"), new XamlParseOptions { DocumentUri = ViewUri }),
            environment,
            cancellationToken: TestContext.Current.CancellationToken);

        await using (session)
        {
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.MalformedInclude);

            // The errors that say what is actually wrong belong to the included file, not to
            // the document that named it.
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.IsError && ColorsUri.Equals(diagnostic.DocumentUri));
        }
    }





    [AvaloniaFact]
    public async Task ACustomControlTemplateProducesItsContentAsTemplateOrigin()
    {
        (XamlLoadEnvironment environment, _) = Setup();

        await using XamlLoadSession session = await Load(
            $"<local:CustomBadge xmlns=\"{AvaloniaNamespace}\" xmlns:local=\"{TestControlsNamespace}\"\n" +
            "                   Width=\"100\" Height=\"40\">\n" +
            "  <local:CustomBadge.Template>\n" +
            "    <ControlTemplate>\n" +
            "      <Border Name=\"PART_Root\" Background=\"Silver\" />\n" +
            "    </ControlTemplate>\n" +
            "  </local:CustomBadge.Template>\n" +
            "</local:CustomBadge>",
            environment);

        var badge = session.GetRoot<CustomBadge>();

        badge.ApplyTemplate();
        badge.Measure(new Avalonia.Size(1000, 1000));

        Border? generated = badge.GetVisualChildren().OfType<Border>().FirstOrDefault();

        Assert.NotNull(generated);

        // The crucial part: the template's output is not passed off as the control's own
        // declaration, however tempting the position in the tree makes it look.
        Assert.Equal(XamlObjectOrigin.Template, session.GetOrigin(generated));
        Assert.Null(session.GetElement(generated));

        // The control itself is still mapped to the element that declared it.
        Assert.NotNull(session.GetElement(badge));
    }

    [AvaloniaFact]
    public async Task AControlThemeAppliesToTheControlItTargets()
    {
        (XamlLoadEnvironment environment, _) = Setup();

        await using XamlLoadSession session = await Load(
            $"<local:CustomBadge xmlns=\"{AvaloniaNamespace}\" xmlns:local=\"{TestControlsNamespace}\"\n" +
            "                   xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <local:CustomBadge.Resources>\n" +
            "    <ControlTheme x:Key=\"{x:Type local:CustomBadge}\" TargetType=\"local:CustomBadge\">\n" +
            "      <Setter Property=\"Width\" Value=\"222\" />\n" +
            "    </ControlTheme>\n" +
            "  </local:CustomBadge.Resources>\n" +
            "</local:CustomBadge>",
            environment);

        var badge = session.GetRoot<CustomBadge>();

        badge.Measure(new Avalonia.Size(1000, 1000));

        Assert.Equal(222d, badge.Width);
    }
}
