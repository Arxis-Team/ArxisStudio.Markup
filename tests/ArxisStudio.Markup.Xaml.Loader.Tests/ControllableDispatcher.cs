using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// A dispatcher a test can stop an operation inside of, and start again when it chooses.
/// </summary>
/// <remarks>
/// <para>
/// How the concurrency tests are made deterministic without sleeping. A session reaches its
/// objects only through its dispatcher, so holding one invocation here holds the update that made
/// it — with the session's mutation gate taken — for exactly as long as the test wants, and
/// whatever the test does meanwhile is racing a known state rather than a timer.
/// </para>
/// <para>
/// Everything is still run on Avalonia's own UI thread underneath, because the objects have
/// thread affinity and a test that lied about that would be testing something else.
/// </para>
/// </remarks>
internal sealed class ControllableDispatcher : IXamlDispatcher
{
    private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile bool _holding;

    /// <summary>Gets or sets something to run before each invocation, given its ordinal from 1.</summary>
    /// <remarks>
    /// For a test that has to make something true at a particular point of an update — cancelling
    /// a token once the writes have happened, say — which is otherwise not reachable from outside.
    /// </remarks>
    public Action<int>? Before { get; set; }

    /// <summary>
    /// Gets or sets something to run after each dispatched operation, given its ordinal from 1.
    /// </summary>
    /// <remarks>
    /// For recording what an operation did at the moment it did it. Watching the tasks complete
    /// records the order a test awaited them in, which is not the question.
    /// </remarks>
    public Action<int>? After { get; set; }

    /// <summary>Gets how many operations have been dispatched.</summary>
    public int Invocations { get; private set; }

    /// <summary>Gets whether a dispatched operation is running right now.</summary>
    /// <remarks>
    /// For asserting that something happens <em>outside</em> a dispatched operation. Resolving a
    /// document's <c>x:Class</c> is the case that matters: it is the caller's resolver, it may be
    /// genuinely asynchronous, and waiting for it from inside an operation is waiting for the
    /// owning thread from the owning thread.
    /// </remarks>
    public bool Dispatching { get; private set; }

    /// <summary>Gets a task that completes once a held invocation has arrived and is waiting.</summary>
    public Task Arrived => _arrived.Task;

    /// <summary>Makes the next invocation wait until <see cref="Release"/>.</summary>
    public void Hold() => _holding = true;

    /// <summary>Lets a held invocation carry on, and stops holding later ones.</summary>
    public void Release()
    {
        _holding = false;
        _released.TrySetResult();
    }

    /// <inheritdoc />
    public bool CheckAccess() => AvaloniaXamlDispatcher.Instance.CheckAccess();

    /// <inheritdoc />
    public async ValueTask<T> InvokeAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
    {
        int ordinal = ++Invocations;

        Before?.Invoke(ordinal);

        if (_holding)
        {
            _arrived.TrySetResult();

            await _released.Task.WaitAsync(cancellationToken);
        }

        // Run inside the dispatched operation rather than after awaiting it, because what it is
        // there to look at is Avalonia objects, and they belong to that thread.
        return await AvaloniaXamlDispatcher.Instance.InvokeAsync(
            () =>
            {
                Dispatching = true;

                try
                {
                    T value = operation();

                    After?.Invoke(ordinal);

                    return value;
                }
                finally
                {
                    Dispatching = false;
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<T> RunAsync<T>(
        Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        int ordinal = ++Invocations;

        Before?.Invoke(ordinal);

        if (_holding)
        {
            _arrived.TrySetResult();

            await _released.Task.WaitAsync(cancellationToken);
        }

        return await AvaloniaXamlDispatcher.Instance.RunAsync(
            async () =>
            {
                Dispatching = true;

                try
                {
                    // Keeping the context is the point of this overload: the operation resumes on
                    // the thread it started on rather than wherever the pool put it.
                    T value = await operation().ConfigureAwait(true);

                    After?.Invoke(ordinal);

                    return value;
                }
                finally
                {
                    Dispatching = false;
                }
            },
            cancellationToken);
    }
}
