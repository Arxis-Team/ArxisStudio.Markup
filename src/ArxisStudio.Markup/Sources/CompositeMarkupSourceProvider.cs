using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup;

/// <summary>
/// A provider that consults an ordered list of providers and returns the first answer.
/// </summary>
/// <remarks>
/// The ordering is the whole point and is honoured strictly: the first provider that knows a
/// URI wins, so putting an <see cref="InMemoryMarkupSourceProvider"/> ahead of a
/// <see cref="FileMarkupSourceProvider"/> lets an unsaved document override the file of the
/// same URI.
/// </remarks>
public sealed class CompositeMarkupSourceProvider : IMarkupSourceProvider
{
    private readonly ImmutableArray<IMarkupSourceProvider> _providers;

    /// <summary>Creates a composite over providers, in priority order.</summary>
    /// <param name="providers">The providers to consult, highest priority first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> or one of its elements is <see langword="null"/>.</exception>
    public CompositeMarkupSourceProvider(params IMarkupSourceProvider[] providers)
        : this((IEnumerable<IMarkupSourceProvider>)providers)
    {
    }

    /// <summary>Creates a composite over providers, in priority order.</summary>
    /// <param name="providers">The providers to consult, highest priority first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> or one of its elements is <see langword="null"/>.</exception>
    public CompositeMarkupSourceProvider(IEnumerable<IMarkupSourceProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = [.. providers];

        for (int index = 0; index < _providers.Length; index++)
        {
            if (_providers[index] is null)
            {
                throw new ArgumentNullException(
                    nameof(providers), $"The provider at index {index} is null.");
            }
        }
    }

    /// <summary>Gets the providers, in the order they are consulted.</summary>
    public IReadOnlyList<IMarkupSourceProvider> Providers => _providers;

    /// <inheritdoc />
    public async ValueTask<MarkupSource?> TryGetSourceAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        foreach (IMarkupSourceProvider provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MarkupSource? source = await provider.TryGetSourceAsync(uri, cancellationToken).ConfigureAwait(false);

            if (source is not null)
            {
                return source;
            }
        }

        return null;
    }
}
