using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Finds the content behind a resource URI.
/// </summary>
/// <remarks>
/// The only way resources enter the loader. Nothing here reads a project file or searches a
/// package cache; a caller that wants a resource found supplies a resolver that can find it.
/// </remarks>
public interface IXamlResourceResolver
{
    /// <summary>Attempts to resolve a resource URI.</summary>
    /// <param name="resourceUri">The URI to resolve, absolute or relative to <paramref name="baseUri"/>.</param>
    /// <param name="baseUri">The URI a relative reference is resolved against.</param>
    /// <param name="cancellationToken">A token to observe while resolving.</param>
    /// <returns>The resource, or <see langword="null"/> when this resolver does not know the URI.</returns>
    ValueTask<XamlResource?> ResolveAsync(Uri resourceUri, Uri? baseUri, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves resources the caller holds in memory.
/// </summary>
/// <remarks>
/// Placed ahead of the others, this is how an unsaved edit shadows the file or embedded
/// resource of the same URI — the resource graph then resolves against what the user is
/// currently looking at.
/// </remarks>
public sealed class InMemoryResourceResolver : IXamlResourceResolver
{
    private readonly ConcurrentDictionary<Uri, SourceText> _resources = new(XamlUri.Comparer);

    /// <summary>Adds or replaces the content held for a URI.</summary>
    /// <param name="uri">The URI to hold content for.</param>
    /// <param name="text">The content.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    public void Update(Uri uri, SourceText text)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(text);

        _resources[uri] = text;
    }

    /// <summary>Adds or replaces the content held for a URI.</summary>
    /// <param name="uri">The URI to hold content for.</param>
    /// <param name="text">The content.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    public void Update(Uri uri, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Update(uri, SourceText.From(text));
    }

    /// <summary>Stops holding content for a URI.</summary>
    /// <param name="uri">The URI to release.</param>
    /// <returns><see langword="true"/> if the URI was held.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public bool Remove(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return _resources.TryRemove(uri, out _);
    }

    /// <inheritdoc />
    public ValueTask<XamlResource?> ResolveAsync(Uri resourceUri, Uri? baseUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);
        cancellationToken.ThrowIfCancellationRequested();

        Uri target = Combine(resourceUri, baseUri);

        return new ValueTask<XamlResource?>(
            _resources.TryGetValue(target, out SourceText? text) ? new XamlResource(target, text) : null);
    }

    internal static Uri Combine(Uri resourceUri, Uri? baseUri) =>
        resourceUri.IsAbsoluteUri || baseUri is null || !Uri.TryCreate(baseUri, resourceUri, out Uri? combined)
            ? resourceUri
            : combined;
}

/// <summary>Resolves <c>file:</c> resources that exist on disk.</summary>
public sealed class FileResourceResolver : IXamlResourceResolver
{
    /// <summary>Gets the shared instance.</summary>
    public static FileResourceResolver Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask<XamlResource?> ResolveAsync(Uri resourceUri, Uri? baseUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);
        cancellationToken.ThrowIfCancellationRequested();

        Uri target = InMemoryResourceResolver.Combine(resourceUri, baseUri);

        if (!target.IsAbsoluteUri || !target.IsFile || !File.Exists(target.LocalPath))
        {
            return new ValueTask<XamlResource?>((XamlResource?)null);
        }

        string path = target.LocalPath;

        return new ValueTask<XamlResource?>(new XamlResource(
            target,
            _ => new ValueTask<Stream>(new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true))));
    }
}

/// <summary>
/// Resolves <c>avares://</c> resources embedded in assemblies the caller supplied.
/// </summary>
/// <remarks>
/// <para>
/// The host of an <c>avares</c> URI is an assembly name, and its path is the resource's path
/// within that assembly. Assembly names are case-sensitive, so lookups here are ordinal — see
/// <see cref="XamlUri.ToKey"/> for why treating them otherwise merges different assemblies.
/// </para>
/// <para>
/// Only assemblies the caller made available are searched. Nothing is loaded to satisfy a
/// lookup.
/// </para>
/// </remarks>
public sealed class AvaloniaResourceResolver : IXamlResourceResolver
{
    private readonly IXamlAssemblyResolver _assemblyResolver;

    /// <summary>Creates a resolver over the assemblies an assembly resolver can find.</summary>
    /// <param name="assemblyResolver">How the assembly named by a URI is found.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assemblyResolver"/> is <see langword="null"/>.</exception>
    public AvaloniaResourceResolver(IXamlAssemblyResolver assemblyResolver)
    {
        ArgumentNullException.ThrowIfNull(assemblyResolver);

        _assemblyResolver = assemblyResolver;
    }

    /// <inheritdoc />
    public async ValueTask<XamlResource?> ResolveAsync(
        Uri resourceUri,
        Uri? baseUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);

        Uri target = InMemoryResourceResolver.Combine(resourceUri, baseUri);

        if (!XamlUri.IsAvaloniaResource(target))
        {
            return null;
        }

        // The host of the original string, not of the parsed Uri, because Uri lower-cases it.
        string assemblyName = XamlUri.GetAssemblyName(target) ?? target.Host;

        Assembly? assembly = await _assemblyResolver
            .ResolveAsync(new AssemblyName(assemblyName), cancellationToken).ConfigureAwait(false);

        if (assembly is null)
        {
            return null;
        }

        string path = target.AbsolutePath.TrimStart('/');
        string? name = assembly.GetManifestResourceNames().FirstOrDefault(
            candidate => candidate.EndsWith(path.Replace('/', '.'), StringComparison.Ordinal)
                || string.Equals(candidate, path, StringComparison.Ordinal));

        if (name is null)
        {
            return null;
        }

        return new XamlResource(
            target,
            _ => new ValueTask<Stream>(assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Resource '{name}' disappeared from '{assemblyName}'.")));
    }

}

/// <summary>
/// Consults an ordered list of resolvers and takes the first answer.
/// </summary>
/// <remarks>
/// Order decides precedence, which is how an in-memory edit overrides the file or embedded
/// resource it was loaded from.
/// </remarks>
public sealed class CompositeResourceResolver : IXamlResourceResolver
{
    private readonly ImmutableArray<IXamlResourceResolver> _resolvers;

    /// <summary>Creates a composite over resolvers, in priority order.</summary>
    /// <param name="resolvers">The resolvers to consult, highest priority first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resolvers"/> or one of its elements is <see langword="null"/>.</exception>
    public CompositeResourceResolver(params IXamlResourceResolver[] resolvers)
        : this((IEnumerable<IXamlResourceResolver>)resolvers)
    {
    }

    /// <summary>Creates a composite over resolvers, in priority order.</summary>
    /// <param name="resolvers">The resolvers to consult, highest priority first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resolvers"/> or one of its elements is <see langword="null"/>.</exception>
    public CompositeResourceResolver(IEnumerable<IXamlResourceResolver> resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);

        _resolvers = [.. resolvers];

        if (_resolvers.Any(static resolver => resolver is null))
        {
            throw new ArgumentNullException(nameof(resolvers), "One of the resolvers is null.");
        }
    }

    /// <summary>Gets the resolvers, in the order they are consulted.</summary>
    public IReadOnlyList<IXamlResourceResolver> Resolvers => _resolvers;

    /// <inheritdoc />
    public async ValueTask<XamlResource?> ResolveAsync(
        Uri resourceUri,
        Uri? baseUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);

        foreach (IXamlResourceResolver resolver in _resolvers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            XamlResource? resource = await resolver
                .ResolveAsync(resourceUri, baseUri, cancellationToken).ConfigureAwait(false);

            if (resource is not null)
            {
                return resource;
            }
        }

        return null;
    }
}
