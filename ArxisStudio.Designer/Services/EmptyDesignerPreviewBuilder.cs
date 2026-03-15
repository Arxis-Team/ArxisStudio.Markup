using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ArxisStudio.Designer.Abstractions;
using ArxisStudio.Designer.Models;
using ArxisStudio.Markup;

namespace ArxisStudio.Designer.Services;

/// <summary>
/// Базовый preview builder-заполнитель, используемый до подключения реального preview engine.
/// </summary>
public sealed class EmptyDesignerPreviewBuilder : IDesignerPreviewBuilder
{
    /// <summary>
    /// Экземпляр builder по умолчанию.
    /// </summary>
    public static EmptyDesignerPreviewBuilder Instance { get; } = new();

    private EmptyDesignerPreviewBuilder()
    {
    }

    /// <inheritdoc />
    public DesignerPreviewScene Build(UiDocument document, DesignerSurfaceContext context)
    {
        var size = new Size(
            document.Design?.SurfaceWidth ?? 1280,
            document.Design?.SurfaceHeight ?? 800);

        var placeholder = new Border
        {
            Background = SolidColorBrush.Parse("#14181E"),
            BorderBrush = SolidColorBrush.Parse("#2F3945"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24),
            Child = new TextBlock
            {
                Text = "DesignerSurfaceControl подключён, но реальный preview builder ещё не настроен.",
                Foreground = Brushes.Gainsboro,
                TextWrapping = TextWrapping.Wrap
            }
        };

        return new DesignerPreviewScene(placeholder, size);
    }
}
