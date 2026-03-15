using ArxisStudio.Markup.Json;
using ArxisStudio.Editor.Models;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArxisStudio.Editor.Services
{
    public static class TreeBuilder
    {
        private static readonly string[] ContentProperties = { "Content", "Child" };
        private static readonly string[] CollectionProperties = { "Children", "Items", "RowDefinitions", "ColumnDefinitions" };

        public static ControlNode? BuildTree(JObject? rootJson)
        {
            if (rootJson == null) return null;

            // Находим корневой объект Properties
            if (!rootJson.TryGetValue("Root", out JToken? rootToken) || rootToken is not JObject rootObject)
            {
                return null;
            }

            if (!rootObject.TryGetValue("Properties", out JToken? propertiesToken) || propertiesToken is not JObject propertiesObject)
            {
                return null;
            }

            // Создаем фиктивный корневой узел, который представляет весь asset.
            var rootNode = new ControlNode(rootObject, rootJson, "Root");
            rootNode.DisplayName = rootJson["Kind"]?.ToObject<UiDocumentKind?>()?.ToString()
                                   ?? rootJson["AssetType"]?.ToObject<UiDocumentKind?>()?.ToString()
                                   ?? "Root Document";
            
            // Рекурсивно строим дерево, начиная со свойств окна
            BuildTreeRecursive(propertiesObject, rootNode.Children);
            return rootNode;
        }

        private static void BuildTreeRecursive(JObject parentJsonProperties, ObservableCollection<ControlNode> children)
        {
            foreach (var property in parentJsonProperties.Properties())
            {
                string propName = property.Name;
                JToken propValue = property.Value!;

                // 1. Обработка коллекций (Children, Items)
                if (CollectionProperties.Contains(propName) && propValue is JArray jArray)
                {
                    // Создаем узел для самой коллекции (e.g., "Children [3]")
                    var collectionNode = new ControlNode(parentJsonProperties, parentJsonProperties, propName);
                    collectionNode.DisplayName = $"{propName} [{jArray.Count}]";
                    children.Add(collectionNode);
                    
                    // Обрабатываем элементы внутри коллекции
                    foreach (var token in jArray.OfType<JObject>())
                    {
                        var childNode = new ControlNode(token, jArray, propName);
                        collectionNode.Children.Add(childNode);
                        
                        // Рекурсия для вложенного контрола
                        if (token.TryGetValue("Properties", out JToken? nestedProperties) && nestedProperties is JObject nestedPropertiesObject)
                        {
                            BuildTreeRecursive(nestedPropertiesObject, childNode.Children);
                        }
                    }
                }
                // 2. Обработка одиночных дочерних контролов (Content, Child)
                else if (ContentProperties.Contains(propName) && propValue is JObject jObject)
                {
                    var childNode = new ControlNode(jObject, parentJsonProperties, propName);
                    children.Add(childNode);

                    // Рекурсия для вложенного контрола
                    if (jObject.TryGetValue("Properties", out JToken? nestedProperties) && nestedProperties is JObject nestedPropertiesObject)
                    {
                        BuildTreeRecursive(nestedPropertiesObject, childNode.Children);
                    }
                }
            }
        }
    }
}
