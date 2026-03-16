namespace ArxisStudio.Markup.Json.Loader.Models;

/// <summary>
/// Представляет индексированный файл проекта, доступный loader-у и editor-у.
/// </summary>
public sealed class ProjectFileItem
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ProjectFileItem"/>.
    /// </summary>
    public ProjectFileItem(string fullPath, string relativePath, string kind)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
        Kind = kind;
    }

    /// <summary>
    /// Возвращает абсолютный путь к файлу.
    /// </summary>
    public string FullPath { get; }

    /// <summary>
    /// Возвращает путь к файлу относительно директории проекта.
    /// </summary>
    public string RelativePath { get; }

    /// <summary>
    /// Возвращает семантический тип файла, например <c>arxui</c> или <c>axaml</c>.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Возвращает относительный путь файла.
    /// </summary>
    public override string ToString() => RelativePath;
}
