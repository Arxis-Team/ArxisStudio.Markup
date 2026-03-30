using System.Collections.Generic;
using Avalonia.Controls;
using ArxisStudio.Markup.Metadata;

namespace ArxisStudio.Markup.DesignEditorBridge;

/// <summary>
/// Фасад для конфигурации и использования bridge-интеграции с редактором дизайна.
/// </summary>
public sealed class DesignEditorBridgeRuntime
{
    /// <summary>
    /// Реестр дескрипторов свойств дизайнера.
    /// </summary>
    public DesignPropertyRegistry PropertyRegistry { get; }

    /// <summary>
    /// Реестр обработчиков применения значений.
    /// </summary>
    public DesignPropertyApplierRegistry ApplierRegistry { get; }

    /// <summary>
    /// Реестр обработчиков чтения значений.
    /// </summary>
    public DesignPropertyReaderRegistry ReaderRegistry { get; }

    /// <summary>
    /// Валидатор метаданных.
    /// </summary>
    public IMetadataValidator Validator { get; }

    /// <summary>
    /// Компонент применения оверлея к контролам.
    /// </summary>
    public DesignOverlayApplier Applier { get; }

    /// <summary>
    /// Компонент извлечения оверлея из контролов.
    /// </summary>
    public DesignOverlayExtractor Extractor { get; }

    /// <summary>
    /// Инициализирует экземпляр класса <see cref="DesignEditorBridgeRuntime"/>.
    /// </summary>
    /// <param name="propertyRegistry">Реестр свойств.</param>
    /// <param name="applierRegistry">Реестр обработчиков применения.</param>
    /// <param name="readerRegistry">Реестр обработчиков чтения.</param>
    /// <param name="validator">Валидатор метаданных.</param>
    public DesignEditorBridgeRuntime(
        DesignPropertyRegistry propertyRegistry,
        DesignPropertyApplierRegistry applierRegistry,
        DesignPropertyReaderRegistry readerRegistry,
        IMetadataValidator validator)
    {
        PropertyRegistry = propertyRegistry;
        ApplierRegistry = applierRegistry;
        ReaderRegistry = readerRegistry;
        Validator = validator;
        Applier = new DesignOverlayApplier(PropertyRegistry, ApplierRegistry);
        Extractor = new DesignOverlayExtractor(PropertyRegistry, ReaderRegistry);
    }

    /// <summary>
    /// Создаёт runtime без предустановленных свойств и маппингов.
    /// </summary>
    /// <param name="validator">Пользовательский валидатор или <see langword="null"/> для стандартного.</param>
    /// <returns>Пустой runtime для ручной конфигурации.</returns>
    public static DesignEditorBridgeRuntime CreateEmpty(IMetadataValidator? validator = null)
    {
        var propertyRegistry = new DesignPropertyRegistry();
        var applierRegistry = new DesignPropertyApplierRegistry();
        var readerRegistry = new DesignPropertyReaderRegistry();

        return new DesignEditorBridgeRuntime(
            propertyRegistry,
            applierRegistry,
            readerRegistry,
            validator ?? new SimpleMetadataValidator());
    }

    /// <summary>
    /// Создаёт runtime со встроенными свойствами и стандартными маппингами.
    /// </summary>
    /// <param name="validator">Пользовательский валидатор или <see langword="null"/> для стандартного.</param>
    /// <returns>Сконфигурированный runtime по умолчанию.</returns>
    public static DesignEditorBridgeRuntime CreateDefault(IMetadataValidator? validator = null)
    {
        var runtime = CreateEmpty(validator);
        runtime.PropertyRegistry.RegisterKnownProperties();
        runtime.ApplierRegistry.RegisterKnownAppliers();
        runtime.ReaderRegistry.RegisterKnownReaders();
        return runtime;
    }

    /// <summary>
    /// Валидирует оверлей метаданных относительно текущего реестра свойств.
    /// </summary>
    /// <param name="overlay">Оверлей метаданных.</param>
    /// <returns>Список диагностик валидации.</returns>
    public IReadOnlyList<MetadataDiagnostic> Validate(DesignOverlay overlay)
    {
        return Validator.Validate(overlay, PropertyRegistry);
    }

    /// <summary>
    /// Применяет оверлей метаданных к указанным контролам.
    /// </summary>
    /// <param name="overlay">Оверлей метаданных.</param>
    /// <param name="controlMap">Сопоставление ссылок узлов и контролов.</param>
    /// <returns>Список диагностик применения.</returns>
    public IReadOnlyList<BridgeDiagnostic> Apply(
        DesignOverlay overlay,
        IReadOnlyDictionary<NodeRef, Control> controlMap)
    {
        return Applier.Apply(overlay, controlMap);
    }

    /// <summary>
    /// Извлекает оверлей метаданных из указанного набора контролов.
    /// </summary>
    /// <param name="controlMap">Сопоставление ссылок узлов и контролов.</param>
    /// <param name="document">Документные метаданные, если требуется сохранить отдельно.</param>
    /// <returns>Извлечённый оверлей метаданных.</returns>
    public DesignOverlay Extract(
        IReadOnlyDictionary<NodeRef, Control> controlMap,
        DocumentDesignData? document = null)
    {
        return Extractor.Extract(controlMap, document);
    }
}
