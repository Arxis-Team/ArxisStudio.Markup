using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// What an update says about the objects when it does not apply, and what a session does after
/// one has stopped part-way.
/// </summary>
/// <remarks>
/// The distinction these cover is the only one a caller can act on. A refusal that touched nothing
/// is the ordinary case — a document caught mid-keystroke — and the session goes on working. A
/// failure that had already written is not recoverable by anything here, because what ran was user
/// code with side effects, and the honest answer is to say so and stop accepting changes.
/// </remarks>
public sealed class SessionStateTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string TestControlsNamespace = "https://arxis.studio/test-controls";
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Uri ViewUri = new("file:///Views/View.axaml");

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri });

    private static XamlLoadEnvironment Environment(IXamlDispatcher? dispatcher = null)
    {
        XamlLoadEnvironment defaults =
            XamlLoadEnvironment.CreateDefault([typeof(ThrowingControl).Assembly]);

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

    private static ValueTask<XamlLoadSession> Load(
        string xaml,
        XamlLoadMode mode = XamlLoadMode.Runtime,
        IXamlDispatcher? dispatcher = null) =>
        XamlLoadSession.CreateAsync(
            Parse(xaml),
            Environment(dispatcher),
            new XamlLoadOptions { Mode = mode },
            TestContext.Current.CancellationToken);

    private static ValueTask<XamlUpdateResult> Update(XamlLoadSession session, string xaml) =>
        session.ApplyDocumentUpdateAsync(Parse(xaml), TestContext.Current.CancellationToken);

    /// <summary>A control whose Limit refuses a negative value, beside one that takes anything.</summary>
    /// <remarks>
    /// Two members changed by one update, in this order. The first lands; the second is refused by
    /// the setter, which nothing could have known in advance — <c>-1</c> is a perfectly good
    /// <see cref="int"/>, so every check an update makes before writing passes.
    /// </remarks>
    private static string Pair(string label, string limit) =>
        $"<local:ThrowingControl xmlns=\"{AvaloniaNamespace}\"\n" +
        $"                         xmlns:local=\"{TestControlsNamespace}\"\n" +
        $"                         Tag=\"{label}\" ThrowsBeforeAssigning=\"{limit}\" />";

    [AvaloniaFact]
    public async Task ADocumentThatDoesNotParseIsRefusedWithoutTouchingAnything()
    {
        await using XamlLoadSession session = await Load($"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"10\" />");

        XamlUpdateResult result = await Update(session, "<Border");

        Assert.Equal(XamlUpdateOutcome.RejectedCleanly, result.Outcome);
        Assert.False(result.Applied);
        Assert.Equal(XamlSessionState.Usable, session.State);
        Assert.Equal(10d, session.GetRoot<Border>().Width);

        // And the session goes on working, which is the whole point of calling it clean.
        Assert.True((await Update(session, $"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"20\" />")).Applied);
        Assert.Equal(20d, session.GetRoot<Border>().Width);
    }

    [AvaloniaFact]
    public async Task TextTheMemberCannotHoldIsRefusedWithoutTouchingAnything()
    {
        await using XamlLoadSession session = await Load($"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"10\" />");

        XamlUpdateResult result = await Update(session, $"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"wide\" />");

        Assert.Equal(XamlUpdateOutcome.RejectedCleanly, result.Outcome);
        Assert.Equal(XamlSessionState.Usable, session.State);
        Assert.Equal(10d, session.GetRoot<Border>().Width);
        Assert.NotNull(session.PendingDocument);
    }

    [AvaloniaFact]
    public async Task AFragmentThatWillNotBuildIsRefusedWithoutTouchingAnything()
    {
        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <StackPanel><TextBlock Text=\"first\" /></StackPanel>\n" +
            "</Border>");

        var panel = (StackPanel)session.GetRoot<Border>().Child!;

        XamlUpdateResult result = await Update(
            session,
            $"<Border xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <StackPanel><TextBlock Text=\"first\" /><NoSuchControl /></StackPanel>\n" +
            "</Border>");

        // Every fragment is built before any object is touched, so one that will not build costs
        // nothing at all.
        Assert.Equal(XamlUpdateOutcome.RejectedCleanly, result.Outcome);
        Assert.Equal(XamlSessionState.Usable, session.State);
        Assert.Single(panel.Children);
    }

    [AvaloniaFact]
    public async Task AChangedRootIsRefusedCleanlyAndSaysSoThroughTheStrategy()
    {
        await using XamlLoadSession session = await Load($"<Border xmlns=\"{AvaloniaNamespace}\" />");

        XamlUpdateResult result = await Update(session, $"<StackPanel xmlns=\"{AvaloniaNamespace}\" />");

        // Two different questions, and the two properties answer one each: nothing was written, so
        // this session is as usable as it was — it is the new document that cannot be reached.
        Assert.Equal(XamlUpdateOutcome.RejectedCleanly, result.Outcome);
        Assert.Equal(XamlUpdateStrategy.RecreateSession, result.Strategy);
        Assert.Equal(XamlSessionState.Usable, session.State);
        Assert.IsType<Border>(session.RootObject);
    }

    [AvaloniaFact]
    public async Task ASetterThatThrowsOnTheVeryFirstWriteStillRequiresANewSession()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        var control = session.GetRoot<ThrowingControl>();

        XamlUpdateResult result = await Update(session, Pair("one", "-1"));

        // The only change the update carried, and it failed — but it failed *inside* the property
        // system, which had already been called. This one happens to validate before assigning;
        // knowing that means knowing how this property is written, and the next control along
        // assigns first. So the answer is the conservative one whatever the control does.
        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, result.Outcome);
        Assert.Equal(XamlSessionState.RequiresNewSession, session.State);
        Assert.Equal(5, control.ThrowsBeforeAssigning);

        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.SessionRequiresRecreation);
    }

    [AvaloniaFact]
    public async Task ASetterThatAssignsAndThenThrowsIsNotCleanEither()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        var control = session.GetRoot<ThrowingControl>();

        XamlUpdateResult result = await Update(
            session,
            $"<local:ThrowingControl xmlns=\"{AvaloniaNamespace}\"\n" +
            $"                       xmlns:local=\"{TestControlsNamespace}\"\n" +
            "                       Tag=\"one\" ThrowsBeforeAssigning=\"5\" AssignsThenThrows=\"x\" />");

        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, result.Outcome);
        Assert.Equal(XamlSessionState.RequiresNewSession, session.State);

        // The evidence that calling it clean would have been a lie: the value is on the object.
        Assert.Equal("x", control.AssignsThenThrows);
    }

    [AvaloniaFact]
    public async Task ASetterThatChangesSomethingElseAndThrowsIsNotCleanEither()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        var control = session.GetRoot<ThrowingControl>();

        XamlUpdateResult result = await Update(
            session,
            $"<local:ThrowingControl xmlns=\"{AvaloniaNamespace}\"\n" +
            $"                       xmlns:local=\"{TestControlsNamespace}\"\n" +
            "                       Tag=\"one\" ThrowsBeforeAssigning=\"5\" SpreadsThenThrows=\"y\" />");

        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, result.Outcome);
        Assert.Equal(XamlSessionState.RequiresNewSession, session.State);

        // And part of what changed is not even the property that was written, which is why
        // comparing the written property before and after could never have been the test.
        Assert.Equal("touched by y", control.Tag);
    }

    [AvaloniaFact]
    public async Task ACollectionThatEmptiesPartOfTheWayAndThrowsRequiresANewSession()
    {
        await using XamlLoadSession session = await Load(
            $"<local:BrittleHost xmlns=\"{AvaloniaNamespace}\" xmlns:local=\"{TestControlsNamespace}\">\n" +
            "  <Border />\n" +
            "  <Border />\n" +
            "</local:BrittleHost>");

        var host = session.GetRoot<BrittleHost>();

        Assert.Equal(2, host.Panels.Count);

        // A collection is not one assignment. Rebuilding this element's content empties it before
        // refilling it, and this one loses an item on the way out.
        host.Panels.Brittle = true;

        XamlUpdateResult result = await Update(
            session,
            $"<local:BrittleHost xmlns=\"{AvaloniaNamespace}\" xmlns:local=\"{TestControlsNamespace}\">\n" +
            "  <Border />\n" +
            "  <Border />\n" +
            "  <Border />\n" +
            "</local:BrittleHost>");

        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, result.Outcome);
        Assert.Equal(XamlSessionState.RequiresNewSession, session.State);
        Assert.Single(host.Panels);
    }

    [AvaloniaFact]
    public async Task NoDiagnosticClaimsNothingWasWrittenAfterASetterHasRun()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        XamlUpdateResult result = await Update(session, Pair("one", "-1"));

        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, result.Outcome);

        foreach (MarkupDiagnostic diagnostic in result.Diagnostics)
        {
            Assert.DoesNotContain("nothing was written", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("left as they were", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("were left", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("untouched", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [AvaloniaFact]
    public async Task AnEditWhoseSetterThrowsMarksTheSessionRatherThanCallingItRefused()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        var control = session.GetRoot<ThrowingControl>();

        XamlEditResult edit = session.SetValue(control, ThrowingControl.ThrowsBeforeAssigningProperty, -1);

        Assert.False(edit.Applied);
        Assert.Equal(XamlSessionState.RequiresNewSession, session.State);
        Assert.Contains(
            edit.Diagnostics,
            static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.SessionRequiresRecreation);

        // And from here nothing else is accepted, by either door.
        Assert.False(session.SetValue(control, ThrowingControl.ThrowsBeforeAssigningProperty, 2).Applied);
        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, (await Update(session, Pair("two", "6"))).Outcome);
    }

    [AvaloniaFact]
    public async Task ValueTheMemberCannotHoldIsStillRefusedBeforeAnySetterRuns()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        var control = session.GetRoot<ThrowingControl>();

        // The conversion is asked before anything is invoked, so this stays the clean case it was.
        XamlEditResult edit = session.SetXamlValue(
            control, ThrowingControl.ThrowsBeforeAssigningProperty, new XamlLiteralValue("not a number"));

        Assert.False(edit.Applied);
        Assert.Equal(XamlSessionState.Usable, session.State);
        Assert.Equal(5, control.ThrowsBeforeAssigning);

        Assert.Equal(
            XamlUpdateOutcome.RejectedCleanly,
            (await Update(session, Pair("one", "not a number"))).Outcome);

        Assert.Equal(XamlSessionState.Usable, session.State);
    }

    [AvaloniaFact]
    public async Task ASetterRefusingAfterAWriteHasLandedRequiresANewSession()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        var control = session.GetRoot<ThrowingControl>();

        XamlUpdateResult result = await Update(session, Pair("two", "-1"));

        // Tag was written and Limit was refused. The objects carry half of a document the session
        // never adopted, and no report may call that untouched.
        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, result.Outcome);
        Assert.False(result.Applied);
        Assert.Equal(XamlSessionState.RequiresNewSession, session.State);
        Assert.Equal("two", control.Tag);
        Assert.Equal(5, control.ThrowsBeforeAssigning);

        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.SessionRequiresRecreation);

        // And nothing in the diagnostics claims otherwise.
        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic => diagnostic.Message.Contains("left as they were", StringComparison.Ordinal)
                || diagnostic.Message.Contains("were left", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task ARebuildRefusedAfterAnEarlierOneLandedRequiresANewSession()
    {
        await using XamlLoadSession session = await Load(
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\">\n" +
            "  <StackPanel x:Name=\"First\"><TextBlock Text=\"a\" /></StackPanel>\n" +
            "  <ListBox x:Name=\"Rows\" />\n" +
            "</StackPanel>");

        var root = session.GetRoot<StackPanel>();
        var first = (StackPanel)root.Children[0];
        var rows = (ListBox)root.Children[1];

        // Items reflects ItemsSource once one is bound and refuses to be written through, so the
        // second rebuild will be refused — after the first has already replaced what it holds.
        rows.ItemsSource = new[] { "from the view model" };

        XamlUpdateResult result = await Update(
            session,
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\">\n" +
            "  <StackPanel x:Name=\"First\"><TextBlock Text=\"a\" /><TextBlock Text=\"b\" /></StackPanel>\n" +
            "  <ListBox x:Name=\"Rows\"><ListBoxItem Content=\"one\" /></ListBox>\n" +
            "</StackPanel>");

        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, result.Outcome);
        Assert.Equal(XamlSessionState.RequiresNewSession, session.State);

        // The evidence that it is not a clean refusal: the first rebuild is on the objects.
        Assert.Equal(2, first.Children.Count);
    }

    [AvaloniaFact]
    public async Task AnUpdateIsRefusedOnceTheSessionRequiresRecreation()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        Assert.Equal(
            XamlUpdateOutcome.RequiresNewSession,
            (await Update(session, Pair("two", "-1"))).Outcome);

        // A change that would otherwise be perfectly ordinary. The refusal is about the session,
        // not about the document.
        XamlUpdateResult later = await Update(session, Pair("three", "9"));

        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, later.Outcome);
        Assert.Equal(XamlUpdateStrategy.RecreateSession, later.Strategy);
        Assert.Equal("two", session.GetRoot<ThrowingControl>().Tag);

        Assert.Contains(
            later.Diagnostics,
            static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.SessionRequiresRecreation);
    }

    [AvaloniaFact]
    public async Task AnEditIsRefusedOnceTheSessionRequiresRecreation()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        Assert.Equal(
            XamlUpdateOutcome.RequiresNewSession,
            (await Update(session, Pair("two", "-1"))).Outcome);

        var control = session.GetRoot<ThrowingControl>();

        XamlEditResult edit = session.SetValue(control, ThrowingControl.ThrowsBeforeAssigningProperty, 3);

        Assert.False(edit.Applied);
        Assert.Equal(5, control.ThrowsBeforeAssigning);
        Assert.Contains(
            edit.Diagnostics,
            static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.SessionRequiresRecreation);

        // The XAML-aware direction is the same door.
        Assert.False(
            session.SetXamlValue(control, ThrowingControl.ThrowsBeforeAssigningProperty, new XamlLiteralValue("3")).Applied);
    }

    [AvaloniaFact]
    public async Task ASourceUpdateIsRefusedOnceTheSessionRequiresRecreation()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        Assert.Equal(
            XamlUpdateOutcome.RequiresNewSession,
            (await Update(session, Pair("two", "-1"))).Outcome);

        XamlUpdateResult result = await session.ApplySourceUpdateAsync(
            new Uri("file:///Themes/Colors.axaml"), TestContext.Current.CancellationToken);

        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, result.Outcome);
    }

    [AvaloniaFact]
    public async Task ThePendingDocumentIsWhatARecoveringCallerLoads()
    {
        await using XamlLoadSession broken = await Load(Pair("one", "5"));

        Assert.Equal(
            XamlUpdateOutcome.RequiresNewSession,
            (await Update(broken, Pair("two", "-1"))).Outcome);

        // What was offered is kept, because it is the state the caller was trying to reach and
        // the one the objects are now part-way towards.
        XamlDocument pending = Assert.IsType<XamlDocument>(broken.PendingDocument);

        Assert.Contains("Tag=\"two\"", pending.GetText(), StringComparison.Ordinal);

        // Loading it is what certainly restores agreement — but only once it is a document that
        // loads, which the caller has to correct first.
        XamlDocument corrected = Parse(Pair("two", "1"));

        await using XamlLoadSession fresh = await XamlLoadSession.CreateAsync(
            corrected, Environment(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(XamlSessionState.Usable, fresh.State);
        Assert.Equal("two", fresh.GetRoot<ThrowingControl>().Tag);
        Assert.Equal(1, fresh.GetRoot<ThrowingControl>().ThrowsBeforeAssigning);
        Assert.True((await Update(fresh, Pair("four", "2"))).Applied);
    }

    [AvaloniaFact]
    public async Task CancellationBeforeAnythingIsWrittenLeavesTheSessionUsable()
    {
        await using XamlLoadSession session = await Load($"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"10\" />");

        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await session.ApplyDocumentUpdateAsync(
                Parse($"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"20\" />"), cancellation.Token));

        Assert.Equal(XamlSessionState.Usable, session.State);
        Assert.Equal(10d, session.GetRoot<Border>().Width);

        // The gate was released on the way out, so the session still takes work.
        Assert.True((await Update(session, $"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"30\" />")).Applied);
    }

    [AvaloniaFact]
    public async Task CancellationAfterSomethingHasBeenWrittenRequiresANewSession()
    {
        var dispatcher = new ControllableDispatcher();

        await using XamlLoadSession session = await Load(
            $"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"10\" />",
            dispatcher: dispatcher);

        using var cancellation = new CancellationTokenSource();

        int loaded = dispatcher.Invocations;

        // An update dispatches twice: once to write the changes onto the objects, and once to
        // adopt the document they now describe. Cancelling as the second arrives is the only way
        // in from outside, and it is exactly the case the classification has to be conservative
        // about — the first one has already put the new width on the object.
        dispatcher.Before = ordinal =>
        {
            if (ordinal == loaded + 2)
            {
                cancellation.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await session.ApplyDocumentUpdateAsync(
                Parse($"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"20\" />"), cancellation.Token));

        // The width is on the object and the session's document never advanced, which is exactly
        // the disagreement this state exists to report.
        Assert.Equal(XamlSessionState.RequiresNewSession, session.State);
        Assert.Equal(20d, ((Border)session.RootObject).Width);
        Assert.Contains("Width=\"10\"", session.Document.GetText(), StringComparison.Ordinal);

        // And the way out is kept. The guides tell a caller to build the replacement session from
        // PendingDocument; a caller told that has to be given something to do it with.
        XamlDocument pending = Assert.IsType<XamlDocument>(session.PendingDocument);

        Assert.Contains("Width=\"20\"", pending.GetText(), StringComparison.Ordinal);

        await using XamlLoadSession fresh = await XamlLoadSession.CreateAsync(
            pending, Environment(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(XamlSessionState.Usable, fresh.State);
        Assert.Equal(20d, fresh.GetRoot<Border>().Width);
    }

    [AvaloniaFact]
    public async Task AFailureAfterAWriteKeepsTheDocumentItWasMovingTowards()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        string wanted = Pair("two", "-1");

        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, (await Update(session, wanted)).Outcome);

        // Not only cancellation: every way of stopping after the first write leaves the same
        // recovery document, because the caller's way out of all of them is the same.
        Assert.Equal(wanted, Assert.IsType<XamlDocument>(session.PendingDocument).GetText());
    }

    [AvaloniaFact]
    public async Task ALaterRefusalDoesNotReplaceTheDocumentThatBrokeTheSession()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        string wanted = Pair("two", "-1");

        Assert.Equal(XamlUpdateOutcome.RequiresNewSession, (await Update(session, wanted)).Outcome);

        // Something else arrives afterwards and is refused. It has no claim on the answer to
        // "what should I load instead": the objects are part-way towards the first document, not
        // this one.
        Assert.Equal(
            XamlUpdateOutcome.RequiresNewSession,
            (await Update(session, Pair("three", "9"))).Outcome);

        Assert.Equal(wanted, Assert.IsType<XamlDocument>(session.PendingDocument).GetText());

        await session.ApplySourceUpdateAsync(
            new Uri("file:///Themes/Colors.axaml"), TestContext.Current.CancellationToken);

        Assert.Equal(wanted, Assert.IsType<XamlDocument>(session.PendingDocument).GetText());
    }

    [AvaloniaFact]
    public async Task AnAppliedUpdateClearsADocumentAnEarlierCleanRefusalLeftBehind()
    {
        await using XamlLoadSession session = await Load($"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"10\" />");

        Assert.Equal(
            XamlUpdateOutcome.RejectedCleanly,
            (await Update(session, $"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"wide\" />")).Outcome);

        Assert.NotNull(session.PendingDocument);

        Assert.True((await Update(session, $"<Border xmlns=\"{AvaloniaNamespace}\" Width=\"20\" />")).Applied);

        // Nothing is pending once a whole update has landed: the document and the objects agree
        // again, and there is nothing left to recover to.
        Assert.Null(session.PendingDocument);
    }

    [AvaloniaFact]
    public async Task AppliedNeverDisagreesWithTheOutcome()
    {
        await using XamlLoadSession session = await Load(Pair("one", "5"));

        XamlUpdateResult applied = await Update(session, Pair("one", "6"));

        Assert.True(applied.Applied);
        Assert.Equal(XamlUpdateOutcome.Applied, applied.Outcome);

        XamlUpdateResult refused = await Update(session, Pair("one", "wide"));

        Assert.False(refused.Applied);
        Assert.NotEqual(XamlUpdateOutcome.Applied, refused.Outcome);
    }
}
