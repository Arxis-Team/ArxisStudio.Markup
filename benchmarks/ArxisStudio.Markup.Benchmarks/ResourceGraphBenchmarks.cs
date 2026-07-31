using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using BenchmarkDotNet.Attributes;

namespace ArxisStudio.Markup.Benchmarks;

/// <summary>
/// Working out what a changed file affects, which is what the contract means by not reloading a
/// whole workspace for one changed document.
/// </summary>
/// <remarks>
/// The graph is a chain of dictionaries each merging the next, which is the shape that makes the
/// difference between rebuilding one document and rebuilding all of them visible: the file at the
/// bottom is a dependency of every one above it.
/// </remarks>
[MemoryDiagnoser]
public class ResourceGraphBenchmarks
{
    private InMemoryMarkupSourceProvider _provider = null!;
    private XamlResourceGraph _graph = null!;
    private Uri _root = null!;
    private Uri _leaf = null!;

    /// <summary>Gets or sets how long the chain of merged dictionaries is.</summary>
    [Params(10, 100)]
    public int Documents { get; set; }

    /// <summary>Builds the chain and the graph over it.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _provider = new InMemoryMarkupSourceProvider();

        var uris = new List<Uri>();

        for (int index = 0; index < Documents; index++)
        {
            uris.Add(new Uri($"file:///Themes/Level{index}.axaml"));
        }

        for (int index = 0; index < Documents; index++)
        {
            string? next = index + 1 < Documents ? $"Level{index + 1}.axaml" : null;

            _provider.Update(uris[index], SampleDocuments.Dictionary(next, $"Key{index}"));
        }

        _root = uris[0];
        _leaf = uris[^1];
        _graph = new XamlResourceGraph(_provider);

        _ = _graph.BuildAsync(_root, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Building the whole graph from the root, which is what opening a project costs.</summary>
    /// <returns>What the build found.</returns>
    [Benchmark]
    public async Task<XamlResourceGraphResult> Build()
    {
        var graph = new XamlResourceGraph(_provider);

        return await graph.BuildAsync(_root, CancellationToken.None);
    }

    /// <summary>
    /// Re-reading one changed document and finding what it invalidates, which is what a keystroke
    /// in the file at the bottom of the chain costs.
    /// </summary>
    /// <returns>What the update found.</returns>
    [Benchmark]
    public async Task<XamlResourceGraphResult> UpdateOneDocument() =>
        await _graph.UpdateAsync(_leaf, CancellationToken.None);

    /// <summary>Asking what depends on the document at the bottom of the chain.</summary>
    /// <returns>How many documents depend on it.</returns>
    [Benchmark]
    public int Dependents() => _graph.GetDependents(_leaf).Count;
}
