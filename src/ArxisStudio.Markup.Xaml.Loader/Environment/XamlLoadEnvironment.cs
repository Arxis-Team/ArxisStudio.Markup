using System;
using System.Collections.Generic;
using System.Reflection;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Everything outside the document that loading it needs.
/// </summary>
/// <remarks>
/// <para>
/// Every external dependency arrives here and nowhere else. Nothing in these packages opens a
/// solution, evaluates a project file, reads <c>project.assets.json</c> or searches a package
/// cache; a future project-system package will supply implementations of these interfaces
/// without any change here.
/// </para>
/// <para>
/// <see cref="CreateDefault"/> is enough for a document of standard controls plus whatever
/// assemblies the caller hands over. It is a starting point, not a discovery mechanism.
/// </para>
/// </remarks>
public sealed class XamlLoadEnvironment
{
    /// <summary>Gets the provider documents are read through.</summary>
    public required IMarkupSourceProvider SourceProvider { get; init; }

    /// <summary>Gets how assemblies named by the document are found.</summary>
    public required IXamlAssemblyResolver AssemblyResolver { get; init; }

    /// <summary>Gets how names in the document become CLR types.</summary>
    public required IXamlTypeResolver TypeResolver { get; init; }

    /// <summary>Gets how resource URIs become content.</summary>
    public required IXamlResourceResolver ResourceResolver { get; init; }

    /// <summary>
    /// Gets how the object a document with <c>x:Class</c> populates is created, if the caller
    /// supplies one.
    /// </summary>
    public IXamlRootInstanceFactory? RootInstanceFactory { get; init; }

    /// <summary>
    /// Gets the thread that owns the Avalonia objects, defaulting to Avalonia's own UI thread.
    /// </summary>
    public IXamlDispatcher Dispatcher { get; init; } = AvaloniaXamlDispatcher.Instance;

    /// <summary>Gets services made available to markup extensions and type converters.</summary>
    public IServiceProvider? Services { get; init; }

    /// <summary>Gets what decides which member of which kind a name on a type is.</summary>
    /// <remarks>
    /// <para>
    /// One per environment, because what it caches is CLR metadata about the assemblies this
    /// environment resolves. A tool that rebuilds the user's control library and loads it again —
    /// which is what a designer does all day — builds a new environment for the new assemblies,
    /// and the answers about the old ones go with the old one. A cache that outlived the
    /// environment would hand out descriptors of types nobody can load any more, and hold those
    /// types alive against a collectible load context that was meant to be unloaded.
    /// </para>
    /// <para>
    /// Set it when building an environment by hand to share the cache between environments that
    /// resolve the same assemblies — which is a decision worth making on purpose, because a shared
    /// cache survives the rebuild the separate environments existed to isolate.
    /// <see cref="XamlMemberResolver.Instance"/> remains for a caller with no environment at all.
    /// </para>
    /// </remarks>
    public XamlMemberResolver MemberResolver { get; init; } = new();

    /// <summary>
    /// Builds an environment over the assemblies the caller supplies plus those already loaded.
    /// </summary>
    /// <remarks>
    /// The supplied assemblies come first, so a caller's own build of a control library wins
    /// over an identically named one the process happens to have loaded.
    /// </remarks>
    /// <param name="assemblies">Assemblies the document may name, such as a custom control library.</param>
    /// <param name="sourceProvider">
    /// Where documents are read from, or <see langword="null"/> for files on disk plus nothing else.
    /// </param>
    /// <returns>The environment.</returns>
    public static XamlLoadEnvironment CreateDefault(
        IEnumerable<Assembly>? assemblies = null,
        IMarkupSourceProvider? sourceProvider = null)
    {
        var explicitly = new ExplicitAssemblyResolver(assemblies ?? []);
        var assemblyResolver = new CompositeAssemblyResolver(explicitly, LoadedAssemblyResolver.Instance);

        return new XamlLoadEnvironment
        {
            SourceProvider = sourceProvider ?? new FileMarkupSourceProvider(),
            AssemblyResolver = assemblyResolver,
            TypeResolver = new XamlTypeResolver(assemblyResolver),
            ResourceResolver = new CompositeResourceResolver(
                new AvaloniaResourceResolver(assemblyResolver),
                FileResourceResolver.Instance),
        };
    }
}
