using System.Collections.Generic;

namespace ArxisStudio.Markup.Metadata;

/// <summary>
/// Базовый тип значения метаданных дизайнера.
/// </summary>
public abstract record DesignValue;

/// <summary>
/// Скалярное значение метаданных.
/// </summary>
/// <param name="Value">Значение скаляра.</param>
public sealed record DesignScalarValue(object? Value) : DesignValue;

/// <summary>
/// Объектное значение метаданных.
/// </summary>
/// <param name="Properties">Набор дочерних свойств.</param>
public sealed record DesignObjectValue(IReadOnlyDictionary<string, DesignValue> Properties) : DesignValue;

/// <summary>
/// Коллекционное значение метаданных.
/// </summary>
/// <param name="Items">Элементы коллекции.</param>
public sealed record DesignCollectionValue(IReadOnlyList<DesignValue> Items) : DesignValue;
