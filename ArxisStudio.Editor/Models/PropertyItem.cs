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

        private readonly JTokenType? _existingValueType;
        private readonly string? _typeName;

        // Значение, которое редактируется в панели
        [ObservableProperty]
        private string _value;

        public PropertyItem(string name, JToken? initialValue, JObject parent, string? typeName = null)
        {
            Name = name;
            ParentPropertiesObject = parent;
            _existingValueType = initialValue is JValue jValue ? jValue.Type : null;
            _typeName = typeName;
            _value = initialValue is JValue value
                ? value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
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
            if (!string.IsNullOrWhiteSpace(_typeName))
            {
                switch (_typeName)
                {
                    case "System.Int32":
                    case "int":
                        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                        {
                            return new JValue(intValue);
                        }
                        break;
                    case "System.Int64":
                    case "long":
                        if (long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
                        {
                            return new JValue(longValue);
                        }
                        break;
                    case "System.Double":
                    case "double":
                    case "System.Single":
                    case "float":
                        if (double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue))
                        {
                            return new JValue(doubleValue);
                        }
                        break;
                    case "System.Boolean":
                    case "bool":
                        if (bool.TryParse(rawValue, out var boolValue))
                        {
                            return new JValue(boolValue);
                        }
                        break;
                }
            }

            return _existingValueType switch
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
