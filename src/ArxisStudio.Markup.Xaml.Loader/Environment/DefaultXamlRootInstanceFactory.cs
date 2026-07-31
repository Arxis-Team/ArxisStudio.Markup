using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Creates a root instance by calling its parameterless constructor.
/// </summary>
/// <remarks>
/// <para>
/// There is a real hazard here, and it is the reason <see cref="IXamlRootInstanceFactory"/>
/// exists rather than this being hard-coded. A generated <c>x:Class</c> partial usually calls
/// <c>InitializeComponent()</c> from its constructor, which loads the document. Constructing
/// such a type and then populating it loads the document twice: the children appear twice, the
/// handlers are attached twice, and nothing about the result says why.
/// </para>
/// <para>
/// This factory does not attempt to detect that. It cannot: whether a constructor calls
/// <c>InitializeComponent</c> is a fact about code it has never seen. A caller whose types
/// initialise themselves should supply a factory that constructs them in whatever way skips
/// that — a purpose-built constructor, a flag the constructor honours, or a pooled instance.
/// </para>
/// <para>
/// What this factory deliberately does not do is create the object without running its
/// constructor at all. That leaves readonly fields unset and invariants unestablished, and the
/// resulting failures surface far from the cause.
/// </para>
/// </remarks>
public sealed class DefaultXamlRootInstanceFactory : IXamlRootInstanceFactory
{
    /// <summary>Gets the shared instance.</summary>
    public static DefaultXamlRootInstanceFactory Instance { get; } = new();

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="rootType"/> is <see langword="null"/>.</exception>
    /// <exception cref="MissingMethodException">The type has no accessible parameterless constructor.</exception>
    public ValueTask<object> CreateAsync(
        Type rootType,
        XamlRootInstanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rootType);
        cancellationToken.ThrowIfCancellationRequested();

        object instance = Activator.CreateInstance(rootType)
            ?? throw new MissingMethodException(
                $"'{rootType.FullName}' produced no instance from its parameterless constructor.");

        return new ValueTask<object>(instance);
    }
}
