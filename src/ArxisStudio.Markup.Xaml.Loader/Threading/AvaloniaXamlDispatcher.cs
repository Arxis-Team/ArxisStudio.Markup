using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The default dispatcher, which defers to Avalonia's own UI thread.
/// </summary>
/// <remarks>
/// Correct for an application and for a headless test session alike, since both establish
/// <see cref="Dispatcher.UIThread"/>. A host with its own message loop should supply its own
/// <see cref="IXamlDispatcher"/> rather than work around this one.
/// </remarks>
public sealed class AvaloniaXamlDispatcher : IXamlDispatcher
{
    /// <summary>Gets the shared instance.</summary>
    public static AvaloniaXamlDispatcher Instance { get; } = new();

    /// <inheritdoc />
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    public async ValueTask<T> InvokeAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Already on the owning thread: run inline rather than posting, which would deadlock a
        // caller that is itself running on the dispatcher and waiting for the result.
        if (Dispatcher.UIThread.CheckAccess())
        {
            cancellationToken.ThrowIfCancellationRequested();

            return operation();
        }

        return await Dispatcher.UIThread.InvokeAsync(operation, DispatcherPriority.Normal, cancellationToken);
    }
}
