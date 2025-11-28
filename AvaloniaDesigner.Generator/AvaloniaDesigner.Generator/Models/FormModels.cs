using System.Collections.Generic;
using System.Text.Json;


namespace AvaloniaDesigner.Generator.Models
{
    public class FormModel
    {
        public string FormName { get; set; } = "GeneratedForm";
        public string NamespaceSuffix { get; set; } = "Forms";
        
        public string ParentClassType { get; set; } = ""; 
        
        public RootContainerModel RootContainer { get; set; } = new();
        public List<ControlModel> Controls { get; set; } = new();
    }

    public class RootContainerModel
    {
        public string Type { get; set; } = ""; // Полное имя типа (e.g., Avalonia.Controls.Grid)
        public Dictionary<string, JsonElement> Properties { get; set; } = new();
    }

    public class ControlModel
    {
        public string Type { get; set; } = ""; // Полное имя типа (e.g., Avalonia.Controls.Button)
        public string Name { get; set; } = ""; // Имя контрола
        public Dictionary<string, JsonElement> Properties { get; set; } = new();
    }
}