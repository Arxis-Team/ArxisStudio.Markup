using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// The order queued updates run in, and what a caller that gives up or disposes does to the queue.
/// </summary>
/// <remarks>
/// <para>
/// Mutual exclusion alone is not enough for a host that watches files. Three saves produce three
/// updates; if the oldest is released last, every mutation was serialised perfectly and the
/// preview still ends up showing text from two saves ago. So the order is decided when the request
/// arrives, and these prove it rather than trusting the scheduler to be fair.
/// </para>
/// <para>
/// Nothing here sleeps. The dispatcher is stopped and started by the test, so each assertion is
/// made against a state the test put the session into.
/// </para>
/// </remarks>
public sealed class SessionOrderingTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";

    private static readonly Uri ViewUri = new("file:///Views/View.axaml");

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri });

    private static string View(string width) =>
        $"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"{width}\" />";

    private static ValueTask<XamlLoadSession> Load(IXamlDispatcher dispatcher)
    {
        XamlLoadEnvironment defaults =
            XamlLoadEnvironment.CreateDefault([typeof(ThrowingControl).Assembly]);

        return XamlLoadSession.CreateAsync(
            Parse(View("10")),
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
    public async Task ThreeQueuedUpdatesRunInTheOrderTheyWereSubmitted()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(dispatcher);

        var border = session.GetRoot<Border>();
        var applied = new List<double>();

        // Recorded as each dispatched operation finishes, so this is the order the writes reached
        // the objects rather than the order the test happened to await the tasks in.
        dispatcher.After = _ => applied.Add(border.Width);

        dispatcher.Hold();

        Task<XamlUpdateResult> first = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        // Submitted while the first owns the gate, so all three queue behind it.
        var queued = new List<Task<XamlUpdateResult>>();

        foreach (string width in new[] { "30", "40", "50" })
        {
            queued.Add(session
                .ApplyDocumentUpdateAsync(Parse(View(width)), TestContext.Current.CancellationToken)
                .AsTask());
        }

        dispatcher.Release();

        Assert.True((await first).Applied);

        foreach (Task<XamlUpdateResult> update in queued)
        {
            Assert.True((await update).Applied);
        }

        // Never smaller than it was: every width is larger than the last, so no older document
        // was ever written after a newer one. A gate that released its waiters in whatever order
        // they happened to wake would show a dip here — and end anywhere.
        Assert.Equal(applied.OrderBy(static width => width), applied);

        Assert.Equal(50d, border.Width);
        Assert.Contains("Width=\"50\"", session.Document.GetText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task CancellingOneWaiterLetsTheRestThrough()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(dispatcher);

        using var abandoned = new CancellationTokenSource();

        dispatcher.Hold();

        Task<XamlUpdateResult> owner = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        Task<XamlUpdateResult> middle = session
            .ApplyDocumentUpdateAsync(Parse(View("30")), abandoned.Token)
            .AsTask();

        Task<XamlUpdateResult> last = session
            .ApplyDocumentUpdateAsync(Parse(View("40")), TestContext.Current.CancellationToken)
            .AsTask();

        await abandoned.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await middle);

        // Giving up removes that turn and nothing else: the one behind it is not stranded, and the
        // one holding the gate was never touched by a registration it did not belong to.
        Assert.False(owner.IsCompleted);

        dispatcher.Release();

        Assert.True((await owner).Applied);
        Assert.True((await last).Applied);

        // The one that gave up never ran, so its width never reached the object; the one behind it
        // did, and is what the tree ends on.
        Assert.Equal(40d, session.GetRoot<Border>().Width);
        Assert.Contains("Width=\"40\"", session.Document.GetText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task CancellingTheFirstQueuedWaiterDoesNotReleaseTheOwner()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(dispatcher);

        using var abandoned = new CancellationTokenSource();

        dispatcher.Hold();

        Task<XamlUpdateResult> owner = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        Task<XamlUpdateResult> queued = session
            .ApplyDocumentUpdateAsync(Parse(View("30")), abandoned.Token)
            .AsTask();

        await abandoned.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queued);

        // Whether the gate is free is asked the way a caller would: a synchronous edit takes it
        // without waiting, so being refused is the gate saying it is still owned. A registration
        // that released a turn it never had would leave the owner sharing the session.
        var border = session.GetRoot<Border>();

        Assert.False(session.SetValue(border, Avalonia.Layout.Layoutable.HeightProperty, 44d).Applied);

        dispatcher.Release();

        Assert.True((await owner).Applied);

        // And once the owner has finished, the same edit goes through.
        Assert.True(session.SetValue(border, Avalonia.Layout.Layoutable.HeightProperty, 44d).Applied);
    }

    [AvaloniaFact]
    public async Task AWaiterThatThrowsReleasesTheNextOne()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(dispatcher);

        int loaded = dispatcher.Invocations;

        dispatcher.Hold();

        Task<XamlUpdateResult> owner = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        Task<XamlUpdateResult> failing = session
            .ApplyDocumentUpdateAsync(Parse(View("30")), TestContext.Current.CancellationToken)
            .AsTask();

        Task<XamlUpdateResult> after = session
            .ApplyDocumentUpdateAsync(Parse(View("40")), TestContext.Current.CancellationToken)
            .AsTask();

        // The owner writes and adopts; the next update's first dispatch is the third from here,
        // and it is made to fail before it runs anything — so the failure is real, arrives while
        // that update owns the gate, and has written nothing.
        dispatcher.Before = ordinal =>
        {
            if (ordinal == loaded + 3)
            {
                throw new InvalidOperationException("thrown from inside the gate");
            }
        };

        dispatcher.Release();

        Assert.True((await owner).Applied);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await failing);

        // The lease is given back by leaving, however it is left, so the one behind it runs.
        Assert.True((await after).Applied);

        Assert.Equal(40d, session.GetRoot<Border>().Width);
        Assert.Equal(XamlSessionState.Usable, session.State);
    }

    [AvaloniaFact]
    public async Task ASynchronousEditCannotStepAheadOfAWaitingUpdate()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(dispatcher);

        var border = session.GetRoot<Border>();

        dispatcher.Hold();

        Task<XamlUpdateResult> owner = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        Task<XamlUpdateResult> queued = session
            .ApplyDocumentUpdateAsync(Parse(View("30")), TestContext.Current.CancellationToken)
            .AsTask();

        // Refused, not because the gate is held this instant, but because somebody asked before
        // it. An edit unwilling to stand in the queue does not get to walk past it.
        XamlEditResult edit = session.SetValue(border, Avalonia.Layout.Layoutable.HeightProperty, 44d);

        Assert.False(edit.Applied);
        Assert.Contains(
            edit.Diagnostics,
            static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.SessionBusy);

        dispatcher.Release();

        Assert.True((await owner).Applied);
        Assert.True((await queued).Applied);
        Assert.False(border.IsSet(Avalonia.Layout.Layoutable.HeightProperty));
    }

    [AvaloniaFact]
    public async Task DisposalDoesNotStrandQueuedUpdates()
    {
        var dispatcher = new ControllableDispatcher();

        XamlLoadSession session = await Load(dispatcher);

        dispatcher.Hold();

        Task<XamlUpdateResult> owner = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        Task<XamlUpdateResult> queued = session
            .ApplyDocumentUpdateAsync(Parse(View("30")), TestContext.Current.CancellationToken)
            .AsTask();

        Task disposal = session.DisposeAsync().AsTask();

        dispatcher.Release();

        Assert.True((await owner).Applied);

        // The queued one took its turn, found the session disposed and said so. What it must not
        // do is wait for ever behind a disposal that is itself waiting for it.
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await queued);

        await disposal;
    }

    [AvaloniaFact]
    public async Task DisposingTwiceAtOnceIsSafeAndBothComplete()
    {
        var dispatcher = new ControllableDispatcher();

        XamlLoadSession session = await Load(dispatcher);

        dispatcher.Hold();

        Task<XamlUpdateResult> owner = session
            .ApplyDocumentUpdateAsync(Parse(View("20")), TestContext.Current.CancellationToken)
            .AsTask();

        await dispatcher.Arrived;

        Task first = session.DisposeAsync().AsTask();
        Task second = session.DisposeAsync().AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        dispatcher.Release();

        Assert.True((await owner).Applied);

        await Task.WhenAll(first, second);
    }
}
