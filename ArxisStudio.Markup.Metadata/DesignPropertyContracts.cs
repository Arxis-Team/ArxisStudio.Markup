using System.Collections.Generic;

namespace ArxisStudio.Markup.Metadata;

/// <summary>
/// Абстракция валидатора metadata.
/// </summary>
public interface IMetadataValidator
{
    /// <summary>
    /// Валидирует metadata-модель.
    /// </summary>
    /// <param name="metadata">Модель метаданных.</param>
    /// <returns>Список диагностик валидации.</returns>
    IReadOnlyList<MetadataDiagnostic> Validate(DesignMetadata metadata);
}

/// <summary>
/// Диагностическое сообщение валидатора метаданных.
/// </summary>
/// <param name="Code">Код диагностики.</param>
/// <param name="Message">Текст диагностики.</param>
/// <param name="NodeRef">Ссылка на узел, если применимо.</param>
/// <param name="PropertyKey">Ключ свойства, если применимо.</param>
public sealed record MetadataDiagnostic(
    string Code,
    string Message,
    string? NodeRef = null,
    string? PropertyKey = null);
