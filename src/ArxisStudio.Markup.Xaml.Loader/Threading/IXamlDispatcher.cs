using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The thread that owns the Avalonia objects a session creates.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia objects have thread affinity: creating or touching one from the wrong thread
/// corrupts state in ways that surface much later and somewhere else. Every operation that
/// reaches an Avalonia object goes through here, so there is one place that decides what the
/// right thread is.
/// </para>
/// <para>
/// It is an interface so a host can supply its own — a headless test runner, a designer with
/// its own message loop — rather than being forced onto whatever
/// <c>Dispatcher.UIThread</c> happens to mean in its process.
/// </para>
/// </remarks>
public interface IXamlDispatcher
{
    /// <summary>Gets a value indicating whether the calling thread owns the Avalonia objects.</summary>
    bool CheckAccess();

    /// <summary>Runs an operation on the owning thread and waits for its result.</summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="operation">The operation to run.</param>
    /// <param name="cancellationToken">A token to observe while waiting.</param>
    /// <returns>The operation's result.</returns>
    ValueTask<T> InvokeAsync<T>(Func<T> operation, CancellationToken cancellationToken = default);

    /// <summary>Runs an asynchronous operation on the owning thread and waits for its result.</summary>
    /// <remarks>
    /// <para>
    /// For work that has to happen on the owning thread and is itself asynchronous — creating a
    /// root instance through a caller's <see cref="IXamlRootInstanceFactory"/> is the worked
    /// example. Such work cannot go through <see cref="InvokeAsync{T}"/>, because the only way to
    /// produce a result there is to wait for the task inside the operation, on the owning thread:
    /// if the operation's continuation needs that thread, it is waiting for the thread its own
    /// caller is holding, and neither side is ever going to move.
    /// </para>
    /// <para>
    /// So an implementation <em>starts</em> the operation on the owning thread and resumes its
    /// continuations there, leaving the thread free while the operation is waiting. The token
    /// abandons an operation that has not started yet; once it has, cancelling is the operation's
    /// own business, and it is handed the same token to do it with.
    /// </para>
    /// <para>
    /// A second name rather than an overload of the first, deliberately. A lambda that returns a
    /// task satisfies both signatures, and which one it binds to is decided by rules nobody reads
    /// at a call site — while the difference between them is whether the task is awaited at all.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="operation">The operation to run.</param>
    /// <param name="cancellationToken">A token to observe while waiting.</param>
    /// <returns>The operation's result.</returns>
    ValueTask<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);
}
