using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Kinds;

/// <summary>
/// A type resolver for a project that has not been built yet: it knows Avalonia and nothing of the
/// project's own.
/// </summary>
/// <remarks>
/// <para>
/// The showcase is its own project, already built and loaded, so the default resolver finds its
/// controls in the process. A designer opening a project does not have that luxury, and this is
/// how the gallery shows what it gets instead — by wrapping the resolver it would otherwise use,
/// which is the only door anything external comes through.
/// </para>
/// <para>
/// A failure is what an unbuilt project's resolver would say, in its words: the diagnostic code
/// the default resolver uses for a type it cannot find.
/// </para>
/// </remarks>
internal sealed class UnbuiltProjectTypeResolver(IXamlTypeResolver inner, Assembly project) : IXamlTypeResolver
{
    /// <inheritdoc />
    public async ValueTask<XamlTypeResolution> ResolveAsync(
        XamlTypeName typeName,
        XamlNamespaceContext namespaceContext,
        CancellationToken cancellationToken)
    {
        XamlTypeResolution resolution = await inner
            .ResolveAsync(typeName, namespaceContext, cancellationToken)
            .ConfigureAwait(false);

        return resolution.Type?.Assembly == project
            ? XamlTypeResolution.Failed(MarkupDiagnostic.Resolution(
                XamlLoaderDiagnosticCodes.UnresolvedType,
                $"Тип '{typeName.LocalName}' объявлен в сборке проекта, а проект ещё не собран."))
            : resolution;
    }

    /// <summary>Builds an environment that sees everything another does, except the project.</summary>
    /// <param name="built">The environment of the built project.</param>
    /// <param name="project">The project's own assembly.</param>
    /// <returns>The environment.</returns>
    internal static XamlLoadEnvironment Around(XamlLoadEnvironment built, Assembly project)
    {
        ArgumentNullException.ThrowIfNull(built);

        return new XamlLoadEnvironment
        {
            SourceProvider = built.SourceProvider,
            AssemblyResolver = built.AssemblyResolver,
            TypeResolver = new UnbuiltProjectTypeResolver(built.TypeResolver, project),
            ResourceResolver = built.ResourceResolver,
        };
    }
}
