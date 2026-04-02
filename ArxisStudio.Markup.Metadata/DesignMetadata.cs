using System.Collections.Generic;

namespace ArxisStudio.Markup.Metadata;

/// <summary>
/// Оверлей метаданных дизайнера для markup-документа.
/// </summary>
/// <param name="Document">Документные метаданные.</param>
/// <param name="Nodes">Метаданные узлов.</param>
public sealed record DesignMetadata(
    DocumentDesignMetadata? Document,
    IReadOnlyDictionary<NodeRef, NodeDesignMetadata> Nodes);

/// <summary>
/// Ссылка на узел внутри документа.
/// </summary>
/// <param name="Value">Текстовое представление ссылки.</param>
public sealed record NodeRef(string Value);

/// <summary>
/// Метаданные дизайнера уровня документа.
/// </summary>
/// <param name="Properties">Набор свойств документа.</param>
public sealed record DocumentDesignMetadata(
    IReadOnlyDictionary<string, DesignValue> Properties);

/// <summary>
/// Метаданные дизайнера уровня узла.
/// </summary>
/// <param name="Properties">Набор свойств узла.</param>
public sealed record NodeDesignMetadata(
    IReadOnlyDictionary<string, DesignValue> Properties);


