using System.Collections.Generic;
using ArxisStudio.Markup.Metadata;
using ArxisStudio.Markup.Metadata.Json;
using Xunit;

namespace ArxisStudio.Markup.Generator.Tests;

/// <summary>
/// Тесты сериализатора оверлея метаданных.
/// </summary>
public class DesignMetadataSerializerTests
{
    /// <summary>
    /// Проверяет round-trip сериализацию и десериализацию оверлея.
    /// </summary>
    [Fact]
    public void Serialize_and_deserialize_should_round_trip_overlay()
    {
        var overlay = new DesignMetadata(
            new DocumentDesignMetadata(new Dictionary<string, DesignValue>
            {
                ["SurfaceWidth"] = new DesignScalarValue(1280),
                ["SurfaceHeight"] = new DesignScalarValue(720)
            }),
            new Dictionary<NodeRef, NodeDesignMetadata>
            {
                [new NodeRef("/Root/Children/0")] = new(new Dictionary<string, DesignValue>
                {
                    ["IsHitTestVisible"] = new DesignScalarValue(false),
                    ["Layout.X"] = new DesignScalarValue(100),
                    ["Layout.Y"] = new DesignScalarValue(200)
                })
            });

        var json = DesignMetadataSerializer.Serialize(overlay);
        var roundTripped = DesignMetadataSerializer.Deserialize(json);

        Assert.NotNull(roundTripped.Document);
        Assert.Equal(2, roundTripped.Document!.Properties.Count);
        Assert.Single(roundTripped.Nodes);

        var node = Assert.Single(roundTripped.Nodes);
        Assert.Equal("/Root/Children/0", node.Key.Value);
        Assert.Equal(3, node.Value.Properties.Count);
    }
}


