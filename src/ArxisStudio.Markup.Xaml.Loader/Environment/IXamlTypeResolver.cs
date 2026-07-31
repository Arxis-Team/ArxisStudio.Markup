using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Turns a name the document uses into a CLR type.
/// </summary>
/// <remarks>
/// <para>
/// Used for this library's own analysis — telling a caller what a name means, classifying
/// members, and reporting what could not be found. Avalonia's runtime loader resolves types
/// independently when it builds an object graph; this resolver does not feed it.
/// </para>
/// <para>
/// Resolution never loads an assembly of its own accord. Everything it can see arrives through
/// an <see cref="IXamlAssemblyResolver"/>.
/// </para>
/// </remarks>
public interface IXamlTypeResolver
{
    /// <summary>Resolves a type name against the namespaces in scope where it was written.</summary>
    /// <param name="typeName">The name to resolve.</param>
    /// <param name="namespaceContext">The namespace declarations in scope at the name's position.</param>
    /// <param name="cancellationToken">A token to observe while resolving.</param>
    /// <returns>The resolved type, or the diagnostics explaining why it was not found.</returns>
    ValueTask<XamlTypeResolution> ResolveAsync(
        XamlTypeName typeName,
        XamlNamespaceContext namespaceContext,
        CancellationToken cancellationToken);
}
