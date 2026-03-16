namespace ArxisStudio.Markup.Workspace.Models;

/// <summary>
/// Описывает индексированную информацию о документе <c>.arxui</c>.
/// </summary>
/// <param name="FullPath">Абсолютный путь к файлу.</param>
/// <param name="RelativePath">Путь относительно каталога проекта.</param>
/// <param name="Kind">Тип документа.</param>
/// <param name="Class">Целевой CLR-класс документа.</param>
/// <param name="RootTypeName">Тип корневого узла.</param>
/// <param name="IsPreviewable">Указывает, может ли документ быть открыт в preview как control/window.</param>
public sealed record ArxuiDocumentInfo(
    string FullPath,
    string RelativePath,
    UiDocumentKind Kind,
    string? Class,
    string RootTypeName,
    bool IsPreviewable);
