using System;

namespace ArxisStudio.Markup.Json.Loader.Abstractions;

/// <summary>
/// Разрешает значения ссылок на ассеты.
/// </summary>
public interface IAssetResolver
{
    /// <summary>
    /// Пытается построить значение ассета для целевого типа свойства.
    /// </summary>
    object? Resolve(UriReferenceValue asset, Type targetType, ArxuiLoadContext context);
}
