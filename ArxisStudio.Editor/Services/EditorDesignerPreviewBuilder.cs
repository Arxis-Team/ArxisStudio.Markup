using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Designer.Abstractions;
using ArxisStudio.Designer.Models;
using ArxisStudio.Editor.Models;
using ArxisStudio.Markup.Json.Loader;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Json;
using ArxisStudio.Markup.Json.Loader.Services;

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
        var loader = new ArxuiLoader();
        var loadContext = new ArxuiLoadContext
        {
            TypeResolver = new ReflectionTypeResolver(),
            AssetResolver = new DefaultAssetResolver(),
            DocumentResolver = ProjectContext != null ? new ProjectMarkupDocumentResolver(ProjectContext.ArxuiFiles.Select(file => file.FullPath).ToList()) : null,
            PathResolver = ProjectContext != null ? new ProjectContextPathResolver(ProjectContext.ProjectDirectory, ProjectContext.AssemblyName) : null,
            TopLevelControlFactory = new DefaultTopLevelControlFactory(),
            NodeMap = nodeMap,
            Options = new ArxuiLoadOptions()
        };
        var rootControl = loader.Load(document.Root, loadContext) ?? new TextBlock();
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
