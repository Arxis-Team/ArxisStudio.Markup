using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// The exit criteria of this milestone: nested and cyclic resource graphs behave, and nothing
/// here needs Avalonia to run.
/// </summary>
public sealed class ResourceGraphTests
{
    private static readonly Uri App = new("file:///App.axaml");
    private static readonly Uri Generic = new("file:///Themes/Generic.axaml");
    private static readonly Uri Colors = new("file:///Themes/Colors.axaml");
    private static readonly Uri Fonts = new("file:///Themes/Fonts.axaml");

    private static InMemoryMarkupSourceProvider Provider(params (Uri Uri, string Text)[] documents)
    {
        var provider = new InMemoryMarkupSourceProvider();

        foreach ((Uri uri, string text) in documents)
        {
            provider.Update(uri, text);
        }

        return provider;
    }

    private static string Includes(params string[] sources) =>
        "<Styles>\n" + string.Concat(sources.Select(static s => $"  <StyleInclude Source=\"{s}\" />\n")) + "</Styles>\n";

    [Fact]
    public void IncludesAreDiscoveredWithTheirKindAndSourceText()
    {
        XamlDocument document = XamlDocument.Parse(
            "<Styles>\n" +
            "  <StyleInclude Source=\"avares://Controls/Themes/Generic.axaml\" />\n" +
            "  <ResourceInclude Source=\"../Themes/Colors.axaml\" />\n" +
            "</Styles>",
            new XamlParseOptions { DocumentUri = App });

        ImmutableArray<XamlResourceReference> references = document.GetResourceReferences();

        Assert.Equal(2, references.Length);
        Assert.Equal(XamlResourceReferenceKind.StyleInclude, references[0].Kind);
        Assert.Equal(XamlResourceReferenceKind.ResourceInclude, references[1].Kind);
        Assert.Equal("avares://Controls/Themes/Generic.axaml", references[0].SourceText);
    }

    [Fact]
    public void RelativeSourcesResolveAgainstTheDocumentsBaseUri()
    {
        XamlDocument document = XamlDocument.Parse(
            Includes("Colors.axaml", "../Shared/Fonts.axaml"),
            new XamlParseOptions { DocumentUri = new Uri("file:///Views/Themes/App.axaml") });

        ImmutableArray<XamlResourceReference> references = document.GetResourceReferences();

        Assert.Equal(new Uri("file:///Views/Themes/Colors.axaml"), references[0].ResolvedUri);
        Assert.Equal(new Uri("file:///Views/Shared/Fonts.axaml"), references[1].ResolvedUri);
    }

    [Fact]
    public void AvaresUrisAreResolvedWithoutBeingRewritten()
    {
        // The scheme is Avalonia's, and this package knows nothing about what it addresses.
        // It has to survive intact all the same.
        XamlDocument document = XamlDocument.Parse(
            Includes("avares://Controls/Themes/Generic.axaml"),
            new XamlParseOptions { DocumentUri = App });

        XamlResourceReference reference = document.GetResourceReferences().Single();

        Assert.Equal("avares://Controls/Themes/Generic.axaml", reference.SourceText);
        Assert.True(XamlUri.IsAvaloniaResource(reference.ResolvedUri!));

        // System.Uri lower-cases the host, which for avares is the assembly name. Rendering
        // through the package's own helper keeps the case the author wrote.
        Assert.Equal("avares://Controls/Themes/Generic.axaml", XamlUri.ToDisplayString(reference.ResolvedUri!));
        Assert.Equal("avares://controls/Themes/Generic.axaml", reference.ResolvedUri!.ToString());
    }

    [Fact]
    public void AvaresUrisDifferingOnlyInAssemblyCaseAreDifferentDocuments()
    {
        // Avalonia's assembly names are case-sensitive, so merging these would silently mix
        // two different assemblies' resources.
        var upper = new Uri("avares://Controls/Themes/Generic.axaml");
        var lower = new Uri("avares://controls/Themes/Generic.axaml");

        Assert.Equal(upper, lower);                            // System.Uri says they match
        Assert.False(XamlUri.Comparer.Equals(upper, lower));   // this package says otherwise
        Assert.NotEqual(XamlUri.ToKey(upper), XamlUri.ToKey(lower));
    }

    [Fact]
    public void AbsoluteSourcesArePreservedRatherThanRebased()
    {
        XamlDocument document = XamlDocument.Parse(
            Includes("file:///Elsewhere/Other.axaml"),
            new XamlParseOptions { DocumentUri = App });

        Assert.Equal(new Uri("file:///Elsewhere/Other.axaml"), document.GetResourceReferences()[0].ResolvedUri);
    }

    [Fact]
    public void AnExplicitBaseUriOverridesTheDocumentsOwnLocation()
    {
        // An unsaved buffer standing in for a file resolves as if it sat where the file does.
        XamlDocument document = XamlDocument.Parse(
            Includes("Colors.axaml"),
            new XamlParseOptions
            {
                DocumentUri = new Uri("inmemory:///buffer-7"),
                BaseUri = new Uri("file:///Themes/App.axaml"),
            });

        Assert.Equal(Colors, document.GetResourceReferences()[0].ResolvedUri);
    }

    [Fact]
    public void ARelativeSourceWithNoBaseIsReportedRatherThanGuessed()
    {
        XamlDocument document = XamlDocument.Parse(Includes("Colors.axaml"));

        XamlResourceAnalyzer.Discover(document, out ImmutableArray<MarkupDiagnostic> diagnostics);

        Assert.Contains(diagnostics, static d => d.Code == XamlDiagnosticCodes.UnresolvedIncludeUri);
        Assert.Null(document.GetResourceReferences()[0].ResolvedUri);
    }

    [Fact]
    public void AnIncludeWithNoSourceIsReported()
    {
        XamlDocument document = XamlDocument.Parse(
            "<Styles><StyleInclude /></Styles>", new XamlParseOptions { DocumentUri = App });

        XamlResourceAnalyzer.Discover(document, out ImmutableArray<MarkupDiagnostic> diagnostics);

        Assert.Contains(diagnostics, static d => d.Code == XamlDiagnosticCodes.MissingIncludeSource);
    }

    [Fact]
    public void ASourceWrittenAsAMarkupExtensionIsKeptButContributesNoEdge()
    {
        // What it resolves to is a runtime question. Recording it keeps the document writable
        // back; leaving the edge out keeps the graph honest.
        XamlDocument document = XamlDocument.Parse(
            Includes("{DynamicResource ThemePath}"), new XamlParseOptions { DocumentUri = App });

        XamlResourceReference reference = document.GetResourceReferences().Single();

        Assert.Equal("{DynamicResource ThemePath}", reference.SourceText);
        Assert.Null(reference.ResolvedUri);
    }

    [Fact]
    public void IncludesInsideMergedDictionariesAreFound()
    {
        XamlDocument document = XamlDocument.Parse(
            "<ResourceDictionary>\n" +
            "  <ResourceDictionary.MergedDictionaries>\n" +
            "    <ResourceInclude Source=\"Themes/Colors.axaml\" />\n" +
            "  </ResourceDictionary.MergedDictionaries>\n" +
            "</ResourceDictionary>",
            new XamlParseOptions { DocumentUri = App });

        Assert.Equal(Colors, document.GetResourceReferences().Single().ResolvedUri);
    }

    [Fact]
    public async Task NestedIncludesAreFollowedToTheirDepth()
    {
        var graph = new XamlResourceGraph(Provider(
            (App, Includes("Themes/Generic.axaml")),
            (Generic, Includes("Colors.axaml")),
            (Colors, Includes("Fonts.axaml")),
            (Fonts, "<Styles />")));

        XamlResourceGraphResult result = await graph.BuildAsync(App, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.False(result.HasCycles);
        Assert.Equal(
            new[] { App, Generic, Colors, Fonts }.OrderBy(static u => u.ToString(), StringComparer.Ordinal),
            result.Documents.OrderBy(static u => u.ToString(), StringComparer.Ordinal));
    }

    [Fact]
    public async Task TransitiveDependentsAreTheInvalidationSet()
    {
        var graph = new XamlResourceGraph(Provider(
            (App, Includes("Themes/Generic.axaml")),
            (Generic, Includes("Colors.axaml")),
            (Colors, "<Styles />")));

        await graph.BuildAsync(App, TestContext.Current.CancellationToken);

        // Editing Colors makes Generic and App stale, and nothing else.
        Assert.Equal(
            new[] { App, Generic }.OrderBy(static u => u.ToString(), StringComparer.Ordinal),
            graph.GetDependents(Colors).OrderBy(static u => u.ToString(), StringComparer.Ordinal));

        Assert.Empty(graph.GetDependents(App));
    }

    [Fact]
    public async Task ADiamondIsWalkedOnceAndIsNotACycle()
    {
        var graph = new XamlResourceGraph(Provider(
            (App, Includes("Themes/Generic.axaml", "Themes/Colors.axaml")),
            (Generic, Includes("Fonts.axaml")),
            (Colors, Includes("Fonts.axaml")),
            (Fonts, "<Styles />")));

        XamlResourceGraphResult result = await graph.BuildAsync(App, TestContext.Current.CancellationToken);

        Assert.False(result.HasCycles);
        Assert.Equal(4, result.Documents.Length);
        Assert.Equal(3, graph.GetDependents(Fonts).Count);
    }

    [Fact]
    public async Task ACycleIsReportedRatherThanFollowedForever()
    {
        var graph = new XamlResourceGraph(Provider(
            (App, Includes("Themes/Generic.axaml")),
            (Generic, Includes("Colors.axaml")),
            (Colors, Includes("Generic.axaml"))));

        XamlResourceGraphResult result = await graph.BuildAsync(App, TestContext.Current.CancellationToken);

        Assert.True(result.HasCycles);
        Assert.False(result.Success);

        MarkupDiagnostic cycle = Assert.Single(
            result.Diagnostics, static d => d.Code == XamlDiagnosticCodes.ResourceIncludeCycle);

        Assert.Equal(MarkupDiagnosticSeverity.Error, cycle.Severity);
        Assert.Equal(2, cycle.RelatedLocations.Length);
    }

    [Fact]
    public async Task ADocumentThatIncludesItselfIsReported()
    {
        var graph = new XamlResourceGraph(Provider((App, Includes("App.axaml"))));

        XamlResourceGraphResult result = await graph.BuildAsync(App, TestContext.Current.CancellationToken);

        Assert.Contains(result.Diagnostics, static d => d.Code == XamlDiagnosticCodes.ResourceIncludeCycle);
    }

    [Fact]
    public async Task ACycleIsReportedOnceHoweverManyWaysInThereAre()
    {
        var graph = new XamlResourceGraph(Provider(
            (App, Includes("Themes/Generic.axaml", "Themes/Colors.axaml")),
            (Generic, Includes("Colors.axaml")),
            (Colors, Includes("Generic.axaml"))));

        XamlResourceGraphResult result = await graph.BuildAsync(App, TestContext.Current.CancellationToken);

        Assert.Single(result.Cycles);
    }

    [Fact]
    public async Task AnIncludeOfADocumentNobodyKnowsIsAWarningNotAnError()
    {
        // In an editor this routinely means the file has not been written yet.
        var graph = new XamlResourceGraph(Provider((App, Includes("Themes/Missing.axaml"))));

        XamlResourceGraphResult result = await graph.BuildAsync(App, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains(
            result.Diagnostics,
            static d => d.Code == XamlDiagnosticCodes.UnresolvedIncludeDocument
                && d.Severity == MarkupDiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task NoPhysicalFileIsEverRequired()
    {
        // Every document here exists only in memory, and the graph neither knows nor cares.
        var graph = new XamlResourceGraph(Provider(
            (new Uri("inmemory:///a"), "<Styles><StyleInclude Source=\"inmemory:///b\" /></Styles>"),
            (new Uri("inmemory:///b"), "<Styles />")));

        XamlResourceGraphResult result = await graph.BuildAsync(
            new Uri("inmemory:///a"), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(2, result.Documents.Length);
    }

    [Fact]
    public async Task EditingADocumentUpdatesOnlyItsOwnEdges()
    {
        var provider = Provider(
            (App, Includes("Themes/Generic.axaml")),
            (Generic, Includes("Colors.axaml")),
            (Colors, "<Styles />"),
            (Fonts, "<Styles />"));

        var graph = new XamlResourceGraph(provider);
        await graph.BuildAsync(App, TestContext.Current.CancellationToken);

        Assert.Contains(Colors, graph.GetDependencies(Generic));

        // Generic now points at Fonts instead of Colors.
        provider.Update(Generic, Includes("Fonts.axaml"));
        await graph.UpdateAsync(Generic, TestContext.Current.CancellationToken);

        Assert.Contains(Fonts, graph.GetDependencies(Generic));
        Assert.DoesNotContain(Colors, graph.GetDependencies(Generic));

        // App's own edge is untouched, because App did not change.
        Assert.Contains(Generic, graph.GetDependencies(App));
        Assert.Contains(App, graph.GetDependents(Fonts));
    }

    [Fact]
    public async Task AnEditThatIntroducesACycleIsCaughtByTheIncrementalUpdate()
    {
        var provider = Provider(
            (App, Includes("Themes/Generic.axaml")),
            (Generic, "<Styles />"));

        var graph = new XamlResourceGraph(provider);
        XamlResourceGraphResult before = await graph.BuildAsync(App, TestContext.Current.CancellationToken);

        Assert.False(before.HasCycles);

        provider.Update(Generic, "<Styles><StyleInclude Source=\"file:///App.axaml\" /></Styles>");
        XamlResourceGraphResult after = await graph.UpdateAsync(Generic, TestContext.Current.CancellationToken);

        Assert.True(after.HasCycles);
    }

    [Fact]
    public async Task AnEditThatAddsANewDocumentPullsItIntoTheGraph()
    {
        var provider = Provider(
            (App, "<Styles />"),
            (Generic, "<Styles />"));

        var graph = new XamlResourceGraph(provider);
        await graph.BuildAsync(App, TestContext.Current.CancellationToken);

        Assert.Single(graph.Documents);

        provider.Update(App, Includes("Themes/Generic.axaml"));
        await graph.UpdateAsync(App, TestContext.Current.CancellationToken);

        Assert.Contains(Generic, graph.Documents);
        Assert.Contains(App, graph.GetDependents(Generic));
    }

    [Fact]
    public void TheGraphNeedsNoAvaloniaAssembly()
    {
        // Enforced for the whole package by the architecture tests; asserted here because it
        // is this milestone's own exit criterion.
        Assert.DoesNotContain(
            typeof(XamlResourceGraph).Assembly.GetReferencedAssemblies(),
            static reference => reference.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true);
    }
}
