using System.Collections.Generic;

namespace ArxisStudio.Editor.Models;

public sealed class ProjectContext
{
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

    public string SourcePath { get; }

    public string ProjectPath { get; }

    public string ProjectDirectory { get; }

    public string AssemblyName { get; }

    public string TargetFramework { get; }

    public IReadOnlyList<ProjectFileItem> ArxuiFiles { get; }

    public IReadOnlyList<ProjectFileItem> AxamlFiles { get; }
}
