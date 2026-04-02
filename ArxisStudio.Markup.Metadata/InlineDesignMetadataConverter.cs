using System;
using System.Collections.Generic;

namespace ArxisStudio.Markup.Metadata;

/// <summary>
/// Конвертирует inline-представление <c>$design</c> в <see cref="DesignMetadata"/> и обратно.
/// </summary>
public static class InlineDesignMetadataConverter
{
    /// <summary>
    /// Строит <see cref="DesignMetadata"/> из inline design-данных документа <c>.arxui</c>.
    /// </summary>
    /// <param name="document">Исходный runtime-документ с inline <c>$design</c>.</param>
    /// <returns>Сформированный overlay для bridge-слоя.</returns>
    public static DesignMetadata FromInlineDesign(ArxisStudio.Markup.UiDocument document)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var nodes = new Dictionary<NodeRef, NodeDesignMetadata>();
        VisitNodeForOverlay(document.Root, "/Root", nodes);

        var documentData = document.Design == null
            ? null
            : new DocumentDesignMetadata(ConvertFromUiDesignDictionary(document.Design.Properties));

        return new DesignMetadata(documentData, nodes);
    }

    /// <summary>
    /// Применяет overlay к runtime-документу и возвращает его копию с обновленным inline <c>$design</c>.
    /// </summary>
    /// <param name="document">Исходный runtime-документ.</param>
    /// <param name="overlay">Overlay с design-time значениями.</param>
    /// <returns>Новый экземпляр документа с inline design-данными.</returns>
    public static ArxisStudio.Markup.UiDocument ApplyOverlay(
        ArxisStudio.Markup.UiDocument document,
        DesignMetadata overlay)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (overlay == null)
        {
            throw new ArgumentNullException(nameof(overlay));
        }

        var nodesByPath = new Dictionary<string, NodeDesignMetadata>(StringComparer.Ordinal);
        foreach (var pair in overlay.Nodes)
        {
            nodesByPath[pair.Key.Value] = pair.Value;
        }

        var root = ApplyOverlayToNode(document.Root, "/Root", nodesByPath);
        var documentDesign = overlay.Document == null
            ? null
            : new ArxisStudio.Markup.UiDesignData(ConvertToUiDesignDictionary(overlay.Document.Properties));

        return document with
        {
            Root = root,
            Design = documentDesign
        };
    }

    private static void VisitNodeForOverlay(
        ArxisStudio.Markup.UiNode node,
        string path,
        IDictionary<NodeRef, NodeDesignMetadata> collector)
    {
        if (node.Design != null && node.Design.Properties.Count > 0)
        {
            collector[new NodeRef(path)] = new NodeDesignMetadata(ConvertFromUiDesignDictionary(node.Design.Properties));
        }

        foreach (var property in node.Properties)
        {
            switch (property.Value)
            {
                case ArxisStudio.Markup.NodeValue nodeValue:
                    VisitNodeForOverlay(nodeValue.Node, $"{path}/{property.Key}", collector);
                    break;
                case ArxisStudio.Markup.CollectionValue collectionValue:
                    for (var index = 0; index < collectionValue.Items.Count; index++)
                    {
                        if (collectionValue.Items[index] is ArxisStudio.Markup.NodeValue collectionNode)
                        {
                            VisitNodeForOverlay(collectionNode.Node, $"{path}/{property.Key}/{index}", collector);
                        }
                    }
                    break;
            }
        }
    }

    private static ArxisStudio.Markup.UiNode ApplyOverlayToNode(
        ArxisStudio.Markup.UiNode node,
        string path,
        IReadOnlyDictionary<string, NodeDesignMetadata> nodesByPath)
    {
        var properties = new Dictionary<string, ArxisStudio.Markup.UiValue>(StringComparer.Ordinal);

        foreach (var property in node.Properties)
        {
            switch (property.Value)
            {
                case ArxisStudio.Markup.NodeValue nodeValue:
                    properties[property.Key] = new ArxisStudio.Markup.NodeValue(
                        ApplyOverlayToNode(nodeValue.Node, $"{path}/{property.Key}", nodesByPath));
                    break;
                case ArxisStudio.Markup.CollectionValue collectionValue:
                    properties[property.Key] = new ArxisStudio.Markup.CollectionValue(
                        ApplyOverlayToCollection(collectionValue.Items, $"{path}/{property.Key}", nodesByPath));
                    break;
                default:
                    properties[property.Key] = property.Value;
                    break;
            }
        }

        ArxisStudio.Markup.UiDesignData? design = null;
        if (nodesByPath.TryGetValue(path, out var nodeDesign))
        {
            design = new ArxisStudio.Markup.UiDesignData(ConvertToUiDesignDictionary(nodeDesign.Properties));
        }

        return node with
        {
            Properties = properties,
            Design = design
        };
    }

    private static IReadOnlyList<ArxisStudio.Markup.UiValue> ApplyOverlayToCollection(
        IReadOnlyList<ArxisStudio.Markup.UiValue> items,
        string path,
        IReadOnlyDictionary<string, NodeDesignMetadata> nodesByPath)
    {
        var result = new List<ArxisStudio.Markup.UiValue>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is ArxisStudio.Markup.NodeValue nodeValue)
            {
                result.Add(new ArxisStudio.Markup.NodeValue(
                    ApplyOverlayToNode(nodeValue.Node, $"{path}/{index}", nodesByPath)));
                continue;
            }

            result.Add(items[index]);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, DesignValue> ConvertFromUiDesignDictionary(
        IReadOnlyDictionary<string, ArxisStudio.Markup.UiDesignValue> source)
    {
        var result = new Dictionary<string, DesignValue>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            result[pair.Key] = ConvertFromUiDesignValue(pair.Value);
        }

        return result;
    }

    private static DesignValue ConvertFromUiDesignValue(ArxisStudio.Markup.UiDesignValue value)
    {
        return value switch
        {
            ArxisStudio.Markup.UiDesignScalarValue scalar => new DesignScalarValue(scalar.Value),
            ArxisStudio.Markup.UiDesignObjectValue obj => new DesignObjectValue(ConvertFromUiDesignDictionary(obj.Properties)),
            ArxisStudio.Markup.UiDesignCollectionValue collection => new DesignCollectionValue(
                ConvertFromUiDesignCollection(collection.Items)),
            _ => throw new InvalidOperationException($"Unsupported UiDesignValue type '{value.GetType().FullName}'.")
        };
    }

    private static IReadOnlyList<DesignValue> ConvertFromUiDesignCollection(
        IReadOnlyList<ArxisStudio.Markup.UiDesignValue> source)
    {
        var result = new List<DesignValue>(source.Count);
        foreach (var item in source)
        {
            result.Add(ConvertFromUiDesignValue(item));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, ArxisStudio.Markup.UiDesignValue> ConvertToUiDesignDictionary(
        IReadOnlyDictionary<string, DesignValue> source)
    {
        var result = new Dictionary<string, ArxisStudio.Markup.UiDesignValue>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            result[pair.Key] = ConvertToUiDesignValue(pair.Value);
        }

        return result;
    }

    private static ArxisStudio.Markup.UiDesignValue ConvertToUiDesignValue(DesignValue value)
    {
        return value switch
        {
            DesignScalarValue scalar => new ArxisStudio.Markup.UiDesignScalarValue(scalar.Value),
            DesignObjectValue obj => new ArxisStudio.Markup.UiDesignObjectValue(ConvertToUiDesignDictionary(obj.Properties)),
            DesignCollectionValue collection => new ArxisStudio.Markup.UiDesignCollectionValue(
                ConvertToUiDesignCollection(collection.Items)),
            _ => throw new InvalidOperationException($"Unsupported DesignValue type '{value.GetType().FullName}'.")
        };
    }

    private static IReadOnlyList<ArxisStudio.Markup.UiDesignValue> ConvertToUiDesignCollection(
        IReadOnlyList<DesignValue> source)
    {
        var result = new List<ArxisStudio.Markup.UiDesignValue>(source.Count);
        foreach (var item in source)
        {
            result.Add(ConvertToUiDesignValue(item));
        }

        return result;
    }
}


