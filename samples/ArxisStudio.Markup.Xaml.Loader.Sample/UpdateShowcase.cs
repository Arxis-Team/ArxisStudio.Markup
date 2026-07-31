using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// Design mode, and bringing a loaded tree in line with a document that changed under it.
/// </summary>
internal static class UpdateShowcase
{
    /// <summary>Loads the same document twice and shows what design mode does differently.</summary>
    internal static async Task DesignAsync(XamlLoadEnvironment environment, CancellationToken cancellationToken)
    {
        Report.Section(10, "Design mode");
        Report.Note(
            "Avalonia's loader understands four names in the design namespace and fails the whole " +
            "document on any other, so d:Text is removed from the projected text and applied " +
            "afterwards. In run mode it is removed and not applied, which is what ignoring a " +
            "design-only value means. The document keeps every one of them either way.");

        foreach (XamlLoadMode mode in new[] { XamlLoadMode.Design, XamlLoadMode.Runtime })
        {
            await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
                XamlDocument.Parse(Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri }),
                environment,
                new XamlLoadOptions { Mode = mode },
                cancellationToken);

            var view = session.GetRoot<UserControl>();
            var title = (TextBlock)((StackPanel)view.Content!).Children[0];

            Report.Note($"{mode}:");
            Report.Value("Width / Height", $"{view.Width} x {view.Height}");
            Report.Value("Title.Text", title.Text ?? "<unset, left to the binding>");
            Report.Check(
                "d:Text is still in the document",
                session.Document.GetText().Contains("d:Text=", StringComparison.Ordinal));
            Report.Check(
                "and not in what Avalonia was given",
                !session.Projection.Text.ToString().Contains("d:Text=", StringComparison.Ordinal));
        }
    }

    /// <summary>Applies a series of changes and shows which strategy each one needed.</summary>
    internal static async Task UpdatesAsync(XamlLoadEnvironment environment, CancellationToken cancellationToken)
    {
        Report.Section(11, "Updates: the smallest change that is certainly enough");
        Report.Note(
            "An update compares two syntax trees and takes the smallest strategy that will do. " +
            "Nothing is compiled. Reindenting costs nothing, a literal is set where it stands, " +
            "and only a change that cannot be set at all rebuilds anything.");

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            XamlDocument.Parse(Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri }),
            environment,
            new XamlLoadOptions { Mode = XamlLoadMode.Runtime },
            cancellationToken);

        var view = session.GetRoot<UserControl>();
        var panel = (StackPanel)view.Content!;
        var button = (Button)((Border)panel.Children[1]).Child!;

        await ApplyAsync(
            session,
            "a comment added and the file reindented",
            Fixtures.View.Replace("<StackPanel", "<!-- reflowed -->\n    <StackPanel", StringComparison.Ordinal),
            cancellationToken);

        await ApplyAsync(
            session,
            "a literal property changed",
            session.Document.GetText().Replace("Content=\"Save\"", "Content=\"Save all\"", StringComparison.Ordinal),
            cancellationToken);

        Report.Check("the same Button object, with the new content", ReferenceEquals(
            button, ((Border)((StackPanel)session.GetRoot<UserControl>().Content!).Children[1]).Child));
        Report.Value("Button.Content", button.Content);

        await ApplyAsync(
            session,
            "a resource added to the document",
            session.Document.GetText().Replace(
                "<Thickness x:Key=\"RowPadding\">8,4</Thickness>",
                "<Thickness x:Key=\"RowPadding\">8,4</Thickness>\n      <SolidColorBrush x:Key=\"Extra\" Color=\"Teal\" />",
                StringComparison.Ordinal),
            cancellationToken);

        await ApplyAsync(
            session,
            "a child added",
            session.Document.GetText().Replace(
                "</StackPanel>",
                "  <TextBlock Text=\"added by an update\" />\n  </StackPanel>",
                StringComparison.Ordinal),
            cancellationToken);

        Report.Value("children now", ((StackPanel)session.GetRoot<UserControl>().Content!).Children.Count);

        await ApplyAsync(
            session,
            "a document that does not parse",
            "<UserControl xmlns=\"" + Fixtures.AvaloniaNamespace + "\"><StackPanel>",
            cancellationToken);

        Report.Check("the tree that worked is still the tree", ReferenceEquals(view, session.RootObject));
        Report.Check("and the refused document was kept", session.PendingDocument is not null);
    }

    /// <summary>Changes an included file and shows the loaded tree follow it.</summary>
    internal static async Task SourceUpdateAsync(
        XamlLoadEnvironment environment,
        InMemoryResourceResolver resources,
        CancellationToken cancellationToken)
    {
        Report.Section(12, "A file the document only includes");
        Report.Note(
            "The document reads the same; what changed is a file it pulls in. Reprojecting is " +
            "what re-reads that file through the environment's resolvers, and the difference it " +
            "makes decides what is rebuilt.");

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            XamlDocument.Parse(Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri }),
            environment,
            new XamlLoadOptions { Mode = XamlLoadMode.Runtime },
            cancellationToken);

        var view = session.GetRoot<UserControl>();

        Report.Value("Accent before", Accent(view));

        resources.Update(Fixtures.PaletteUri, Fixtures.Palette("Lime"));

        XamlUpdateResult result = await session.ApplySourceUpdateAsync(Fixtures.PaletteUri, cancellationToken);

        Report.Value("strategy", result.Strategy);
        Report.Value("applied", result.Applied);
        Report.Value("Accent after", Accent(view));
        Report.Diagnostics("diagnostics", result.Diagnostics);
    }

    private static async Task ApplyAsync(
        XamlLoadSession session,
        string what,
        string xaml,
        CancellationToken cancellationToken)
    {
        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = Fixtures.ViewUri }),
            cancellationToken);

        Report.Value(
            what,
            $"{result.Strategy,-16} applied={result.Applied,-6} changes={result.Changes.Length}");

        foreach (MarkupDiagnostic diagnostic in result.Diagnostics.Where(static d => d.Severity >= MarkupDiagnosticSeverity.Warning))
        {
            Console.WriteLine($"        {diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
        }
    }

    private static string Accent(UserControl view) =>
        view.TryFindResource("Accent", out object? value) && value is ISolidColorBrush brush
            ? brush.Color.ToString()
            : "<not found>";
}
