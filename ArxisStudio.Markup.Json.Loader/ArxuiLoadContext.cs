using System.Collections.Generic;
using Avalonia.Controls;
using ArxisStudio.Markup.Json.Loader.Abstractions;

namespace ArxisStudio.Markup.Json.Loader;

/// <summary>
/// Описывает зависимости и параметры, необходимые для загрузки дерева <c>.arxui</c>.
/// </summary>
public sealed class ArxuiLoadContext
{
    /// <summary>
    /// Получает или задаёт резолвер CLR-типов.
    /// </summary>
    public ITypeResolver TypeResolver { get; init; } = null!;

    /// <summary>
    /// Получает или задаёт резолвер ассетов.
    /// </summary>
    public IAssetResolver? AssetResolver { get; init; }

    /// <summary>
    /// Получает или задаёт резолвер документов <c>.arxui</c> по значению <see cref="UiDocument.Class"/>.
    /// </summary>
    public IMarkupDocumentResolver? DocumentResolver { get; init; }

    /// <summary>
    /// Получает или задаёт резолвер внешних путей ресурсов и ассетов.
    /// </summary>
    public IPathResolver? PathResolver { get; init; }

    /// <summary>
    /// Получает или задаёт фабрику preview-представлений для top-level контролов.
    /// </summary>
    public ITopLevelControlFactory? TopLevelControlFactory { get; init; }

    /// <summary>
    /// Получает или задаёт карту соответствия узлов модели и созданных контролов.
    /// </summary>
    public IDictionary<UiNode, Control>? NodeMap { get; init; }

    /// <summary>
    /// Получает или задаёт параметры загрузки.
    /// </summary>
    public ArxuiLoadOptions Options { get; init; } = new();
}
