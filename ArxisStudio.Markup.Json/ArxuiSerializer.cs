using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using ArxisStudio.Markup;

namespace ArxisStudio.Markup.Json;

/// <summary>
/// Выполняет сериализацию и десериализацию документов <c>.arxui</c>.
/// </summary>
public static class ArxuiSerializer
{
    private const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Десериализует JSON-строку в модель <see cref="UiDocument"/>.
    /// </summary>
    /// <param name="json">Исходный JSON-документ.</param>
    /// <returns>Десериализованный документ или <see langword="null"/>, если документ не содержит корневого узла.</returns>
    public static UiDocument? Deserialize(string json)
    {
        var root = JObject.Parse(json);
        return DeserializeCore(root);
    }

    /// <summary>
    /// Десериализует <see cref="JObject"/> в модель <see cref="UiDocument"/>.
    /// </summary>
    /// <param name="root">Корневой JSON-объект документа.</param>
    /// <returns>Десериализованный документ или <see langword="null"/>, если документ не содержит корневого узла.</returns>
    internal static UiDocument? Deserialize(JObject root)
    {
        return DeserializeCore(root);
    }

    private static UiDocument? DeserializeCore(JObject root)
    {
        var kindToken = root["Kind"] ?? root["AssetType"];
        UiDocumentKind kind;
        if (kindToken == null)
        {
            kind = UiDocumentKind.Control;
        }
        else if (!Enum.TryParse(kindToken.ToString(), ignoreCase: true, out kind))
        {
            throw new JsonSerializationException($"Unsupported document kind '{kindToken}'.");
        }

        var schemaVersion = root["SchemaVersion"]?.Value<int>() ?? CurrentSchemaVersion;
        var className = root["Class"]?.ToString();
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

        return new UiDocument(schemaVersion, kind, className, rootNode, ReadDocumentDesign(root["$design"] as JObject));
    }

    /// <summary>
    /// Сериализует документ в JSON-строку.
    /// </summary>
    /// <param name="document">Документ для сериализации.</param>
    /// <returns>JSON-представление документа в формате <c>.arxui</c>.</returns>
    public static string Serialize(UiDocument document)
    {
        var root = new JObject
        {
            ["SchemaVersion"] = document.SchemaVersion,
            ["Kind"] = document.Kind.ToString(),
            ["Root"] = WriteNode(document.Root)
        };

        if (!string.IsNullOrWhiteSpace(document.Class))
        {
            root["Class"] = document.Class;
        }

        var design = WriteDocumentDesign(document.Design);
        if (design != null)
        {
            root["$design"] = design;
        }

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

        return new UiNode(
            typeName,
            properties,
            ReadStyles(nodeObject["Styles"] as JArray),
            ReadResources(nodeObject["Resources"] as JObject),
            ReadNodeDesign(nodeObject["$design"] as JObject));
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

        return new NodeValue(new UiNode(
            "System.Object",
            nestedProperties,
            ReadStyles(obj["Styles"] as JArray),
            ReadResources(obj["Resources"] as JObject),
            ReadNodeDesign(obj["$design"] as JObject)));
    }

    private static UiStyles? ReadStyles(JArray? stylesArray)
    {
        if (stylesArray == null)
        {
            return null;
        }

        var items = new List<UiStyleValue>(stylesArray.Count);
        foreach (var token in stylesArray)
        {
            switch (token)
            {
                case JObject obj when obj.TryGetValue("$styleInclude", out var includeToken):
                {
                    var source = includeToken.Type == JTokenType.Object
                        ? includeToken["Source"]?.ToString()
                        : includeToken.ToString();

                    if (!string.IsNullOrWhiteSpace(source))
                    {
                        items.Add(new StyleIncludeValue(source!));
                    }

                    break;
                }
                case JObject obj:
                    items.Add(new StyleNodeValue(ReadNode(obj)));
                    break;
                default:
                    throw new JsonSerializationException("Styles entries must be objects.");
            }
        }

        return new UiStyles(items);
    }

    private static UiResources? ReadResources(JObject? resourcesObject)
    {
        if (resourcesObject == null)
        {
            return null;
        }

        var mergedDictionaries = new List<UiResourceDictionaryInclude>();
        var values = new Dictionary<string, UiValue>(StringComparer.Ordinal);

        foreach (var property in resourcesObject.Properties())
        {
            if (string.Equals(property.Name, "$mergedDictionaries", StringComparison.Ordinal))
            {
                if (property.Value is not JArray dictionariesArray)
                {
                    throw new JsonSerializationException("'$mergedDictionaries' must be an array.");
                }

                foreach (var dictionaryToken in dictionariesArray)
                {
                    string? source = dictionaryToken switch
                    {
                        JValue value when value.Type == JTokenType.String => value.ToString(),
                        JObject obj => obj["Source"]?.ToString(),
                        _ => null
                    };

                    if (string.IsNullOrWhiteSpace(source))
                    {
                        throw new JsonSerializationException("Merged dictionary entry must declare Source.");
                    }

                    mergedDictionaries.Add(new UiResourceDictionaryInclude(source!));
                }

                continue;
            }

            values[property.Name] = ReadValue(property.Value);
        }

        return new UiResources(mergedDictionaries, values);
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

        var obj = new JObject
        {
            ["TypeName"] = node.TypeName,
            ["Properties"] = properties
        };

        if (node.Styles != null)
        {
            obj["Styles"] = WriteStyles(node.Styles);
        }

        if (node.Resources != null)
        {
            obj["Resources"] = WriteResources(node.Resources);
        }

        var design = WriteNodeDesign(node.Design);
        if (design != null)
        {
            obj["$design"] = design;
        }

        return obj;
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

    private static JArray WriteStyles(UiStyles styles)
    {
        var array = new JArray();
        foreach (var item in styles.Items)
        {
            switch (item)
            {
                case StyleIncludeValue include:
                    array.Add(new JObject
                    {
                        ["$styleInclude"] = include.Source
                    });
                    break;
                case StyleNodeValue node:
                    array.Add(WriteNode(node.Node));
                    break;
                default:
                    throw new JsonSerializationException($"Unsupported style entry type '{item.GetType().Name}'.");
            }
        }

        return array;
    }

    private static JObject WriteResources(UiResources resources)
    {
        var obj = new JObject();
        if (resources.MergedDictionaries.Count > 0)
        {
            var mergedDictionaries = new JArray();
            foreach (var include in resources.MergedDictionaries)
            {
                mergedDictionaries.Add(new JObject
                {
                    ["Source"] = include.Source
                });
            }

            obj["$mergedDictionaries"] = mergedDictionaries;
        }

        foreach (var value in resources.Values)
        {
            obj[value.Key] = WriteValue(value.Value);
        }

        return obj;
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
            UiDocumentKind.Application => "Avalonia.Application",
            UiDocumentKind.Window => "Avalonia.Controls.Window",
            UiDocumentKind.Styles => "Avalonia.Styling.Styles",
            UiDocumentKind.ResourceDictionary => "Avalonia.Controls.ResourceDictionary",
            _ => "Avalonia.Controls.UserControl"
        };
    }

    private static UiDocumentDesign? ReadDocumentDesign(JObject? obj)
    {
        if (obj == null)
        {
            return null;
        }

        return new UiDocumentDesign(
            obj["SurfaceWidth"]?.Value<double?>(),
            obj["SurfaceHeight"]?.Value<double?>());
    }

    private static UiNodeDesign? ReadNodeDesign(JObject? obj)
    {
        if (obj == null)
        {
            return null;
        }

        return new UiNodeDesign(
            obj["Locked"]?.Value<bool?>(),
            obj["Hidden"]?.Value<bool?>(),
            obj["IgnorePreviewInput"]?.Value<bool?>(),
            obj["AllowMove"]?.Value<bool?>(),
            obj["AllowResize"]?.Value<bool?>());
    }

    private static JObject? WriteDocumentDesign(UiDocumentDesign? design)
    {
        if (design == null)
        {
            return null;
        }

        var obj = new JObject();
        WriteOptional(obj, "SurfaceWidth", design.SurfaceWidth);
        WriteOptional(obj, "SurfaceHeight", design.SurfaceHeight);
        return obj.HasValues ? obj : null;
    }

    private static JObject? WriteNodeDesign(UiNodeDesign? design)
    {
        if (design == null)
        {
            return null;
        }

        var obj = new JObject();
        WriteOptional(obj, "Locked", design.Locked);
        WriteOptional(obj, "Hidden", design.Hidden);
        WriteOptional(obj, "IgnorePreviewInput", design.IgnorePreviewInput);
        WriteOptional(obj, "AllowMove", design.AllowMove);
        WriteOptional(obj, "AllowResize", design.AllowResize);
        return obj.HasValues ? obj : null;
    }
}
