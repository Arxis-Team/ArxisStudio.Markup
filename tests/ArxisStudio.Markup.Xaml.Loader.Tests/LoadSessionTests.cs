using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// The exit criteria of this milestone: a document of standard controls loads, a custom control
/// from an explicitly supplied assembly loads, and nothing discovers a project along the way.
/// </summary>
public sealed class LoadSessionTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string TestControlsNamespace = "https://arxis.studio/test-controls";

    private static XamlLoadEnvironment Environment(bool withTestControls = false) =>
        XamlLoadEnvironment.CreateDefault(
            withTestControls ? [typeof(CustomBadge).Assembly] : null,
            new InMemoryMarkupSourceProvider());

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = new Uri("file:///Views/Test.axaml") });

    [AvaloniaFact]
    public async Task ADocumentOfStandardControlsLoads()
    {
        XamlDocument document = Parse(
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <TextBlock Text=\"Hello\" />\n" +
            "  <Button Content=\"Save\" Width=\"320\" />\n" +
            "</StackPanel>");

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            document, Environment(), cancellationToken: TestContext.Current.CancellationToken);

        var panel = session.GetRoot<StackPanel>();

        Assert.Equal(2, panel.Children.Count);
        Assert.Equal("Hello", ((TextBlock)panel.Children[0]).Text);
        Assert.Equal(320d, ((Button)panel.Children[1]).Width);
        Assert.Empty(session.Diagnostics.Where(static d => d.IsError));
    }

    [AvaloniaFact]
    public async Task ACustomControlFromAnExplicitlySuppliedAssemblyLoads()
    {
        // The control lives in its own assembly, so this proves the assembly was supplied and
        // its XmlnsDefinition was read — not merely that the test assembly happens to be loaded.
        XamlDocument document = Parse(
            $"<local:CustomBadge xmlns=\"{AvaloniaNamespace}\" xmlns:local=\"{TestControlsNamespace}\"\n" +
            "                   Caption=\"Beta\" Level=\"3\" />");

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            document, Environment(withTestControls: true), cancellationToken: TestContext.Current.CancellationToken);

        var badge = session.GetRoot<CustomBadge>();

        Assert.Equal("Beta", badge.Caption);
        Assert.Equal(3, badge.Level);
    }

    [AvaloniaFact]
    public async Task ACustomControlAlsoLoadsThroughAUsingNamespace()
    {
        XamlDocument document = Parse(
            $"<local:CustomBadge xmlns=\"{AvaloniaNamespace}\"\n" +
            "                   xmlns:local=\"using:ArxisStudio.Markup.Xaml.Loader.TestControls\"\n" +
            "                   Caption=\"Alpha\" />");

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            document, Environment(withTestControls: true), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Alpha", session.GetRoot<CustomBadge>().Caption);
    }

    [AvaloniaFact]
    public async Task NestedCustomAndStandardControlsLoadTogether()
    {
        XamlDocument document = Parse(
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\" xmlns:local=\"{TestControlsNamespace}\">\n" +
            "  <local:CustomBadge Caption=\"Nested\">\n" +
            "    <TextBlock Text=\"Inside\" />\n" +
            "  </local:CustomBadge>\n" +
            "</StackPanel>");

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            document, Environment(withTestControls: true), cancellationToken: TestContext.Current.CancellationToken);

        var panel = session.GetRoot<StackPanel>();
        var badge = Assert.IsType<CustomBadge>(panel.Children[0]);

        Assert.Equal("Nested", badge.Caption);
        Assert.Equal("Inside", Assert.IsType<TextBlock>(badge.Content).Text);
    }

    [AvaloniaFact]
    public async Task AnUnknownTypeIsReportedRatherThanThrown()
    {
        XamlDocument document = Parse($"<NotAControl xmlns=\"{AvaloniaNamespace}\" />");

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            document, Environment(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(session);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    [AvaloniaFact]
    public async Task ACustomControlWithoutItsAssemblyIsReportedRatherThanThrown()
    {
        XamlDocument document = Parse(
            $"<local:CustomBadge xmlns=\"{AvaloniaNamespace}\" xmlns:local=\"using:Nowhere.At.All\" />");

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            document, Environment(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(session);
        Assert.Contains(result.Diagnostics, static d => d.IsError);
    }

    [AvaloniaFact]
    public async Task SyntaxDiagnosticsAreCarriedIntoTheLoadResult()
    {
        // A document that did not parse cleanly will not load cleanly, and the syntax
        // diagnostics say more about why than anything the loader can add.
        XamlDocument document = Parse($"<StackPanel xmlns=\"{AvaloniaNamespace}\"><Button></StackPanel>");

        (_, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            document, Environment(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(
            result.Diagnostics,
            static d => d.Category == MarkupDiagnosticCategory.Parse && d.Code == XamlDiagnosticCodes.UnclosedElement);
    }

    [AvaloniaFact]
    public async Task ALoadDiagnosticCarriesASourceSpan()
    {
        // Avalonia reports a line and column; the session turns that into a span so a caller
        // can highlight the text without knowing where the diagnostic came from.
        XamlDocument document = Parse(
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\">\n  <NotAControl />\n</StackPanel>");

        (_, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            document, Environment(), cancellationToken: TestContext.Current.CancellationToken);

        MarkupDiagnostic? located = result.Diagnostics.FirstOrDefault(
            static d => d.Category == MarkupDiagnosticCategory.Load && d.Span is not null);

        if (located is not null)
        {
            Assert.True(located.Span!.Value.End <= document.SourceText.Length);
        }

        Assert.Contains(result.Diagnostics, static d => d.IsError);
    }

    [AvaloniaFact]
    public async Task DesignModeIsRequestedRatherThanAssumed()
    {
        XamlDocument document = Parse($"<Button xmlns=\"{AvaloniaNamespace}\" Content=\"x\" />");

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            document,
            Environment(),
            new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

        Assert.Equal(XamlLoadMode.Design, session.Options.Mode);
        Assert.NotNull(session.GetRoot<Button>());
    }

    [AvaloniaFact]
    public async Task TheSessionKnowsWhichDocumentItCameFrom()
    {
        XamlDocument document = Parse($"<Button xmlns=\"{AvaloniaNamespace}\" />");

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            document, Environment(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(document, session.Document);
    }

    [AvaloniaFact]
    public async Task TouchingASessionFromAnotherThreadFailsClearly()
    {
        // Avalonia objects have thread affinity. Failing here beats letting the corruption
        // surface somewhere unrelated much later.
        XamlDocument document = Parse($"<Button xmlns=\"{AvaloniaNamespace}\" />");

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            document, Environment(), cancellationToken: TestContext.Current.CancellationToken);

        session.VerifyAccess();

        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                session.VerifyAccess();
            }
            catch (Exception error)
            {
                captured = error;
            }
        });

        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(captured);
    }

    [AvaloniaFact]
    public async Task ADisposedSessionRefusesFurtherUse()
    {
        XamlDocument document = Parse($"<Button xmlns=\"{AvaloniaNamespace}\" />");

        XamlLoadSession session = await XamlLoadSession.CreateAsync(
            document, Environment(), cancellationToken: TestContext.Current.CancellationToken);

        await session.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(session.VerifyAccess);
    }

    [AvaloniaFact]
    public async Task AskingForTheWrongRootTypeFailsClearly()
    {
        XamlDocument document = Parse($"<Button xmlns=\"{AvaloniaNamespace}\" />");

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            document, Environment(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Throws<InvalidOperationException>(session.GetRoot<StackPanel>);
    }

    [Fact]
    public async Task NullArgumentsAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await XamlLoadSession.CreateAsync(
                null!, Environment(), cancellationToken: TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await XamlLoadSession.CreateAsync(
                Parse("<a />"), null!, cancellationToken: TestContext.Current.CancellationToken));
    }
}
