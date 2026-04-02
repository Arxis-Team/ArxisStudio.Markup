using System.Collections.Generic;
using System.Linq;
using ArxisStudio.Markup.Metadata;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArxisStudio.Markup.Metadata.Json;

/// <summary>
/// JSON-сериализатор оверлея метаданных дизайнера.
/// </summary>
public static class DesignMetadataSerializer
{
    /// <summary>
    /// Десериализует JSON-представление оверлея метаданных.
    /// </summary>
    /// <param name="json">JSON-строка с оверлеем метаданных.</param>
    /// <returns>Десериализованный оверлей.</returns>
    public static DesignMetadata Deserialize(string json)
    {
        var root = JObject.Parse(json);
        return Deserialize(root);
    }

    internal static DesignMetadata Deserialize(JObject root)
    {
        var document = root["Document"] is JObject documentObject
            ? new DocumentDesignMetadata(ReadValueObject(documentObject))
            : null;

        var nodes = new Dictionary<NodeRef, NodeDesignMetadata>();
        if (root["Nodes"] is JObject nodesObject)
        {
            foreach (var property in nodesObject.Properties())
            {
                if (property.Value is not JObject nodeObject)
                {
                    continue;
                }

                nodes[new NodeRef(property.Name)] = new NodeDesignMetadata(ReadValueObject(nodeObject));
            }
        }

        return new DesignMetadata(document, nodes);
    }

    /// <summary>
    /// Сериализует оверлей метаданных в JSON.
    /// </summary>
    /// <param name="overlay">Оверлей метаданных.</param>
    /// <returns>JSON-строка.</returns>
    public static string Serialize(DesignMetadata overlay)
    {
        var root = new JObject();

        if (overlay.Document != null)
        {
            root["Document"] = WriteValueObject(overlay.Document.Properties);
        }

        var nodes = new JObject();
        foreach (var node in overlay.Nodes)
        {
            nodes[node.Key.Value] = WriteValueObject(node.Value.Properties);
        }

        root["Nodes"] = nodes;
        return root.ToString(Formatting.Indented);
    }

    private static IReadOnlyDictionary<string, DesignValue> ReadValueObject(JObject obj)
    {
        var result = new Dictionary<string, DesignValue>();
        foreach (var property in obj.Properties())
        {
            result[property.Name] = ReadValue(property.Value);
        }

        return result;
    }

    private static DesignValue ReadValue(JToken token)
    {
        return token.Type switch
        {
            JTokenType.Object => new DesignObjectValue(ReadValueObject((JObject)token)),
            JTokenType.Array => new DesignCollectionValue(((JArray)token).Select(ReadValue).ToList()),
            JTokenType.Null => new DesignScalarValue(null),
            _ => new DesignScalarValue(((JValue)token).Value)
        };
    }

    private static JObject WriteValueObject(IReadOnlyDictionary<string, DesignValue> values)
    {
        var obj = new JObject();
        foreach (var value in values)
        {
            obj[value.Key] = WriteValue(value.Value);
        }

        return obj;
    }

    private static JToken WriteValue(DesignValue value)
    {
        return value switch
        {
            DesignScalarValue scalar => scalar.Value == null ? JValue.CreateNull() : JToken.FromObject(scalar.Value),
            DesignObjectValue obj => WriteValueObject(obj.Properties),
            DesignCollectionValue collection => new JArray(collection.Items.Select(WriteValue)),
            _ => throw new JsonSerializationException($"Unsupported design value type '{value.GetType().Name}'.")
        };
    }
}


