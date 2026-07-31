using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace ArxisStudio.Markup;

/// <summary>
/// A thread-safe dependency graph that keeps both edge directions materialised.
/// </summary>
/// <remarks>
/// Reverse edges are maintained on every write so that <see cref="GetDependents"/> is a lookup
/// rather than a scan of the whole graph. Dependents are read far more often than dependencies
/// are set — every edit asks who is now stale — so paying on write is the right trade.
/// <para>
/// Both maps live in one immutable state object, published with a single reference write, so a
/// reader can never see forward and reverse edges disagreeing.
/// </para>
/// </remarks>
public sealed class MarkupDependencyGraph : IMarkupDependencyGraph
{
    private readonly Lock _gate = new();

    private State _state = State.Empty;

    /// <summary>Gets the documents that currently have outgoing or incoming edges.</summary>
    public IReadOnlyCollection<MarkupDocumentId> Documents
    {
        get
        {
            State state = Volatile.Read(ref _state);
            var documents = new HashSet<MarkupDocumentId>(state.Forward.Keys);

            documents.UnionWith(state.Reverse.Keys);

            return documents;
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="dependencies"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="document"/> appears in its own dependencies.</exception>
    public void SetDependencies(MarkupDocumentId document, IReadOnlyCollection<MarkupDocumentId> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        var updated = ImmutableHashSet.CreateRange(dependencies);

        if (updated.Contains(document))
        {
            throw new ArgumentException(
                $"Document '{document}' cannot depend on itself.", nameof(dependencies));
        }

        lock (_gate)
        {
            State state = _state;
            ImmutableHashSet<MarkupDocumentId> previous = Lookup(state.Forward, document);

            ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> reverse = state.Reverse;

            foreach (MarkupDocumentId removed in previous.Except(updated))
            {
                reverse = Unlink(reverse, removed, document);
            }

            foreach (MarkupDocumentId added in updated.Except(previous))
            {
                reverse = Link(reverse, added, document);
            }

            _state = new State(
                updated.IsEmpty ? state.Forward.Remove(document) : state.Forward.SetItem(document, updated),
                reverse);
        }
    }

    /// <inheritdoc />
    public IReadOnlySet<MarkupDocumentId> GetDependencies(MarkupDocumentId document) =>
        Lookup(Volatile.Read(ref _state).Forward, document);

    /// <inheritdoc />
    public IReadOnlySet<MarkupDocumentId> GetDependents(MarkupDocumentId document) =>
        Lookup(Volatile.Read(ref _state).Reverse, document);

    /// <summary>
    /// Gets everything a document depends on, directly or through other documents.
    /// </summary>
    /// <param name="document">The dependent document.</param>
    /// <returns>Its transitive dependencies, excluding itself. Cycles terminate the walk safely.</returns>
    public IReadOnlySet<MarkupDocumentId> GetTransitiveDependencies(MarkupDocumentId document) =>
        Walk(document, Volatile.Read(ref _state).Forward);

    /// <summary>
    /// Gets everything that depends on a document, directly or through other documents.
    /// </summary>
    /// <remarks>This is the invalidation set: exactly what a change to the document makes stale.</remarks>
    /// <param name="document">The depended-upon document.</param>
    /// <returns>Its transitive dependents, excluding itself. Cycles terminate the walk safely.</returns>
    public IReadOnlySet<MarkupDocumentId> GetTransitiveDependents(MarkupDocumentId document) =>
        Walk(document, Volatile.Read(ref _state).Reverse);

    /// <summary>Determines whether a document participates in a dependency cycle.</summary>
    /// <param name="document">The document to test.</param>
    /// <returns><see langword="true"/> if the document can reach itself through its dependencies.</returns>
    public bool IsInCycle(MarkupDocumentId document) =>
        Walk(document, Volatile.Read(ref _state).Forward).Contains(document);

    /// <summary>
    /// Finds a dependency cycle reachable from a document, if there is one.
    /// </summary>
    /// <remarks>
    /// The graph reports the shape; deciding that a cycle of resource includes deserves a
    /// diagnostic, and with what code, belongs to the package that created the edges.
    /// </remarks>
    /// <param name="document">The document to start from.</param>
    /// <param name="cycle">
    /// The documents forming the cycle, in dependency order and starting at the repeated
    /// document, when one is found.
    /// </param>
    /// <returns><see langword="true"/> if a cycle was found.</returns>
    public bool TryFindCycle(MarkupDocumentId document, out IReadOnlyList<MarkupDocumentId> cycle)
    {
        ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> forward =
            Volatile.Read(ref _state).Forward;

        var path = new List<MarkupDocumentId>();
        var onPath = new HashSet<MarkupDocumentId>();
        var settled = new HashSet<MarkupDocumentId>();

        if (Visit(document, forward, path, onPath, settled, out cycle!))
        {
            return true;
        }

        cycle = [];

        return false;
    }

    /// <summary>Removes a document's outgoing and incoming edges.</summary>
    /// <param name="document">The document to detach.</param>
    /// <returns><see langword="true"/> if the document had any edge.</returns>
    public bool Remove(MarkupDocumentId document)
    {
        lock (_gate)
        {
            State state = _state;
            ImmutableHashSet<MarkupDocumentId> dependencies = Lookup(state.Forward, document);
            ImmutableHashSet<MarkupDocumentId> dependents = Lookup(state.Reverse, document);

            if (dependencies.IsEmpty && dependents.IsEmpty)
            {
                return false;
            }

            ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> forward = state.Forward.Remove(document);
            ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> reverse = state.Reverse.Remove(document);

            foreach (MarkupDocumentId dependency in dependencies)
            {
                reverse = Unlink(reverse, dependency, document);
            }

            foreach (MarkupDocumentId dependent in dependents)
            {
                forward = Unlink(forward, dependent, document);
            }

            _state = new State(forward, reverse);

            return true;
        }
    }

    /// <summary>Removes every edge from the graph.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _state = State.Empty;
        }
    }

    private static bool Visit(
        MarkupDocumentId current,
        ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> forward,
        List<MarkupDocumentId> path,
        HashSet<MarkupDocumentId> onPath,
        HashSet<MarkupDocumentId> settled,
        out IReadOnlyList<MarkupDocumentId>? cycle)
    {
        if (!onPath.Add(current))
        {
            // Report from the point the path re-entered, so the result is the cycle itself
            // rather than the walk that led into it.
            cycle = [.. path.GetRange(path.IndexOf(current), path.Count - path.IndexOf(current))];

            return true;
        }

        path.Add(current);

        foreach (MarkupDocumentId dependency in Lookup(forward, current))
        {
            if (!settled.Contains(dependency) && Visit(dependency, forward, path, onPath, settled, out cycle))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        onPath.Remove(current);
        settled.Add(current);
        cycle = null;

        return false;
    }

    private static ImmutableHashSet<MarkupDocumentId> Walk(
        MarkupDocumentId start,
        ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> edges)
    {
        var reached = new HashSet<MarkupDocumentId>();
        var queue = new Queue<MarkupDocumentId>(Lookup(edges, start));

        while (queue.Count > 0)
        {
            MarkupDocumentId current = queue.Dequeue();

            // Marking on dequeue is what keeps a cycle from spinning here forever.
            if (!reached.Add(current))
            {
                continue;
            }

            foreach (MarkupDocumentId next in Lookup(edges, current))
            {
                queue.Enqueue(next);
            }
        }

        return [.. reached];
    }

    private static ImmutableHashSet<MarkupDocumentId> Lookup(
        ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> edges,
        MarkupDocumentId key) =>
        edges.TryGetValue(key, out ImmutableHashSet<MarkupDocumentId>? value)
            ? value
            : ImmutableHashSet<MarkupDocumentId>.Empty;

    private static ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> Link(
        ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> edges,
        MarkupDocumentId key,
        MarkupDocumentId value) =>
        edges.SetItem(key, Lookup(edges, key).Add(value));

    private static ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> Unlink(
        ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> edges,
        MarkupDocumentId key,
        MarkupDocumentId value)
    {
        ImmutableHashSet<MarkupDocumentId> remaining = Lookup(edges, key).Remove(value);

        return remaining.IsEmpty ? edges.Remove(key) : edges.SetItem(key, remaining);
    }

    /// <summary>Both edge directions, swapped together so they can never disagree.</summary>
    private sealed record State(
        ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> Forward,
        ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>> Reverse)
    {
        public static State Empty { get; } = new(
            ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>>.Empty,
            ImmutableDictionary<MarkupDocumentId, ImmutableHashSet<MarkupDocumentId>>.Empty);
    }
}
