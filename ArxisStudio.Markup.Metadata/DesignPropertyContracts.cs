using System;
using System.Collections.Generic;

namespace ArxisStudio.Markup.Metadata;

/// <summary>
/// Дескриптор свойства метаданных дизайнера.
/// </summary>
/// <param name="CanonicalKey">Канонический ключ свойства.</param>
/// <param name="ValueType">Ожидаемый CLR-тип значения.</param>
/// <param name="Aliases">Допустимые псевдонимы ключа.</param>
public sealed record DesignPropertyDescriptor(
    string CanonicalKey,
    Type ValueType,
    IReadOnlyCollection<string>? Aliases = null);

/// <summary>
/// Абстракция реестра свойств метаданных.
/// </summary>
public interface IDesignPropertyRegistry
{
    /// <summary>
    /// Пытается получить дескриптор по каноническому ключу.
    /// </summary>
    /// <param name="canonicalKey">Канонический ключ свойства.</param>
    /// <param name="descriptor">Найденный дескриптор.</param>
    /// <returns><see langword="true"/>, если дескриптор найден.</returns>
    bool TryGetByCanonicalKey(string canonicalKey, out DesignPropertyDescriptor descriptor);

    /// <summary>
    /// Пытается разрешить ключ или псевдоним в дескриптор свойства.
    /// </summary>
    /// <param name="keyOrAlias">Ключ или псевдоним свойства.</param>
    /// <param name="descriptor">Найденный дескриптор.</param>
    /// <returns><see langword="true"/>, если дескриптор найден.</returns>
    bool TryResolve(string keyOrAlias, out DesignPropertyDescriptor descriptor);

    /// <summary>
    /// Возвращает все зарегистрированные дескрипторы свойств.
    /// </summary>
    /// <returns>Коллекция дескрипторов.</returns>
    IReadOnlyCollection<DesignPropertyDescriptor> GetAll();
}

/// <summary>
/// Абстракция валидатора метаданных.
/// </summary>
public interface IMetadataValidator
{
    /// <summary>
    /// Валидирует оверлей метаданных по правилам реестра свойств.
    /// </summary>
    /// <param name="overlay">Оверлей метаданных.</param>
    /// <param name="registry">Реестр допустимых свойств.</param>
    /// <returns>Список диагностик валидации.</returns>
    IReadOnlyList<MetadataDiagnostic> Validate(DesignOverlay overlay, IDesignPropertyRegistry registry);
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
