using System.Collections.Generic;
using Avalonia.Controls;
using ArxisStudio.Markup.Metadata;

namespace ArxisStudio.Markup.DesignEditorBridge;

/// <summary>
/// Применяет оверлей метаданных дизайнера к контролам редактора.
/// </summary>
public sealed class DesignOverlayApplier
{
    private readonly IDesignPropertyRegistry _propertyRegistry;
    private readonly IDesignPropertyApplierRegistry _applierRegistry;

    /// <summary>
    /// Инициализирует экземпляр класса <see cref="DesignOverlayApplier"/>.
    /// </summary>
    /// <param name="propertyRegistry">Реестр свойств метаданных.</param>
    /// <param name="applierRegistry">Реестр обработчиков применения.</param>
    public DesignOverlayApplier(
        IDesignPropertyRegistry propertyRegistry,
        IDesignPropertyApplierRegistry applierRegistry)
    {
        _propertyRegistry = propertyRegistry;
        _applierRegistry = applierRegistry;
    }

    /// <summary>
    /// Применяет метаданные к контролам.
    /// </summary>
    /// <param name="overlay">Оверлей метаданных.</param>
    /// <param name="controlMap">Сопоставление ссылок узлов и контролов.</param>
    /// <returns>Список диагностик применения.</returns>
    public IReadOnlyList<BridgeDiagnostic> Apply(
        DesignOverlay overlay,
        IReadOnlyDictionary<NodeRef, Control> controlMap)
    {
        var diagnostics = new List<BridgeDiagnostic>();

        foreach (var node in overlay.Nodes)
        {
            if (!controlMap.TryGetValue(node.Key, out var control))
            {
                diagnostics.Add(new BridgeDiagnostic(
                    BridgeDiagnosticCodes.ControlNotFound,
                    $"Control not found for node '{node.Key.Value}'.",
                    node.Key.Value,
                    null));
                continue;
            }

            foreach (var property in node.Value.Properties)
            {
                if (!_propertyRegistry.TryResolve(property.Key, out var descriptor))
                {
                    diagnostics.Add(new BridgeDiagnostic(
                        BridgeDiagnosticCodes.UnknownDesignProperty,
                        $"Design property '{property.Key}' is not registered.",
                        node.Key.Value,
                        property.Key));
                    continue;
                }

                if (!_applierRegistry.TryGet(descriptor.CanonicalKey, out var applier))
                {
                    diagnostics.Add(new BridgeDiagnostic(
                        BridgeDiagnosticCodes.ApplierNotRegistered,
                        $"No applier is registered for '{descriptor.CanonicalKey}'.",
                        node.Key.Value,
                        descriptor.CanonicalKey));
                    continue;
                }

                if (property.Value is not DesignScalarValue scalar)
                {
                    diagnostics.Add(new BridgeDiagnostic(
                        BridgeDiagnosticCodes.NonScalarValue,
                        $"Property '{property.Key}' requires scalar value.",
                        node.Key.Value,
                        property.Key));
                    continue;
                }

                applier.Apply(control, scalar.Value);
            }
        }

        return diagnostics;
    }
}

/// <summary>
/// Реестр обработчиков применения значений свойств дизайнера.
/// </summary>
public interface IDesignPropertyApplierRegistry
{
    /// <summary>
    /// Пытается получить обработчик применения по каноническому ключу.
    /// </summary>
    /// <param name="canonicalKey">Канонический ключ свойства.</param>
    /// <param name="applier">Найденный обработчик.</param>
    /// <returns><see langword="true"/>, если обработчик найден.</returns>
    bool TryGet(string canonicalKey, out IDesignPropertyApplier applier);
}

/// <summary>
/// Обработчик применения значения свойства к контролу.
/// </summary>
public interface IDesignPropertyApplier
{
    /// <summary>
    /// Применяет значение свойства к контролу.
    /// </summary>
    /// <param name="control">Целевой контрол.</param>
    /// <param name="value">Значение свойства.</param>
    void Apply(Control control, object? value);
}

/// <summary>
/// Диагностика bridge-слоя при применении метаданных.
/// </summary>
/// <param name="Code">Код диагностики.</param>
/// <param name="Message">Текст диагностики.</param>
/// <param name="NodeRef">Ссылка на узел, если применимо.</param>
/// <param name="PropertyKey">Ключ свойства, если применимо.</param>
public sealed record BridgeDiagnostic(
    string Code,
    string Message,
    string? NodeRef,
    string? PropertyKey);

/// <summary>
/// Реестр обработчиков чтения значений свойств дизайнера.
/// </summary>
public interface IDesignPropertyReaderRegistry
{
    /// <summary>
    /// Пытается получить обработчик чтения по каноническому ключу.
    /// </summary>
    /// <param name="canonicalKey">Канонический ключ свойства.</param>
    /// <param name="reader">Найденный обработчик.</param>
    /// <returns><see langword="true"/>, если обработчик найден.</returns>
    bool TryGet(string canonicalKey, out IDesignPropertyReader reader);
}

/// <summary>
/// Обработчик чтения значения свойства из контрола.
/// </summary>
public interface IDesignPropertyReader
{
    /// <summary>
    /// Пытается прочитать значение свойства из контрола.
    /// </summary>
    /// <param name="control">Источник значения.</param>
    /// <param name="value">Прочитанное значение.</param>
    /// <returns><see langword="true"/>, если чтение успешно.</returns>
    bool TryRead(Control control, out object? value);
}

/// <summary>
/// Извлекает оверлей метаданных из контролов редактора.
/// </summary>
public sealed class DesignOverlayExtractor
{
    private readonly IDesignPropertyRegistry _propertyRegistry;
    private readonly IDesignPropertyReaderRegistry _readerRegistry;

    /// <summary>
    /// Инициализирует экземпляр класса <see cref="DesignOverlayExtractor"/>.
    /// </summary>
    /// <param name="propertyRegistry">Реестр свойств метаданных.</param>
    /// <param name="readerRegistry">Реестр обработчиков чтения.</param>
    public DesignOverlayExtractor(
        IDesignPropertyRegistry propertyRegistry,
        IDesignPropertyReaderRegistry readerRegistry)
    {
        _propertyRegistry = propertyRegistry;
        _readerRegistry = readerRegistry;
    }

    /// <summary>
    /// Строит оверлей метаданных на основе текущего состояния контролов.
    /// </summary>
    /// <param name="controlMap">Сопоставление ссылок узлов и контролов.</param>
    /// <param name="document">Документные метаданные, если известны.</param>
    /// <returns>Сформированный оверлей метаданных.</returns>
    public DesignOverlay Extract(
        IReadOnlyDictionary<NodeRef, Control> controlMap,
        DocumentDesignData? document = null)
    {
        var nodes = new Dictionary<NodeRef, NodeDesignData>();
        var descriptors = _propertyRegistry.GetAll();

        foreach (var controlEntry in controlMap)
        {
            var properties = new Dictionary<string, DesignValue>();
            foreach (var descriptor in descriptors)
            {
                if (!_readerRegistry.TryGet(descriptor.CanonicalKey, out var reader))
                {
                    continue;
                }

                if (!reader.TryRead(controlEntry.Value, out var value))
                {
                    continue;
                }

                properties[descriptor.CanonicalKey] = new DesignScalarValue(value);
            }

            if (properties.Count > 0)
            {
                nodes[controlEntry.Key] = new NodeDesignData(properties);
            }
        }

        return new DesignOverlay(document, nodes);
    }
}
