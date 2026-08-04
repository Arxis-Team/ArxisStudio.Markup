using System;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// One session mutates at a time, and what happens to whoever asks second.
/// </summary>
/// <remarks>
/// <para>
/// The case this is really about is a host watching a folder: a form and the dictionary it reads
/// are saved together, two notifications arrive, and two updates start. Without a boundary they
/// interleave their reads and writes of the document, the projection, the object map and the tree
/// itself, and what comes out agrees with nothing.
/// </para>
/// <para>
/// Nothing here waits on a clock. <see cref="ControllableDispatcher"/> stops an update inside the
/// one place it reaches its objects, so every assertion is made against a state the test put the
/// session into rather than one it hoped for.
/// </para>
/// </remarks>
public sealed class SessionConcurrencyTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string TestControlsNamespace = "https://arxis.studio/test-controls";

    private static readonly Uri ViewUri = new("file:///Views/View.axaml");

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri });

    private static string View(string width) =>
        $"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"{width}\" />";

    private static ValueTask<XamlLoadSession> Load(IXamlDispatcher dispatcher, string? xaml = null)
    {
        XamlLoadEnvironment defaults =
            XamlLoadEnvironment.CreateDefault([typeof(ThrowingControl).Assembly]);

        return XamlLoadSession.CreateAsync(
            Parse(xaml ?? View("10")),
            new XamlLoadEnvironment
            {
                SourceProvider = defaults.SourceProvider,
                AssemblyResolver = defaults.AssemblyResolver,
                TypeResolver = defaults.TypeResolver,
                ResourceResolver = defaults.ResourceResolver,
                Dispatcher = dispatcher,
            },
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [AvaloniaFact]
    public async Task TheSecondOfTwoDocumentUpdatesWaitsForTheFirst()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(dispatcher);

        dispatcher.Hold();

        Task<XamlUpdateResult> first = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        int dispatched = dispatcher.Invocations;

        Task<XamlUpdateResult> second = session
            .ApplyDocumentUpdateAsync(Parse(View("30")), TestContext.Current.CancellationToken)
            .AsTask();

        // Not merely incomplete: it has not reached the objects at all. An update that got past
        // the gate would have dispatched its writes by now, and the count would say so.
        Assert.False(second.IsCompleted);
        Assert.Equal(dispatched, dispatcher.Invocations);

        dispatcher.Release();

        Assert.True((await first).Applied);
        Assert.True((await second).Applied);

        // Queued, so both landed, and the later one is what the objects and the document read.
        Assert.Equal(30d, session.GetRoot<Border>().Width);
        Assert.Contains("Width=\"30\"", session.Document.GetText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ASourceUpdateWaitsForADocumentUpdateAlreadyRunning()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(dispatcher);

        dispatcher.Hold();

        Task<XamlUpdateResult> document = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        Task<XamlUpdateResult> source = session
            .ApplySourceUpdateAsync(new Uri("file:///Themes/Colors.axaml"), TestContext.Current.CancellationToken)
            .AsTask();

        // The two kinds of update share one boundary, because they change the same three things.
        Assert.False(source.IsCompleted);

        dispatcher.Release();

        Assert.True((await document).Applied);
        Assert.True((await source).Applied);
        Assert.Equal(20d, session.GetRoot<Border>().Width);
    }

    [AvaloniaFact]
    public async Task ASynchronousEditDuringAnUpdateIsRefusedRatherThanQueued()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(dispatcher);

        var border = session.GetRoot<Border>();

        dispatcher.Hold();

        Task<XamlUpdateResult> update = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        // Waiting here would block the thread the update is dispatching to, which is a deadlock
        // rather than a delay. So it refuses, and says which of the two things happened.
        XamlEditResult edit = session.SetValue(border, Avalonia.Layout.Layoutable.HeightProperty, 44d);

        Assert.False(edit.Applied);
        Assert.Contains(
            edit.Diagnostics,
            static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.SessionBusy);

        // Nothing was written, so the refusal really is one.
        Assert.False(border.IsSet(Avalonia.Layout.Layoutable.HeightProperty));

        dispatcher.Release();

        Assert.True((await update).Applied);

        // And the same edit works as soon as the update has finished with the session.
        Assert.True(session.SetValue(border, Avalonia.Layout.Layoutable.HeightProperty, 44d).Applied);
        Assert.Equal(44d, border.Height);
    }

    [AvaloniaFact]
    public async Task DisposalWaitsForAnUpdateAlreadyRunning()
    {
        var dispatcher = new ControllableDispatcher();

        XamlLoadSession session = await Load(dispatcher);

        dispatcher.Hold();

        Task<XamlUpdateResult> update = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        Task disposal = session.DisposeAsync().AsTask();

        // Cutting the update off is what would leave objects part-way built with nothing left to
        // report it, so disposal waits instead.
        Assert.False(disposal.IsCompleted);

        dispatcher.Release();

        Assert.True((await update).Applied);

        await disposal;

        // And what arrives after disposal is refused rather than half-done.
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await session.ApplyDocumentUpdateAsync(Parse(View("30")), TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task CancellationWhileWaitingForTheBoundaryLeavesItUntaken()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(dispatcher);

        dispatcher.Hold();

        Task<XamlUpdateResult> holding = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        using var cancellation = new CancellationTokenSource();

        Task<XamlUpdateResult> waiting = session
            .ApplyDocumentUpdateAsync(Parse(View("30")), cancellation.Token)
            .AsTask();

        Assert.False(waiting.IsCompleted);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);

        dispatcher.Release();

        Assert.True((await holding).Applied);
        Assert.Equal(XamlSessionState.Usable, session.State);

        // The one that gave up never held the boundary, so it cannot have left it held.
        Assert.True(
            (await session.ApplyDocumentUpdateAsync(
                Parse(View("40")), TestContext.Current.CancellationToken)).Applied);

        Assert.Equal(40d, session.GetRoot<Border>().Width);
    }

    [AvaloniaFact]
    public async Task AnUpdateThatThrowsFromInsideTheBoundaryStillReleasesIt()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(dispatcher);

        using var cancellation = new CancellationTokenSource();

        dispatcher.Hold();

        Task<XamlUpdateResult> cancelled = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), cancellation.Token)
            .AsTask();

        await dispatcher.Arrived;

        // Cancelled while it owns the boundary and before it has written anything: the exception
        // leaves through the same finally the ordinary path does.
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);

        dispatcher.Release();

        Assert.Equal(XamlSessionState.Usable, session.State);
        Assert.Equal(10d, session.GetRoot<Border>().Width);

        Assert.True(
            (await session.ApplyDocumentUpdateAsync(
                Parse(View("50")), TestContext.Current.CancellationToken)).Applied);
    }

    [AvaloniaFact]
    public async Task AnEditThatStartedWhileAnUpdateWasBreakingTheSessionStillCannotApply()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(
            dispatcher,
            $"<local:ThrowingControl xmlns=\"{AvaloniaNamespace}\"\n" +
            $"                       xmlns:local=\"{TestControlsNamespace}\"\n" +
            "                       Tag=\"one\" ThrowsBeforeAssigning=\"5\" />");

        var control = session.GetRoot<ThrowingControl>();

        dispatcher.Hold();

        // This update will write Tag and then be refused by the setter, which leaves the session
        // describing nothing.
        Task<XamlUpdateResult> breaking = session
            .ApplyDocumentUpdateAsync(
                XamlDocument.Parse(
                    $"<local:ThrowingControl xmlns=\"{AvaloniaNamespace}\"\n" +
                    $"                       xmlns:local=\"{TestControlsNamespace}\"\n" +
                    "                       Tag=\"two\" ThrowsBeforeAssigning=\"-1\" />",
                    new XamlParseOptions { DocumentUri = ViewUri }),
                TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        // While the update owns the session the edit is refused for being too early, which is the
        // easy half. The half that used to be wrong is what happens once it lets go.
        Assert.Contains(
            session.SetValue(control, ThrowingControl.ThrowsBeforeAssigningProperty, 3).Diagnostics,
            static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.SessionBusy);

        dispatcher.Release();

        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, (await breaking).Outcome);

        // The gate is free now, so an edit can take it — and the answer it gets must be the state
        // as it is inside the gate, not the state it might have read on the way in.
        XamlEditResult edit = session.SetValue(control, ThrowingControl.ThrowsBeforeAssigningProperty, 3);

        Assert.False(edit.Applied);
        Assert.Contains(
            edit.Diagnostics,
            static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.SessionRequiresRecreation);

        Assert.False(
            session.SetXamlValue(
                control, ThrowingControl.ThrowsBeforeAssigningProperty, new XamlLiteralValue("3")).Applied);

        Assert.Equal(5, control.ThrowsBeforeAssigning);
    }

    [AvaloniaFact]
    public async Task BothEditingMethodsRefuseOnceDisposalHasBegun()
    {
        var dispatcher = new ControllableDispatcher();

        XamlLoadSession session = await Load(dispatcher);

        var border = session.GetRoot<Border>();

        dispatcher.Hold();

        Task<XamlUpdateResult> update = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        Task disposal = session.DisposeAsync().AsTask();

        dispatcher.Release();

        Assert.True((await update).Applied);

        await disposal;

        // Disposal is decided inside the gate too, so an edit that arrives afterwards is refused
        // by the same rule rather than by whatever it happened to read on the way in.
        Assert.Throws<ObjectDisposedException>(() =>
            session.SetValue(border, Avalonia.Layout.Layoutable.HeightProperty, 44d));

        Assert.Throws<ObjectDisposedException>(() =>
            session.SetXamlValue(
                border, Avalonia.Layout.Layoutable.HeightProperty, new XamlLiteralValue("44")));

        Assert.False(border.IsSet(Avalonia.Layout.Layoutable.HeightProperty));
    }

    [AvaloniaFact]
    public async Task ARefusedUpdateReleasesTheBoundaryForTheNextOne()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(dispatcher);

        XamlUpdateResult refused = await session.ApplyDocumentUpdateAsync(
            Parse(View("wide")), TestContext.Current.CancellationToken);

        Assert.Equal(XamlUpdateOutcome.RejectedCleanly, refused.Outcome);

        // A clean refusal is an ordinary outcome, so the very next keystroke has to be able to
        // land — which it cannot if the refusal walked out holding the gate.
        Assert.True(
            (await session.ApplyDocumentUpdateAsync(
                Parse(View("60")), TestContext.Current.CancellationToken)).Applied);

        Assert.Equal(60d, session.GetRoot<Border>().Width);
        Assert.True(session.SetValue(session.GetRoot<Border>(), Avalonia.Layout.Layoutable.WidthProperty, 70d).Applied);
    }
}
