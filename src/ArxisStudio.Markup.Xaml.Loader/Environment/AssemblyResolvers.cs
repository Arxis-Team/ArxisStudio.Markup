using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Resolves only the assemblies the caller handed over.
/// </summary>
/// <remarks>
/// The explicit path, and the one a project system will eventually feed. An assembly obtained
/// from a NuGet package arrives here as an <see cref="Assembly"/>; how the caller got hold of
/// it is outside this repository entirely.
/// </remarks>
public sealed class ExplicitAssemblyResolver : IXamlAssemblyResolver
{
    private readonly ImmutableDictionary<string, Assembly> _assemblies;

    /// <summary>Creates a resolver over a fixed set of assemblies.</summary>
    /// <param name="assemblies">The assemblies to resolve, keyed by their simple names.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assemblies"/> or one of its elements is <see langword="null"/>.</exception>
    public ExplicitAssemblyResolver(params Assembly[] assemblies)
        : this((IEnumerable<Assembly>)assemblies)
    {
    }

    /// <summary>Creates a resolver over a fixed set of assemblies.</summary>
    /// <param name="assemblies">The assemblies to resolve.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assemblies"/> or one of its elements is <see langword="null"/>.</exception>
    public ExplicitAssemblyResolver(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        ImmutableDictionary<string, Assembly>.Builder builder =
            ImmutableDictionary.CreateBuilder<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        foreach (Assembly assembly in assemblies)
        {
            ArgumentNullException.ThrowIfNull(assembly, nameof(assemblies));

            builder[assembly.GetName().Name ?? assembly.FullName ?? string.Empty] = assembly;
        }

        _assemblies = builder.ToImmutable();
    }

    /// <summary>Gets the assemblies this resolver knows.</summary>
    /// <remarks>
    /// Materialised rather than cast: a dictionary's value view is a lazy sequence, and casting
    /// it to a collection interface compiles and then fails at run time.
    /// </remarks>
    public IReadOnlyCollection<Assembly> Assemblies => [.. _assemblies.Values];

    /// <inheritdoc />
    public ValueTask<Assembly?> ResolveAsync(AssemblyName assemblyName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        cancellationToken.ThrowIfCancellationRequested();

        _assemblies.TryGetValue(assemblyName.Name ?? string.Empty, out Assembly? assembly);

        return new ValueTask<Assembly?>(assembly);
    }
}

/// <summary>
/// Resolves assemblies already loaded into the current context.
/// </summary>
/// <remarks>
/// Covers Avalonia itself and anything the host process already references, which is what makes
/// a document of standard controls load with no configuration at all.
/// </remarks>
public sealed class LoadedAssemblyResolver : IXamlAssemblyResolver
{
    /// <summary>Gets the shared instance.</summary>
    public static LoadedAssemblyResolver Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask<Assembly?> ResolveAsync(AssemblyName assemblyName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        cancellationToken.ThrowIfCancellationRequested();

        Assembly? match = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(
            assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));

        return new ValueTask<Assembly?>(match);
    }
}

/// <summary>
/// Resolves assemblies from directories the caller named.
/// </summary>
/// <remarks>
/// <para>
/// The directories are the caller's to choose. This resolver never goes looking for a package
/// cache or an output folder of its own accord.
/// </para>
/// <para>
/// Loading an assembly runs its module initialisers, and an assembly loaded this way cannot be
/// unloaded. That is a real cost, and the reason this is opt-in rather than a default.
/// </para>
/// </remarks>
public sealed class DirectoryAssemblyResolver : IXamlAssemblyResolver
{
    private readonly ImmutableArray<string> _directories;
    private readonly Dictionary<string, Assembly?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>Creates a resolver over directories.</summary>
    /// <param name="directories">The directories to search, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="directories"/> is <see langword="null"/>.</exception>
    public DirectoryAssemblyResolver(params string[] directories)
        : this((IEnumerable<string>)directories)
    {
    }

    /// <summary>Creates a resolver over directories.</summary>
    /// <param name="directories">The directories to search, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="directories"/> is <see langword="null"/>.</exception>
    public DirectoryAssemblyResolver(IEnumerable<string> directories)
    {
        ArgumentNullException.ThrowIfNull(directories);

        _directories = [.. directories];
    }

    /// <summary>Gets the directories this resolver searches.</summary>
    public IReadOnlyList<string> Directories => _directories;

    /// <inheritdoc />
    public ValueTask<Assembly?> ResolveAsync(AssemblyName assemblyName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        cancellationToken.ThrowIfCancellationRequested();

        string name = assemblyName.Name ?? string.Empty;

        lock (_gate)
        {
            // Both hits and misses are cached: an assembly cannot be loaded twice, and probing
            // the filesystem again for a name already known to be absent buys nothing.
            if (_cache.TryGetValue(name, out Assembly? cached))
            {
                return new ValueTask<Assembly?>(cached);
            }

            Assembly? resolved = Probe(name);

            _cache[name] = resolved;

            return new ValueTask<Assembly?>(resolved);
        }
    }

    private Assembly? Probe(string name)
    {
        foreach (string directory in _directories)
        {
            string candidate = Path.Combine(directory, name + ".dll");

            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                return Assembly.LoadFrom(candidate);
            }
            catch (Exception error) when (error is BadImageFormatException or FileLoadException or IOException)
            {
                // A file that is not a usable assembly is a miss, not a crash. The caller
                // pointed at a directory, not at this particular file.
            }
        }

        return null;
    }
}

/// <summary>
/// Consults an ordered list of resolvers and takes the first answer.
/// </summary>
/// <remarks>
/// Order is the whole point: putting an <see cref="ExplicitAssemblyResolver"/> first lets a
/// caller override what the process happens to have loaded.
/// </remarks>
public sealed class CompositeAssemblyResolver : IXamlAssemblyResolver
{
    private readonly ImmutableArray<IXamlAssemblyResolver> _resolvers;

    /// <summary>Creates a composite over resolvers, in priority order.</summary>
    /// <param name="resolvers">The resolvers to consult, highest priority first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resolvers"/> or one of its elements is <see langword="null"/>.</exception>
    public CompositeAssemblyResolver(params IXamlAssemblyResolver[] resolvers)
        : this((IEnumerable<IXamlAssemblyResolver>)resolvers)
    {
    }

    /// <summary>Creates a composite over resolvers, in priority order.</summary>
    /// <param name="resolvers">The resolvers to consult, highest priority first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resolvers"/> or one of its elements is <see langword="null"/>.</exception>
    public CompositeAssemblyResolver(IEnumerable<IXamlAssemblyResolver> resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);

        _resolvers = [.. resolvers];

        if (_resolvers.Any(static resolver => resolver is null))
        {
            throw new ArgumentNullException(nameof(resolvers), "One of the resolvers is null.");
        }
    }

    /// <summary>Gets the resolvers, in the order they are consulted.</summary>
    public IReadOnlyList<IXamlAssemblyResolver> Resolvers => _resolvers;

    /// <inheritdoc />
    public async ValueTask<Assembly?> ResolveAsync(AssemblyName assemblyName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);

        foreach (IXamlAssemblyResolver resolver in _resolvers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Assembly? assembly = await resolver.ResolveAsync(assemblyName, cancellationToken).ConfigureAwait(false);

            if (assembly is not null)
            {
                return assembly;
            }
        }

        return null;
    }
}
