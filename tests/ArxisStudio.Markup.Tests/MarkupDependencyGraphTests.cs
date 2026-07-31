using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ArxisStudio.Markup.Tests;

public sealed class MarkupDependencyGraphTests
{
    private static readonly MarkupDocumentId View = MarkupDocumentId.New();
    private static readonly MarkupDocumentId Theme = MarkupDocumentId.New();
    private static readonly MarkupDocumentId Colors = MarkupDocumentId.New();
    private static readonly MarkupDocumentId Fonts = MarkupDocumentId.New();

    [Fact]
    public void UnknownDocument_HasNoEdges()
    {
        var graph = new MarkupDependencyGraph();

        Assert.Empty(graph.GetDependencies(View));
        Assert.Empty(graph.GetDependents(View));
        Assert.Empty(graph.Documents);
    }

    [Fact]
    public void SetDependencies_RecordsBothDirections()
    {
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme]);

        Assert.Equal([Theme], graph.GetDependencies(View));
        Assert.Equal([View], graph.GetDependents(Theme));
    }

    [Fact]
    public void SetDependencies_ReplacesRatherThanAdds()
    {
        // An edited document declares its includes afresh; edges it no longer has must go.
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme, Colors]);
        graph.SetDependencies(View, [Colors, Fonts]);

        Assert.Equal(
            new[] { Colors, Fonts }.OrderBy(static id => id.Value),
            graph.GetDependencies(View).OrderBy(static id => id.Value));
        Assert.Empty(graph.GetDependents(Theme));
        Assert.Equal([View], graph.GetDependents(Fonts));
    }

    [Fact]
    public void SetDependencies_WithAnEmptySetDetachesTheDocument()
    {
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme]);
        graph.SetDependencies(View, []);

        Assert.Empty(graph.GetDependencies(View));
        Assert.Empty(graph.GetDependents(Theme));
    }

    [Fact]
    public void SetDependencies_RejectsSelfDependencyAndNull()
    {
        var graph = new MarkupDependencyGraph();

        Assert.Throws<ArgumentException>(() => graph.SetDependencies(View, [View]));
        Assert.Throws<ArgumentNullException>(() => graph.SetDependencies(View, null!));
    }

    [Fact]
    public void TransitiveDependencies_FollowTheChain()
    {
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme]);
        graph.SetDependencies(Theme, [Colors]);
        graph.SetDependencies(Colors, [Fonts]);

        Assert.Equal(
            new[] { Theme, Colors, Fonts }.OrderBy(static id => id.Value),
            graph.GetTransitiveDependencies(View).OrderBy(static id => id.Value));
    }

    [Fact]
    public void TransitiveDependents_AreTheInvalidationSet()
    {
        // Editing Fonts must invalidate everything that reaches it, at any depth.
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme]);
        graph.SetDependencies(Theme, [Colors]);
        graph.SetDependencies(Colors, [Fonts]);

        Assert.Equal(
            new[] { View, Theme, Colors }.OrderBy(static id => id.Value),
            graph.GetTransitiveDependents(Fonts).OrderBy(static id => id.Value));
    }

    [Fact]
    public void TransitiveWalks_ExcludeTheStartingDocument()
    {
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme]);

        Assert.DoesNotContain(View, graph.GetTransitiveDependencies(View));
    }

    [Fact]
    public void ADiamondIsNotACycle()
    {
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme, Colors]);
        graph.SetDependencies(Theme, [Fonts]);
        graph.SetDependencies(Colors, [Fonts]);

        Assert.Equal(
            new[] { Theme, Colors, Fonts }.OrderBy(static id => id.Value),
            graph.GetTransitiveDependencies(View).OrderBy(static id => id.Value));
        Assert.False(graph.IsInCycle(View));
        Assert.False(graph.TryFindCycle(View, out _));
    }

    [Fact]
    public void TransitiveWalksTerminateOnACycle()
    {
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme]);
        graph.SetDependencies(Theme, [Colors]);
        graph.SetDependencies(Colors, [View]);

        Assert.Contains(View, graph.GetTransitiveDependencies(View));
        Assert.True(graph.IsInCycle(View));
    }

    [Fact]
    public void TryFindCycle_ReportsTheDocumentsFormingIt()
    {
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme]);
        graph.SetDependencies(Theme, [Colors]);
        graph.SetDependencies(Colors, [Theme]);

        Assert.True(graph.TryFindCycle(View, out IReadOnlyList<MarkupDocumentId> cycle));

        // The walk into the cycle is not part of it.
        Assert.Equal([Theme, Colors], cycle);
        Assert.DoesNotContain(View, cycle);
    }

    [Fact]
    public void TryFindCycle_FindsATwoDocumentCycle()
    {
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme]);
        graph.SetDependencies(Theme, [View]);

        Assert.True(graph.TryFindCycle(View, out IReadOnlyList<MarkupDocumentId> cycle));
        Assert.Equal([View, Theme], cycle);
    }

    [Fact]
    public void Remove_DetachesADocumentFromBothDirections()
    {
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme]);
        graph.SetDependencies(Theme, [Colors]);

        Assert.True(graph.Remove(Theme));

        Assert.Empty(graph.GetDependencies(View));
        Assert.Empty(graph.GetDependencies(Theme));
        Assert.Empty(graph.GetDependents(Colors));
        Assert.False(graph.Remove(Theme));
    }

    [Fact]
    public void Clear_EmptiesTheGraph()
    {
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme]);
        graph.Clear();

        Assert.Empty(graph.Documents);
        Assert.Empty(graph.GetDependencies(View));
        Assert.Empty(graph.GetDependents(Theme));
    }

    [Fact]
    public void Documents_ReportsEveryDocumentWithAnEdge()
    {
        var graph = new MarkupDependencyGraph();

        graph.SetDependencies(View, [Theme]);

        Assert.Equal(
            new[] { View, Theme }.OrderBy(static id => id.Value),
            graph.Documents.OrderBy(static id => id.Value));
    }

    [Fact]
    public void ConcurrentReadsNeverSeeTheTwoDirectionsDisagree()
    {
        // Forward and reverse edges are published together. A reader that finds an edge in one
        // direction must find its counterpart in the other.
        var graph = new MarkupDependencyGraph();
        var documents = Enumerable.Range(0, 24).Select(static _ => MarkupDocumentId.New()).ToArray();
        var failures = new List<string>();

        Parallel.For(0, 96, index =>
        {
            MarkupDocumentId dependent = documents[index % documents.Length];
            MarkupDocumentId dependency = documents[(index + 1) % documents.Length];

            graph.SetDependencies(dependent, [dependency]);

            foreach (MarkupDocumentId edge in graph.GetDependencies(dependent))
            {
                if (!graph.GetDependents(edge).Contains(dependent))
                {
                    lock (failures)
                    {
                        failures.Add($"{dependent} -> {edge} has no reverse edge");
                    }
                }
            }
        });

        Assert.Empty(failures);
    }
}
