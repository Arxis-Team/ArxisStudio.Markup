using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The one thing a session lets change it at a time, handed out in the order it was asked for.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SemaphoreSlim"/> was what this used to be. It gives mutual exclusion, which is
/// most of the job, but it does not promise the order its waiters are released in — and that
/// promise is the rest of the job. A host watching a folder submits an update per save; if three
/// are queued and the second-oldest is released last, the preview ends up showing text the user
/// typed two saves ago, with every individual mutation perfectly serialised.
/// </para>
/// <para>
/// So the order is decided here, when the request arrives, rather than by whatever the thread pool
/// does with the continuations. The lock is held only long enough to move a link in a list, never
/// across an <c>await</c>, and ownership passes straight from the leaving operation to the next
/// waiter rather than being dropped for whoever wakes first to grab.
/// </para>
/// </remarks>
internal sealed class XamlMutationGate
{
    private readonly object _sync = new();

    /// <summary>Who is waiting, oldest first.</summary>
    /// <remarks>
    /// A linked list rather than a queue so that a waiter which gives up can be taken out of the
    /// middle when it does, instead of being skipped over later. A caller that cancels a hundred
    /// updates behind one slow one should not leave a hundred dead entries behind.
    /// </remarks>
    private readonly LinkedList<Waiter> _waiting = new();

    private bool _held;

    /// <summary>
    /// Takes the gate, waiting behind everyone who asked first.
    /// </summary>
    /// <param name="cancellationToken">A token to give up waiting on.</param>
    /// <returns>A lease that releases the gate when it is disposed.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled while waiting.</exception>
    internal ValueTask<XamlMutationLease> EnterAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<XamlMutationLease>(cancellationToken);
        }

        Waiter waiter;

        lock (_sync)
        {
            if (!_held)
            {
                _held = true;

                return new ValueTask<XamlMutationLease>(new XamlMutationLease(this));
            }

            waiter = new Waiter(this);
            waiter.Node = _waiting.AddLast(waiter);
        }

        return WaitAsync(waiter, cancellationToken);
    }

    /// <summary>
    /// Takes the gate only if it is free this instant, for a caller that must not wait.
    /// </summary>
    /// <remarks>
    /// Free means nobody holds it <em>and</em> nobody is queued for it. A synchronous edit that
    /// took the gate out from under a queue of updates would be jumping a line it is not even
    /// willing to stand in.
    /// </remarks>
    /// <returns>A lease, or <see langword="null"/> when the gate is busy.</returns>
    internal XamlMutationLease? TryEnter()
    {
        lock (_sync)
        {
            if (_held || _waiting.Count > 0)
            {
                return null;
            }

            _held = true;

            return new XamlMutationLease(this);
        }
    }

    /// <summary>Waits for a queued turn, and gives it straight back if the wait was for nothing.</summary>
    private async ValueTask<XamlMutationLease> WaitAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        using (cancellationToken.Register(static state => ((Waiter)state!).Gate.Abandon((Waiter)state), waiter))
        {
            await waiter.Completion.Task.ConfigureAwait(false);
        }

        var lease = new XamlMutationLease(this);

        // Cancelled between being granted the turn and taking it: the registration deliberately
        // cannot release ownership, because a waiter that never had it would then be releasing
        // somebody else's. Here the turn really was granted, so it is given back before throwing.
        if (cancellationToken.IsCancellationRequested)
        {
            lease.Dispose();

            cancellationToken.ThrowIfCancellationRequested();
        }

        return lease;
    }

    /// <summary>Takes a waiter out of the queue because it gave up.</summary>
    /// <remarks>
    /// Only ever settles a waiter that has not already been granted its turn. A registration that
    /// could release the gate would be releasing something it never held, which is how a queue
    /// ends up with two owners.
    /// </remarks>
    private void Abandon(Waiter waiter)
    {
        bool abandoned;

        lock (_sync)
        {
            abandoned = !waiter.Settled;

            if (abandoned)
            {
                waiter.Settled = true;

                if (waiter.Node is { List: not null } node)
                {
                    _waiting.Remove(node);
                }
            }
        }

        if (abandoned)
        {
            waiter.Completion.TrySetCanceled();
        }
    }

    /// <summary>Passes the gate to whoever asked next, or leaves it free.</summary>
    private void Exit()
    {
        Waiter? next = null;

        lock (_sync)
        {
            while (_waiting.First is { } first)
            {
                _waiting.RemoveFirst();

                if (first.Value.Settled)
                {
                    continue;
                }

                first.Value.Settled = true;
                next = first.Value;

                break;
            }

            // Ownership is handed over rather than dropped: _held stays true for the waiter that
            // is about to have it, so nothing that arrives in between can take it instead.
            if (next is null)
            {
                _held = false;
            }
        }

        // Outside the lock, and asynchronously, so the next operation never runs any of itself
        // while this one is still unwinding.
        next?.Completion.TrySetResult();
    }

    /// <summary>One asked-for turn.</summary>
    private sealed class Waiter
    {
        internal Waiter(XamlMutationGate gate) => Gate = gate;

        /// <summary>Completed when the turn is granted, cancelled when it is given up.</summary>
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Where this waiter sits, so that giving up is not a search.</summary>
        internal LinkedListNode<Waiter>? Node { get; set; }

        /// <summary>Whether the turn has been decided, either way. Guarded by the gate's lock.</summary>
        internal bool Settled { get; set; }

        /// <summary>The gate, so the cancellation registration reaches it without a closure.</summary>
        internal XamlMutationGate Gate { get; }
    }

    /// <summary>
    /// A turn at changing the session, given back exactly once however it ends.
    /// </summary>
    internal sealed class XamlMutationLease : IDisposable
    {
        private XamlMutationGate? _gate;

        internal XamlMutationLease(XamlMutationGate gate) => _gate = gate;

        /// <summary>Gives the turn back, and does nothing if it already has been.</summary>
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Exit();
    }
}
