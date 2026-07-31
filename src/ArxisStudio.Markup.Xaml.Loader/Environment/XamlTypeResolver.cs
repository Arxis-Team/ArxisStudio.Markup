using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Metadata;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The default type resolver: reads a XAML namespace URI, finds the assemblies it names, and
/// looks the type up in them.
/// </summary>
/// <remarks>
/// <para>
/// Three forms of namespace URI are understood. <c>using:Some.Namespace</c> and
/// <c>clr-namespace:Some.Namespace;assembly=Some.Assembly</c> name a CLR namespace directly.
/// Anything else is treated as a mapped namespace and matched against the
/// <see cref="XmlnsDefinitionAttribute"/> declarations of the assemblies in play, which is how
/// Avalonia's own <c>https://github.com/avaloniaui</c> resolves without being special-cased
/// here.
/// </para>
/// <para>
/// Lookups are cached per resolver. The contract's performance principles rule out a
/// reflection scan of every loaded assembly for every lookup, and a document mentions the same
/// handful of namespaces over and over.
/// </para>
/// </remarks>
public sealed class XamlTypeResolver : IXamlTypeResolver
{
    private const string UsingPrefix = "using:";
    private const string ClrNamespacePrefix = "clr-namespace:";
    private const string AssemblyMarker = ";assembly=";

    private readonly IXamlAssemblyResolver _assemblyResolver;
    private readonly ImmutableArray<Assembly> _searchAssemblies;
    private readonly ConcurrentDictionary<string, XamlTypeResolution> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Assembly, ImmutableArray<XmlnsDefinitionAttribute>> _xmlns = new();

    /// <summary>Creates a resolver.</summary>
    /// <param name="assemblyResolver">How assemblies named by a namespace are found.</param>
    /// <param name="searchAssemblies">
    /// Assemblies searched for mapped namespaces, where the URI names no assembly of its own.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="assemblyResolver"/> is <see langword="null"/>.</exception>
    public XamlTypeResolver(IXamlAssemblyResolver assemblyResolver, IEnumerable<Assembly>? searchAssemblies = null)
    {
        ArgumentNullException.ThrowIfNull(assemblyResolver);

        _assemblyResolver = assemblyResolver;
        _searchAssemblies = [.. searchAssemblies ?? []];
    }

    /// <inheritdoc />
    public async ValueTask<XamlTypeResolution> ResolveAsync(
        XamlTypeName typeName,
        XamlNamespaceContext namespaceContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(namespaceContext);

        string key = typeName.ToString();

        if (_cache.TryGetValue(key, out XamlTypeResolution? cached))
        {
            return cached;
        }

        XamlTypeResolution resolution = await ResolveCoreAsync(typeName, cancellationToken).ConfigureAwait(false);

        _cache[key] = resolution;

        return resolution;
    }

    private async ValueTask<XamlTypeResolution> ResolveCoreAsync(
        XamlTypeName typeName,
        CancellationToken cancellationToken)
    {
        if (typeName.IsGeneric)
        {
            // Generic types need x:TypeArguments handling that belongs with the directive work,
            // not here. Saying so plainly beats silently resolving the open type.
            return XamlTypeResolution.Failed(Diagnostic(
                XamlLoaderDiagnosticCodes.UnsupportedGenericType,
                $"Generic type '{typeName}' is not resolved by this resolver yet."));
        }

        string uri = typeName.NamespaceUri;

        if (uri.StartsWith(UsingPrefix, StringComparison.Ordinal))
        {
            return await ResolveClrAsync(
                uri[UsingPrefix.Length..], assemblyName: null, typeName, cancellationToken).ConfigureAwait(false);
        }

        if (uri.StartsWith(ClrNamespacePrefix, StringComparison.Ordinal))
        {
            string body = uri[ClrNamespacePrefix.Length..];
            int marker = body.IndexOf(AssemblyMarker, StringComparison.Ordinal);

            return marker < 0
                ? await ResolveClrAsync(body, null, typeName, cancellationToken).ConfigureAwait(false)
                : await ResolveClrAsync(
                    body[..marker], body[(marker + AssemblyMarker.Length)..], typeName, cancellationToken).ConfigureAwait(false);
        }

        return await ResolveMappedAsync(uri, typeName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves a name written against a CLR namespace.</summary>
    private async ValueTask<XamlTypeResolution> ResolveClrAsync(
        string clrNamespace,
        string? assemblyName,
        XamlTypeName typeName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Assembly> candidates;

        if (assemblyName is null)
        {
            // "using:" without an assembly means "wherever it is", so every assembly in play is
            // fair game. That is Avalonia's own convention for local types.
            candidates = await SearchAssembliesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Assembly? assembly = await _assemblyResolver
                .ResolveAsync(new AssemblyName(assemblyName), cancellationToken).ConfigureAwait(false);

            if (assembly is null)
            {
                return XamlTypeResolution.Failed(Diagnostic(
                    XamlLoaderDiagnosticCodes.UnresolvedAssembly,
                    $"Assembly '{assemblyName}', named by '{typeName.NamespaceUri}', was not supplied. " +
                    "Add it to the environment's assembly resolver."));
            }

            candidates = [assembly];
        }

        string fullName = $"{clrNamespace}.{typeName.LocalName}";

        foreach (Assembly assembly in candidates)
        {
            if (assembly.GetType(fullName, throwOnError: false) is { } type)
            {
                return XamlTypeResolution.Resolved(type);
            }
        }

        return XamlTypeResolution.Failed(Diagnostic(
            XamlLoaderDiagnosticCodes.UnresolvedType,
            $"Type '{fullName}' was not found in {Describe(candidates)}."));
    }

    /// <summary>Resolves a name written against a mapped XML namespace.</summary>
    private async ValueTask<XamlTypeResolution> ResolveMappedAsync(
        string namespaceUri,
        XamlTypeName typeName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Assembly> assemblies = await SearchAssembliesAsync(cancellationToken).ConfigureAwait(false);
        var mapped = false;

        foreach (Assembly assembly in assemblies)
        {
            foreach (XmlnsDefinitionAttribute definition in XmlnsOf(assembly))
            {
                if (!string.Equals(definition.XmlNamespace, namespaceUri, StringComparison.Ordinal))
                {
                    continue;
                }

                mapped = true;

                if (assembly.GetType($"{definition.ClrNamespace}.{typeName.LocalName}", throwOnError: false) is { } type)
                {
                    return XamlTypeResolution.Resolved(type);
                }
            }
        }

        return XamlTypeResolution.Failed(Diagnostic(
            mapped ? XamlLoaderDiagnosticCodes.UnresolvedType : XamlLoaderDiagnosticCodes.UnknownNamespace,
            mapped
                ? $"Type '{typeName.LocalName}' was not found in any CLR namespace mapped to '{namespaceUri}'."
                : $"XAML namespace '{namespaceUri}' is not mapped by any assembly in play. " +
                  "Supply the assembly that declares it."));
    }

    /// <summary>
    /// Gets the assemblies searched when a namespace names none of its own.
    /// </summary>
    /// <remarks>
    /// The explicitly supplied ones come first so a caller's assembly wins over an identically
    /// named type that happens to be loaded in the process.
    /// </remarks>
    private async ValueTask<IReadOnlyList<Assembly>> SearchAssembliesAsync(CancellationToken cancellationToken)
    {
        var assemblies = new List<Assembly>(_searchAssemblies);

        if (_assemblyResolver is ExplicitAssemblyResolver explicitResolver)
        {
            assemblies.AddRange(explicitResolver.Assemblies);
        }
        else if (_assemblyResolver is CompositeAssemblyResolver composite)
        {
            assemblies.AddRange(composite.Resolvers.OfType<ExplicitAssemblyResolver>().SelectMany(static r => r.Assemblies));
        }

        // Avalonia's own assemblies are always in play, because a document of standard controls
        // has to load with no configuration at all.
        assemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic));

        await ValueTask.CompletedTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return [.. assemblies.Distinct()];
    }

    /// <summary>Reads an assembly's XAML namespace declarations, once.</summary>
    private ImmutableArray<XmlnsDefinitionAttribute> XmlnsOf(Assembly assembly) =>
        _xmlns.GetOrAdd(assembly, static value =>
        {
            try
            {
                return [.. value.GetCustomAttributes<XmlnsDefinitionAttribute>()];
            }
            catch (Exception error) when (error is TypeLoadException or FileNotFoundException or FileLoadException)
            {
                // An assembly whose attributes cannot be read contributes nothing. It is not a
                // reason to fail the whole resolution.
                return [];
            }
        });

    private static string Describe(IReadOnlyList<Assembly> assemblies) =>
        assemblies.Count == 1
            ? $"assembly '{assemblies[0].GetName().Name}'"
            : $"any of the {assemblies.Count} assemblies in play";

    private static MarkupDiagnostic Diagnostic(string code, string message) =>
        MarkupDiagnostic.Resolution(code, message);
}
