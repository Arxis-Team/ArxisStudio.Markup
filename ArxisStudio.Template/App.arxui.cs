using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ArxisStudio.Markup.Template.Views;

namespace ArxisStudio.Markup.Template;

/// <summary>
/// Точка входа шаблонного приложения.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Инициализирует ресурсы приложения.
    /// </summary>
    public override void Initialize()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Завершает инициализацию фреймворка и создаёт главное окно.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
