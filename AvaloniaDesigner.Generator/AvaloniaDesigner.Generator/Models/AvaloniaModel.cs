using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    [JsonConverter(typeof(PropertyModelConverter))]
    public class PropertyModel
    {
        /// <summary>
        /// Полное имя типа, если это вложенный контрол (например, "Avalonia.Controls.Button").
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Значение свойства (примитив, enum, строка).
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// Словарь, содержащий вложенные свойства (Width, Height, Content, Child, Name, и т.п.).
        /// В старом формате сюда также попадали элементы коллекций с ключами "0","1",...
        /// </summary>
        public Dictionary<string, PropertyModel> Properties { get; set; } = new();

        /// <summary>
        /// Элементы коллекции (новый формат: Children: [ {...}, {...} ]).
        /// </summary>
        public List<PropertyModel>? Items { get; set; }
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