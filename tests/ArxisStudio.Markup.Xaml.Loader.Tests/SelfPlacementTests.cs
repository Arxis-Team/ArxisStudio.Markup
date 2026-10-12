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
/// A form that places its own class inside itself is shown without the placement, and never takes the
/// process down.
/// </summary>
/// <remarks>
/// <para>
/// A data context made of the form — <c>&lt;UserControl.DataContext&gt;&lt;local:Self/&gt;&lt;/UserControl.DataContext&gt;</c>
/// — is the shape this takes in practice. Avalonia's compiler builds it, and constructing it constructs
/// it again inside its own population, for as long as there is stack (ADR 0032).
/// </para>
/// <para>
/// <see cref="SelfPlacingView"/> is that form, compiled. Its constructor throws past a depth limit, so
/// a path that hands it to its compiled markup fails a test here rather than overflowing the stack of
/// the process running them.
/// </para>
/// </remarks>
public sealed class SelfPlacementTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string ControlsNamespace = "using:ArxisStudio.Markup.Xaml.Loader.TestControls";

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault(
            [typeof(SelfPlacingView).Assembly], new InMemoryMarkupSourceProvider());

    /// <summary>The view's document, as somebody editing it has it — the placement written or not.</summary>
    private static XamlDocument View(string content, bool placesItself = true) => XamlDocument.Parse(
        $"<UserControl xmlns=\"{AvaloniaNamespace}\"\n" +
        $"             xmlns:x=\"{XamlNamespaces.Xaml}\"\n" +
        $"             xmlns:tc=\"{ControlsNamespace}\"\n" +
        $"             x:Class=\"{typeof(SelfPlacingView).FullName}\">\n" +
        (placesItself
            ? "    <UserControl.DataContext>\n" +
              "        <tc:SelfPlacingView />\n" +
              "    </UserControl.DataContext>\n"
            : string.Empty) +
        $"    {content}\n" +
        "</UserControl>",
        new XamlParseOptions { DocumentUri = new Uri("file:///Views/SelfPlacingView.axaml") });

    private static XamlDocument LiveControlDocument(string content) => XamlDocument.Parse(
        $"<UserControl xmlns=\"{AvaloniaNamespace}\"\n" +
        $"             xmlns:x=\"{XamlNamespaces.Xaml}\"\n" +
        $"             xmlns:tc=\"{ControlsNamespace}\"\n" +
        $"             x:Class=\"{typeof(LiveControl).FullName}\">\n" +
        $"    {content}\n" +
        "</UserControl>",
        new XamlParseOptions { DocumentUri = new Uri("file:///Views/LiveControl.axaml") });

    [AvaloniaFact]
    public void AProjectWhoseMarkupPlacesItself_BuildsAndConstructsItselfWithoutEnd()
    {
        // What ADR 0014 assumed could not happen: the compiler builds it. Run with nothing standing in for
        // the compiled markup, it constructs itself until the fixture's limit — and without one, until the
        // stack gives out.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(static () => new SelfPlacingView());

        Assert.Contains(nameof(SelfPlacingView), error.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ASessionLeavesThePlacementOut_AndSaysWhereItIs()
    {
        XamlDocument document = View("<TextBlock Text=\"Live\" />");

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            document, Environment(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(session is not null, string.Join(" | ", result.Diagnostics));

        await using (session)
        {
            var view = session.GetRoot<SelfPlacingView>();

            // The rest of the form is there; the copy it would have made of itself is not.
            Assert.Null(view.DataContext);
            Assert.Equal("Live", Assert.IsType<TextBlock>(view.Content).Text);

            XamlElement placed = document.DescendantElements()
                .Single(static element => element.Name.LocalName == nameof(SelfPlacingView));

            MarkupDiagnostic reported = Assert.Single(
                result.Diagnostics, static d => d.Code == XamlLoaderDiagnosticCodes.SelfPlacement);

            // An error: the application written this way stops with a stack overflow.
            Assert.Equal(MarkupDiagnosticSeverity.Error, reported.Severity);
            Assert.Equal<TextSpan?>(placed.NameSpan, reported.Span);
        }
    }

    [AvaloniaFact]
    public async Task ARegistrationLeavesThePlacementOutOfEveryCopy()
    {
        using var population = new XamlLivePopulation(Environment());

        XamlLivePopulationResult result = await population.SetDocumentAsync(
            typeof(SelfPlacingView), View("<TextBlock Text=\"Live\" />"), TestContext.Current.CancellationToken);

        Assert.True(result.Installed);
        Assert.Contains(result.Diagnostics, static d => d.Code == XamlLoaderDiagnosticCodes.SelfPlacement);

        var view = new SelfPlacingView();

        Assert.Null(view.DataContext);
        Assert.Equal("Live", Assert.IsType<TextBlock>(view.Content).Text);
    }

    [AvaloniaFact]
    public async Task ACopyOfTheRootConstructedInsideIt_IsLeftEmpty_WithoutARegistrationToAsk()
    {
        XamlLoadEnvironment environment = Environment();

        using var population = new XamlLivePopulation(environment);

        // The form places a control whose document places the form: a cycle through another document,
        // which the form's own checks cannot see. Nothing is registered for the form itself, so the copy
        // goes through the root's own loan.
        await population.SetDocumentAsync(
            typeof(LiveControl), LiveControlDocument("<tc:SelfPlacingView />"), TestContext.Current.CancellationToken);

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            View("<tc:LiveControl />", placesItself: false), environment,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(session is not null, string.Join(" | ", result.Diagnostics));

        await using (session)
        {
            var view = session.GetRoot<SelfPlacingView>();
            var placed = Assert.IsType<LiveControl>(view.Content);
            var copy = Assert.IsType<SelfPlacingView>(placed.Content);

            // Empty — not the compiled markup, which would have constructed it again until the limit.
            Assert.Null(copy.Content);
            Assert.Null(copy.DataContext);

            Assert.Contains(result.Diagnostics, static d => d.Code == XamlLoaderDiagnosticCodes.LivePopulationCycle);
        }
    }

    [AvaloniaFact]
    public async Task ATypeFoundPlacingItself_IsNotHandedToItsCompiledMarkupWhenItsDocumentFails()
    {
        using var population = new XamlLivePopulation(Environment());

        var failures = new List<XamlLivePopulationFailedEventArgs>();

        population.PopulationFailed += (_, args) => failures.Add(args);

        // The form places itself through another document, and then its own document fails further on —
        // a value its control refuses. Its compiled markup is what a failed document falls back to, and
        // for this type it is exactly what must not run.
        await population.SetDocumentAsync(
            typeof(LiveControl), LiveControlDocument("<tc:SelfPlacingView />"), TestContext.Current.CancellationToken);

        await population.SetDocumentAsync(
            typeof(SelfPlacingView),
            View(
                "<StackPanel><tc:LiveControl /><tc:ThrowingControl AssignsThenThrows=\"now\" /></StackPanel>",
                placesItself: false),
            TestContext.Current.CancellationToken);

        // Constructing it does not throw: the compiled markup, which would have constructed it until the
        // fixture's limit, is not run. What the instance holds is what its document built before it failed.
        var view = new SelfPlacingView();

        Assert.IsNotType<TextBlock>(view.Content);

        Assert.Contains(
            failures,
            static failure => failure.ControlType == typeof(SelfPlacingView)
                && failure.Diagnostics.Any(static d => d.Code == XamlLoaderDiagnosticCodes.LivePopulationFailed));

        Assert.Contains(
            failures,
            static failure => failure.Diagnostics.Any(static d => d.Code == XamlLoaderDiagnosticCodes.LivePopulationCycle));
    }
}
