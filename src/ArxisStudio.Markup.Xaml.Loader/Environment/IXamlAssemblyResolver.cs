using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Finds the assembly behind an assembly name.
/// </summary>
/// <remarks>
/// This is the only way assemblies enter the loader. Nothing here searches a package cache,
/// reads a project file or restores anything — a caller that obtained an assembly from a NuGet
/// package hands it over explicitly, and discovering that package is somebody else's job.
/// </remarks>
public interface IXamlAssemblyResolver
{
    /// <summary>Attempts to resolve an assembly name.</summary>
    /// <param name="assemblyName">The name to resolve.</param>
    /// <param name="cancellationToken">A token to observe while resolving.</param>
    /// <returns>
    /// The assembly, or <see langword="null"/> when this resolver does not know it. Not knowing
    /// a name is an ordinary answer, not a failure.
    /// </returns>
    ValueTask<Assembly?> ResolveAsync(AssemblyName assemblyName, CancellationToken cancellationToken);
}
