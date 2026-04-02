using System.Collections.Generic;

using ArxisStudio.Markup;
using ArxisStudio.Markup.Metadata;

namespace ArxisStudio.Markup.Generator.Tests;

/// <summary>
/// Тесты конвертера между inline <c>$design</c> и <see cref="DesignMetadata"/>.
/// </summary>
public class InlineDesignMetadataConverterTests
{
    /// <summary>
    /// Проверяет построение overlay из inline design-данных документа.
    /// </summary>
    [Fact]
    public void FromInlineDesign_should_collect_document_and_node_design()
    {
        var document = new UiDocument(
            1,
            UiDocumentKind.Control,
            null,
            new UiNode(
                "Avalonia.Controls.Canvas",
                new Dictionary<string, UiValue>
                {
                    ["Children"] = new CollectionValue(new UiValue[]
                    {
                        new NodeValue(new UiNode(
                            "Avalonia.Controls.Border",
                            new Dictionary<string, UiValue>(),
                            Design: new UiDesignData(new Dictionary<string, UiDesignValue>
                            {
                                ["Layout.X"] = new UiDesignScalarValue(100)
                            })))
                    })
                }),
            new UiDesignData(new Dictionary<string, UiDesignValue>
            {
                ["SurfaceWidth"] = new UiDesignScalarValue(1920)
            }));

        var overlay = InlineDesignMetadataConverter.FromInlineDesign(document);

        Assert.NotNull(overlay.Document);
        Assert.True(overlay.Document!.Properties.ContainsKey("SurfaceWidth"));
        Assert.True(overlay.Nodes.ContainsKey(new NodeRef("/Root/Children/0")));
    }

    /// <summary>
    /// Проверяет применение overlay к документу и запись результата в inline <c>$design</c>.
    /// </summary>
    [Fact]
    public void ApplyOverlay_should_write_inline_design_to_document_and_nodes()
    {
        var document = new UiDocument(
            1,
            UiDocumentKind.Control,
            null,
            new UiNode(
                "Avalonia.Controls.Canvas",
                new Dictionary<string, UiValue>
                {
                    ["Children"] = new CollectionValue(new UiValue[]
                    {
                        new NodeValue(new UiNode("Avalonia.Controls.Border", new Dictionary<string, UiValue>()))
                    })
                }));

        var overlay = new DesignMetadata(
            new DocumentDesignMetadata(new Dictionary<string, DesignValue>
            {
                ["SurfaceHeight"] = new DesignScalarValue(1080)
            }),
            new Dictionary<NodeRef, NodeDesignMetadata>
            {
                [new NodeRef("/Root/Children/0")] = new NodeDesignMetadata(new Dictionary<string, DesignValue>
                {
                    ["Layout.Y"] = new DesignScalarValue(250)
                })
            });

        var updated = InlineDesignMetadataConverter.ApplyOverlay(document, overlay);

        Assert.NotNull(updated.Design);
        Assert.True(updated.Design!.Properties.ContainsKey("SurfaceHeight"));

        var children = Assert.IsType<CollectionValue>(updated.Root.Properties["Children"]);
        var childNode = Assert.IsType<NodeValue>(children.Items[0]).Node;
        Assert.NotNull(childNode.Design);
        Assert.True(childNode.Design!.Properties.ContainsKey("Layout.Y"));
    }
}


