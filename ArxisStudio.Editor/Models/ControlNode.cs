using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArxisStudio.Editor.Models
{
    public partial class ControlNode : ObservableObject
    {
        // Имя, отображаемое в TreeView (например, "StackPanel" или "Button")
        [ObservableProperty]
        private string _displayName;

        // Ссылка на JSON-объект, который описывает этот контрол (JObject)
        public JObject JsonNode { get; }

        // Ссылка на родительский JSON-контейнер (JArray или JObject Properties), 
        // чтобы знать, куда добавить/откуда удалить этот узел.
        public JContainer? ParentJsonContainer { get; }

        // Имя свойства, через которое этот узел присоединен к родителю (e.g., "Content", "Children")
        public string PropertyName { get; } 

        // Стабильный путь узла внутри документа для синхронизации с preview model.
        public string NodePath { get; }
        
        // Дочерние узлы для рекурсивного отображения в TreeView
        public ObservableCollection<ControlNode> Children { get; } = new();

        public ControlNode(JObject jsonNode, JContainer? parentContainer, string propertyName, string nodePath = "")
        {
            JsonNode = jsonNode;
            ParentJsonContainer = parentContainer;
            PropertyName = propertyName;
            NodePath = nodePath;
            
            // Получаем короткое имя класса (e.g., "Canvas" из "Avalonia.Controls.Canvas")
            DisplayName = (jsonNode["TypeName"] ?? jsonNode["Type"])?.ToString()?.Split('.').Last() ?? "Unknown";
        }
    }
}
