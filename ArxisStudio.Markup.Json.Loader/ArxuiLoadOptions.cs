namespace ArxisStudio.Markup.Json.Loader;

/// <summary>
/// Определяет параметры загрузки документа <c>.arxui</c>.
/// </summary>
public sealed class ArxuiLoadOptions
{
    /// <summary>
    /// Получает или задаёт значение, указывающее, следует ли учитывать привязки при построении дерева.
    /// </summary>
    public bool AllowBindings { get; init; }

    /// <summary>
    /// Получает или задаёт значение, указывающее, следует ли разрешать ссылки на ассеты.
    /// </summary>
    public bool AllowAssets { get; init; } = true;

    /// <summary>
    /// Получает или задаёт значение, указывающее, следует ли загружать внешние словари ресурсов и стили.
    /// </summary>
    public bool AllowExternalIncludes { get; init; } = true;

    /// <summary>
    /// Получает или задаёт значение, указывающее, разрешён ли fallback на другой <c>.arxui</c>-документ по значению <see cref="UiDocument.Class"/>.
    /// </summary>
    public bool AllowDocumentFallback { get; init; } = true;
}
