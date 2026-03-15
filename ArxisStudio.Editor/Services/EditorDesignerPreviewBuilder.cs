using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Designer.Abstractions;
using ArxisStudio.Designer.Models;
using ArxisStudio.Markup.Json.Loader.Models;
using ArxisStudio.Markup.Json.Loader;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Json;

namespace ArxisStudio.Editor.Services;

/// <summary>
/// Адаптирует текущий preview editor к контракту <see cref="IDesignerPreviewBuilder"/>.
/// </summary>
public sealed class EditorDesignerPreviewBuilder : IDesignerPreviewBuilder
{
    /// <summary>
    /// Возвращает или задаёт проектный контекст, используемый при построении preview.
    /// </summary>
    public ProjectContext? ProjectContext { get; set; }

    /// <inheritdoc />
    public DesignerPreviewScene Build(UiDocument document, DesignerSurfaceContext context)
    {
        var nodeMap = new Dictionary<UiNode, Control>();
        var rootControl = UiBuilder.Build(document.Root, ProjectContext, nodeMap);
        var surfaceSize = new Size(
            document.Design?.SurfaceWidth ?? 1280,
            document.Design?.SurfaceHeight ?? 800);

        return new DesignerPreviewScene(
            rootControl,
            surfaceSize,
            hitTest: point => HitTestNode(rootControl, nodeMap, point),
            boundsLookup: node => GetBounds(rootControl, nodeMap, node),
            bringIntoView: node => BringNodeIntoView(nodeMap, node));
    }

    private static UiNode? HitTestNode(Control rootControl, IDictionary<UiNode, Control> nodeMap, Point point)
    {
        foreach (var pair in nodeMap.Reverse())
        {
            var bounds = GetBounds(rootControl, nodeMap, pair.Key);
            if (bounds.HasValue && pair.Value.IsVisible && bounds.Value.Contains(point))
            {
                return pair.Key;
            }
        }

        return null;
    }

    private static Rect? GetBounds(Control rootControl, IDictionary<UiNode, Control> nodeMap, UiNode node)
    {
        if (!nodeMap.TryGetValue(node, out var control))
        {
            return null;
        }

        var topLeft = control.TranslatePoint(default, rootControl);
        if (topLeft == null)
        {
            return null;
        }

        var size = control.Bounds.Size;
        return new Rect(topLeft.Value, size);
    }

    private static bool BringNodeIntoView(IDictionary<UiNode, Control> nodeMap, UiNode node)
    {
        if (!nodeMap.TryGetValue(node, out var control))
        {
            return false;
        }

        control.BringIntoView();
        return true;
    }
}
