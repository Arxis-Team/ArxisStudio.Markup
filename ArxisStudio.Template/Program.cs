using Avalonia;
using System;

namespace ArxisStudio.Markup.Template;

internal static class Program
{
    /// <summary>
    /// Главная точка входа шаблонного приложения.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Создаёт и настраивает <see cref="AppBuilder"/> шаблонного приложения.
    /// </summary>
    /// <returns>Сконфигурированный экземпляр <see cref="AppBuilder"/>.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
