using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ArxisStudio.Markup.Tests;

public sealed class MarkupTransactionTests
{
    private static readonly Uri ViewUri = new("file:///Views/MainView.axaml");
    private static readonly Uri ThemeUri = new("file:///Themes/Colors.axaml");
    private static readonly Uri StyleUri = new("file:///Themes/Generic.axaml");

    private static MarkupWorkspace CreateWorkspace() => new(new InMemoryMarkupSourceProvider());

    private static (MarkupWorkspace Workspace, MarkupDocument View, MarkupDocument Theme) CreateTwoDocuments()
    {
        MarkupWorkspace workspace = CreateWorkspace();

        (MarkupWorkspace Workspace, MarkupDocument View, MarkupDocument Theme) result = (
            workspace,
            workspace.AddDocument(ViewUri, SourceText.From("<UserControl />")),
            workspace.AddDocument(ThemeUri, SourceText.From("<ResourceDictionary />")));

        // Adding a document is itself a committed transaction and is undoable. Dropping the
        // setup's history lets these tests assert about their own transactions alone.
        workspace.ClearHistory();

        return result;
    }

    [Fact]
    public void Transaction_CarriesItsDescription()
    {
        MarkupWorkspace workspace = CreateWorkspace();

        using MarkupTransaction transaction = workspace.BeginTransaction("Rename resource");

        Assert.Equal("Rename resource", transaction.Description);
        Assert.Equal(MarkupTransactionState.Active, transaction.State);
        Assert.False(transaction.HasChanges);
    }

    [Fact]
    public void BeginTransaction_RequiresADescription()
    {
        MarkupWorkspace workspace = CreateWorkspace();

        Assert.Throws<ArgumentNullException>(() => workspace.BeginTransaction(null!));
        Assert.Throws<ArgumentException>(() => workspace.BeginTransaction("   "));
    }

    [Fact]
    public void StagedChanges_AreInvisibleUntilCommit()
    {
        (MarkupWorkspace workspace, MarkupDocument view, _) = CreateTwoDocuments();

        using MarkupTransaction transaction = workspace.BeginTransaction("Rename resource");
        transaction.UpdateDocument(view.Id, SourceText.From("<Border />"));

        // The workspace still shows the old text; only the transaction sees the new one.
        Assert.Equal("<UserControl />", workspace.GetDocument(view.Id).Text.ToString());
        Assert.Equal("<Border />", transaction.GetDocument(view.Id).Text.ToString());

        transaction.Commit();

        Assert.Equal("<Border />", workspace.GetDocument(view.Id).Text.ToString());
    }

    [Fact]
    public void MultiDocumentCommit_PublishesEveryChangeAtOnce()
    {
        (MarkupWorkspace workspace, MarkupDocument view, MarkupDocument theme) = CreateTwoDocuments();

        using (MarkupTransaction transaction = workspace.BeginTransaction("Rename resource"))
        {
            transaction.UpdateDocument(view.Id, SourceText.From("<Border />"));
            transaction.UpdateDocument(theme.Id, SourceText.From("<Styles />"));
            transaction.AddDocument(StyleUri, SourceText.From("<Style />"));

            Assert.Equal(3, transaction.ChangedDocumentCount);
            transaction.Commit();
        }

        Assert.Equal("<Border />", workspace.GetDocument(view.Id).Text.ToString());
        Assert.Equal("<Styles />", workspace.GetDocument(theme.Id).Text.ToString());
        Assert.True(workspace.TryGetDocumentByUri(StyleUri, out _));
    }

    [Fact]
    public void EveryHandlerSeesTheWholeCommittedChange()
    {
        // The point of batching: a handler notified about the first document must already be
        // able to read the second one's new text. Anything else lets a consumer act on half
        // an edit.
        (MarkupWorkspace workspace, MarkupDocument view, MarkupDocument theme) = CreateTwoDocuments();
        var observed = new List<(string View, string Theme)>();

        workspace.DocumentChanged += (_, _) => observed.Add((
            workspace.GetDocument(view.Id).Text.ToString(),
            workspace.GetDocument(theme.Id).Text.ToString()));

        using (MarkupTransaction transaction = workspace.BeginTransaction("Rename resource"))
        {
            transaction.UpdateDocument(view.Id, SourceText.From("<Border />"));
            transaction.UpdateDocument(theme.Id, SourceText.From("<Styles />"));
            transaction.Commit();
        }

        Assert.Equal(2, observed.Count);
        Assert.All(observed, entry => Assert.Equal(("<Border />", "<Styles />"), entry));
    }

    [Fact]
    public void Rollback_LeavesNothingBehind()
    {
        (MarkupWorkspace workspace, MarkupDocument view, MarkupDocument theme) = CreateTwoDocuments();

        using (MarkupTransaction transaction = workspace.BeginTransaction("Rename resource"))
        {
            transaction.UpdateDocument(view.Id, SourceText.From("<Border />"));
            transaction.UpdateDocument(theme.Id, SourceText.From("<Styles />"));
            transaction.AddDocument(StyleUri, SourceText.From("<Style />"));
            transaction.RemoveDocument(theme.Id);

            transaction.Rollback();

            Assert.Equal(MarkupTransactionState.RolledBack, transaction.State);
        }

        Assert.Equal("<UserControl />", workspace.GetDocument(view.Id).Text.ToString());
        Assert.Equal("<ResourceDictionary />", workspace.GetDocument(theme.Id).Text.ToString());
        Assert.False(workspace.TryGetDocumentByUri(StyleUri, out _));
        Assert.Equal(2, workspace.Count);
    }

    [Fact]
    public void Rollback_ConsumesNoVersionsAndRaisesNoEvents()
    {
        (MarkupWorkspace workspace, MarkupDocument view, _) = CreateTwoDocuments();
        DocumentVersion before = workspace.GetDocument(view.Id).Version;
        var raised = 0;

        workspace.DocumentChanged += (_, _) => Interlocked.Increment(ref raised);

        using (MarkupTransaction transaction = workspace.BeginTransaction("Rename resource"))
        {
            transaction.UpdateDocument(view.Id, SourceText.From("<Border />"));
            transaction.Rollback();
        }

        Assert.Equal(before, workspace.GetDocument(view.Id).Version);
        Assert.Equal(0, raised);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void DisposingAnUncommittedTransaction_RollsItBack()
    {
        // This is what makes `using` safe: an exception between the first change and the
        // commit discards the partial work instead of publishing it.
        (MarkupWorkspace workspace, MarkupDocument view, MarkupDocument theme) = CreateTwoDocuments();

        Assert.Throws<InvalidOperationException>(() =>
        {
            using MarkupTransaction transaction = workspace.BeginTransaction("Failing operation");

            transaction.UpdateDocument(view.Id, SourceText.From("<Border />"));

            // A realistic mid-operation failure: the second document is not what was assumed.
            transaction.UpdateDocument(MarkupDocumentId.New(), SourceText.From("<Styles />"));
        });

        Assert.Equal("<UserControl />", workspace.GetDocument(view.Id).Text.ToString());
        Assert.Equal("<ResourceDictionary />", workspace.GetDocument(theme.Id).Text.ToString());
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void AFailedChangeMidTransaction_DoesNotStrandTheEarlierOnes()
    {
        (MarkupWorkspace workspace, MarkupDocument view, _) = CreateTwoDocuments();

        using (MarkupTransaction transaction = workspace.BeginTransaction("Partially invalid edit"))
        {
            transaction.UpdateDocument(view.Id, SourceText.From("<Border />"));

            // An overlapping change is invalid API use and throws out of the transaction.
            Assert.Throws<ArgumentException>(() => transaction.ApplyChanges(view.Id,
            [
                new TextChange(new TextSpan(0, 5), "x"),
                new TextChange(new TextSpan(3, 5), "y"),
            ]));
        }

        Assert.Equal("<UserControl />", workspace.GetDocument(view.Id).Text.ToString());
    }

    [Fact]
    public void CommittedTransaction_CannotBeReused()
    {
        (MarkupWorkspace workspace, MarkupDocument view, _) = CreateTwoDocuments();

        using MarkupTransaction transaction = workspace.BeginTransaction("Rename resource");
        transaction.UpdateDocument(view.Id, SourceText.From("<Border />"));
        transaction.Commit();

        Assert.Equal(MarkupTransactionState.Committed, transaction.State);
        Assert.Throws<InvalidOperationException>(() => transaction.UpdateDocument(view.Id, SourceText.From("<Grid />")));
        Assert.Throws<InvalidOperationException>(transaction.Commit);
        Assert.Throws<InvalidOperationException>(transaction.Rollback);
    }

    [Fact]
    public void RolledBackTransaction_CannotBeReused()
    {
        (MarkupWorkspace workspace, MarkupDocument view, _) = CreateTwoDocuments();

        using MarkupTransaction transaction = workspace.BeginTransaction("Rename resource");
        transaction.Rollback();

        Assert.Throws<InvalidOperationException>(() => transaction.UpdateDocument(view.Id, SourceText.From("<Grid />")));
        Assert.Throws<InvalidOperationException>(transaction.Commit);
    }

    [Fact]
    public void ASecondCommitOverTheSameStateIsRejected()
    {
        // Both transactions computed their changes against the same state. Letting the second
        // win would silently discard the first one's work.
        (MarkupWorkspace workspace, MarkupDocument view, _) = CreateTwoDocuments();

        using MarkupTransaction first = workspace.BeginTransaction("First edit");
        using MarkupTransaction second = workspace.BeginTransaction("Second edit");

        first.UpdateDocument(view.Id, SourceText.From("<First />"));
        second.UpdateDocument(view.Id, SourceText.From("<Second />"));

        first.Commit();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(second.Commit);

        Assert.Contains("Second edit", error.Message, StringComparison.Ordinal);
        Assert.Equal("<First />", workspace.GetDocument(view.Id).Text.ToString());
    }

    [Fact]
    public void ACommitThatChangedNothingIsANoOp()
    {
        (MarkupWorkspace workspace, MarkupDocument view, _) = CreateTwoDocuments();

        using MarkupTransaction transaction = workspace.BeginTransaction("Rename resource");
        transaction.UpdateDocument(view.Id, view.Text);
        transaction.Commit();

        Assert.False(workspace.CanUndo);
        Assert.Equal(view.Version, workspace.GetDocument(view.Id).Version);
    }

    [Fact]
    public void SeveralEditsToOneDocumentCollapseIntoOneUndoStep()
    {
        (MarkupWorkspace workspace, MarkupDocument view, _) = CreateTwoDocuments();

        using (MarkupTransaction transaction = workspace.BeginTransaction("Rename resource"))
        {
            transaction.UpdateDocument(view.Id, SourceText.From("<Step1 />"));
            transaction.UpdateDocument(view.Id, SourceText.From("<Step2 />"));
            transaction.UpdateDocument(view.Id, SourceText.From("<Step3 />"));

            Assert.Equal(1, transaction.ChangedDocumentCount);
            transaction.Commit();
        }

        Assert.Equal("<Step3 />", workspace.GetDocument(view.Id).Text.ToString());

        // Undoing returns straight to the pre-transaction text, not to an intermediate step.
        Assert.True(workspace.Undo());
        Assert.Equal("<UserControl />", workspace.GetDocument(view.Id).Text.ToString());
    }

    [Fact]
    public void TransactionsCanAddAndRemoveDocumentsTogether()
    {
        (MarkupWorkspace workspace, _, MarkupDocument theme) = CreateTwoDocuments();

        using (MarkupTransaction transaction = workspace.BeginTransaction("Replace theme"))
        {
            transaction.RemoveDocument(theme.Id);
            transaction.AddDocument(StyleUri, SourceText.From("<Style />"));
            transaction.Commit();
        }

        Assert.False(workspace.TryGetDocument(theme.Id, out _));
        Assert.True(workspace.TryGetDocumentByUri(StyleUri, out _));
        Assert.Equal(2, workspace.Count);
    }

    [Fact]
    public void RemovingAnAbsentDocumentReportsFalse()
    {
        MarkupWorkspace workspace = CreateWorkspace();

        using MarkupTransaction transaction = workspace.BeginTransaction("Remove nothing");

        Assert.False(transaction.RemoveDocument(MarkupDocumentId.New()));
        Assert.False(transaction.HasChanges);
    }

    [Fact]
    public void TransactionRejectsASecondDocumentAtTheSameUri()
    {
        (MarkupWorkspace workspace, _, _) = CreateTwoDocuments();

        using MarkupTransaction transaction = workspace.BeginTransaction("Duplicate");

        Assert.Throws<InvalidOperationException>(
            () => transaction.AddDocument(ViewUri, SourceText.From("<Grid />")));
    }

    [Fact]
    public void ConcurrentTransactionsNeverPublishAPartialState()
    {
        // Many writers race to commit the same two-document edit. Whoever loses is rejected,
        // and the workspace must never end up with one document updated and the other not.
        MarkupWorkspace workspace = CreateWorkspace();
        MarkupDocument view = workspace.AddDocument(ViewUri, SourceText.From("0"));
        MarkupDocument theme = workspace.AddDocument(ThemeUri, SourceText.From("0"));

        var torn = new List<string>();
        var committed = 0;

        Parallel.For(1, 65, index =>
        {
            try
            {
                using MarkupTransaction transaction = workspace.BeginTransaction($"Edit {index}");
                string value = index.ToString(System.Globalization.CultureInfo.InvariantCulture);

                transaction.UpdateDocument(view.Id, SourceText.From(value));
                transaction.UpdateDocument(theme.Id, SourceText.From(value));
                transaction.Commit();

                Interlocked.Increment(ref committed);
            }
            catch (InvalidOperationException)
            {
                // Lost the race; the winner's state stands.
            }

            // Both documents must come from one snapshot. Two separate reads can legitimately
            // straddle another transaction's commit, which would look like a torn state and is
            // not one.
            var snapshot = workspace.Documents.ToDictionary(static d => d.Id);
            string observedView = snapshot[view.Id].Text.ToString();
            string observedTheme = snapshot[theme.Id].Text.ToString();

            if (!string.Equals(observedView, observedTheme, StringComparison.Ordinal))
            {
                lock (torn)
                {
                    torn.Add($"{observedView} != {observedTheme}");
                }
            }
        });

        Assert.Empty(torn);
        Assert.True(committed > 0);
    }
}
