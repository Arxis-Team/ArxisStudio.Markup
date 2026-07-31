using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Follows resource and style includes from one document to the next, recording what depends on
/// what.
/// </summary>
/// <remarks>
/// <para>
/// The graph exists so that a change to one file invalidates exactly the files that reach it,
/// and no others. In a theme with hundreds of documents, that difference is what makes editing
/// one of them cheap.
/// </para>
/// <para>
/// Documents arrive through an <see cref="IMarkupSourceProvider"/> and nothing else. There is
/// no filesystem walk, no project file, no assembly: a URI the provider does not know simply
/// has no content, which is the normal state of an include whose target has not been written
/// yet.
/// </para>
/// <para>
/// Cycle detection is delegated to the dependency graph, which already knows how to find one.
/// Deciding that a cycle of includes deserves a diagnostic is this package's judgement; walking
/// the edges is not.
/// </para>
/// </remarks>
public sealed class XamlResourceGraph
{
    private readonly IMarkupSourceProvider _sourceProvider;
    private readonly MarkupDependencyGraph _dependencies = new();
    private readonly Dictionary<Uri, MarkupDocumentId> _identities = new(XamlUri.Comparer);
    private readonly Dictionary<MarkupDocumentId, Uri> _uris = [];

    /// <summary>Creates a graph that reads documents through a provider.</summary>
    /// <param name="sourceProvider">The provider documents are read through.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sourceProvider"/> is <see langword="null"/>.</exception>
    public XamlResourceGraph(IMarkupSourceProvider sourceProvider)
    {
        ArgumentNullException.ThrowIfNull(sourceProvider);

        _sourceProvider = sourceProvider;
    }

    /// <summary>Gets the dependency edges discovered so far.</summary>
    public IMarkupDependencyGraph Dependencies => _dependencies;

    /// <summary>Gets the documents the graph currently knows about.</summary>
    public IReadOnlyCollection<Uri> Documents => [.. _identities.Keys];

    /// <summary>
    /// Walks the includes reachable from a document, adding every document it reaches to the
    /// graph.
    /// </summary>
    /// <param name="rootUri">The document to start from.</param>
    /// <param name="cancellationToken">A token to observe while reading documents.</param>
    /// <returns>What the walk found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rootUri"/> is <see langword="null"/>.</exception>
    public async ValueTask<XamlResourceGraphResult> BuildAsync(
        Uri rootUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rootUri);

        var diagnostics = new List<MarkupDiagnostic>();
        var visited = new HashSet<Uri>(XamlUri.Comparer);
        var queue = new Queue<Uri>();

        queue.Enqueue(rootUri);

        while (queue.Count > 0)
        {
            Uri current = queue.Dequeue();

            // Marking on dequeue rather than enqueue is what stops a cycle from queueing its
            // own members forever.
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (Uri dependency in await AnalyseAsync(current, diagnostics, cancellationToken).ConfigureAwait(false))
            {
                queue.Enqueue(dependency);
            }
        }

        ImmutableArray<ImmutableArray<Uri>> cycles = FindCycles(visited, diagnostics);

        return new XamlResourceGraphResult([.. visited], cycles, [.. diagnostics]);
    }

    /// <summary>
    /// Re-reads one document and replaces its outgoing edges, leaving the rest of the graph
    /// alone.
    /// </summary>
    /// <remarks>
    /// This is what an edit calls. Rebuilding the whole graph because one file changed is
    /// exactly the full-workspace reload the contract's performance principles rule out; only
    /// the edited document's own includes can have changed.
    /// </remarks>
    /// <param name="uri">The document whose includes may have changed.</param>
    /// <param name="cancellationToken">A token to observe while reading.</param>
    /// <returns>What re-reading that document found, including any cycle it now takes part in.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public async ValueTask<XamlResourceGraphResult> UpdateAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var diagnostics = new List<MarkupDiagnostic>();
        IReadOnlyList<Uri> dependencies = await AnalyseAsync(uri, diagnostics, cancellationToken).ConfigureAwait(false);

        // Documents the edit newly points at may be unknown, so they are walked in turn; ones
        // it stopped pointing at keep their own edges, since something else may still reach
        // them.
        foreach (Uri dependency in dependencies.Where(dependency => !_identities.ContainsKey(dependency)).ToArray())
        {
            await BuildAsync(dependency, cancellationToken).ConfigureAwait(false);
        }

        ImmutableArray<ImmutableArray<Uri>> cycles = FindCycles([uri], diagnostics);

        return new XamlResourceGraphResult([uri, .. dependencies], cycles, [.. diagnostics]);
    }

    /// <summary>Gets the documents that reach a document, directly or through others.</summary>
    /// <remarks>This is the invalidation set: exactly what a change to the document makes stale.</remarks>
    /// <param name="uri">The document that changed.</param>
    /// <returns>The documents whose analysis the change invalidates.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public IReadOnlyCollection<Uri> GetDependents(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!_identities.TryGetValue(uri, out MarkupDocumentId id))
        {
            return [];
        }

        return [.. _dependencies.GetTransitiveDependents(id).Select(dependent => _uris[dependent])];
    }

    /// <summary>Gets the documents a document reaches, directly or through others.</summary>
    /// <param name="uri">The document to start from.</param>
    /// <returns>Everything it depends on.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public IReadOnlyCollection<Uri> GetDependencies(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!_identities.TryGetValue(uri, out MarkupDocumentId id))
        {
            return [];
        }

        return [.. _dependencies.GetTransitiveDependencies(id).Select(dependency => _uris[dependency])];
    }

    /// <summary>Reads one document, records its edges, and reports what it points at.</summary>
    private async ValueTask<IReadOnlyList<Uri>> AnalyseAsync(
        Uri uri,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        MarkupDocumentId id = IdentityOf(uri);
        MarkupSource? source = await _sourceProvider.TryGetSourceAsync(uri, cancellationToken).ConfigureAwait(false);

        if (source is null)
        {
            // A URI nobody knows. In an editor this routinely means the file has not been
            // written yet, so it is a warning and the edge is simply not there.
            diagnostics.Add(MarkupDiagnostic.Parse(
                XamlDiagnosticCodes.UnresolvedIncludeDocument,
                $"No source provider could resolve '{uri}'.",
                MarkupDiagnosticSeverity.Warning,
                uri));

            _dependencies.SetDependencies(id, []);

            return [];
        }

        SourceText text = await source.GetTextAsync(cancellationToken).ConfigureAwait(false);
        XamlDocument document = XamlDocument.Parse(text, new XamlParseOptions { DocumentUri = uri });

        ImmutableArray<XamlResourceReference> references =
            XamlResourceAnalyzer.Discover(document, out ImmutableArray<MarkupDiagnostic> discovery);

        diagnostics.AddRange(discovery);

        var dependencies = new List<Uri>();
        var edges = new List<MarkupDocumentId>();

        foreach (XamlResourceReference reference in references)
        {
            if (reference.ResolvedUri is not { } target || XamlUri.Comparer.Equals(target, uri))
            {
                // A document that includes itself is a cycle of one; the graph refuses
                // self-edges, so it is reported here instead.
                if (reference.ResolvedUri is not null && XamlUri.Comparer.Equals(reference.ResolvedUri, uri))
                {
                    diagnostics.Add(CycleDiagnostic([uri], uri, reference.Span));
                }

                continue;
            }

            dependencies.Add(target);
            edges.Add(IdentityOf(target));
        }

        _dependencies.SetDependencies(id, edges);

        return dependencies;
    }

    /// <summary>Looks for cycles reachable from the given documents.</summary>
    private ImmutableArray<ImmutableArray<Uri>> FindCycles(
        IEnumerable<Uri> starts,
        List<MarkupDiagnostic> diagnostics)
    {
        ImmutableArray<ImmutableArray<Uri>>.Builder cycles =
            ImmutableArray.CreateBuilder<ImmutableArray<Uri>>();

        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (Uri start in starts)
        {
            if (!_identities.TryGetValue(start, out MarkupDocumentId id)
                || !_dependencies.TryFindCycle(id, out IReadOnlyList<MarkupDocumentId> cycle))
            {
                continue;
            }

            ImmutableArray<Uri> uris = [.. cycle.Select(member => _uris[member])];

            // The same cycle is reachable from every document in it, so it is reported once.
            string key = string.Join(" -> ", uris.Select(XamlUri.ToKey).OrderBy(static u => u, StringComparer.Ordinal));

            if (!reported.Add(key))
            {
                continue;
            }

            cycles.Add(uris);
            diagnostics.Add(CycleDiagnostic(uris, uris[0], span: null));
        }

        return cycles.ToImmutable();
    }

    private static MarkupDiagnostic CycleDiagnostic(IReadOnlyList<Uri> cycle, Uri at, TextSpan? span)
    {
        string path = string.Join(" -> ", cycle.Select(XamlUri.ToDisplayString));

        return MarkupDiagnostic.Parse(
                XamlDiagnosticCodes.ResourceIncludeCycle,
                $"The includes form a cycle: {path} -> {cycle[0]}.",
                MarkupDiagnosticSeverity.Error,
                at,
                span)
            .WithRelatedLocations([.. cycle.Select(static uri => new MarkupLocation(uri))]);
    }

    /// <summary>Gets the graph's identity for a URI, creating one the first time it is seen.</summary>
    private MarkupDocumentId IdentityOf(Uri uri)
    {
        if (_identities.TryGetValue(uri, out MarkupDocumentId existing))
        {
            return existing;
        }

        MarkupDocumentId id = MarkupDocumentId.New();

        _identities[uri] = id;
        _uris[id] = uri;

        return id;
    }
}
