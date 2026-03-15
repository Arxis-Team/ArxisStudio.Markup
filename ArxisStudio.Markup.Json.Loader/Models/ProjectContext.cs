using System.Collections.Generic;

namespace ArxisStudio.Markup.Json.Loader.Models;

/// <summary>
/// Представляет проектный контекст, необходимый runtime/design-time loader-у
/// для разрешения файлов <c>.arxui</c>, <c>.axaml</c> и project-relative assets.
/// </summary>
public sealed class ProjectContext
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ProjectContext"/>.
    /// </summary>
    public ProjectContext(
        string sourcePath,
        string projectPath,
        string projectDirectory,
        string assemblyName,
        string targetFramework,
        IReadOnlyList<ProjectFileItem> arxuiFiles,
        IReadOnlyList<ProjectFileItem> axamlFiles)
    {
        SourcePath = sourcePath;
        ProjectPath = projectPath;
        ProjectDirectory = projectDirectory;
        AssemblyName = assemblyName;
        TargetFramework = targetFramework;
        ArxuiFiles = arxuiFiles;
        AxamlFiles = axamlFiles;
    }

    /// <summary>
    /// Возвращает исходный путь, переданный пользователем: <c>.sln</c> или <c>.csproj</c>.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Возвращает путь к целевому <c>.csproj</c>.
    /// </summary>
    public string ProjectPath { get; }

    /// <summary>
    /// Возвращает корневую директорию проекта.
    /// </summary>
    public string ProjectDirectory { get; }

    /// <summary>
    /// Возвращает имя сборки проекта.
    /// </summary>
    public string AssemblyName { get; }

    /// <summary>
    /// Возвращает target framework проекта.
    /// </summary>
    public string TargetFramework { get; }

    /// <summary>
    /// Возвращает найденные файлы <c>.arxui</c>.
    /// </summary>
    public IReadOnlyList<ProjectFileItem> ArxuiFiles { get; }

    /// <summary>
    /// Возвращает найденные файлы <c>.axaml</c>.
    /// </summary>
    public IReadOnlyList<ProjectFileItem> AxamlFiles { get; }
}
