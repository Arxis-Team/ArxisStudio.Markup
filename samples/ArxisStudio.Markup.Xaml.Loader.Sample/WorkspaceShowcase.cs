using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// The workspace, transactions and the resource graph — the base package's half of the library.
/// </summary>
internal static class WorkspaceShowcase
{
    /// <summary>Shows documents, versions, transactions and undo.</summary>
    internal static void Workspace()
    {
        Report.Section(5, "Workspace, transactions and undo");
        Report.Note(
            "Documents are held by identity and version rather than by path, and a transaction " +
            "either lands whole or leaves nothing behind. Undo and redo are over the workspace, " +
            "so one step covers every document a transaction touched.");

        var workspace = new MarkupWorkspace(new InMemoryMarkupSourceProvider());
        var changed = new List<string>();

        workspace.DocumentChanged += (_, args) => changed.Add($"{args.Kind} {args.NewDocument?.Uri ?? args.OldDocument?.Uri}");

        MarkupDocument view = workspace.AddDocument(Fixtures.ViewUri, SourceText.From(Fixtures.View));
        MarkupDocument palette = workspace.AddDocument(Fixtures.PaletteUri, SourceText.From(Fixtures.Palette("Red")));

        Report.Value("documents", workspace.Count);
        Report.Value("view version", view.Version);

        using (MarkupTransaction transaction = workspace.BeginTransaction("Rename the accent"))
        {
            transaction.UpdateDocument(palette.Id, SourceText.From(Fixtures.Palette("Blue")));
            transaction.Commit();
        }

        Report.Value("after commit", workspace.GetDocument(palette.Id).Version);
        Report.Check("Blue is in the palette", workspace.GetDocument(palette.Id).Text.ToString().Contains("Blue", StringComparison.Ordinal));

        using (MarkupTransaction abandoned = workspace.BeginTransaction("Never committed"))
        {
            abandoned.UpdateDocument(palette.Id, SourceText.From(Fixtures.Palette("Green")));
        }

        Report.Check(
            "an abandoned transaction changed nothing",
            !workspace.GetDocument(palette.Id).Text.ToString().Contains("Green", StringComparison.Ordinal));

        Report.Value("can undo", $"{workspace.CanUndo} ({workspace.UndoDescription})");

        workspace.Undo();

        Report.Check(
            "undo put Red back",
            workspace.GetDocument(palette.Id).Text.ToString().Contains("Red", StringComparison.Ordinal));

        workspace.Redo();

        Report.Check(
            "redo put Blue back",
            workspace.GetDocument(palette.Id).Text.ToString().Contains("Blue", StringComparison.Ordinal));
        Report.Value("change events seen", changed.Count);
    }

    /// <summary>Shows the dependency graph a chain of includes forms.</summary>
    internal static async Task GraphAsync(CancellationToken cancellationToken)
    {
        Report.Section(6, "Resource dependencies, and what one changed file invalidates");
        Report.Note(
            "Discovery is syntactic: an element is an include because of its name and its Source, " +
            "not because anything resolved a type. The graph that builds is what answers 'what " +
            "has to be rebuilt' without reloading a workspace.");

        var provider = new InMemoryMarkupSourceProvider();

        provider.Update(Fixtures.ViewUri, Fixtures.View);
        provider.Update(Fixtures.PaletteUri, Fixtures.Palette("Red"));
        provider.Update(Fixtures.BrandUri, Fixtures.Brand);

        var graph = new XamlResourceGraph(provider);
        XamlResourceGraphResult result = await graph.BuildAsync(Fixtures.ViewUri, cancellationToken);

        Report.Value("documents reached", graph.Documents.Count);
        Report.Value("the view depends on", Join(graph.GetDependencies(Fixtures.ViewUri)));
        Report.Value("Brand.axaml is needed by", Join(graph.GetDependents(Fixtures.BrandUri)));
        Report.Diagnostics("diagnostics", result.Diagnostics);

        provider.Update(Fixtures.BrandUri, Fixtures.Brand.Replace("#FFF2F2F2", "#FF101010", StringComparison.Ordinal));

        XamlResourceGraphResult updated = await graph.UpdateAsync(Fixtures.BrandUri, cancellationToken);

        Report.Note("Brand.axaml changed. Everything that reaches it, transitively, is what a host has to refresh:");
        Report.Value("invalidated", Join(graph.GetDependents(Fixtures.BrandUri)));
        Report.Diagnostics("diagnostics", updated.Diagnostics);
    }

    private static string Join(IEnumerable<Uri> uris) =>
        string.Join(", ", uris.Select(static uri => uri.Segments[^1])) is { Length: > 0 } text
            ? text
            : "<nothing>";
}
