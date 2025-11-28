using System.Collections.Generic;
using System.Text.Json;

namespace AvaloniaDesigner.Generator.Models
{
    /// <summary>
    /// Вспомогательный класс для сбора информации о полях, 
    /// которые будут объявлены в генерируемом классе.
    /// </summary>
    public class ControlInfo
    {
        public string Type { get; set; } = "";
        public string? Name { get; set; } = "";
    }

    /// <summary>
    /// Класс для представления свойства (примитив, вложенный объект или элемент коллекции).
    /// </summary>
    public class PropertyModel
    {
        /// <summary>
        /// Полное имя типа, если это вложенный контрол (например, "Avalonia.Controls.Button").
        /// </summary>
        public string Type { get; set; } = ""; 
        
        // 🛑 УДАЛЕНО: public string Name { get; set; } = "";
        // Это поле было избыточным, так как имя контрола должно находиться 
        // в словаре Properties как ключ "Name". Его присутствие могло сбить с толку десериализатор.
        
        /// <summary>
        /// Значение свойства, если это примитивный тип или enum.
        /// </summary>
        public JsonElement? Value { get; set; } 
        
        /// <summary>
        /// Словарь, содержащий вложенные свойства или элементы коллекции (с ключами "0", "1", ...).
        /// </summary>
        public Dictionary<string, PropertyModel> Properties { get; set; } = new();
    }
    
    /// <summary>
    /// Основная модель, представляющая UserControl/Window.
    /// </summary>
    public class AvaloniaModel
    {
        public string FormName { get; set; } = "GeneratedView";
        public string NamespaceSuffix { get; set; } = "Views";
        public string ParentClassType { get; set; } = ""; 
        
        /// <summary>
        /// Словарь свойств корневого элемента (Width, Height, Content).
        /// </summary>
        public Dictionary<string, PropertyModel> Properties { get; set; } = new(); 
    }
}