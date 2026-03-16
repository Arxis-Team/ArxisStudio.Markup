using System;
using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Markup;

namespace ArxisStudio.Designer.Models;

/// <summary>
/// Представляет построенную design-time сцену preview.
/// </summary>
public sealed class DesignerPreviewScene
{
    private readonly Func<Point, UiNode?>? _hitTest;
    private readonly Func<UiNode, Rect?>? _boundsLookup;
    private readonly Func<UiNode, bool>? _bringIntoView;

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="DesignerPreviewScene"/>.
    /// </summary>
    /// <param name="rootControl">Корневой visual preview.</param>
    /// <param name="surfaceSize">Желаемый размер design surface.</param>
    /// <param name="hitTest">Функция hit testing для выбора узлов.</param>
    /// <param name="boundsLookup">Функция поиска границ узла.</param>
    /// <param name="bringIntoView">Функция прокрутки preview к выбранному узлу.</param>
    public DesignerPreviewScene(
        Control? rootControl,
        Size? surfaceSize = null,
        Func<Point, UiNode?>? hitTest = null,
        Func<UiNode, Rect?>? boundsLookup = null,
        Func<UiNode, bool>? bringIntoView = null)
    {
        RootControl = rootControl;
        SurfaceSize = surfaceSize;
        _hitTest = hitTest;
        _boundsLookup = boundsLookup;
        _bringIntoView = bringIntoView;
    }

    /// <summary>
    /// Возвращает корневой visual preview.
    /// </summary>
    public Control? RootControl { get; }

    /// <summary>
    /// Возвращает желаемый размер design surface.
    /// </summary>
    public Size? SurfaceSize { get; }

    /// <summary>
    /// Выполняет hit testing и возвращает узел под указанной точкой.
    /// </summary>
    public UiNode? HitTest(Point point) => _hitTest?.Invoke(point);

    /// <summary>
    /// Пытается определить границы указанного узла в координатах surface.
    /// </summary>
    public bool TryGetBounds(UiNode node, out Rect bounds)
    {
        var result = _boundsLookup?.Invoke(node);
        if (result.HasValue)
        {
            bounds = result.Value;
            return true;
        }

        bounds = default;
        return false;
    }

    /// <summary>
    /// Пытается прокрутить preview до указанного узла.
    /// </summary>
    public bool BringIntoView(UiNode node) => _bringIntoView?.Invoke(node) == true;
}
