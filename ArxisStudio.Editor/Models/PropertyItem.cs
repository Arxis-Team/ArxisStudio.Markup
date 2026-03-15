using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using Newtonsoft.Json.Linq;
using ArxisStudio.Editor.Services; // Для доступа к DesignerService

namespace ArxisStudio.Editor.Models
{
    public partial class PropertyItem : ObservableObject
    {
        public string Name { get; } 

        // Ссылка на JSON-объект, содержащий словарь "Properties"
        public JObject ParentPropertiesObject { get; }

        private readonly JTokenType _valueType;

        // Значение, которое редактируется в панели
        [ObservableProperty]
        private string _value;

        public PropertyItem(string name, JValue initialValue, JObject parent)
        {
            Name = name;
            ParentPropertiesObject = parent;
            _valueType = initialValue.Type;
            _value = initialValue.ToString(CultureInfo.InvariantCulture);
        }

        // Вызывается при изменении значения в UI-панели свойств
        partial void OnValueChanged(string? oldValue, string newValue)
        {
            if (ParentPropertiesObject.ContainsKey(Name))
            {
                // 1. Обновление JSON
                ParentPropertiesObject[Name] = CreateTypedValue(newValue);
                
                // 2. Оповещение сервиса о том, что JSON изменился, для перерисовки UI.
                DesignerService.Instance.NotifyJsonChanged(DesignerChangeKind.PropertyValue); 
            }
        }

        private JValue CreateTypedValue(string rawValue)
        {
            return _valueType switch
            {
                JTokenType.Integer when long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue)
                    => new JValue(longValue),
                JTokenType.Float when double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue)
                    => new JValue(doubleValue),
                JTokenType.Boolean when bool.TryParse(rawValue, out var boolValue)
                    => new JValue(boolValue),
                _ => new JValue(rawValue)
            };
        }
    }
}
