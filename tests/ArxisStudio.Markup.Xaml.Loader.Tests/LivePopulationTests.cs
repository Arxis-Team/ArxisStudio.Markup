using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// A compiled control's instances follow a registered live document, and stop following it when
/// the registration goes.
/// </summary>
/// <remarks>
/// These tests construct the controls directly, the way Avalonia's loader does when a document
/// places one — which is the whole point: nothing between <c>new</c> and the populated content
/// belongs to this library except the override it installed.
/// </remarks>
public sealed class LivePopulationTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string ControlsNamespace = "using:ArxisStudio.Markup.Xaml.Loader.TestControls";

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault(
            [typeof(LiveControl).Assembly], new InMemoryMarkupSourceProvider());

    private static XamlDocument LiveControlDocument(string content) => XamlDocument.Parse(
        $"<UserControl xmlns=\"{AvaloniaNamespace}\"\n" +
        $"             xmlns:x=\"{XamlNamespaces.Xaml}\"\n" +
        $"             xmlns:tc=\"{ControlsNamespace}\"\n" +
        "             x:Class=\"ArxisStudio.Markup.Xaml.Loader.TestControls.LiveControl\">\n" +
        $"    {content}\n" +
        "</UserControl>",
        new XamlParseOptions { DocumentUri = new Uri("file:///Views/LiveControl.axaml") });

    private static XamlDocument HostDocument(string content) => XamlDocument.Parse(
        $"<UserControl xmlns=\"{AvaloniaNamespace}\"\n" +
        $"             xmlns:x=\"{XamlNamespaces.Xaml}\"\n" +
        $"             xmlns:tc=\"{ControlsNamespace}\"\n" +
        "             x:Class=\"ArxisStudio.Markup.Xaml.Loader.TestControls.LiveHostControl\">\n" +
        $"    {content}\n" +
        "</UserControl>",
        new XamlParseOptions { DocumentUri = new Uri("file:///Views/LiveHostControl.axaml") });

    /// <summary>The one text the compiled markup shows, asserted against often enough to name.</summary>
    private static string ShownText(UserControl control)
    {
        Control? content = control.Content as Control;

        while (content is not null)
        {
            switch (content)
            {
                case TextBlock text:
                    return text.Text ?? string.Empty;

                case Panel panel:
                    content = panel.Children.FirstOrDefault();
                    continue;

                case Decorator decorator:
                    content = decorator.Child;
                    continue;

                default:
                    return content.GetType().Name;
            }
        }

        return string.Empty;
    }

    [AvaloniaFact]
    public async Task ARegisteredDocument_IsWhatNewInstancesShow_AndRemovalPutsTheCompiledMarkupBack()
    {
        using var population = new XamlLivePopulation(Environment());

        Assert.Equal("Compiled", ShownText(new LiveControl()));

        XamlLivePopulationResult result = await population.SetDocumentAsync(
            typeof(LiveControl),
            LiveControlDocument("<TextBlock Text=\"Live\" />"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(result.Installed);
        Assert.True(population.Contains(typeof(LiveControl)));

        Assert.Equal("Live", ShownText(new LiveControl()));

        Assert.True(population.Remove(typeof(LiveControl)));
        Assert.False(population.Contains(typeof(LiveControl)));

        Assert.Equal("Compiled", ShownText(new LiveControl()));
    }

    [AvaloniaFact]
    public async Task RegisteringAgain_ReplacesWhatInstancesShow()
    {
        using var population = new XamlLivePopulation(Environment());

        await population.SetDocumentAsync(
            typeof(LiveControl),
            LiveControlDocument("<TextBlock Text=\"First\" />"),
            TestContext.Current.CancellationToken);

        await population.SetDocumentAsync(
            typeof(LiveControl),
            LiveControlDocument("<TextBlock Text=\"Second\" />"),
            TestContext.Current.CancellationToken);

        Assert.Equal("Second", ShownText(new LiveControl()));
    }

    [AvaloniaFact]
    public async Task Disposal_PutsTheCompiledMarkupBack()
    {
        var population = new XamlLivePopulation(Environment());

        await population.SetDocumentAsync(
            typeof(LiveControl),
            LiveControlDocument("<TextBlock Text=\"Live\" />"),
            TestContext.Current.CancellationToken);

        population.Dispose();

        Assert.Equal("Compiled", ShownText(new LiveControl()));

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await population.SetDocumentAsync(
            typeof(LiveControl),
            LiveControlDocument("<TextBlock Text=\"Late\" />"),
            TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task ATypeWithoutCompiledMarkup_IsRefused()
    {
        using var population = new XamlLivePopulation(Environment());

        // CustomerView is a real control, but nothing compiled markup into it — there is no
        // populate to stand in for.
        XamlLivePopulationResult result = await population.SetDocumentAsync(
            typeof(CustomerView),
            LiveControlDocument("<TextBlock Text=\"Live\" />"),
            TestContext.Current.CancellationToken);

        Assert.False(result.Installed);
        Assert.False(result.Success);
        Assert.False(population.Contains(typeof(CustomerView)));

        MarkupDiagnostic reported = Assert.Single(
            result.Diagnostics, static d => d.Code == XamlLoaderDiagnosticCodes.NotPopulatable);

        Assert.Equal(MarkupDiagnosticSeverity.Error, reported.Severity);
    }

    [AvaloniaFact]
    public async Task ADocumentNamingAnotherClass_WarnsAndFallsBackToTheCompiledMarkup()
    {
        using var population = new XamlLivePopulation(Environment());

        var failures = new List<XamlLivePopulationFailedEventArgs>();

        population.PopulationFailed += (_, args) => failures.Add(args);

        XamlDocument foreign = XamlDocument.Parse(
            $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\"\n" +
            "             x:Class=\"Some.Other.View\">\n" +
            "    <TextBlock Text=\"Live\" />\n" +
            "</UserControl>");

        XamlLivePopulationResult result = await population.SetDocumentAsync(
            typeof(LiveControl), foreign, TestContext.Current.CancellationToken);

        // The registration stands — the document is the caller's to fix — but the warning says
        // up front what population will then prove: Avalonia refuses to populate an instance
        // from a document naming another class.
        Assert.True(result.Installed);
        Assert.Contains(
            result.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.LivePopulationClassMismatch
                && d.Severity == MarkupDiagnosticSeverity.Warning);

        Assert.Equal("Compiled", ShownText(new LiveControl()));
        Assert.NotEmpty(failures);
    }

    [AvaloniaFact]
    public async Task ADocumentThatDoesNotCompile_FallsBackToTheCompiledMarkup_AndSaysSo()
    {
        using var population = new XamlLivePopulation(Environment());

        var failures = new List<XamlLivePopulationFailedEventArgs>();

        population.PopulationFailed += (_, args) => failures.Add(args);

        // The document is well-formed XML naming a type that does not exist, which is what a
        // document mid-edit routinely is.
        XamlLivePopulationResult result = await population.SetDocumentAsync(
            typeof(LiveControl),
            LiveControlDocument("<NoSuchControlAnywhere />"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Installed);

        Assert.Equal("Compiled", ShownText(new LiveControl()));

        XamlLivePopulationFailedEventArgs failure = Assert.Single(failures);

        Assert.Equal(typeof(LiveControl), failure.ControlType);
        Assert.Contains(
            failure.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.LivePopulationFailed && d.IsError);
    }

    [AvaloniaFact]
    public async Task AControlPlacedInsideAnotherLiveDocument_IsPopulatedFromItsOwn()
    {
        using var population = new XamlLivePopulation(Environment());

        await population.SetDocumentAsync(
            typeof(LiveControl),
            LiveControlDocument("<TextBlock Text=\"LiveInner\" />"),
            TestContext.Current.CancellationToken);

        await population.SetDocumentAsync(
            typeof(LiveHostControl),
            HostDocument("<tc:LiveControl />"),
            TestContext.Current.CancellationToken);

        // Constructing the host runs its live document; that document places the inner control,
        // whose construction runs its live document in turn. This is the designer scenario: a
        // form placing a project's own control shows the control as it is being edited.
        var host = new LiveHostControl();

        var inner = Assert.IsType<LiveControl>(host.Content);

        Assert.Equal("LiveInner", ShownText(inner));
    }

    [AvaloniaFact]
    public async Task TwoDocumentsPlacingEachOther_TerminateAtTheCompiledMarkup()
    {
        using var population = new XamlLivePopulation(Environment());

        var failures = new List<XamlLivePopulationFailedEventArgs>();

        population.PopulationFailed += (_, args) => failures.Add(args);

        await population.SetDocumentAsync(
            typeof(LiveControl),
            LiveControlDocument("<tc:LiveHostControl />"),
            TestContext.Current.CancellationToken);

        await population.SetDocumentAsync(
            typeof(LiveHostControl),
            HostDocument("<tc:LiveControl />"),
            TestContext.Current.CancellationToken);

        // Host places control places host: only live documents can state this cycle, because a
        // project whose compiled markup contained it would never have built. Construction has to
        // bottom out rather than overflow, and where it bottoms out is the compiled markup.
        var host = new LiveHostControl();

        var inner = Assert.IsType<LiveControl>(host.Content);
        var innermost = Assert.IsType<LiveHostControl>(inner.Content);

        // The innermost host is the compiled one — a Border, not another LiveControl.
        Assert.IsType<Border>(innermost.Content);

        Assert.Contains(
            failures,
            static failure => failure.Diagnostics.Any(
                static d => d.Code == XamlLoaderDiagnosticCodes.LivePopulationCycle));
    }
}
