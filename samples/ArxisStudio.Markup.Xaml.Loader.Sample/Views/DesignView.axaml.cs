using System;
using System.Globalization;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.Sample.Reporting;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Views;

/// <summary>
/// The same document loaded twice, once for design and once to run.
/// </summary>
/// <remarks>
/// Both previews are the real objects. The difference between them is entirely what the library
/// did with the design-time attributes, which is the point: the document is identical.
/// </remarks>
internal sealed partial class DesignView : UserControl
{
    private readonly Report _design = new();
    private readonly Report _runtime = new();

    private bool _started;

    public DesignView()
    {
        InitializeComponent();

        DesignFacts.ItemsSource = _design.Rows;
        RuntimeFacts.ItemsSource = _runtime.Rows;
        DocumentText.Text = Fixtures.View;
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (!_started)
        {
            _started = true;

            _ = FillAsync();
        }
    }

    private async Task FillAsync()
    {
        await LoadAsync(XamlLoadMode.Design, DesignPreview, _design);
        await LoadAsync(XamlLoadMode.Runtime, RuntimePreview, _runtime);
    }

    private async Task LoadAsync(XamlLoadMode mode, ContentControl surface, Report report)
    {
        (XamlLoadEnvironment environment, _) = ShowcaseEnvironment.Create();

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri }),
            environment,
            new XamlLoadOptions { Mode = mode });

        if (session is null)
        {
            report.Clear().Caption("DIAGNOSTICS").Diagnostics(result.Diagnostics);

            return;
        }

        // Only the run-mode pane is given data. A design-time value is applied as a local value,
        // and a binding on the same property overwrites it the moment a data context arrives —
        // which is the whole reason a design value exists: so that a document can be shown
        // without one.
        var view = (Control)session.RootObject;

        if (mode == XamlLoadMode.Runtime)
        {
            SampleData.Attach(view);
        }

        surface.Content = view;

        if (mode == XamlLoadMode.Design)
        {
            ProjectedText.Text = session.Projection.Text.ToString();
        }

        report.Clear()
            .Field("mode", mode.ToString())
            .Field("Width / Height", $"{Size(view.Width)} x {Size(view.Height)}")
            .Verdict(
                "d:Text is still in the document",
                session.Document.GetText().Contains("d:Text=", StringComparison.Ordinal))
            .Verdict(
                "and absent from what Avalonia was given",
                !session.Projection.Text.ToString().Contains("d:Text=", StringComparison.Ordinal));
    }

    private static string Size(double value) =>
        double.IsNaN(value) ? "auto" : value.ToString(CultureInfo.InvariantCulture);
}
