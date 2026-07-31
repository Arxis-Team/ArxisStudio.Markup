using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Tests;

public sealed class UndoRedoTests
{
    private static readonly Uri ViewUri = new("file:///Views/MainView.axaml");
    private static readonly Uri ThemeUri = new("file:///Themes/Colors.axaml");

    private static MarkupWorkspace CreateWorkspace() => new(new InMemoryMarkupSourceProvider());

    [Fact]
    public void NewWorkspace_HasNothingToUndoOrRedo()
    {
        MarkupWorkspace workspace = CreateWorkspace();

        Assert.False(workspace.CanUndo);
        Assert.False(workspace.CanRedo);
        Assert.Null(workspace.UndoDescription);
        Assert.Null(workspace.RedoDescription);
        Assert.False(workspace.Undo());
        Assert.False(workspace.Redo());
    }

    [Fact]
    public void Undo_ReversesTheLastTransactionAndRedoReappliesIt()
    {
        MarkupWorkspace workspace = CreateWorkspace();
        MarkupDocument view = workspace.AddDocument(ViewUri, SourceText.From("<UserControl />"));

        using (MarkupTransaction transaction = workspace.BeginTransaction("Rename resource"))
        {
            transaction.UpdateDocument(view.Id, SourceText.From("<Border />"));
            transaction.Commit();
        }

        Assert.Equal("Rename resource", workspace.UndoDescription);
        Assert.True(workspace.Undo());
        Assert.Equal("<UserControl />", workspace.GetDocument(view.Id).Text.ToString());

        Assert.Equal("Rename resource", workspace.RedoDescription);
        Assert.True(workspace.Redo());
        Assert.Equal("<Border />", workspace.GetDocument(view.Id).Text.ToString());
    }

    [Fact]
    public void Undo_ReversesEveryDocumentOfAMultiDocumentTransaction()
    {
        MarkupWorkspace workspace = CreateWorkspace();
        MarkupDocument view = workspace.AddDocument(ViewUri, SourceText.From("<UserControl />"));
        MarkupDocument theme = workspace.AddDocument(ThemeUri, SourceText.From("<ResourceDictionary />"));

        using (MarkupTransaction transaction = workspace.BeginTransaction("Rename resource"))
        {
            transaction.UpdateDocument(view.Id, SourceText.From("<Border />"));
            transaction.UpdateDocument(theme.Id, SourceText.From("<Styles />"));
            transaction.Commit();
        }

        workspace.Undo();

        Assert.Equal("<UserControl />", workspace.GetDocument(view.Id).Text.ToString());
        Assert.Equal("<ResourceDictionary />", workspace.GetDocument(theme.Id).Text.ToString());
    }

    [Fact]
    public void UndoAndRedo_RestoreDocumentAdditionsAndRemovals()
    {
        MarkupWorkspace workspace = CreateWorkspace();
        MarkupDocument theme = workspace.AddDocument(ThemeUri, SourceText.From("<ResourceDictionary />"));

        using (MarkupTransaction transaction = workspace.BeginTransaction("Replace theme"))
        {
            transaction.RemoveDocument(theme.Id);
            transaction.AddDocument(ViewUri, SourceText.From("<UserControl />"));
            transaction.Commit();
        }

        Assert.False(workspace.TryGetDocument(theme.Id, out _));

        workspace.Undo();

        // The restored document keeps its original identity, not a fresh one.
        Assert.True(workspace.TryGetDocument(theme.Id, out MarkupDocument? restored));
        Assert.Equal("<ResourceDictionary />", restored.Text.ToString());
        Assert.False(workspace.TryGetDocumentByUri(ViewUri, out _));

        workspace.Redo();

        Assert.False(workspace.TryGetDocument(theme.Id, out _));
        Assert.True(workspace.TryGetDocumentByUri(ViewUri, out _));
    }

    [Fact]
    public void UndoNeverReissuesAVersionThatHasAlreadyBeenUsed()
    {
        // Later milestones cache parse trees against a document version and validate that an
        // edit's target nodes came from the version it believes it is editing. If undo put an
        // old version back, a different subsequent edit would produce a second snapshot with
        // that same version, and the check would pass for nodes whose spans no longer fit.
        MarkupWorkspace workspace = CreateWorkspace();
        MarkupDocument view = workspace.AddDocument(ViewUri, SourceText.From("v0"));
        var seen = new List<DocumentVersion> { view.Version };

        for (var round = 0; round < 5; round++)
        {
            seen.Add(workspace.UpdateDocument(view.Id, SourceText.From($"edit {round}")).Version);

            workspace.Undo();
            seen.Add(workspace.GetDocument(view.Id).Version);

            workspace.Redo();
            seen.Add(workspace.GetDocument(view.Id).Version);
        }

        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(seen.OrderBy(static version => version.Value), seen);
    }

    [Fact]
    public void UndoRestoresTheTextEvenThoughTheVersionMovesForward()
    {
        MarkupWorkspace workspace = CreateWorkspace();
        MarkupDocument view = workspace.AddDocument(ViewUri, SourceText.From("original"));

        workspace.UpdateDocument(view.Id, SourceText.From("edited"));
        workspace.Undo();

        MarkupDocument current = workspace.GetDocument(view.Id);

        Assert.Equal("original", current.Text.ToString());
        Assert.True(current.Version > view.Version);
    }

    [Fact]
    public void CommittingAfterAnUndoDiscardsTheRedoHistory()
    {
        MarkupWorkspace workspace = CreateWorkspace();
        MarkupDocument view = workspace.AddDocument(ViewUri, SourceText.From("original"));

        workspace.UpdateDocument(view.Id, SourceText.From("first branch"));
        workspace.Undo();

        Assert.True(workspace.CanRedo);

        workspace.UpdateDocument(view.Id, SourceText.From("second branch"));

        Assert.False(workspace.CanRedo);
        Assert.Equal("second branch", workspace.GetDocument(view.Id).Text.ToString());
    }

    [Fact]
    public void UndoWalksBackThroughSeveralTransactionsInOrder()
    {
        MarkupWorkspace workspace = CreateWorkspace();
        MarkupDocument view = workspace.AddDocument(ViewUri, SourceText.From("0"));

        // Discard the add so the walk below is over the four numbered steps alone.
        workspace.ClearHistory();

        foreach (int step in Enumerable.Range(1, 4))
        {
            using MarkupTransaction transaction = workspace.BeginTransaction($"Step {step}");
            transaction.UpdateDocument(view.Id, SourceText.From(step.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            transaction.Commit();
        }

        Assert.Equal("Step 4", workspace.UndoDescription);

        foreach (int expected in Enumerable.Range(0, 4).Reverse())
        {
            workspace.Undo();
            Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture),
                workspace.GetDocument(view.Id).Text.ToString());
        }

        Assert.False(workspace.CanUndo);

        foreach (int expected in Enumerable.Range(1, 4))
        {
            workspace.Redo();
            Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture),
                workspace.GetDocument(view.Id).Text.ToString());
        }

        Assert.False(workspace.CanRedo);
    }

    [Fact]
    public void UndoRaisesTheSameKindOfNotificationsAsTheOriginalChange()
    {
        MarkupWorkspace workspace = CreateWorkspace();
        MarkupDocument view = workspace.AddDocument(ViewUri, SourceText.From("original"));
        var kinds = new List<DocumentChangeKind>();

        workspace.UpdateDocument(view.Id, SourceText.From("edited"));
        workspace.DocumentChanged += (_, e) => kinds.Add(e.Kind);

        workspace.Undo();
        workspace.Redo();

        Assert.Equal([DocumentChangeKind.Changed, DocumentChangeKind.Changed], kinds);
    }

    [Fact]
    public void UndoingAnAdditionReportsItAsAClosure()
    {
        MarkupWorkspace workspace = CreateWorkspace();
        var kinds = new List<DocumentChangeKind>();

        MarkupDocument view = workspace.AddDocument(ViewUri, SourceText.From("<UserControl />"));
        workspace.DocumentChanged += (_, e) => kinds.Add(e.Kind);

        workspace.Undo();

        Assert.Equal([DocumentChangeKind.Closed], kinds);
        Assert.False(workspace.TryGetDocument(view.Id, out _));
    }

    [Fact]
    public void SingleDocumentConvenienceMethodsAreUndoableToo()
    {
        // Every mutation goes through a transaction, including the convenience methods, so
        // the history is a complete record of what has happened to the workspace rather than
        // only of the changes a caller happened to wrap explicitly.
        MarkupWorkspace workspace = CreateWorkspace();

        MarkupDocument view = workspace.AddDocument(ViewUri, SourceText.From("original"));
        Assert.True(workspace.CanUndo);

        workspace.ApplyChanges(view.Id, [new TextChange(new TextSpan(0, 8), "edited")]);
        Assert.Equal("edited", workspace.GetDocument(view.Id).Text.ToString());

        workspace.Undo();
        Assert.Equal("original", workspace.GetDocument(view.Id).Text.ToString());

        workspace.CloseDocument(view.Id);
        Assert.False(workspace.TryGetDocument(view.Id, out _));

        workspace.Undo();
        Assert.True(workspace.TryGetDocument(view.Id, out _));

        // Back past the add itself, leaving an empty workspace.
        workspace.Undo();
        Assert.Equal(0, workspace.Count);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void ClearHistory_KeepsTheDocumentsButDropsTheHistory()
    {
        MarkupWorkspace workspace = CreateWorkspace();
        MarkupDocument view = workspace.AddDocument(ViewUri, SourceText.From("original"));

        workspace.UpdateDocument(view.Id, SourceText.From("edited"));
        workspace.Undo();
        workspace.ClearHistory();

        Assert.False(workspace.CanUndo);
        Assert.False(workspace.CanRedo);
        Assert.Equal("original", workspace.GetDocument(view.Id).Text.ToString());
    }
}
