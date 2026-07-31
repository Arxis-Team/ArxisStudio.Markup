using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ArxisStudio.Markup.Tests;

public sealed class MarkupWorkspaceTests
{
    private static readonly Uri Uri = new("file:///Views/MainView.axaml");

    private static (MarkupWorkspace Workspace, InMemoryMarkupSourceProvider Provider) CreateWorkspace(
        string text = "<Grid />")
    {
        var provider = new InMemoryMarkupSourceProvider();
        provider.Update(Uri, text);

        return (new MarkupWorkspace(provider), provider);
    }

    [Fact]
    public async Task OpenDocumentAsync_ResolvesThroughTheProvider()
    {
        (MarkupWorkspace workspace, _) = CreateWorkspace();

        MarkupDocument document = await workspace.OpenDocumentAsync(Uri, TestContext.Current.CancellationToken);

        Assert.Equal(Uri, document.Uri);
        Assert.Equal("<Grid />", document.Text.ToString());
        Assert.Equal(DocumentVersion.Initial, document.Version);
        Assert.Equal(1, workspace.Count);
    }

    [Fact]
    public async Task OpenDocumentAsync_IsIdempotentForTheSameUri()
    {
        (MarkupWorkspace workspace, _) = CreateWorkspace();

        MarkupDocument first = await workspace.OpenDocumentAsync(Uri, TestContext.Current.CancellationToken);
        MarkupDocument second = await workspace.OpenDocumentAsync(Uri, TestContext.Current.CancellationToken);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, workspace.Count);
    }

    [Fact]
    public async Task OpenDocumentAsync_FailsWhenNoProviderKnowsTheUri()
    {
        (MarkupWorkspace workspace, _) = CreateWorkspace();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await workspace.OpenDocumentAsync(new Uri("file:///Views/Missing.axaml"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateDocument_AdvancesTheVersionAndKeepsIdentity()
    {
        (MarkupWorkspace workspace, _) = CreateWorkspace();
        MarkupDocument opened = await workspace.OpenDocumentAsync(Uri, TestContext.Current.CancellationToken);

        MarkupDocument updated = workspace.UpdateDocument(opened.Id, SourceText.From("<Border />"));

        Assert.Equal(opened.Id, updated.Id);
        Assert.Equal(opened.Version.Next(), updated.Version);
        Assert.Equal("<Border />", workspace.GetDocument(opened.Id).Text.ToString());
    }

    [Fact]
    public async Task PreviouslyObtainedSnapshots_AreUnaffectedByLaterUpdates()
    {
        // Holding a snapshot across an edit is the normal case for a parser or a background
        // analysis. The snapshot it holds must not change under it.
        (MarkupWorkspace workspace, _) = CreateWorkspace();
        MarkupDocument opened = await workspace.OpenDocumentAsync(Uri, TestContext.Current.CancellationToken);

        workspace.UpdateDocument(opened.Id, SourceText.From("<Border />"));

        Assert.Equal("<Grid />", opened.Text.ToString());
        Assert.Equal(DocumentVersion.Initial, opened.Version);
    }

    [Fact]
    public async Task ApplyChanges_EditsTheCurrentSnapshot()
    {
        (MarkupWorkspace workspace, _) = CreateWorkspace("Width=\"320\"");
        MarkupDocument opened = await workspace.OpenDocumentAsync(Uri, TestContext.Current.CancellationToken);

        MarkupDocument updated = workspace.ApplyChanges(opened.Id, [new TextChange(new TextSpan(7, 3), "480")]);

        Assert.Equal("Width=\"480\"", updated.Text.ToString());
    }

    [Fact]
    public async Task UpdateDocument_WithUnchangedTextConsumesNoVersion()
    {
        (MarkupWorkspace workspace, _) = CreateWorkspace();
        MarkupDocument opened = await workspace.OpenDocumentAsync(Uri, TestContext.Current.CancellationToken);
        var raised = 0;

        workspace.DocumentChanged += (_, _) => Interlocked.Increment(ref raised);
        MarkupDocument result = workspace.UpdateDocument(opened.Id, opened.Text);

        Assert.Same(opened, result);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task CloseDocument_RemovesItAndFreesItsUri()
    {
        (MarkupWorkspace workspace, _) = CreateWorkspace();
        MarkupDocument opened = await workspace.OpenDocumentAsync(Uri, TestContext.Current.CancellationToken);

        Assert.True(workspace.CloseDocument(opened.Id));
        Assert.Equal(0, workspace.Count);
        Assert.False(workspace.TryGetDocument(opened.Id, out _));
        Assert.False(workspace.CloseDocument(opened.Id));

        MarkupDocument reopened = await workspace.OpenDocumentAsync(Uri, TestContext.Current.CancellationToken);
        Assert.NotEqual(opened.Id, reopened.Id);
    }

    [Fact]
    public async Task DocumentChanged_ReportsEachTransitionWithBothSnapshots()
    {
        (MarkupWorkspace workspace, _) = CreateWorkspace();
        var events = new List<DocumentChangedEventArgs>();

        workspace.DocumentChanged += (_, e) => events.Add(e);

        MarkupDocument opened = await workspace.OpenDocumentAsync(Uri, TestContext.Current.CancellationToken);
        workspace.UpdateDocument(opened.Id, SourceText.From("<Border />"));
        workspace.CloseDocument(opened.Id);

        Assert.Equal(
            [DocumentChangeKind.Opened, DocumentChangeKind.Changed, DocumentChangeKind.Closed],
            events.Select(static e => e.Kind));

        Assert.Null(events[0].OldDocument);
        Assert.NotNull(events[0].NewDocument);

        Assert.Equal("<Grid />", events[1].OldDocument!.Text.ToString());
        Assert.Equal("<Border />", events[1].NewDocument!.Text.ToString());

        Assert.NotNull(events[2].OldDocument);
        Assert.Null(events[2].NewDocument);
        Assert.All(events, e => Assert.Equal(opened.Id, e.DocumentId));
    }

    [Fact]
    public void AddDocument_AcceptsADocumentWithNoProviderBehindIt()
    {
        // An unsaved document is a first-class document. It has a URI no file corresponds to.
        var workspace = new MarkupWorkspace(new InMemoryMarkupSourceProvider());

        MarkupDocument document = workspace.AddDocument(
            new Uri("file:///Views/Untitled.axaml"), SourceText.From("<UserControl />"));

        Assert.Equal(1, workspace.Count);
        Assert.True(workspace.TryGetDocumentByUri(document.Uri, out MarkupDocument? found));
        Assert.Equal(document.Id, found.Id);
    }

    [Fact]
    public void AddDocument_RejectsASecondDocumentAtTheSameUri()
    {
        var workspace = new MarkupWorkspace(new InMemoryMarkupSourceProvider());
        var uri = new Uri("file:///Views/Untitled.axaml");

        workspace.AddDocument(uri, SourceText.From("<UserControl />"));

        Assert.Throws<InvalidOperationException>(() => workspace.AddDocument(uri, SourceText.From("<Grid />")));
    }

    [Fact]
    public void GetDocument_ThrowsForAnUnopenedDocument()
    {
        var workspace = new MarkupWorkspace(new InMemoryMarkupSourceProvider());

        Assert.Throws<InvalidOperationException>(() => workspace.GetDocument(MarkupDocumentId.New()));
        Assert.Throws<InvalidOperationException>(
            () => workspace.UpdateDocument(MarkupDocumentId.New(), SourceText.From("x")));
    }

    [Fact]
    public async Task ConcurrentReadsRemainConsistentWhileDocumentsAreBeingUpdated()
    {
        // The contract requires concurrent reads of immutable snapshots. A reader must always
        // observe some complete snapshot, never a half-applied edit.
        (MarkupWorkspace workspace, _) = CreateWorkspace("0");
        MarkupDocument opened = await workspace.OpenDocumentAsync(Uri, TestContext.Current.CancellationToken);

        var failures = new List<string>();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        Task writer = Task.Run(() =>
        {
            var counter = 0;


            while (!stop.IsCancellationRequested)
            {
                counter++;
                workspace.UpdateDocument(
                    opened.Id,
                    SourceText.From(counter.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }, TestContext.Current.CancellationToken);

        Task[] readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                MarkupDocument current = workspace.GetDocument(opened.Id);
                string text = current.Text.ToString();

                // Every observed snapshot must be internally coherent: its text must parse as
                // the integer the writer wrote, never a mixture of two writes.
                if (!int.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    lock (failures)
                    {
                        failures.Add(text);
                    }
                }
            }
        }, TestContext.Current.CancellationToken)).ToArray();

        await Task.WhenAll([writer, .. readers]);

        Assert.Empty(failures);
        Assert.True(workspace.GetDocument(opened.Id).Version > DocumentVersion.Initial);
    }

    [Fact]
    public async Task Documents_ReturnsAPointInTimeView()
    {
        (MarkupWorkspace workspace, _) = CreateWorkspace();
        MarkupDocument opened = await workspace.OpenDocumentAsync(Uri, TestContext.Current.CancellationToken);

        IReadOnlyCollection<MarkupDocument> before = workspace.Documents;
        workspace.CloseDocument(opened.Id);

        Assert.Single(before);
        Assert.Empty(workspace.Documents);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new MarkupWorkspace(null!));

        var workspace = new MarkupWorkspace(new InMemoryMarkupSourceProvider());

        Assert.Throws<ArgumentNullException>(() => workspace.AddDocument(null!, SourceText.From("x")));
        Assert.Throws<ArgumentNullException>(() => workspace.AddDocument(Uri, null!));
        Assert.Throws<ArgumentNullException>(() => workspace.TryGetDocumentByUri(null!, out _));
    }
}
