using Avalonia;
using Avalonia.Headless;
using ArxisStudio.Markup.Xaml.Loader.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// Headless Avalonia application used by every <c>[AvaloniaTest]</c> in this project.
/// Loader tests create real Avalonia objects, so they must run on a properly
/// initialised Avalonia thread; the headless platform provides one without a display.
/// </summary>
public static class TestAppBuilder
{
    /// <summary>Builds the headless application used to host loader tests.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
