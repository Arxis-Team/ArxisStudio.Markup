using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A view a document names with <c>x:Class</c>, which counts how often it is constructed and how
/// often its handlers run.
/// </summary>
/// <remarks>
/// The class an update must not construct a second time: a constructor is the author's code, and
/// one that subscribes to something or opens a file does it once per construction. The count is
/// process-wide, so a test reads it before and after rather than expecting a number.
/// </remarks>
public class CountedView : UserControl
{
    private static int s_constructed;

    /// <summary>Creates the view and counts it.</summary>
    public CountedView() => Interlocked.Increment(ref s_constructed);

    /// <summary>Gets how many views of this class have been constructed in the process.</summary>
    public static int Constructed => Volatile.Read(ref s_constructed);

    /// <summary>Gets how many times <see cref="SaveClicked"/> has run on this view.</summary>
    public int SaveClickCount { get; private set; }

    /// <summary>Gets which control <see cref="SaveClicked"/> was last raised by.</summary>
    public object? LastSender { get; private set; }

    /// <summary>Handles a click, named by a document's <c>Click</c> attribute.</summary>
    /// <param name="sender">The control that was clicked.</param>
    /// <param name="e">The event's arguments.</param>
    public void SaveClicked(object? sender, RoutedEventArgs e)
    {
        SaveClickCount++;
        LastSender = sender;
    }

    /// <summary>A method a <c>Click</c> attribute can name and no click can be delivered to.</summary>
    /// <param name="count">Not a click's argument at all.</param>
    public void Miscounted(int count) => SaveClickCount += count;
}

/// <summary>
/// A window that remembers every instance of itself, so a test can ask which are still open.
/// </summary>
/// <remarks>
/// The element a document's root is written as when the class it names derives from it. A rebuild
/// that builds the root's content without the class builds one of these, and that copy has a
/// platform window of its own until somebody closes it.
/// </remarks>
public class TrackedWindow : Window
{
    private static readonly List<WeakReference<TrackedWindow>> s_created = [];

    /// <summary>Creates the window and remembers it.</summary>
    public TrackedWindow()
    {
        lock (s_created)
        {
            s_created.Add(new WeakReference<TrackedWindow>(this));
        }
    }

    /// <summary>Gets every window of this kind that still has a platform window.</summary>
    /// <returns>The open windows, in the order they were created.</returns>
    public static IReadOnlyList<TrackedWindow> Open()
    {
        var open = new List<TrackedWindow>();

        lock (s_created)
        {
            foreach (WeakReference<TrackedWindow> created in s_created)
            {
                if (created.TryGetTarget(out TrackedWindow? window) && window.PlatformImpl is not null)
                {
                    open.Add(window);
                }
            }
        }

        return open;
    }
}

/// <summary>
/// The class a window document names, which counts how often it is constructed.
/// </summary>
public class CountedWindow : TrackedWindow
{
    private static int s_constructed;

    /// <summary>Creates the window and counts it.</summary>
    public CountedWindow() => Interlocked.Increment(ref s_constructed);

    /// <summary>Gets how many windows of this class have been constructed in the process.</summary>
    public static int Constructed => Volatile.Read(ref s_constructed);

    /// <summary>Gets how many times <see cref="OkClicked"/> has run on this window.</summary>
    public int OkClickCount { get; private set; }

    /// <summary>Handles a click, named by a document's <c>Click</c> attribute.</summary>
    /// <param name="sender">The control that was clicked.</param>
    /// <param name="e">The event's arguments.</param>
    public void OkClicked(object? sender, RoutedEventArgs e) => OkClickCount++;
}

/// <summary>A window that refuses to close until it is told it may.</summary>
/// <remarks>
/// The element a root is written as when the copy a rebuild makes of it has to fail to close — a
/// window whose author cancels closing, or throws doing so, is a window the platform goes on holding.
/// </remarks>
public class StubbornWindow : TrackedWindow
{
    /// <summary>Gets or sets a value indicating whether this window lets itself be closed.</summary>
    public bool MayClose { get; set; }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!MayClose)
        {
            throw new InvalidOperationException("This window refuses to close, by design.");
        }

        base.OnClosing(e);
    }
}

/// <summary>The class a stubborn window's document names.</summary>
public class StubbornClassWindow : StubbornWindow
{
}

/// <summary>Values a document can read with <c>x:Static</c>.</summary>
public static class Greetings
{
    /// <summary>Gets the greeting a static property holds.</summary>
    public static string Morning { get; } = "Good morning";

    /// <summary>The greeting a static field holds.</summary>
    public static readonly string Evening = "Good evening";
}

/// <summary>A view model a document can name as its <c>x:DataType</c>.</summary>
public sealed class GreetingModel
{
    /// <summary>Gets or sets the name a binding reads.</summary>
    public string Name { get; set; } = "Ada";
}
