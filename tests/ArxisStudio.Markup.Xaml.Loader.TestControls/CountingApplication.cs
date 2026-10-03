using System.Threading;
using Avalonia;
using Avalonia.Interactivity;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// An application's class, as an <c>App.axaml</c> names it, that counts how often it is constructed.
/// </summary>
/// <remarks>
/// A host that wants what an application's document declares — its styles and resources — and not
/// the application itself must be able to say so: constructing the class is running the user's
/// startup code inside the host. The count is what tells the two apart.
/// </remarks>
public class CountingApplication : Application
{
    private static int _constructions;

    /// <summary>Counts the construction.</summary>
    public CountingApplication() => Interlocked.Increment(ref _constructions);

    /// <summary>Gets how many instances have been constructed in this process.</summary>
    public static int Constructions => Volatile.Read(ref _constructions);

    /// <summary>A handler a document of the application can name.</summary>
    /// <param name="sender">The object that raised the event.</param>
    /// <param name="e">The event's arguments.</param>
    public void TrayClicked(object? sender, RoutedEventArgs e)
    {
    }
}
