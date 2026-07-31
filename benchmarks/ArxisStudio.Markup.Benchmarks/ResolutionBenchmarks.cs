using System;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using Avalonia.Controls;
using BenchmarkDotNet.Attributes;

namespace ArxisStudio.Markup.Benchmarks;

/// <summary>
/// Turning names into CLR types and members, which the contract requires to be cached rather
/// than rediscovered by scanning every loaded assembly on every lookup.
/// </summary>
/// <remarks>
/// Both are measured warm, on the second and later lookups, because that is the state an editor
/// spends its life in: the first resolution of a name pays for the reflection, and every one
/// after it is what the interface actually waits on.
/// </remarks>
[MemoryDiagnoser]
public class ResolutionBenchmarks
{
    private XamlTypeResolver _types = null!;

    /// <summary>Builds the resolvers and warms their caches.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _types = new XamlTypeResolver(LoadedAssemblyResolver.Instance);

        // Warm, so what is measured is a lookup rather than the reflection behind the first one.
        _ = ResolveType().GetAwaiter().GetResult();
        _ = ResolveMember();
    }

    /// <summary>Resolving an element name to a type through the environment's resolvers.</summary>
    /// <returns>What the name resolved to.</returns>
    [Benchmark]
    public async Task<XamlTypeResolution> ResolveType() =>
        await _types.ResolveAsync(
            new XamlTypeName(SampleDocuments.AvaloniaNamespace, "Button"),
            XamlNamespaceContext.Empty,
            CancellationToken.None);

    /// <summary>Resolving an attribute name to a member of a type.</summary>
    /// <returns>What the name resolved to.</returns>
    [Benchmark]
    public XamlMemberDescriptor ResolveMember() =>
        XamlMemberResolver.Instance.Resolve(typeof(Button), "Content");

    /// <summary>Resolving an attached member, which has to be found on its owner instead.</summary>
    /// <returns>What the name resolved to.</returns>
    [Benchmark]
    public XamlMemberDescriptor ResolveAttachedMember() =>
        XamlMemberResolver.Instance.Resolve(typeof(Button), "Grid.Row");
}
