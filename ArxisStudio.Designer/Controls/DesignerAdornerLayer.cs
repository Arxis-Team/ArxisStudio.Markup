using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ArxisStudio.Designer.Models;
using ArxisStudio.Markup;

namespace ArxisStudio.Designer.Controls;

/// <summary>
/// Рисует design-time adorners поверх preview-сцены.
/// </summary>
public sealed class DesignerAdornerLayer : Control
{
    private static readonly Pen SelectionPen = new(SolidColorBrush.Parse("#4EA1F3"), 1);
    private static readonly IBrush HandleBrush = Brushes.White;
    private static readonly Size HandleSize = new(8, 8);

    /// <summary>
    /// Возвращает или задаёт preview-сцену, для которой рисуются adorners.
    /// </summary>
    public DesignerPreviewScene? Scene
    {
        get => _scene;
        set
        {
            _scene = value;
            InvalidateVisual();
        }
    }
    private DesignerPreviewScene? _scene;

    /// <summary>
    /// Возвращает или задаёт выбранный узел.
    /// </summary>
    public UiNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            _selectedNode = value;
            InvalidateVisual();
        }
    }
    private UiNode? _selectedNode;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Scene == null || SelectedNode == null || !Scene.TryGetBounds(SelectedNode, out var bounds))
        {
            return;
        }

        context.DrawRectangle(null, SelectionPen, bounds);

        foreach (var handle in GetHandleRects(bounds))
        {
            context.DrawRectangle(HandleBrush, SelectionPen, handle);
        }
    }

    private static Rect[] GetHandleRects(Rect bounds)
    {
        var halfWidth = HandleSize.Width / 2;
        var halfHeight = HandleSize.Height / 2;

        Rect Create(double x, double y) => new(x - halfWidth, y - halfHeight, HandleSize.Width, HandleSize.Height);

        return
        [
            Create(bounds.Left, bounds.Top),
            Create(bounds.Right, bounds.Top),
            Create(bounds.Left, bounds.Bottom),
            Create(bounds.Right, bounds.Bottom)
        ];
    }
}
