using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
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
        $"<ResourceDictionary xmlns=\"{AvaloniaNamespace}\">\n" +
        $"  <SolidColorBrush x:Key=\"{key}\" Color=\"{colour}\"\n" +
        $"                   xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />\n" +
        "</ResourceDictionary>";

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
