using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>The showcase application.</summary>
internal sealed class ShowcaseApplication : Application
{
    /// <inheritdoc />
    public override void Initialize() => Styles.Add(new FluentTheme());

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new ShowcaseWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
