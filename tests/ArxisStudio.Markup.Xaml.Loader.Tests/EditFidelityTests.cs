using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// What a synchronous edit writes into the document, and what it leaves the session knowing, are
/// what the document and the objects say — for an attached property, after an update, and before a
/// source update.
/// </summary>
public sealed class EditFidelityTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string ControlsNamespace = "https://arxis.studio/test-controls";

    private static readonly Uri ViewUri = new("file:///Views/View.axaml");
    private static readonly Uri ColorsUri = new("file:///Themes/Colors.axaml");

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri });

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault([typeof(MemberMatrixControl).Assembly], new InMemoryMarkupSourceProvider());

    private static ValueTask<XamlLoadSession> Load(string xaml, XamlLoadEnvironment? environment = null) =>
        XamlLoadSession.CreateAsync(
            Parse(xaml), environment ?? Environment(), cancellationToken: TestContext.Current.CancellationToken);

    private static string Describe(XamlEditResult result) =>
        $"{result.Applied}: " + string.Join(" | ", result.Diagnostics);

    [AvaloniaFact]
    public async Task SetValueOnAnAttachedPropertyWritesOwnerDotName()
    {
        await using XamlLoadSession session = await Load(
            $"<Grid xmlns=\"{AvaloniaNamespace}\">\n  <Button />\n</Grid>");

        Button button = session.GetRoot<Grid>().Children.OfType<Button>().Single();

        XamlEditResult result = session.SetValue(button, Grid.RowProperty, 1);

        Assert.True(result.Applied, Describe(result));

        string text = session.Document.GetText();

        Assert.Contains("<Button Grid.Row=\"1\" />", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" Row=", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AnAttachedPropertyOfANamespaceTheDocumentLacksIsDeclaredAndWritten()
    {
        await using XamlLoadSession session = await Load(
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\">\n  <Button />\n</StackPanel>");

        Button button = session.GetRoot<StackPanel>().Children.OfType<Button>().Single();

        XamlEditResult result = session.SetValue(button, MemberMatrixControl.SlotProperty, 3);

        Assert.True(result.Applied, Describe(result));

        // Read back by loading what was written, which is the only test of a name that matters.
        await using XamlLoadSession again = await Load(session.Document.GetText());

        Button reloaded = again.GetRoot<StackPanel>().Children.OfType<Button>().Single();

        Assert.Equal(3, MemberMatrixControl.GetSlot(reloaded));
        Assert.Contains($"\"{ControlsNamespace}\"", session.Document.GetText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TheSourceValueOfAnAttachedPropertyIsReadAsOwnerDotName()
    {
        await using XamlLoadSession session = await Load(
            $"<Grid xmlns=\"{AvaloniaNamespace}\">\n  <Button Grid.Row=\"2\" />\n</Grid>");

        Button button = session.GetRoot<Grid>().Children.OfType<Button>().Single();

        XamlValueInfo info = session.GetValueInfo(button, Grid.RowProperty);

        Assert.Equal("2", Assert.IsType<XamlLiteralValue>(info.SourceValue).Text);
    }

    [AvaloniaFact]
    public async Task SetValueKeepsObjectsAnEarlierUpdateRebuiltMapped()
    {
        // The panel sits a few lines down, so the lines of the fragment an update builds it from are
        // not the lines of the document: an object read back through the wrong text lands on the
        // wrong element, or on none.
        static string Xaml(string children) =>
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <!-- one -->\n" +
            "  <!-- two -->\n" +
            "  <StackPanel>\n" +
            children +
            "  </StackPanel>\n" +
            "</Border>";

        await using XamlLoadSession session = await Load(Xaml("    <TextBlock Text=\"a\" />\n"));

        // A child added rebuilds the panel's content, so every child is an object the update built
        // from a fragment of its own.
        XamlUpdateResult rebuilt = await session.ApplyDocumentUpdateAsync(
            Parse(Xaml("    <TextBlock Text=\"a\" />\n    <TextBlock Text=\"b\" />\n")),
            TestContext.Current.CancellationToken);

        Assert.True(rebuilt.Applied, string.Join(" | ", rebuilt.Diagnostics));

        var root = session.GetRoot<Border>();
        var panel = Assert.IsType<StackPanel>(root.Child);

        Assert.True(session.SetValue(root, Border.WidthProperty, 40d).Applied);

        foreach (TextBlock text in panel.Children.OfType<TextBlock>())
        {
            XamlElement? element = session.GetElement(text);

            Assert.True(element is not null, $"\"{text.Text}\" lost its element.");
            Assert.Equal(text.Text, element.GetAttribute("Text")?.GetValueText());
        }
    }

    [AvaloniaFact]
    public async Task ASourceUpdateAfterSetValueReadsAsNothing()
    {
        var resources = new InMemoryResourceResolver();
        XamlLoadEnvironment defaults = Environment();

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

        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <Border.Resources>\n" +
            "    <ResourceDictionary>\n" +
            "      <ResourceDictionary.MergedDictionaries>\n" +
            "        <ResourceInclude Source=\"/Themes/Colors.axaml\" />\n" +
            "      </ResourceDictionary.MergedDictionaries>\n" +
            "    </ResourceDictionary>\n" +
            "  </Border.Resources>\n" +
            "</Border>",
            environment);

        Assert.True(session.SetValue(session.GetRoot<Border>(), Border.WidthProperty, 120d).Applied);

        XamlUpdateResult unchanged = await session.ApplySourceUpdateAsync(
            ColorsUri, TestContext.Current.CancellationToken);

        Assert.True(unchanged.Applied, string.Join(" | ", unchanged.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.None, unchanged.Strategy);
    }

    [AvaloniaFact]
    public async Task ALiteralReplacingABindingEndsTheBinding()
    {
        await using XamlLoadSession session = await Load(
            $"<TextBlock xmlns=\"{AvaloniaNamespace}\" Text=\"{{Binding Name}}\" />");

        var text = session.GetRoot<TextBlock>();
        var source = new NamedSource { Name = "first" };

        text.DataContext = source;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("first", text.Text);

        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            Parse($"<TextBlock xmlns=\"{AvaloniaNamespace}\" Text=\"literal now\" />"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));

        source.Name = "second";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("literal now", text.Text);
        Assert.False(session.GetValueInfo(text, TextBlock.TextProperty).HasBinding);
    }

    /// <summary>A source a binding follows, which says when its one value changes.</summary>
    private sealed class NamedSource : INotifyPropertyChanged
    {
        private string _name = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;

            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }
}
