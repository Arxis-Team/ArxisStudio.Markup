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

    /// <summary>Gets how many operations have been dispatched.</summary>
    public int Invocations { get; private set; }

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
        Invocations++;
        Before?.Invoke(Invocations);

        if (_holding)
        {
            _arrived.TrySetResult();

            await _released.Task.WaitAsync(cancellationToken);
        }

        return await AvaloniaXamlDispatcher.Instance.InvokeAsync(operation, cancellationToken);
    }
}
