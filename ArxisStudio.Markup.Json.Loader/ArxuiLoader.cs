using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

using ArxisStudio.Markup.Json.Loader.Abstractions;
using ArxisStudio.Markup.Json.Loader.Services;

namespace ArxisStudio.Markup.Json.Loader;

/// <summary>
/// Загружает дерево <see cref="UiNode"/> в набор Avalonia-контролов.
/// </summary>
public sealed class ArxuiLoader
{
    /// <summary>
    /// Строит Avalonia-контрол по описанию узла.
    /// </summary>
    public Control? Load(UiNode node, ArxuiLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.TypeResolver);

        if (string.IsNullOrWhiteSpace(node.TypeName))
        {
            return new TextBlock { Text = "Error: Root control type is missing", Foreground = Brushes.Red };
        }

        return Build(node, context);
    }

    private Control Build(UiNode node, ArxuiLoadContext context)
    {
        var controlType = context.TypeResolver.Resolve(node.TypeName);
        if (controlType == null)
        {
            var arxuiControl = TryBuildArxuiBackedControl(node, context);
            if (arxuiControl != null)
            {
                return arxuiControl;
            }

            return new TextBlock { Text = $"Error: Type '{node.TypeName}' not found", Foreground = Brushes.Red };
        }

        if (!typeof(Control).IsAssignableFrom(controlType))
        {
            return new TextBlock
            {
                Text = $"Preview for root type '{node.TypeName}' is not supported. Open a Control or Window document.",
                Foreground = Brushes.Orange,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16)
            };
        }

        if (typeof(Window).IsAssignableFrom(controlType))
        {
            return BuildTopLevelControl(node, controlType, context);
        }

        var control = (Control)Activator.CreateInstance(controlType)!;
        context.NodeMap?.Add(node, control);

        var resourceScope = new Dictionary<string, object?>(StringComparer.Ordinal);

        ApplyDesignMetadata(control, node.Design);
        ApplyResources(control, node.Resources, resourceScope, context);
        ApplyStyles(control, node.Styles, context);

        foreach (var property in node.Properties)
        {
            ApplyProperty(control, property.Key, property.Value, resourceScope, context);
        }

        return control;
    }

    private Control BuildTopLevelControl(UiNode node, Type resolvedType, ArxuiLoadContext context)
    {
        var topLevelFactory = context.TopLevelControlFactory ?? new DefaultTopLevelControlFactory();
        var built = topLevelFactory.Create(node, resolvedType, context);
        if (built == null)
        {
            return new TextBlock
            {
                Text = $"Preview for top-level type '{node.TypeName}' is not supported.",
                Foreground = Brushes.Orange,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16)
            };
        }

        context.NodeMap?.Add(node, built.Root);

        var resourceScope = new Dictionary<string, object?>(StringComparer.Ordinal);
        ApplyDesignMetadata(built.Root, node.Design);
        ApplyResources(built.Root, node.Resources, resourceScope, context);
        ApplyStyles(built.Root, node.Styles, context);

        foreach (var property in node.Properties)
        {
            if (string.Equals(property.Key, "Content", StringComparison.Ordinal))
            {
                if (property.Value is NodeValue nodeValue)
                {
                    var content = CreateComplexObject(nodeValue.Node, resourceScope, context);
                    if (content is Control contentControl && built.ContentHost != null)
                    {
                        built.ContentHost.Content = contentControl;
                    }
                }

                continue;
            }

            if (string.Equals(property.Key, "Title", StringComparison.Ordinal))
            {
                continue;
            }

            ApplyProperty(built.Root, property.Key, property.Value, resourceScope, context);
        }

        return built.Root;
    }

    private Control? TryBuildArxuiBackedControl(UiNode node, ArxuiLoadContext context)
    {
        if (!context.Options.AllowDocumentFallback || context.DocumentResolver == null || string.IsNullOrWhiteSpace(node.TypeName))
        {
            return null;
        }

        var documentRoot = context.DocumentResolver.ResolveRootByClass(node.TypeName);
        if (documentRoot == null)
        {
            return null;
        }

        var builtControl = Build(documentRoot, context);
        if (context.NodeMap != null)
        {
            context.NodeMap[node] = builtControl;
        }

        return builtControl;
    }

    private static void ApplyDesignMetadata(Control control, UiNodeDesign? design)
    {
        if (design == null)
        {
            return;
        }

        if (design.Hidden == true)
        {
            control.IsVisible = false;
        }

        if (design.IgnorePreviewInput == true)
        {
            control.IsHitTestVisible = false;
        }
    }

    private void ApplyProperty(
        Control control,
        string propertyName,
        UiValue value,
        IDictionary<string, object?> resourceScope,
        ArxuiLoadContext context)
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
                ApplyAttachedProperty(control, propertyName, value, resourceScope, context);
                return;
            }

            if (value is CollectionValue collectionValue)
            {
                ApplyCollectionProperty(control, propertyName, collectionValue.Items, resourceScope, context);
                return;
            }

            if (value is NodeValue nodeValue)
            {
                var complexObject = CreateComplexObject(nodeValue.Node, resourceScope, context);
                if (complexObject != null)
                {
                    SetComplexProperty(control, propertyName, complexObject);
                }

                return;
            }

            if (value is BindingValue && !context.Options.AllowBindings)
            {
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

            var convertedValue = ConvertPropertyValue(value, targetType, resourceScope, context);
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

    private void ApplyAttachedProperty(
        Control control,
        string propertyName,
        UiValue value,
        IDictionary<string, object?> resourceScope,
        ArxuiLoadContext context)
    {
        var setter = FindAttachedPropertySetMethod(propertyName, context);
        if (setter == null)
        {
            return;
        }

        var targetType = setter.GetParameters()[1].ParameterType;
        var convertedValue = ConvertPropertyValue(value, targetType, resourceScope, context);
        if (convertedValue == null)
        {
            return;
        }

        setter.Invoke(null, new[] { control, convertedValue });
    }

    private MethodInfo? FindAttachedPropertySetMethod(string propertyName, ArxuiLoadContext context)
    {
        var lastDotIndex = propertyName.LastIndexOf('.');
        if (lastDotIndex == -1)
        {
            return null;
        }

        var ownerTypeName = propertyName.Substring(0, lastDotIndex);
        var attachedPropertyName = propertyName.Substring(lastDotIndex + 1);
        var setterName = "Set" + attachedPropertyName;

        var ownerType = context.TypeResolver.Resolve(ownerTypeName);
        if (ownerType == null)
        {
            return null;
        }

        return ownerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == setterName)
            .Where(method => method.GetParameters().Length == 2)
            .FirstOrDefault(method => typeof(AvaloniaObject).IsAssignableFrom(method.GetParameters()[0].ParameterType));
    }

    private void ApplyCollectionProperty(
        object parentObject,
        string propertyName,
        IReadOnlyCollection<UiValue> items,
        IDictionary<string, object?> resourceScope,
        ArxuiLoadContext context)
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
                builtItem = CreateComplexObject(nodeItem.Node, resourceScope, context);
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

    private object? CreateComplexObject(
        UiNode node,
        IDictionary<string, object?> inheritedResources,
        ArxuiLoadContext context)
    {
        var resolvedType = context.TypeResolver.Resolve(node.TypeName);
        if (resolvedType == null)
        {
            return TryBuildArxuiBackedControl(node, context);
        }

        if (typeof(Control).IsAssignableFrom(resolvedType))
        {
            return Build(node, context);
        }

        var complexObject = Activator.CreateInstance(resolvedType);
        if (complexObject == null)
        {
            return null;
        }

        var localScope = new Dictionary<string, object?>(inheritedResources, StringComparer.Ordinal);
        ApplyResources(complexObject, node.Resources, localScope, context);

        foreach (var nestedProperty in node.Properties)
        {
            if (nestedProperty.Value is CollectionValue collectionValue)
            {
                ApplyCollectionProperty(complexObject, nestedProperty.Key, collectionValue.Items, localScope, context);
                continue;
            }

            var propertyInfo = complexObject.GetType().GetProperty(nestedProperty.Key);
            if (propertyInfo == null || !propertyInfo.CanWrite)
            {
                continue;
            }

            var convertedValue = ConvertPropertyValue(nestedProperty.Value, propertyInfo.PropertyType, localScope, context);
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

    private object? ConvertPropertyValue(
        UiValue value,
        Type targetType,
        IDictionary<string, object?> resourceScope,
        ArxuiLoadContext context)
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
            if (!context.Options.AllowAssets)
            {
                return null;
            }

            return context.AssetResolver?.Resolve(assetReference, targetType, context);
        }

        if (value is not ScalarValue scalar || scalar.Value == null)
        {
            return null;
        }

        return ConvertPrimitive(scalar.Value, targetType);
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

    private void ApplyResources(
        object host,
        UiResources? resources,
        IDictionary<string, object?> resourceScope,
        ArxuiLoadContext context)
    {
        if (resources == null)
        {
            return;
        }

        if (host is StyledElement styledElement && context.Options.AllowExternalIncludes)
        {
            foreach (var include in resources.MergedDictionaries)
            {
                var dictionary = LoadResourceDictionary(include.Source, context);
                if (dictionary != null)
                {
                    styledElement.Resources.MergedDictionaries.Add(dictionary);
                }
            }
        }

        foreach (var entry in resources.Values)
        {
            var resourceValue = BuildResourceValue(entry.Value, resourceScope, context);
            resourceScope[entry.Key] = resourceValue;

            if (host is StyledElement keyedResourceHost && resourceValue != null)
            {
                keyedResourceHost.Resources[entry.Key] = resourceValue;
            }
        }
    }

    private object? BuildResourceValue(
        UiValue value,
        IDictionary<string, object?> resourceScope,
        ArxuiLoadContext context)
    {
        return value switch
        {
            ScalarValue { Value: not null } scalar => scalar.Value,
            NodeValue node => CreateComplexObject(node.Node, resourceScope, context),
            ResourceValue resource => resourceScope.TryGetValue(resource.Key, out var resourceValue) ? resourceValue : null,
            UriReferenceValue asset when context.Options.AllowAssets => context.AssetResolver?.Resolve(asset, typeof(IImage), context),
            _ => null
        };
    }

    private void ApplyStyles(Control control, UiStyles? styles, ArxuiLoadContext context)
    {
        if (styles == null)
        {
            return;
        }

        foreach (var styleValue in styles.Items)
        {
            switch (styleValue)
            {
                case StyleIncludeValue include when context.Options.AllowExternalIncludes:
                {
                    var parsedStyles = LoadStyles(include.Source, context);
                    if (parsedStyles != null)
                    {
                        control.Styles.Add(parsedStyles);
                    }

                    break;
                }
                case StyleNodeValue nodeValue:
                {
                    var styleObject = CreateComplexObject(nodeValue.Node, new Dictionary<string, object?>(StringComparer.Ordinal), context);
                    if (styleObject is IStyle style)
                    {
                        control.Styles.Add(style);
                    }

                    break;
                }
            }
        }
    }

    private static Styles? LoadStyles(string source, ArxuiLoadContext context)
    {
        var filePath = ProjectPathResolver.ResolveProjectRelativePath(source, null, context.ProjectContext);
        if (filePath == null || !File.Exists(filePath))
        {
            return null;
        }

        var xaml = File.ReadAllText(filePath);
        return AvaloniaRuntimeXamlLoader.Parse<Styles>(xaml, typeof(ArxuiLoader).Assembly);
    }

    private static ResourceDictionary? LoadResourceDictionary(string source, ArxuiLoadContext context)
    {
        var filePath = ProjectPathResolver.ResolveProjectRelativePath(source, null, context.ProjectContext);
        if (filePath == null || !File.Exists(filePath))
        {
            return null;
        }

        var xaml = File.ReadAllText(filePath);
        return AvaloniaRuntimeXamlLoader.Parse<ResourceDictionary>(xaml, typeof(ArxuiLoader).Assembly);
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
}
