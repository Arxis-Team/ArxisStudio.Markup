using ArxisStudio.Markup;
using ArxisStudio.Markup.Json;
using ArxisStudio.Markup.Json.Loader.Models;
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
            BuildTreeRecursive(propertiesObject, rootNode.Children, "");
            return rootNode;
        }

        private static void BuildTreeRecursive(JObject parentJsonProperties, ObservableCollection<ControlNode> children, string parentPath)
        {
            foreach (var property in parentJsonProperties.Properties())
            {
                string propName = property.Name;
                JToken propValue = property.Value!;

                // 1. Обработка коллекций (Children, Items)
                if (CollectionProperties.Contains(propName) && propValue is JArray jArray)
                {
                    var collectionPath = AppendPath(parentPath, propName);

                    // Обрабатываем элементы внутри коллекции
                    var index = 0;
                    foreach (var token in jArray.OfType<JObject>())
                    {
                        var childNode = new ControlNode(token, jArray, propName, $"{collectionPath}[{index}]");
                        children.Add(childNode);
                        
                        // Рекурсия для вложенного контрола
                        if (token.TryGetValue("Properties", out JToken? nestedProperties) && nestedProperties is JObject nestedPropertiesObject)
                        {
                            BuildTreeRecursive(nestedPropertiesObject, childNode.Children, childNode.NodePath);
                        }

                        index++;
                    }
                }
                // 2. Обработка одиночных дочерних контролов (Content, Child)
                else if (ContentProperties.Contains(propName) && propValue is JObject jObject)
                {
                    var childNode = new ControlNode(jObject, parentJsonProperties, propName, AppendPath(parentPath, propName));
                    children.Add(childNode);

                    // Рекурсия для вложенного контрола
                    if (jObject.TryGetValue("Properties", out JToken? nestedProperties) && nestedProperties is JObject nestedPropertiesObject)
                    {
                        BuildTreeRecursive(nestedPropertiesObject, childNode.Children, childNode.NodePath);
                    }
                }
            }
        }

        private static string AppendPath(string parentPath, string segment)
        {
            return string.IsNullOrWhiteSpace(parentPath) ? segment : $"{parentPath}/{segment}";
        }
    }
}
