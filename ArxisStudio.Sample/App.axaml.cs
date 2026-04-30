using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ArxisStudio.Markup.Sample.ViewModels;
using ArxisStudio.Markup.Sample.Views;

namespace ArxisStudio.Markup.Sample;

/// <summary>
/// Точка входа Avalonia-приложения примера.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Инициализирует ресурсы приложения.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Завершает инициализацию фреймворка и создаёт главное окно.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
