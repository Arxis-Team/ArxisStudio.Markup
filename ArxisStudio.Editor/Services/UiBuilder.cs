using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using ArxisStudio.Markup.Json;
using ArxisStudio.Editor.Models;

namespace ArxisStudio.Editor.Services
{
    public static class UiBuilder
    {
        private static readonly Dictionary<string, Type> TypeCache = new();

        public static Control Build(UiNode node, ProjectContext? projectContext = null)
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
            var resourceScope = new Dictionary<string, object?>(StringComparer.Ordinal);

            ApplyResources(control, node.Resources, resourceScope, projectContext);
            ApplyStyles(control, node.Styles, projectContext);

            foreach (var property in node.Properties)
            {
                ApplyProperty(control, property.Key, property.Value, resourceScope, projectContext);
            }

            return control;
        }

        private static void ApplyProperty(
            Control control,
            string propertyName,
            UiValue value,
            IDictionary<string, object?> resourceScope,
            ProjectContext? projectContext)
        {
            try
            {
                if (string.Equals(propertyName, "Classes", StringComparison.Ordinal))
                {
                    ApplyClasses(control, value);
                    return;
                }

                if (propertyName.Contains("."))
                {
                    ApplyAttachedProperty(control, propertyName, value, resourceScope);
                    return;
                }

                if (value is CollectionValue collectionValue)
                {
                    ApplyCollectionProperty(control, propertyName, collectionValue.Items, resourceScope, projectContext);
                    return;
                }

                if (value is NodeValue nodeValue)
                {
                    var complexObject = CreateComplexObject(nodeValue.Node, resourceScope, projectContext);
                    if (complexObject != null)
                    {
                        SetComplexProperty(control, propertyName, complexObject);
                    }

                    return;
                }

                if (value is BindingValue)
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

                var convertedValue = ConvertPropertyValue(value, targetType, resourceScope, projectContext);
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

        private static void ApplyAttachedProperty(
            Control control,
            string propertyName,
            UiValue value,
            IDictionary<string, object?> resourceScope)
        {
            var setter = FindAttachedPropertySetMethod(propertyName);
            if (setter == null)
            {
                return;
            }

            var targetType = setter.GetParameters()[1].ParameterType;
            var convertedValue = ConvertPropertyValue(value, targetType, resourceScope, projectContext: null);
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

        private static void ApplyCollectionProperty(
            object parentObject,
            string propertyName,
            IReadOnlyCollection<UiValue> items,
            IDictionary<string, object?> resourceScope,
            ProjectContext? projectContext)
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
                    builtItem = CreateComplexObject(nodeItem.Node, resourceScope, projectContext);
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

        private static object? CreateComplexObject(
            UiNode node,
            IDictionary<string, object?> inheritedResources,
            ProjectContext? projectContext)
        {
            var resolvedType = FindType(node.TypeName);
            if (resolvedType == null)
            {
                return null;
            }

            if (typeof(Control).IsAssignableFrom(resolvedType))
            {
                return Build(node, projectContext);
            }

            var complexObject = Activator.CreateInstance(resolvedType);
            if (complexObject == null)
            {
                return null;
            }

            var localScope = new Dictionary<string, object?>(inheritedResources, StringComparer.Ordinal);
            ApplyResources(complexObject, node.Resources, localScope, projectContext);

            foreach (var nestedProperty in node.Properties)
            {
                if (nestedProperty.Value is CollectionValue collectionValue)
                {
                    ApplyCollectionProperty(complexObject, nestedProperty.Key, collectionValue.Items, localScope, projectContext);
                    continue;
                }

                var propertyInfo = complexObject.GetType().GetProperty(nestedProperty.Key);
                if (propertyInfo == null || !propertyInfo.CanWrite)
                {
                    continue;
                }

                var convertedValue = ConvertPropertyValue(nestedProperty.Value, propertyInfo.PropertyType, localScope, projectContext);
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

        private static object? ConvertPropertyValue(
            UiValue value,
            Type targetType,
            IDictionary<string, object?> resourceScope,
            ProjectContext? projectContext)
        {
            if (value is ResourceValue resourceValue)
            {
                if (resourceScope.TryGetValue(resourceValue.Key, out var scopedResource))
                {
                    return scopedResource;
                }

                return null;
            }

            if (value is UriReferenceValue assetReference)
            {
                return CreateAssetValue(assetReference, targetType, projectContext);
            }

            if (value is not ScalarValue scalar || scalar.Value == null)
            {
                return null;
            }

            return ConvertPrimitive(scalar.Value, targetType);
        }

        private static object? CreateAssetValue(UriReferenceValue assetReference, Type targetType, ProjectContext? projectContext)
        {
            var assetPath = ResolveProjectRelativePath(assetReference.Path, assetReference.Assembly, projectContext);
            if (assetPath == null || !File.Exists(assetPath))
            {
                return null;
            }

            if (typeof(IImage).IsAssignableFrom(targetType))
            {
                return new Avalonia.Media.Imaging.Bitmap(assetPath);
            }

            return null;
        }

        private static void ApplyClasses(Control control, UiValue value)
        {
            if (value is not ScalarValue { Value: not null } scalar)
            {
                return;
            }

            var classNames = Convert.ToString(scalar.Value, CultureInfo.InvariantCulture)?
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (classNames == null)
            {
                return;
            }

            foreach (var className in classNames)
            {
                if (!control.Classes.Contains(className))
                {
                    control.Classes.Add(className);
                }
            }
        }

        private static void ApplyResources(
            object host,
            UiResources? resources,
            IDictionary<string, object?> resourceScope,
            ProjectContext? projectContext)
        {
            if (resources == null)
            {
                return;
            }

            if (host is StyledElement styledElement)
            {
                foreach (var include in resources.MergedDictionaries)
                {
                    var dictionary = LoadResourceDictionary(include.Source, projectContext);
                    if (dictionary != null)
                    {
                        styledElement.Resources.MergedDictionaries.Add(dictionary);
                    }
                }
            }

            foreach (var entry in resources.Values)
            {
                var resourceValue = BuildResourceValue(entry.Value, resourceScope, projectContext);
                resourceScope[entry.Key] = resourceValue;

                if (host is StyledElement keyedResourceHost && resourceValue != null)
                {
                    keyedResourceHost.Resources[entry.Key] = resourceValue;
                }
            }
        }

        private static object? BuildResourceValue(
            UiValue value,
            IDictionary<string, object?> resourceScope,
            ProjectContext? projectContext)
        {
            return value switch
            {
                ScalarValue { Value: not null } scalar => scalar.Value,
                NodeValue node => CreateComplexObject(node.Node, resourceScope, projectContext),
                ResourceValue resource => resourceScope.TryGetValue(resource.Key, out var resourceValue) ? resourceValue : null,
                UriReferenceValue asset => CreateAssetValue(asset, typeof(IImage), projectContext),
                _ => null
            };
        }

        private static void ApplyStyles(Control control, UiStyles? styles, ProjectContext? projectContext)
        {
            if (styles == null)
            {
                return;
            }

            foreach (var styleValue in styles.Items)
            {
                switch (styleValue)
                {
                    case StyleIncludeValue include:
                    {
                        var parsedStyles = LoadStyles(include.Source, projectContext);
                        if (parsedStyles != null)
                        {
                            control.Styles.Add(parsedStyles);
                        }

                        break;
                    }
                    case StyleNodeValue nodeValue:
                    {
                        var styleObject = CreateComplexObject(nodeValue.Node, new Dictionary<string, object?>(StringComparer.Ordinal), projectContext);
                        if (styleObject is IStyle style)
                        {
                            control.Styles.Add(style);
                        }

                        break;
                    }
                }
            }
        }

        private static Styles? LoadStyles(string source, ProjectContext? projectContext)
        {
            var filePath = ResolveProjectRelativePath(source, null, projectContext);
            if (filePath == null || !File.Exists(filePath))
            {
                return null;
            }

            var xaml = File.ReadAllText(filePath);
            return AvaloniaRuntimeXamlLoader.Parse<Styles>(xaml, typeof(App).Assembly);
        }

        private static ResourceDictionary? LoadResourceDictionary(string source, ProjectContext? projectContext)
        {
            var filePath = ResolveProjectRelativePath(source, null, projectContext);
            if (filePath == null || !File.Exists(filePath))
            {
                return null;
            }

            var xaml = File.ReadAllText(filePath);
            return AvaloniaRuntimeXamlLoader.Parse<ResourceDictionary>(xaml, typeof(App).Assembly);
        }

        private static string? ResolveProjectRelativePath(string path, string? assemblyName, ProjectContext? projectContext)
        {
            if (projectContext == null || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
            {
                if (!string.Equals(absoluteUri.Scheme, "avares", StringComparison.OrdinalIgnoreCase))
                {
                    return absoluteUri.IsFile ? absoluteUri.LocalPath : null;
                }

                if (!string.IsNullOrWhiteSpace(assemblyName) &&
                    !string.Equals(assemblyName, projectContext.AssemblyName, StringComparison.Ordinal))
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(absoluteUri.Host) &&
                    !string.Equals(absoluteUri.Host, projectContext.AssemblyName, StringComparison.Ordinal))
                {
                    return null;
                }

                var relativePath = absoluteUri.AbsolutePath.TrimStart('/');
                return System.IO.Path.Combine(projectContext.ProjectDirectory, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            }

            return System.IO.Path.Combine(projectContext.ProjectDirectory, path.TrimStart('/', '\\'));
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
