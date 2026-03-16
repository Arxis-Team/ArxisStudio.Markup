using System;

namespace ArxisStudio.Markup.Json.Loader.Abstractions;

/// <summary>
/// Разрешает CLR-типы по строковому имени.
/// </summary>
public interface ITypeResolver
{
    /// <summary>
    /// Пытается разрешить тип по полному или сокращённому имени.
    /// </summary>
    Type? Resolve(string typeName);
}
