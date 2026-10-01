using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Creates a root instance by calling its parameterless constructor.
/// </summary>
/// <remarks>
/// <para>
/// A generated <c>x:Class</c> partial calls <c>InitializeComponent()</c> from its constructor,
/// which loads markup. That load is where the session's document goes: while this factory runs,
/// the session has lent the type's populate hook its own population, so a type built by
/// Avalonia's XAML compiler is populated once, from the document, inside its constructor
/// (ADR 0015). This factory needs to know nothing about it, and does not detect it.
/// </para>
/// <para>
/// What it cannot cover is a type that loads markup some other way than through the compiled
/// hook — a hand-written <c>AvaloniaXamlLoader.Load(uri)</c>, say. Constructing such a type and
/// then populating it still loads twice. A caller whose types do that supplies a factory that
/// constructs them in whatever way skips it — a purpose-built constructor, a flag the constructor
/// honours, or a pooled instance.
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
