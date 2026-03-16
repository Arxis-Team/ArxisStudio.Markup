using System;
using System.Collections.Generic;

using ArxisStudio.Markup.Json.Loader.Abstractions;

namespace ArxisStudio.Markup.Json.Loader.Services;

/// <summary>
/// Разрешает типы через загруженные в текущем домене приложения сборки.
/// </summary>
public sealed class ReflectionTypeResolver : ITypeResolver
{
    private static readonly Dictionary<string, Type> TypeCache = new();

    /// <inheritdoc />
    public Type? Resolve(string typeName)
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
