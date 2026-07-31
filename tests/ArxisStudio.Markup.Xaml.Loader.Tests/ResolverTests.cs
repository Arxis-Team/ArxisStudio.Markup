using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

public sealed class ResolverTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string TestControlsNamespace = "https://arxis.studio/test-controls";

    private static readonly Assembly Controls = typeof(CustomBadge).Assembly;

    private static XamlTypeResolver Resolver(params Assembly[] assemblies) =>
        new(new CompositeAssemblyResolver(
            new ExplicitAssemblyResolver(assemblies),
            LoadedAssemblyResolver.Instance));

    private static ValueTask<XamlTypeResolution> Resolve(
        XamlTypeResolver resolver, string namespaceUri, string localName, CancellationToken cancellationToken) =>
        resolver.ResolveAsync(
            new XamlTypeName(namespaceUri, localName), XamlNamespaceContext.Empty, cancellationToken);

    [Fact]
    public async Task ExplicitlySuppliedAssembliesResolveByName()
    {
        var resolver = new ExplicitAssemblyResolver(Controls);

        Assert.Same(
            Controls,
            await resolver.ResolveAsync(Controls.GetName(), TestContext.Current.CancellationToken));
        Assert.Null(await resolver.ResolveAsync(
            new AssemblyName("Nowhere.At.All"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadedAssembliesResolveWithoutBeingSupplied()
    {
        // This is what makes a document of standard controls load with no configuration.
        Assert.NotNull(await LoadedAssemblyResolver.Instance.ResolveAsync(
            typeof(Button).Assembly.GetName(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ACompositeTakesTheFirstAnswer()
    {
        var composite = new CompositeAssemblyResolver(
            new ExplicitAssemblyResolver(Controls),
            LoadedAssemblyResolver.Instance);

        Assert.Same(
            Controls,
            await composite.ResolveAsync(Controls.GetName(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADirectoryResolverFindsAnAssemblyTheCallerPointedAt()
    {
        // The directory is the caller's choice. Nothing here goes looking for an output folder
        // or a package cache of its own accord.
        var resolver = new DirectoryAssemblyResolver(Path.GetDirectoryName(Controls.Location)!);

        Assert.NotNull(await resolver.ResolveAsync(
            Controls.GetName(), TestContext.Current.CancellationToken));
        Assert.Null(await resolver.ResolveAsync(
            new AssemblyName("Nowhere.At.All"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ResolversRejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new ExplicitAssemblyResolver((Assembly[])null!));
        Assert.Throws<ArgumentNullException>(() => new ExplicitAssemblyResolver(Controls, null!));
        Assert.Throws<ArgumentNullException>(() => new CompositeAssemblyResolver(null!, LoadedAssemblyResolver.Instance));
        Assert.Throws<ArgumentNullException>(() => new CompositeResourceResolver(null!, FileResourceResolver.Instance));
    }

    [Fact]
    public async Task AMappedNamespaceResolvesThroughXmlnsDefinition()
    {
        // Avalonia's own namespace resolves by exactly this route, without being special-cased.
        XamlTypeResolution avalonia = await Resolve(
            Resolver(), AvaloniaNamespace, "Button", TestContext.Current.CancellationToken);

        Assert.True(avalonia.Success);
        Assert.Equal(typeof(Button), avalonia.Type);

        XamlTypeResolution custom = await Resolve(
            Resolver(Controls), TestControlsNamespace, nameof(CustomBadge), TestContext.Current.CancellationToken);

        Assert.Equal(typeof(CustomBadge), custom.Type);
    }

    [Fact]
    public async Task AUsingNamespaceResolvesAClrNamespace()
    {
        XamlTypeResolution resolution = await Resolve(
            Resolver(Controls),
            "using:ArxisStudio.Markup.Xaml.Loader.TestControls",
            nameof(CustomBadge),
            TestContext.Current.CancellationToken);

        Assert.Equal(typeof(CustomBadge), resolution.Type);
    }

    [Fact]
    public async Task AClrNamespaceWithAnAssemblyResolves()
    {
        XamlTypeResolution resolution = await Resolve(
            Resolver(Controls),
            $"clr-namespace:ArxisStudio.Markup.Xaml.Loader.TestControls;assembly={Controls.GetName().Name}",
            nameof(CustomBadge),
            TestContext.Current.CancellationToken);

        Assert.Equal(typeof(CustomBadge), resolution.Type);
    }

    [Fact]
    public async Task AnUnsuppliedAssemblyIsReportedByName()
    {
        XamlTypeResolution resolution = await Resolve(
            Resolver(),
            "clr-namespace:Whatever;assembly=Nowhere.At.All",
            "Thing",
            TestContext.Current.CancellationToken);

        Assert.False(resolution.Success);
        Assert.Contains(
            resolution.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.UnresolvedAssembly);
    }

    [Fact]
    public async Task AnUnmappedNamespaceIsDistinguishedFromAMissingType()
    {
        XamlTypeResolution unknownNamespace = await Resolve(
            Resolver(), "urn:not-mapped-by-anything", "Thing", TestContext.Current.CancellationToken);

        XamlTypeResolution missingType = await Resolve(
            Resolver(), AvaloniaNamespace, "NotAControl", TestContext.Current.CancellationToken);

        Assert.Contains(
            unknownNamespace.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.UnknownNamespace);
        Assert.Contains(
            missingType.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.UnresolvedType);
    }

    [Fact]
    public async Task ResolutionDiagnosticsAreCategorisedAsResolution()
    {
        XamlTypeResolution resolution = await Resolve(
            Resolver(), AvaloniaNamespace, "NotAControl", TestContext.Current.CancellationToken);

        Assert.All(
            resolution.Diagnostics,
            static d => Assert.Equal(MarkupDiagnosticCategory.Resolution, d.Category));
    }

    [Fact]
    public async Task AGenericTypeIsReportedRatherThanSilentlyResolvedOpen()
    {
        var name = new XamlTypeName(AvaloniaNamespace, "SomeGeneric", [new XamlTypeName(AvaloniaNamespace, "Button")]);

        XamlTypeResolution resolution = await Resolver()
            .ResolveAsync(name, XamlNamespaceContext.Empty, TestContext.Current.CancellationToken);

        Assert.Contains(
            resolution.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.UnsupportedGenericType);
    }

    [Fact]
    public async Task AnInMemoryResourceOverridesTheFileOfTheSameUri()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".axaml");
        await File.WriteAllTextAsync(path, "<from-disk />", TestContext.Current.CancellationToken);

        try
        {
            var uri = new Uri(path);
            var inMemory = new InMemoryResourceResolver();
            inMemory.Update(uri, "<unsaved-edit />");

            var composite = new CompositeResourceResolver(inMemory, FileResourceResolver.Instance);

            XamlResource? resource = await composite.ResolveAsync(
                uri, null, TestContext.Current.CancellationToken);

            SourceText text = await resource!.ReadTextAsync(TestContext.Current.CancellationToken);

            Assert.Equal("<unsaved-edit />", text.ToString());

            inMemory.Remove(uri);
            resource = await composite.ResolveAsync(uri, null, TestContext.Current.CancellationToken);

            Assert.Equal(
                "<from-disk />",
                (await resource!.ReadTextAsync(TestContext.Current.CancellationToken)).ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AResourceUriNobodyKnowsResolvesToNothing()
    {
        var composite = new CompositeResourceResolver(
            new InMemoryResourceResolver(), FileResourceResolver.Instance);

        Assert.Null(await composite.ResolveAsync(
            new Uri("file:///nowhere/at/all.axaml"), null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnAvaresResolverOnlyAnswersForAvaresUris()
    {
        var resolver = new AvaloniaResourceResolver(new ExplicitAssemblyResolver(Controls));

        Assert.Null(await resolver.ResolveAsync(
            new Uri("file:///something.axaml"), null, TestContext.Current.CancellationToken));

        // The assembly is supplied but holds no such resource, so this is a miss rather than
        // a crash.
        Assert.Null(await resolver.ResolveAsync(
            new Uri($"avares://{Controls.GetName().Name}/Nowhere.axaml"),
            null,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheDefaultEnvironmentSuppliesEveryRequiredResolver()
    {
        XamlLoadEnvironment environment = XamlLoadEnvironment.CreateDefault([Controls]);

        Assert.NotNull(environment.SourceProvider);
        Assert.NotNull(environment.AssemblyResolver);
        Assert.NotNull(environment.TypeResolver);
        Assert.NotNull(environment.ResourceResolver);
        Assert.NotNull(environment.Dispatcher);

        // Nothing is discovered: a root instance factory is only present if the caller supplies
        // one, and nothing here went looking for a project.
        Assert.Null(environment.RootInstanceFactory);
    }
}
