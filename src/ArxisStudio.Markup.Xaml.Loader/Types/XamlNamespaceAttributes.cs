using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using Avalonia.Metadata;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Reads what an assembly says about the XAML namespaces its types are written in.
/// </summary>
/// <remarks>
/// One reading for the resolver that finds a type by the namespace a document wrote and the catalog
/// that says which namespace to write it in, so the two cannot disagree about where a type lives.
/// Nothing is cached here: an assembly may live in a context that is meant to unload, and a table
/// held by a static would keep it — each caller caches for as long as it lives itself.
/// </remarks>
internal static class XamlNamespaceAttributes
{
    /// <summary>Reads the namespaces an assembly maps its CLR namespaces to.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>The mappings in declaration order, or none when they cannot be read.</returns>
    internal static ImmutableArray<XmlnsDefinitionAttribute> DefinitionsOf(Assembly assembly) =>
        Read<XmlnsDefinitionAttribute>(assembly);

    /// <summary>Reads the prefixes an assembly suggests for its namespaces.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>The suggestions in declaration order, or none when they cannot be read.</returns>
    internal static ImmutableArray<XmlnsPrefixAttribute> PrefixesOf(Assembly assembly) =>
        Read<XmlnsPrefixAttribute>(assembly);

    private static ImmutableArray<T> Read<T>(Assembly assembly)
        where T : Attribute
    {
        try
        {
            return [.. assembly.GetCustomAttributes<T>()];
        }
        catch (Exception error) when (error is TypeLoadException or FileNotFoundException or FileLoadException)
        {
            // An assembly whose attributes cannot be read contributes nothing. It is not a reason to
            // fail whatever asked.
            return [];
        }
    }
}
