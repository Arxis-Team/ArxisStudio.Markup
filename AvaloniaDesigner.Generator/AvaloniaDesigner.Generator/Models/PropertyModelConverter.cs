using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AvaloniaDesigner.Generator.Models;

/// <summary>
/// Кастомный конвертер, который позволяет PropertyModel
/// десериализоваться как:
/// - обычный объект { type, value, properties }
/// - ИЛИ как массив [{...}, {...}] для коллекций (Children и т.п.).
/// </summary>
public class PropertyModelConverter : JsonConverter<PropertyModel>
{
    public override PropertyModel? ReadJson(
        JsonReader reader,
        System.Type objectType,
        PropertyModel? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        // СЛУЧАЙ 1: массив => коллекция элементов (Children: [ {...}, {...} ])
        if (reader.TokenType == JsonToken.StartArray)
        {
            var array = JArray.Load(reader);
            var result = new PropertyModel
            {
                Items = new List<PropertyModel>()
            };

            foreach (var item in array)
            {
                if (item.Type == JTokenType.Object)
                {
                    var child = item.ToObject<PropertyModel>(serializer);
                    if (child != null)
                        result.Items.Add(child);
                }
            }

            return result;
        }

        // СЛУЧАЙ 2: обычный объект => стандартная модель
        if (reader.TokenType == JsonToken.StartObject)
        {
            var obj = JObject.Load(reader);
            var result = new PropertyModel();
            serializer.Populate(obj.CreateReader(), result);
            return result;
        }

        // СЛУЧАЙ 3: примитив (на всякий случай)
        var token = JToken.Load(reader);
        return new PropertyModel
        {
            Value = token.ToObject<object?>()
        };
    }

    public override void WriteJson(JsonWriter writer, PropertyModel? value, JsonSerializer serializer)
    {
        // Обратная сериализация не нужна генератору.
        throw new System.NotImplementedException();
    }
}
