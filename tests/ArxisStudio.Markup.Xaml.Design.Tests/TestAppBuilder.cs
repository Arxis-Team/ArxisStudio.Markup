using Avalonia;
using Avalonia.Headless;
using ArxisStudio.Markup.Xaml.Design.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace ArxisStudio.Markup.Xaml.Design.Tests;

/// <summary>
/// Headless Avalonia application used by every <c>[AvaloniaFact]</c> in this project.
/// </summary>
/// <remarks>
/// These tests create windows, take their content apart and put it back, so they need a properly
/// initialised Avalonia thread. The headless platform provides one without a display.
/// </remarks>
public static class TestAppBuilder
{
    /// <summary>Builds the headless application used to host these tests.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
