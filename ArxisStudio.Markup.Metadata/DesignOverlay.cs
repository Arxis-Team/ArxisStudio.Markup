using System.Collections.Generic;

namespace ArxisStudio.Markup.Metadata;

/// <summary>
/// Оверлей метаданных дизайнера для markup-документа.
/// </summary>
/// <param name="Document">Документные метаданные.</param>
/// <param name="Nodes">Метаданные узлов.</param>
public sealed record DesignOverlay(
    DocumentDesignData? Document,
    IReadOnlyDictionary<NodeRef, NodeDesignData> Nodes);

/// <summary>
/// Ссылка на узел внутри документа.
/// </summary>
/// <param name="Value">Текстовое представление ссылки.</param>
public sealed record NodeRef(string Value);

/// <summary>
/// Метаданные дизайнера уровня документа.
/// </summary>
/// <param name="Properties">Набор свойств документа.</param>
public sealed record DocumentDesignData(
    IReadOnlyDictionary<string, DesignValue> Properties);

/// <summary>
/// Метаданные дизайнера уровня узла.
/// </summary>
/// <param name="Properties">Набор свойств узла.</param>
public sealed record NodeDesignData(
    IReadOnlyDictionary<string, DesignValue> Properties);
