using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// A document with its own history, kept in step with the session that shows it — whichever way a
/// change arrives: an edit, undo and redo, or the file written by another editor.
/// </summary>
public sealed class LiveDocumentTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";

    private static readonly Uri ViewUri = new("file:///Views/View.axaml");

    private static string View(string title) =>
        $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\">\n" +
        "  <StackPanel>\n" +
        $"    <TextBlock x:Name=\"Title\" Text=\"{title}\" />\n" +
        "  </StackPanel>\n" +
        "</UserControl>\n";

    private static XamlLoadEnvironment Environment(IXamlDispatcher? dispatcher = null)
    {
        XamlLoadEnvironment defaults = XamlLoadEnvironment.CreateDefault(
            [typeof(CountedView).Assembly], new InMemoryMarkupSourceProvider());

        return dispatcher is null
            ? defaults
            : new XamlLoadEnvironment
            {
                SourceProvider = defaults.SourceProvider,
                AssemblyResolver = defaults.AssemblyResolver,
                TypeResolver = defaults.TypeResolver,
                ResourceResolver = defaults.ResourceResolver,
                Dispatcher = dispatcher,
            };
    }

    private static XamlLoadOptions Design => new() { Mode = XamlLoadMode.Design };

    private static async Task<XamlLiveDocument> OpenAsync(string text, IXamlDispatcher? dispatcher = null)
    {
        var document = XamlLiveDocument.Open(ViewUri, SourceText.From(text));

        XamlLiveEditResult attached = await document.AttachAsync(
            Environment(dispatcher), Design, TestContext.Current.CancellationToken);

        Assert.True(
            attached.State == XamlLiveDocumentState.Live,
            $"{attached.State}: " + string.Join(" | ", attached.Diagnostics));

        return document;
    }

    private static XamlElement Title(XamlDocument document) =>
        document.DescendantElements().Single(static element => element.Identity == "Title");

    private static TextBlock ShownTitle(XamlLiveDocument document) =>
        (TextBlock)((StackPanel)document.Session!.GetRoot<UserControl>().Content!).Children.Single();

    private static void SetTitle(XamlDocumentEditor editor, string text) =>
        editor.SetAttribute(Title(editor.Document), XamlQualifiedName.Unprefixed("Text"), text);

    private static string Describe(XamlLiveEditResult result) =>
        $"{result.State}: " + string.Join(" | ", result.Diagnostics);

    [AvaloniaFact]
    public async Task AnEditIsOneStepOfTheHistoryAndTheSessionFollowsItInPlace()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));
        XamlLoadSession session = document.Session!;

        XamlLiveEditResult edited = await document.EditAsync(
            editor => SetTitle(editor, "two"), "Rename the title", TestContext.Current.CancellationToken);

        Assert.True(edited.TextChanged);
        Assert.False(edited.SessionReplaced);
        Assert.Equal(XamlLiveDocumentState.Live, edited.State);
        Assert.True(edited.Update!.Applied);
        Assert.Same(session, document.Session);
        Assert.Equal("two", ShownTitle(document).Text);
        Assert.Equal(View("two"), document.Document.GetText());
        Assert.True(document.IsDirty);
        Assert.Equal("Rename the title", document.UndoDescription);

        XamlLiveEditResult undone = await document.UndoAsync(TestContext.Current.CancellationToken);

        Assert.True(undone.TextChanged);
        Assert.Equal("one", ShownTitle(document).Text);
        Assert.Equal(View("one"), document.Document.GetText());
        Assert.False(document.IsDirty);
        Assert.Equal("Rename the title", document.RedoDescription);

        await document.RedoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("two", ShownTitle(document).Text);
        Assert.True(document.IsDirty);
    }

    [AvaloniaFact]
    public async Task OpeningIsNotAStepOfTheHistory()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));

        Assert.False(document.CanUndo);

        // Undoing the open would close the document; there is nothing the author did to take back.
        XamlLiveEditResult undone = await document.UndoAsync(TestContext.Current.CancellationToken);

        Assert.False(undone.TextChanged);
        Assert.Equal(View("one"), document.Document.GetText());
        Assert.False(document.IsDirty);
    }

    [AvaloniaFact]
    public async Task AnEditorThatRecordsNothingChangesNothing()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));
        var changes = new List<XamlLiveDocumentChanges>();

        document.Changed += (_, e) => changes.Add(e.Changes);

        XamlLiveEditResult result = await document.EditAsync(
            static _ => { }, "Nothing", TestContext.Current.CancellationToken);

        Assert.False(result.TextChanged);
        Assert.False(document.CanUndo);
        Assert.Empty(changes);
    }

    [AvaloniaFact]
    public async Task TextFromOutsideOnACleanDocumentIsAStepThatLeavesItSaved()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));

        XamlExternalTextResult result = await document.AcceptExternalTextAsync(
            SourceText.From(View("two")),
            "Changed outside the designer",
            XamlExternalTextPolicy.ApplyIfClean,
            TestContext.Current.CancellationToken);

        Assert.Equal(XamlExternalTextOutcome.Taken, result.Outcome);
        Assert.Equal(XamlLiveDocumentState.Live, result.Edit!.State);
        Assert.Equal("two", ShownTitle(document).Text);

        // It came from where the document is saved, so the document is what is saved.
        Assert.False(document.IsDirty);
        Assert.Equal("Changed outside the designer", document.UndoDescription);

        // And the author can take it back — after which the document differs from the file.
        await document.UndoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("one", ShownTitle(document).Text);
        Assert.True(document.IsDirty);
    }

    [AvaloniaFact]
    public async Task TextFromOutsideOverUnsavedChangesIsAConflictThatChangesNothing()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));

        await document.EditAsync(editor => SetTitle(editor, "mine"), "Mine", TestContext.Current.CancellationToken);

        XamlExternalTextResult result = await document.AcceptExternalTextAsync(
            SourceText.From(View("theirs")),
            "Changed outside the designer",
            XamlExternalTextPolicy.ApplyIfClean,
            TestContext.Current.CancellationToken);

        Assert.Equal(XamlExternalTextOutcome.Conflict, result.Outcome);
        Assert.Null(result.Edit);
        Assert.Equal(View("mine"), document.Document.GetText());
        Assert.Equal("mine", ShownTitle(document).Text);
        Assert.True(document.IsDirty);
        Assert.Equal("Mine", document.UndoDescription);
    }

    [AvaloniaFact]
    public async Task TakingTheirsKeepsMineOneUndoAway()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));

        await document.EditAsync(editor => SetTitle(editor, "mine"), "Mine", TestContext.Current.CancellationToken);

        XamlExternalTextResult result = await document.AcceptExternalTextAsync(
            SourceText.From(View("theirs")),
            "Taken from disk",
            XamlExternalTextPolicy.TakeTheirs,
            TestContext.Current.CancellationToken);

        Assert.Equal(XamlExternalTextOutcome.Taken, result.Outcome);
        Assert.Equal("theirs", ShownTitle(document).Text);
        Assert.False(document.IsDirty);

        await document.UndoAsync(TestContext.Current.CancellationToken);

        Assert.Equal(View("mine"), document.Document.GetText());
        Assert.True(document.IsDirty);
    }

    [AvaloniaFact]
    public async Task KeepingMineRecordsTheirsAsWhatIsSaved()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));

        await document.EditAsync(editor => SetTitle(editor, "mine"), "Mine", TestContext.Current.CancellationToken);

        XamlExternalTextResult result = await document.AcceptExternalTextAsync(
            SourceText.From(View("theirs")),
            "Changed outside the designer",
            XamlExternalTextPolicy.KeepMine,
            TestContext.Current.CancellationToken);

        Assert.Equal(XamlExternalTextOutcome.KeptMine, result.Outcome);
        Assert.Equal(View("mine"), document.Document.GetText());
        Assert.Equal(View("theirs"), document.SavedText.ToString());
        Assert.True(document.IsDirty);
        Assert.Equal("Mine", document.UndoDescription);
    }

    [AvaloniaFact]
    public async Task TheEchoOfTheDocumentsOwnSaveLeavesItSaved()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));

        await document.EditAsync(editor => SetTitle(editor, "two"), "Two", TestContext.Current.CancellationToken);

        // The host wrote the file, and the watcher reports what the document already says.
        XamlExternalTextResult result = await document.AcceptExternalTextAsync(
            SourceText.From(View("two")),
            "Changed outside the designer",
            XamlExternalTextPolicy.ApplyIfClean,
            TestContext.Current.CancellationToken);

        Assert.Equal(XamlExternalTextOutcome.AlreadyCurrent, result.Outcome);
        Assert.False(document.IsDirty);
        Assert.Equal("Two", document.UndoDescription);

        // A second echo moves nothing at all, and says nothing.
        var changes = new List<XamlLiveDocumentChanges>();

        document.Changed += (_, e) => changes.Add(e.Changes);

        await document.AcceptExternalTextAsync(
            SourceText.From(View("two")),
            "Changed outside the designer",
            XamlExternalTextPolicy.ApplyIfClean,
            TestContext.Current.CancellationToken);

        await document.MarkSavedAsync(document.Document.SourceText, TestContext.Current.CancellationToken);

        Assert.Empty(changes);
    }

    [AvaloniaFact]
    public async Task WhetherTheDocumentIsChangedIsADifferenceOfText()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));

        await document.EditAsync(editor => SetTitle(editor, "two"), "Two", TestContext.Current.CancellationToken);
        await document.MarkSavedAsync(document.Document.SourceText, TestContext.Current.CancellationToken);

        Assert.False(document.IsDirty);

        await document.EditAsync(editor => SetTitle(editor, "three"), "Three", TestContext.Current.CancellationToken);

        Assert.True(document.IsDirty);

        // Back to what is saved by another road: an edit that writes the saved text again.
        await document.EditAsync(editor => SetTitle(editor, "two"), "Two again", TestContext.Current.CancellationToken);

        Assert.False(document.IsDirty);

        await document.UndoAsync(TestContext.Current.CancellationToken);

        Assert.True(document.IsDirty);
    }

    [AvaloniaFact]
    public async Task TextThatDoesNotParseLeavesTheSessionShowingTheLastThatDid()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));
        XamlLoadSession session = document.Session!;

        // Somebody halfway through typing in another editor, which saved on losing focus.
        string halfway = View("two").Replace("\" />", "\"", StringComparison.Ordinal);

        XamlExternalTextResult typing = await document.AcceptExternalTextAsync(
            SourceText.From(halfway), "Outside", XamlExternalTextPolicy.ApplyIfClean, TestContext.Current.CancellationToken);

        Assert.Equal(XamlExternalTextOutcome.Taken, typing.Outcome);
        Assert.Equal(XamlLiveDocumentState.Behind, document.State);

        // Not even tried: a recovered parse would show something nobody wrote.
        Assert.Null(typing.Edit!.Load);
        Assert.Contains(document.Diagnostics, static diagnostic => diagnostic.IsError);
        Assert.Same(session, document.Session);
        Assert.Equal("one", ShownTitle(document).Text);
        Assert.Equal(halfway, document.Document.GetText());

        await document.AcceptExternalTextAsync(
            SourceText.From(View("three")), "Outside", XamlExternalTextPolicy.ApplyIfClean, TestContext.Current.CancellationToken);

        Assert.Equal(XamlLiveDocumentState.Live, document.State);
        Assert.Same(session, document.Session);
        Assert.Equal("three", ShownTitle(document).Text);
    }

    [AvaloniaFact]
    public async Task AChangeTheSessionCannotFollowBuildsANewOne()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));
        XamlLoadSession previous = document.Session!;
        var replaced = new List<XamlSessionReplacedEventArgs>();
        var stillOpen = new List<bool>();

        document.SessionReplaced += (_, e) =>
        {
            replaced.Add(e);

            // Told while the previous session is still open — a disposed one refuses this outright —
            // so the host can still read its root and its map to take them off the canvas.
            UserControl root = e.Previous!.GetRoot<UserControl>();

            stillOpen.Add(e.Previous.SetValue(root, Control.TagProperty, "still open").Applied);
        };

        // Another root is another session: nothing in place can turn a control into a border.
        string border =
            $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\">\n" +
            "  <TextBlock x:Name=\"Title\" Text=\"two\" />\n" +
            "</Border>\n";

        XamlExternalTextResult result = await document.AcceptExternalTextAsync(
            SourceText.From(border), "Outside", XamlExternalTextPolicy.ApplyIfClean, TestContext.Current.CancellationToken);

        Assert.True(result.Edit!.SessionReplaced, Describe(result.Edit));
        Assert.Equal(XamlLiveDocumentState.Live, result.Edit.State);

        XamlSessionReplacedEventArgs told = Assert.Single(replaced);

        Assert.Same(previous, told.Previous);
        Assert.Same(document.Session, told.Current);
        Assert.Equal([true], stillOpen);
        Assert.IsType<Border>(document.Session!.RootObject);

        // And the previous one is gone once the operation is over.
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            previous.ApplyDocumentUpdateAsync(previous.Document, TestContext.Current.CancellationToken).AsTask());
    }

    [AvaloniaFact]
    public async Task TextThatLoadsNowhereIsBrokenUntilATextThatLoadsArrives()
    {
        var document = XamlLiveDocument.Open(
            ViewUri, SourceText.From($"<NoSuchControl xmlns=\"{AvaloniaNamespace}\" />"));

        await using (document)
        {
            XamlLiveEditResult attached = await document.AttachAsync(
                Environment(), Design, TestContext.Current.CancellationToken);

            Assert.Equal(XamlLiveDocumentState.Broken, attached.State);
            Assert.False(attached.SessionReplaced);
            Assert.Null(document.Session);
            Assert.Contains(document.Diagnostics, static diagnostic => diagnostic.IsError);

            XamlExternalTextResult fixedText = await document.AcceptExternalTextAsync(
                SourceText.From(View("one")), "Outside", XamlExternalTextPolicy.ApplyIfClean, TestContext.Current.CancellationToken);

            Assert.Equal(XamlLiveDocumentState.Live, fixedText.Edit!.State);
            Assert.True(fixedText.Edit.SessionReplaced);
            Assert.Equal("one", ShownTitle(document).Text);
        }
    }

    [AvaloniaFact]
    public async Task AnEditThatCannotBeShownIsKeptAndTheSessionStaysBehind()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));
        XamlLoadSession session = document.Session!;

        XamlLiveEditResult result = await document.EditAsync(
            editor => editor.InsertElement(
                editor.Document.Root!.ContentElements.Single(), 1, "<NoSuchControl />"),
            "Insert",
            TestContext.Current.CancellationToken);

        // The edit is the author's whatever the objects make of it.
        Assert.True(result.TextChanged);
        Assert.Contains("<NoSuchControl />", document.Document.GetText(), StringComparison.Ordinal);
        Assert.Equal(XamlLiveDocumentState.Behind, result.State);
        Assert.NotNull(result.Load);
        Assert.Same(session, document.Session);
        Assert.Contains(document.Diagnostics, static diagnostic => diagnostic.IsError);

        await document.UndoAsync(TestContext.Current.CancellationToken);

        Assert.Equal(XamlLiveDocumentState.Live, document.State);
        Assert.Same(session, document.Session);
    }

    [AvaloniaFact]
    public async Task AttachingAgainLetsGoOfTheSessionInPlaceEvenWhenNothingNewCanBeBuilt()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));
        XamlLoadSession previous = document.Session!;

        // Behind: the text names a root nothing can build, and the session shows the last text.
        await document.AcceptExternalTextAsync(
            SourceText.From($"<NoSuchControl xmlns=\"{AvaloniaNamespace}\" />"),
            "Outside",
            XamlExternalTextPolicy.ApplyIfClean,
            TestContext.Current.CancellationToken);

        Assert.Equal(XamlLiveDocumentState.Behind, document.State);
        Assert.Same(previous, document.Session);

        // A new environment is a new generation of the code: the session of the old one goes, or it
        // would keep the old generation alive.
        XamlLiveEditResult attached = await document.AttachAsync(
            Environment(), Design, TestContext.Current.CancellationToken);

        Assert.Equal(XamlLiveDocumentState.Broken, attached.State);
        Assert.True(attached.SessionReplaced);
        Assert.Null(document.Session);
    }

    [AvaloniaFact]
    public async Task ADetachedDocumentKeepsItsTextAndHistoryAndShowsThemWhenAttached()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));

        await document.EditAsync(editor => SetTitle(editor, "two"), "Two", TestContext.Current.CancellationToken);
        await document.DetachAsync(TestContext.Current.CancellationToken);

        Assert.Equal(XamlLiveDocumentState.Detached, document.State);
        Assert.Null(document.Session);

        // Text keeps landing while nothing shows it.
        XamlLiveEditResult edited = await document.EditAsync(
            editor => SetTitle(editor, "three"), "Three", TestContext.Current.CancellationToken);

        Assert.True(edited.TextChanged);
        Assert.Equal(XamlLiveDocumentState.Detached, edited.State);
        Assert.True(document.IsDirty);
        Assert.Equal("Three", document.UndoDescription);

        XamlLiveEditResult attached = await document.AttachAsync(
            Environment(), Design, TestContext.Current.CancellationToken);

        Assert.True(attached.SessionReplaced);
        Assert.Equal("three", ShownTitle(document).Text);

        await document.UndoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("two", ShownTitle(document).Text);
    }

    [AvaloniaFact]
    public async Task DetachingLetsGoOfTheEnvironmentTheOptionsAndTheSession()
    {
        await using XamlLiveDocument document = XamlLiveDocument.Open(ViewUri, SourceText.From(View("one")));

        WeakReference[] held = await AttachHeldAsync(document);

        await document.DetachAsync(TestContext.Current.CancellationToken);

        Collect();

        // What a generation of a project's code hangs from: once its documents are detached, nothing
        // here may keep it.
        Assert.All(held, static reference => Assert.False(reference.IsAlive));
        Assert.Equal(View("one"), document.Document.GetText());
    }

    [AvaloniaFact]
    public async Task TwoEditsInFlightLandInTheOrderTheyWereAskedFor()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLiveDocument document = await OpenAsync(View("one"), dispatcher);

        dispatcher.Hold();

        Task<XamlLiveEditResult> first = document
            .EditAsync(editor => SetTitle(editor, "two"), "Two", TestContext.Current.CancellationToken)
            .AsTask();

        // The first edit is recorded and its session update is held on the owning thread.
        await dispatcher.Arrived;

        bool secondRan = false;
        string? secondSaw = null;
        Task<XamlLiveEditResult> second;

        try
        {
            second = document
                .EditAsync(
                    editor =>
                    {
                        secondRan = true;
                        secondSaw = Title(editor.Document).GetAttribute("Text")!.GetValueText();
                        SetTitle(editor, "three");
                    },
                    "Three",
                    TestContext.Current.CancellationToken)
                .AsTask();

            Assert.False(secondRan);
        }
        finally
        {
            // Released whatever the assertion said: the held update owns the document's turn, and
            // disposing the document waits for it.
            dispatcher.Release();
        }

        Assert.True((await first).TextChanged);
        Assert.True((await second).TextChanged);

        // Computed against what the first edit left, and shown after it.
        Assert.Equal("two", secondSaw);
        Assert.Equal("three", ShownTitle(document).Text);
        Assert.Equal("Three", document.UndoDescription);

        await document.UndoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("two", ShownTitle(document).Text);
    }

    [AvaloniaFact]
    public async Task EventsAreRaisedOnTheOwningThreadOnceTheOperationIsOver()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));
        var seen = new List<(XamlLiveDocumentChanges Changes, bool OnOwner, string Text)>();

        document.Changed += (_, e) => seen.Add(
            (e.Changes, Dispatcher.UIThread.CheckAccess(), ShownTitle(document).Text!));

        // Asked for from the pool, as a host watching files does: the events still arrive where the
        // objects live, with the objects already showing the change.
        await Task.Run(
            async () =>
            {
                await document.EditAsync(editor => SetTitle(editor, "two"), "Two", TestContext.Current.CancellationToken);
                await document.UndoAsync(TestContext.Current.CancellationToken);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                (XamlLiveDocumentChanges.Text | XamlLiveDocumentChanges.Saved, true, "two"),
                (XamlLiveDocumentChanges.Text | XamlLiveDocumentChanges.Saved, true, "one"),
            ],
            seen);
    }

    [AvaloniaFact]
    public async Task AMovedDocumentKeepsItsHistoryAtItsNewPlace()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));
        var moved = new Uri("file:///Views/Renamed.axaml");

        await document.EditAsync(editor => SetTitle(editor, "two"), "Two", TestContext.Current.CancellationToken);

        XamlLiveEditResult result = await document.RetargetAsync(moved, TestContext.Current.CancellationToken);

        Assert.False(result.TextChanged);
        Assert.True(result.SessionReplaced);
        Assert.Equal(moved, document.Uri);
        Assert.Equal(moved, document.Session!.Document.Uri);
        Assert.Equal("Two", document.UndoDescription);

        await document.UndoAsync(TestContext.Current.CancellationToken);

        Assert.Equal(moved, document.Uri);
        Assert.Equal("one", ShownTitle(document).Text);
    }

    [AvaloniaFact]
    public async Task ARebuildBuildsANewSessionFromTheTextAsItReads()
    {
        await using XamlLiveDocument document = await OpenAsync(View("one"));
        XamlLoadSession previous = document.Session!;

        await document.EditAsync(editor => SetTitle(editor, "two"), "Two", TestContext.Current.CancellationToken);

        XamlLiveEditResult rebuilt = await document.RebuildAsync(TestContext.Current.CancellationToken);

        Assert.True(rebuilt.SessionReplaced);
        Assert.False(rebuilt.TextChanged);
        Assert.NotSame(previous, document.Session);
        Assert.Equal("two", ShownTitle(document).Text);
    }

    [AvaloniaFact]
    public async Task ADisposedDocumentRefusesWhatComesAfter()
    {
        XamlLiveDocument document = await OpenAsync(View("one"));
        XamlLoadSession session = document.Session!;

        await document.DisposeAsync();
        await document.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            document.EditAsync(editor => SetTitle(editor, "two"), "Two").AsTask());

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.ApplyDocumentUpdateAsync(session.Document, TestContext.Current.CancellationToken).AsTask());
    }

    [AvaloniaFact]
    public async Task ARestoredDocumentReadsAsChangedAgainstWhatIsSaved()
    {
        await using var document = XamlLiveDocument.Open(
            ViewUri, SourceText.From(View("unsaved")), savedText: SourceText.From(View("saved")));

        Assert.True(document.IsDirty);
        Assert.False(document.CanUndo);
        Assert.Equal(XamlLiveDocumentState.Detached, document.State);
    }

    /// <summary>
    /// Attaches with an environment, options and a root access made here, and hands back only weak
    /// references to them and to the session — so nothing in the test keeps them.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference[]> AttachHeldAsync(XamlLiveDocument document)
    {
        XamlLoadEnvironment environment = Environment();
        var options = new XamlLoadOptions { Mode = XamlLoadMode.Design, RootAccess = new NothingLentAccess() };

        await document.AttachAsync(environment, options, TestContext.Current.CancellationToken);

        return
        [
            new WeakReference(environment),
            new WeakReference(options),
            new WeakReference(options.RootAccess),
            new WeakReference(document.Session),
        ];
    }

    private static void Collect()
    {
        for (int pass = 0; pass < 3; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>A host's root access that borrows nothing, standing for one that holds a canvas.</summary>
    private sealed class NothingLentAccess : IXamlRootAccess
    {
        public IDisposable Lend(object root) => new Lease();

        private sealed class Lease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
