using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ArxisStudio.Markup.Json;

namespace ArxisStudio.Markup.Json.Loader.Services
{
    public static class UiBuilder
    {
        private static readonly Dictionary<string, Type> TypeCache = new();

        public static Control Build(UiNode node)
        {
            if (string.IsNullOrWhiteSpace(node.TypeName))
            {
                return new TextBlock { Text = "Error: Root control type is missing", Foreground = Brushes.Red };
            }

            var controlType = FindType(node.TypeName);
            if (controlType == null)
            {
                return new TextBlock { Text = $"Error: Type '{node.TypeName}' not found", Foreground = Brushes.Red };
            }

            var control = (Control)Activator.CreateInstance(controlType)!;

            foreach (var property in node.Properties)
            {
                ApplyProperty(control, property.Key, property.Value);
            }

            return control;
        }

        private static void ApplyProperty(Control control, string propertyName, UiValue value)
        {
            try
            {
                if (propertyName.Contains("."))
                {
                    ApplyAttachedProperty(control, propertyName, value);
                    return;
                }

                if (value is CollectionValue collectionValue)
                {
                    ApplyCollectionProperty(control, propertyName, collectionValue.Items);
                    return;
                }

                if (value is NodeValue nodeValue)
                {
                    var complexObject = CreateComplexObject(nodeValue.Node);
                    if (complexObject != null)
                    {
                        SetComplexProperty(control, propertyName, complexObject);
                    }

                    return;
                }

                // Designer preview currently ignores runtime-only concepts that need Avalonia binding/resource context.
                if (value is BindingValue or ResourceValue or UriReferenceValue)
                {
                    return;
                }

                var avaloniaProperty = AvaloniaPropertyRegistry.Instance.FindRegistered(control, propertyName);
                var targetType = avaloniaProperty?.PropertyType;

                if (targetType == null)
                {
                    var propertyInfo = control.GetType().GetProperty(propertyName);
                    if (propertyInfo == null || !propertyInfo.CanWrite)
                    {
                        return;
                    }

                    targetType = propertyInfo.PropertyType;
                }

                var convertedValue = ConvertPropertyValue(value, targetType);
                if (convertedValue == null)
                {
                    return;
                }

                if (avaloniaProperty != null)
                {
                    control.SetValue(avaloniaProperty, convertedValue);
                }
                else
                {
                    control.GetType().GetProperty(propertyName)?.SetValue(control, convertedValue);
                }
            }
            catch
            {
                // Preview should stay resilient even when a single property cannot be rendered.
            }
        }

        private static void ApplyAttachedProperty(Control control, string propertyName, UiValue value)
        {
            var setter = FindAttachedPropertySetMethod(propertyName);
            if (setter == null)
            {
                return;
            }

            var targetType = setter.GetParameters()[1].ParameterType;
            var convertedValue = ConvertPropertyValue(value, targetType);
            if (convertedValue == null)
            {
                return;
            }

            setter.Invoke(null, new[] { control, convertedValue });
        }

        private static MethodInfo? FindAttachedPropertySetMethod(string propertyName)
        {
            var lastDotIndex = propertyName.LastIndexOf('.');
            if (lastDotIndex == -1)
            {
                return null;
            }

            var ownerTypeName = propertyName.Substring(0, lastDotIndex);
            var attachedPropertyName = propertyName.Substring(lastDotIndex + 1);
            var setterName = "Set" + attachedPropertyName;

            var ownerType = FindType(ownerTypeName);
            if (ownerType == null)
            {
                return null;
            }

            return ownerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == setterName)
                .Where(method => method.GetParameters().Length == 2)
                .FirstOrDefault(method => typeof(AvaloniaObject).IsAssignableFrom(method.GetParameters()[0].ParameterType));
        }

        private static void ApplyCollectionProperty(object parentObject, string propertyName, IReadOnlyCollection<UiValue> items)
        {
            var collectionProperty = parentObject.GetType().GetProperty(propertyName);
            if (collectionProperty == null)
            {
                return;
            }

            var collection = collectionProperty.GetValue(parentObject);
            if (collection == null)
            {
                return;
            }

            var addMethod = collection.GetType().GetMethod("Add");
            if (addMethod == null)
            {
                return;
            }

            var collectionItemType = addMethod.GetParameters().FirstOrDefault()?.ParameterType ?? typeof(object);
            foreach (var item in items)
            {
                object? builtItem = null;

                if (item is NodeValue nodeItem)
                {
                    builtItem = CreateComplexObject(nodeItem.Node);
                }
                else if (item is ScalarValue scalar && scalar.Value != null)
                {
                    builtItem = ConvertPrimitive(scalar.Value, collectionItemType);
                }

                if (builtItem != null)
                {
                    addMethod.Invoke(collection, new[] { builtItem });
                }
            }
        }

        private static object? CreateComplexObject(UiNode node)
        {
            var resolvedType = FindType(node.TypeName);
            if (resolvedType == null)
            {
                return null;
            }

            if (typeof(Control).IsAssignableFrom(resolvedType))
            {
                return Build(node);
            }

            var complexObject = Activator.CreateInstance(resolvedType);
            if (complexObject == null)
            {
                return null;
            }

            foreach (var nestedProperty in node.Properties)
            {
                if (nestedProperty.Value is CollectionValue collectionValue)
                {
                    ApplyCollectionProperty(complexObject, nestedProperty.Key, collectionValue.Items);
                    continue;
                }

                var propertyInfo = complexObject.GetType().GetProperty(nestedProperty.Key);
                if (propertyInfo == null || !propertyInfo.CanWrite)
                {
                    continue;
                }

                var convertedValue = ConvertPropertyValue(nestedProperty.Value, propertyInfo.PropertyType);
                propertyInfo.SetValue(complexObject, convertedValue);
            }

            return complexObject;
        }

        private static void SetComplexProperty(Control control, string propertyName, object complexObject)
        {
            var propertyInfo = control.GetType().GetProperty(propertyName);
            if (propertyInfo != null && propertyInfo.CanWrite)
            {
                propertyInfo.SetValue(control, complexObject);
            }
        }

        private static object? ConvertPropertyValue(UiValue value, Type targetType)
        {
            if (value is not ScalarValue scalar || scalar.Value == null)
            {
                return null;
            }

            return ConvertPrimitive(scalar.Value, targetType);
        }

        private static object? ConvertPrimitive(object value, Type targetType)
        {
            var stringValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, stringValue);
            }

            var parser = targetType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (parser != null)
            {
                return parser.Invoke(null, new object[] { stringValue });
            }

            var converter = TypeDescriptor.GetConverter(targetType);
            if (converter.CanConvertFrom(typeof(string)))
            {
                return converter.ConvertFromInvariantString(stringValue);
            }

            if (targetType == typeof(string))
            {
                return stringValue;
            }

            if (targetType == typeof(double))
            {
                return double.Parse(stringValue, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(int))
            {
                return int.Parse(stringValue, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(bool))
            {
                return bool.Parse(stringValue);
            }

            return value;
        }

        private static Type? FindType(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            if (TypeCache.TryGetValue(typeName, out var cachedType))
            {
                return cachedType;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var exactType = assembly.GetType(typeName);
                if (exactType != null)
                {
                    TypeCache[typeName] = exactType;
                    return exactType;
                }
            }

            var commonPrefixes = new[]
            {
                "Avalonia.Controls.",
                "Avalonia.Controls.Shapes.",
                "Avalonia.Media."
            };

            foreach (var prefix in commonPrefixes)
            {
                var prefixedName = prefix + typeName;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var prefixedType = assembly.GetType(prefixedName);
                    if (prefixedType != null)
                    {
                        TypeCache[typeName] = prefixedType;
                        return prefixedType;
                    }
                }
            }

            return null;
        }
    }
}
