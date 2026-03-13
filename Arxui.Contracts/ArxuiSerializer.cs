using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArxisStudio.Markup.Json;

public static class ArxuiSerializer
{
    private const int CurrentSchemaVersion = 1;

    public static UiDocument? Deserialize(string json)
    {
        var root = JObject.Parse(json);
        return Deserialize(root);
    }

    public static UiDocument? Deserialize(JObject root)
    {
        var kind = Enum.TryParse<UiDocumentKind>(root["Kind"]?.ToString(), out var parsedKind)
            ? parsedKind
            : Enum.TryParse<UiDocumentKind>(root["AssetType"]?.ToString(), out parsedKind)
                ? parsedKind
                : UiDocumentKind.UserControl;

        var schemaVersion = root["SchemaVersion"]?.Value<int>() ?? CurrentSchemaVersion;
        var rootToken = root["Root"];

        UiNode rootNode;
        if (rootToken is JObject rootObject)
        {
            rootNode = ReadNode(rootObject);
        }
        else if (root["Properties"] is JObject propertiesObject)
        {
            rootNode = new UiNode(
                InferRootTypeName(kind),
                ReadProperties(propertiesObject));
        }
        else
        {
            return null;
        }

        return new UiDocument(schemaVersion, kind, rootNode);
    }

    public static string Serialize(UiDocument document)
    {
        var root = new JObject
        {
            ["SchemaVersion"] = document.SchemaVersion,
            ["Kind"] = document.Kind.ToString(),
            ["Root"] = WriteNode(document.Root)
        };

        return root.ToString(Formatting.Indented);
    }

    private static UiNode ReadNode(JObject nodeObject)
    {
        var typeName = nodeObject["TypeName"]?.ToString() ??
                       nodeObject["Type"]?.ToString() ??
                       throw new JsonSerializationException("Node is missing TypeName.");

        var propertiesToken = nodeObject["Properties"] as JObject;
        var properties = propertiesToken != null
            ? ReadProperties(propertiesToken)
            : new Dictionary<string, UiValue>();

        return new UiNode(typeName, properties);
    }

    private static IReadOnlyDictionary<string, UiValue> ReadProperties(JObject propertiesObject)
    {
        var properties = new Dictionary<string, UiValue>(StringComparer.Ordinal);
        foreach (var property in propertiesObject.Properties())
        {
            properties[property.Name] = ReadValue(property.Value);
        }

        return properties;
    }

    private static UiValue ReadValue(JToken token)
    {
        return token.Type switch
        {
            JTokenType.Array => new CollectionValue(ReadCollection((JArray)token)),
            JTokenType.Object => ReadObjectValue((JObject)token),
            JTokenType.Null => new ScalarValue(null),
            _ => new ScalarValue(((JValue)token).Value)
        };
    }

    private static IReadOnlyList<UiValue> ReadCollection(JArray array)
    {
        var items = new List<UiValue>(array.Count);
        foreach (var item in array)
        {
            items.Add(ReadValue(item));
        }

        return items;
    }

    private static UiValue ReadObjectValue(JObject obj)
    {
        if (obj.TryGetValue("$binding", out var bindingToken))
        {
            var path = bindingToken.Type == JTokenType.Object
                ? bindingToken["Path"]?.ToString() ?? string.Empty
                : bindingToken.ToString();

            var mode = TryParseEnum<BindingMode>(obj["Mode"]?.ToString());
            var relativeSource = ReadRelativeSource(obj["RelativeSource"] as JObject);

            return new BindingValue(new BindingSpec(
                path,
                mode,
                obj["Converter"]?.ToString(),
                obj["StringFormat"]?.ToString(),
                obj["ElementName"]?.ToString(),
                ReadScalarToken(obj["FallbackValue"]),
                ReadScalarToken(obj["TargetNullValue"]),
                ReadScalarToken(obj["ConverterParameter"]),
                relativeSource));
        }

        if (obj.TryGetValue("$resource", out var resourceToken))
        {
            return new ResourceValue(resourceToken.ToString());
        }

        if (obj.TryGetValue("$asset", out var assetToken))
        {
            if (assetToken.Type == JTokenType.Object)
            {
                return new UriReferenceValue(
                    assetToken["Path"]?.ToString() ?? string.Empty,
                    assetToken["Assembly"]?.ToString());
            }

            return new UriReferenceValue(assetToken.ToString());
        }

        if (obj["TypeName"] != null || obj["Type"] != null)
        {
            return new NodeValue(ReadNode(obj));
        }

        var nestedProperties = new Dictionary<string, UiValue>(StringComparer.Ordinal);
        foreach (var property in obj.Properties())
        {
            nestedProperties[property.Name] = ReadValue(property.Value);
        }

        return new NodeValue(new UiNode("System.Object", nestedProperties));
    }

    private static RelativeSourceSpec? ReadRelativeSource(JObject? obj)
    {
        if (obj == null)
        {
            return null;
        }

        var mode = TryParseEnum<RelativeSourceMode>(obj["Mode"]?.ToString()) ?? RelativeSourceMode.FindAncestor;
        return new RelativeSourceSpec(
            mode,
            obj["AncestorType"]?.ToString(),
            obj["AncestorLevel"]?.Value<int?>(),
            obj["Tree"]?.ToString());
    }

    private static object? ReadScalarToken(JToken? token)
    {
        return token is JValue value ? value.Value : token?.ToString();
    }

    private static JObject WriteNode(UiNode node)
    {
        var properties = new JObject();
        foreach (var property in node.Properties)
        {
            properties[property.Key] = WriteValue(property.Value);
        }

        return new JObject
        {
            ["TypeName"] = node.TypeName,
            ["Properties"] = properties
        };
    }

    private static JToken WriteValue(UiValue value)
    {
        return value switch
        {
            ScalarValue scalar => scalar.Value == null ? JValue.CreateNull() : JToken.FromObject(scalar.Value),
            NodeValue node => WriteNode(node.Node),
            CollectionValue collection => WriteCollection(collection),
            BindingValue binding => WriteBinding(binding.Binding),
            ResourceValue resource => new JObject { ["$resource"] = resource.Key },
            UriReferenceValue reference => WriteUriReference(reference),
            _ => throw new JsonSerializationException($"Unsupported asset value type '{value.GetType().Name}'.")
        };
    }

    private static JArray WriteCollection(CollectionValue collection)
    {
        var array = new JArray();
        foreach (var item in collection.Items)
        {
            array.Add(WriteValue(item));
        }

        return array;
    }

    private static JObject WriteBinding(BindingSpec binding)
    {
        var obj = new JObject
        {
            ["$binding"] = binding.Path
        };

        WriteOptional(obj, "Mode", binding.Mode?.ToString());
        WriteOptional(obj, "Converter", binding.ConverterKey);
        WriteOptional(obj, "StringFormat", binding.StringFormat);
        WriteOptional(obj, "ElementName", binding.ElementName);
        WriteOptional(obj, "FallbackValue", binding.FallbackValue);
        WriteOptional(obj, "TargetNullValue", binding.TargetNullValue);
        WriteOptional(obj, "ConverterParameter", binding.ConverterParameter);

        if (binding.RelativeSource != null)
        {
            var relativeSource = new JObject
            {
                ["Mode"] = binding.RelativeSource.Mode.ToString()
            };

            WriteOptional(relativeSource, "AncestorType", binding.RelativeSource.AncestorType);
            WriteOptional(relativeSource, "AncestorLevel", binding.RelativeSource.AncestorLevel);
            WriteOptional(relativeSource, "Tree", binding.RelativeSource.Tree);
            obj["RelativeSource"] = relativeSource;
        }

        return obj;
    }

    private static JObject WriteUriReference(UriReferenceValue reference)
    {
        if (string.IsNullOrWhiteSpace(reference.Assembly))
        {
            return new JObject { ["$asset"] = reference.Path };
        }

        return new JObject
        {
            ["$asset"] = new JObject
            {
                ["Path"] = reference.Path,
                ["Assembly"] = reference.Assembly
            }
        };
    }

    private static void WriteOptional(JObject obj, string propertyName, object? value)
    {
        if (value == null)
        {
            return;
        }

        obj[propertyName] = JToken.FromObject(value);
    }

    private static TEnum? TryParseEnum<TEnum>(string? value)
        where TEnum : struct
    {
        return Enum.TryParse<TEnum>(value, out var parsed) ? parsed : null;
    }

    private static string InferRootTypeName(UiDocumentKind kind)
    {
        return kind switch
        {
            UiDocumentKind.Window => "Avalonia.Controls.Window",
            UiDocumentKind.Styles => "Avalonia.Styling.Styles",
            UiDocumentKind.ResourceDictionary => "Avalonia.Controls.ResourceDictionary",
            UiDocumentKind.CustomControl => "Avalonia.Controls.Control",
            _ => "Avalonia.Controls.UserControl"
        };
    }
}
