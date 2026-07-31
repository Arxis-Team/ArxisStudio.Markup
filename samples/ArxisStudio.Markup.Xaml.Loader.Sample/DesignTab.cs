using System;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// The same document loaded twice, once for design and once to run.
/// </summary>
/// <remarks>
/// Both previews are the real objects. The difference between them is entirely what the library
/// did with the design-time attributes, which is the point: the document is identical.
/// </remarks>
internal static class DesignTab
{
    /// <summary>Builds the tab.</summary>
    /// <returns>The tab's content.</returns>
    internal static async Task<Control> BuildAsync()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1*,16,1*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
        };

        StackPanel header = Ui.Stack(
            Ui.Heading("Design mode"),
            Ui.Body(
                "Avalonia's loader understands four names in the design namespace and fails the " +
                "whole document on any other, so a d:Text is taken out of the text it is given " +
                "and applied afterwards. In run mode it is taken out and not applied, which is " +
                "what ignoring a design-only value means. The document keeps every one of them " +
                "either way, which is why both sides here are loaded from the same text."));

        Grid.SetColumnSpan(header, 3);
        grid.Children.Add(header);

        Control design = await PaneAsync(XamlLoadMode.Design);
        Control runtime = await PaneAsync(XamlLoadMode.Runtime);

        Grid.SetRow(design, 1);
        Grid.SetColumn(design, 0);
        grid.Children.Add(design);

        Grid.SetRow(runtime, 1);
        Grid.SetColumn(runtime, 2);
        grid.Children.Add(runtime);

        return Ui.Page(grid);
    }

    private static async Task<Control> PaneAsync(XamlLoadMode mode)
    {
        (XamlLoadEnvironment environment, _) = ShowcaseEnvironment.Create();

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri }),
            environment,
            new XamlLoadOptions { Mode = mode });

        if (session is null)
        {
            return Ui.Stack(Ui.Caption(mode.ToString()), Ui.Diagnostics(result.Diagnostics));
        }

        // Only the run-mode pane is given data. A design-time value is applied as a local
        // value, and a binding on the same property overwrites it the moment a data context
        // arrives — which is the whole reason a design value exists: so that a document can be
        // shown without one.
        var view = (Control)session.RootObject;

        if (mode == XamlLoadMode.Runtime)
        {
            SampleData.Attach(view);
        }

        return Ui.Stack(
            Ui.Caption(mode.ToString()),
            Ui.Frame(view),
            Ui.Field("Width / Height", $"{view.Width} x {view.Height}"),
            Ui.Verdict(
                "d:Text is still in the document",
                session.Document.GetText().Contains("d:Text=", StringComparison.Ordinal)),
            Ui.Verdict(
                "and absent from what Avalonia was given",
                !session.Projection.Text.ToString().Contains("d:Text=", StringComparison.Ordinal)));
    }
}
