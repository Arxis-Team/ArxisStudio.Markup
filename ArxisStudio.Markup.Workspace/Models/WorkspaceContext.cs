using System.Collections.Generic;

namespace ArxisStudio.Markup.Workspace.Models;

/// <summary>
/// Содержит проиндексированное представление проекта для design-time анализа.
/// </summary>
public sealed class WorkspaceContext
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WorkspaceContext"/>.
    /// </summary>
    public WorkspaceContext(
        string sourcePath,
        string projectPath,
        string projectDirectory,
        string assemblyName,
        string targetFramework,
        IReadOnlyList<ArxuiDocumentInfo> documents,
        IReadOnlyDictionary<string, TypeMetadata> types,
        IReadOnlyDictionary<string, TypeMetadata> frameworkTypes)
    {
        SourcePath = sourcePath;
        ProjectPath = projectPath;
        ProjectDirectory = projectDirectory;
        AssemblyName = assemblyName;
        TargetFramework = targetFramework;
        Documents = documents;
        Types = types;
        FrameworkTypes = frameworkTypes;
    }

    /// <summary>
    /// Получает исходный путь, переданный при открытии проекта.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Получает путь к файлу проекта.
    /// </summary>
    public string ProjectPath { get; }

    /// <summary>
    /// Получает каталог проекта.
    /// </summary>
    public string ProjectDirectory { get; }

    /// <summary>
    /// Получает имя сборки проекта.
    /// </summary>
    public string AssemblyName { get; }

    /// <summary>
    /// Получает целевой TFM проекта.
    /// </summary>
    public string TargetFramework { get; }

    /// <summary>
    /// Получает проиндексированные документы <c>.arxui</c>.
    /// </summary>
    public IReadOnlyList<ArxuiDocumentInfo> Documents { get; }

    /// <summary>
    /// Получает индекс типов проекта по полному имени.
    /// </summary>
    public IReadOnlyDictionary<string, TypeMetadata> Types { get; }

    /// <summary>
    /// Получает индекс стандартных framework-типов по полному имени.
    /// </summary>
    public IReadOnlyDictionary<string, TypeMetadata> FrameworkTypes { get; }
}
