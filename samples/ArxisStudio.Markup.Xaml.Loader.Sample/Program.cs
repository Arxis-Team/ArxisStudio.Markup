using System;
using Avalonia;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// A windowed showcase of what the three packages do, driven entirely through their public API.
/// </summary>
/// <remarks>
/// <para>
/// A showcase, not a designer. The contract's out-of-scope list rules out selection adorners, a
/// property-inspector UI, drag and drop, pointer or keyboard interception and Play/Stop UI, and
/// none of them is here: nothing is edited by pointing at the preview, nothing is drawn over it,
/// and no input destined for it is intercepted. What the window does is show a document, show
/// the objects the library builds from it, and show what it says about both.
/// </para>
/// <para>
/// The window's own interface is written in C# rather than XAML on purpose. Every piece of
/// markup on screen is then the library's subject rather than the application's own chrome,
/// which removes any question about which XAML is being demonstrated and which is scaffolding.
/// </para>
/// </remarks>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"The showcase could not start: {error}");

            return 1;
        }
    }

    /// <summary>Builds the application, as an Avalonia entry point is expected to.</summary>
    /// <returns>The configured builder.</returns>
    internal static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ShowcaseApplication>()
            .UsePlatformDetect()
            .LogToTrace();
}
