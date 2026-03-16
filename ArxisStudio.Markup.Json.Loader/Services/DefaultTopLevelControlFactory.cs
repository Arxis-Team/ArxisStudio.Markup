using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using ArxisStudio.Markup.Json.Loader.Abstractions;

namespace ArxisStudio.Markup.Json.Loader.Services;

/// <summary>
/// Создаёт preview-представление окна внутри обычного визуального дерева.
/// </summary>
public sealed class DefaultTopLevelControlFactory : ITopLevelControlFactory
{
    /// <inheritdoc />
    public TopLevelControlBuildResult? Create(UiNode node, System.Type resolvedType, ArxuiLoadContext context)
    {
        if (!typeof(Window).IsAssignableFrom(resolvedType))
        {
            return null;
        }

        var chromeBrush = SolidColorBrush.Parse("#20242A");
        var bodyBackground = SolidColorBrush.Parse("#11161D");
        var borderBrush = SolidColorBrush.Parse("#495668");
        var titleBrush = Brushes.White;

        var titleBar = new Border
        {
            Background = chromeBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 10),
            Child = new TextBlock
            {
                Text = GetScalarString(node, "Title") ?? "Window",
                Foreground = titleBrush,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var contentHost = new ContentControl
        {
            Background = bodyBackground
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(contentHost, 1);
        layout.Children.Add(titleBar);
        layout.Children.Add(contentHost);

        var shell = new Border
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Background = bodyBackground,
            Child = layout
        };

        return new TopLevelControlBuildResult(shell, contentHost);
    }

    private static string? GetScalarString(UiNode node, string propertyName)
    {
        if (!node.Properties.TryGetValue(propertyName, out var value))
        {
            return null;
        }

        if (value is ScalarValue scalar && scalar.Value != null)
        {
            return System.Convert.ToString(scalar.Value, CultureInfo.InvariantCulture);
        }

        return null;
    }
}
