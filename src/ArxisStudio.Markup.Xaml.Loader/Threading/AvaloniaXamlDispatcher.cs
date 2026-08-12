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

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    public async ValueTask<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (Dispatcher.UIThread.CheckAccess())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Already on the owning thread, so there is nothing to post. The await keeps the
            // context rather than dropping it, which is what brings the operation's own
            // continuations back to this thread: Avalonia's dispatcher is its synchronization
            // context, and the objects the operation is making belong to it.
            return await operation().ConfigureAwait(true);
        }

        // Avalonia's own overload for a callback that returns a task. It runs the callback on its
        // thread and completes when the task the callback returned finishes — so the thread is
        // released for the wait rather than held inside it, which is the whole difference between
        // this and handing the same work to InvokeAsync.
        //
        // That overload takes no token, so the check moves inside: refusing there is refusing at
        // the moment the operation would have started, which is what the token means for an
        // operation still queued in InvokeAsync too.
        return await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                return operation();
            },
            DispatcherPriority.Normal);
    }
}
