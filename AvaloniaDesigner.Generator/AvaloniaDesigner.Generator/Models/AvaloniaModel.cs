using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AvaloniaDesigner.Generator.Models
{
    public enum AssetType
    {
        UserControl,
        Window,
        Styles,
        ResourceDictionary,
        CustomControl,
        Unknown
    }

    public class RelativeSourceModel
    {
        public string Mode { get; set; } = "FindAncestor"; 
        public string? AncestorType { get; set; } 
        public int? AncestorLevel { get; set; }
        public string? Tree { get; set; } 
    }

    public record ControlInfo(string Type, string Name);

    [JsonConverter(typeof(PropertyModelConverter))]
    public class PropertyModel : IEquatable<PropertyModel>
    {
        public string Type { get; set; } = ""; 
        public object? Value { get; set; }
        
        // --- Привязки ---
        public string? BindingPath { get; set; }
        public string? BindingMode { get; set; }      
        public string? BindingConverter { get; set; } 
        public string? BindingStringFormat { get; set; }
        public string? BindingElementName { get; set; }
        public object? BindingFallbackValue { get; set; }
        public object? BindingTargetNullValue { get; set; }
        public object? BindingConverterParameter { get; set; }
        public RelativeSourceModel? BindingRelativeSource { get; set; }
        
        // --- Ресурсы и Ассеты ---
        public string? ResourceKey { get; set; }
        public string? AssetPath { get; set; }
        public string? AssetAssembly { get; set; }

        public Dictionary<string, PropertyModel> Properties { get; set; } = new();
        public List<PropertyModel>? Items { get; set; }

        public bool Equals(PropertyModel? other)
        {
            if (other is null) return false;
            return Type == other.Type && 
                   BindingPath == other.BindingPath && 
                   BindingMode == other.BindingMode &&
                   ResourceKey == other.ResourceKey && 
                   AssetPath == other.AssetPath &&
                   AssetAssembly == other.AssetAssembly &&
                   Equals(Value, other.Value);
        }

        public override bool Equals(object? obj) => Equals(obj as PropertyModel);
        public override int GetHashCode() => (Type, Value, BindingPath, AssetPath).GetHashCode();
    }

    public class AvaloniaModel : IEquatable<AvaloniaModel>
    {
        public AssetType AssetType { get; set; } = AssetType.UserControl;
        public Dictionary<string, PropertyModel> Properties { get; set; } = new();

        public bool Equals(AvaloniaModel? other)
        {
            if (other is null) return false;
            return AssetType == other.AssetType; 
        }
    }

    public class PropertyModelConverter : JsonConverter<PropertyModel>
    {
        public override PropertyModel? ReadJson(JsonReader reader, Type objectType, PropertyModel? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            if (reader.TokenType == JsonToken.StartArray)
            {
                var array = JArray.Load(reader);
                var result = new PropertyModel { Items = new List<PropertyModel>() };
                foreach (var item in array)
                {
                    var child = item.ToObject<PropertyModel>(serializer);
                    if (child != null) result.Items.Add(child);
                }
                return result;
            }

            if (reader.TokenType == JsonToken.StartObject)
            {
                var obj = JObject.Load(reader);
                var result = new PropertyModel();

                // 1. Привязка
                if (obj.TryGetValue("$binding", out var bindingToken))
                {
                    if (bindingToken.Type == JTokenType.String)
                        result.BindingPath = bindingToken.ToString();
                    else if (bindingToken.Type == JTokenType.Object)
                        result.BindingPath = bindingToken["Path"]?.ToString();

                    if (obj.TryGetValue("Mode", out var mode)) result.BindingMode = mode.ToString();
                    if (obj.TryGetValue("Converter", out var conv)) result.BindingConverter = conv.ToString();
                    if (obj.TryGetValue("StringFormat", out var sf)) result.BindingStringFormat = sf.ToString();
                    if (obj.TryGetValue("ElementName", out var elName)) result.BindingElementName = elName.ToString();
                    
                    if (obj.TryGetValue("FallbackValue", out var fb)) result.BindingFallbackValue = fb.ToObject<object>();
                    if (obj.TryGetValue("TargetNullValue", out var tn)) result.BindingTargetNullValue = tn.ToObject<object>();
                    if (obj.TryGetValue("ConverterParameter", out var cp)) result.BindingConverterParameter = cp.ToObject<object>();

                    if (obj.TryGetValue("RelativeSource", out var rsToken) && rsToken.Type == JTokenType.Object)
                        result.BindingRelativeSource = rsToken.ToObject<RelativeSourceModel>(serializer);

                    return result;
                }

                // 2. Ресурс
                if (obj.TryGetValue("$resource", out var resourceToken))
                {
                    result.ResourceKey = resourceToken.ToString();
                    return result;
                }

                // 3. Ассет
                if (obj.TryGetValue("$asset", out var assetToken))
                {
                    if (assetToken.Type == JTokenType.String)
                    {
                        result.AssetPath = assetToken.ToString();
                    }
                    else if (assetToken.Type == JTokenType.Object)
                    {
                        result.AssetPath = assetToken["Path"]?.ToString();
                        result.AssetAssembly = assetToken["Assembly"]?.ToString();
                    }
                    return result;
                }

                // 4. Контрол
                if (obj.TryGetValue("Type", out var typeToken))
                {
                    result.Type = typeToken.ToString();
                    if (obj.TryGetValue("Properties", out var propsToken) && propsToken is JObject propsObj)
                    {
                        foreach (var p in propsObj.Properties())
                        {
                            var child = p.Value.ToObject<PropertyModel>(serializer);
                            if (child != null) result.Properties[p.Name] = child;
                        }
                    }
                    return result;
                }

                foreach (var p in obj.Properties())
                {
                    var child = p.Value.ToObject<PropertyModel>(serializer);
                    if (child != null) result.Properties[p.Name] = child;
                }
                return result;
            }

            var token = JToken.Load(reader);
            return new PropertyModel { Value = token.ToObject<object?>(serializer) };
        }

        public override void WriteJson(JsonWriter writer, PropertyModel? value, JsonSerializer serializer) => throw new NotImplementedException();
    }
}