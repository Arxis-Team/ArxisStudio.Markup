using System.Collections.Generic;

namespace JsonUiEditor.Models
{
    /// <summary>
    /// Представляет любой контрол или сложный объект (Brush, Thickness),
    /// который задан через Type.
    /// </summary>
    public class ControlModel
    {
        public string Type { get; set; } = "";
        
        // Свойства этого контрола или сложного объекта. 
        // Может содержать примитивы (string, int), JObject (для Content/Child/Background) 
        // ИЛИ JArray (для коллекций Children/Items/GradientStops).
        public Dictionary<string, object>? Properties { get; set; }
        
        // Поле Children оставлено пустым, т.к. оно теперь обрабатывается внутри Properties как JArray.
    }
    
    /// <summary>
    /// Представляет корневую структуру JSON-файла с метаданными.
    /// </summary>
    public class RootModel
    {
        public string? FormName { get; set; }
        public string? NamespaceSuffix { get; set; }
        public string? ParentClassType { get; set; }
        public Dictionary<string, object>? Properties { get; set; } 
    }
}