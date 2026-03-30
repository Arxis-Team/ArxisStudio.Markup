﻿using Avalonia;
using System;

namespace ArxisStudio.Markup.Sample;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    /// <summary>
    /// Главная точка входа демонстрационного приложения.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    /// <summary>
    /// Создаёт и настраивает <see cref="AppBuilder"/> приложения.
    /// </summary>
    /// <returns>Сконфигурированный экземпляр <see cref="AppBuilder"/>.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
