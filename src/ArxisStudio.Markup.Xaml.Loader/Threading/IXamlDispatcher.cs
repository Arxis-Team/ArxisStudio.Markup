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
}
