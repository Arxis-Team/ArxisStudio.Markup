using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Avalonia.Controls;

using ArxisStudio.Markup.Workspace.Models;

namespace ArxisStudio.Markup.Workspace.Services;

/// <summary>
/// Строит каталог стандартных Avalonia-контролов, доступных в дизайнере.
/// </summary>
public sealed class FrameworkTypeCatalogService
{
    /// <summary>
    /// Возвращает индекс типов framework-контролов по полному имени.
    /// </summary>
    public IReadOnlyDictionary<string, TypeMetadata> Build()
    {
        var controlBaseType = typeof(Control);
        var topLevelBaseType = typeof(TopLevel);

        return controlBaseType.Assembly
            .GetTypes()
            .Where(type => type.IsPublic)
            .Where(type => !type.IsAbstract)
            .Where(type => controlBaseType.IsAssignableFrom(type))
            .Where(type => type.Namespace != null && type.Namespace.StartsWith("Avalonia.", StringComparison.Ordinal))
            .Select(type => new TypeMetadata(
                type.FullName ?? type.Name,
                type.Name,
                type.BaseType?.FullName,
                true,
                topLevelBaseType.IsAssignableFrom(type) || typeof(Window).IsAssignableFrom(type),
                type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(property => property.SetMethod != null && property.SetMethod.IsPublic)
                    .Select(property => new PropertyMetadata(
                        property.Name,
                        property.PropertyType.FullName ?? property.PropertyType.Name,
                        true,
                        typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType) &&
                        property.PropertyType != typeof(string)))
                    .GroupBy(property => property.Name, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToList()))
            .GroupBy(type => type.FullName, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    }
}
