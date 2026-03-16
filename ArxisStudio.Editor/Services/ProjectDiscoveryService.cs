using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ArxisStudio.Markup.Json.Loader.Models;

namespace ArxisStudio.Editor.Services;

public sealed class ProjectDiscoveryService
{
    public ProjectContext Load(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new InvalidOperationException("Project path is empty.");
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("Project file was not found.", fullSourcePath);
        }

        var extension = Path.GetExtension(fullSourcePath);
        var projectPath = extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            ? ResolveProjectPathFromSolution(fullSourcePath)
            : fullSourcePath;

        if (!projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .sln and .csproj files are supported.");
        }

        return LoadProjectContext(fullSourcePath, projectPath);
    }

    private static ProjectContext LoadProjectContext(string sourcePath, string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
                               ?? throw new InvalidOperationException("Project directory was not found.");

        var projectXml = XDocument.Load(projectPath);
        var propertyGroups = projectXml.Root?
            .Elements()
            .Where(element => element.Name.LocalName == "PropertyGroup")
            .ToList() ?? new List<XElement>();

        var assemblyName = FindFirstProperty(propertyGroups, "AssemblyName")
                           ?? Path.GetFileNameWithoutExtension(projectPath);
        var targetFramework = FindFirstProperty(propertyGroups, "TargetFramework")
                              ?? FindFirstProperty(propertyGroups, "TargetFrameworks")?.Split(';').FirstOrDefault()
                              ?? "unknown";

        var arxuiFiles = IndexFiles(projectDirectory, "*.arxui", "arxui");
        var axamlFiles = IndexFiles(projectDirectory, "*.axaml", "axaml");

        return new ProjectContext(
            sourcePath,
            projectPath,
            projectDirectory,
            assemblyName,
            targetFramework,
            arxuiFiles,
            axamlFiles);
    }

    private static string ResolveProjectPathFromSolution(string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)
                                ?? throw new InvalidOperationException("Solution directory was not found.");

        var projectPaths = File.ReadLines(solutionPath)
            .Select(ParseProjectPath)
            .Where(path => path != null)
            .Select(path => Path.GetFullPath(Path.Combine(solutionDirectory, path!)))
            .Where(File.Exists)
            .ToList();

        if (projectPaths.Count == 0)
        {
            throw new InvalidOperationException("No .csproj files were found in the solution.");
        }

        var avaloniaProject = projectPaths.FirstOrDefault(IsAvaloniaProject);
        return avaloniaProject ?? projectPaths[0];
    }

    private static string? ParseProjectPath(string line)
    {
        if (!line.Contains(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = line.Split('"');
        return parts.FirstOrDefault(part => part.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAvaloniaProject(string projectPath)
    {
        var content = File.ReadAllText(projectPath);
        return content.Contains("PackageReference Include=\"Avalonia\"", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ProjectFileItem> IndexFiles(string projectDirectory, string searchPattern, string kind)
    {
        return Directory.GetFiles(projectDirectory, searchPattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ProjectFileItem(
                path,
                Path.GetRelativePath(projectDirectory, path),
                kind))
            .ToList();
    }

    private static string? FindFirstProperty(IEnumerable<XElement> propertyGroups, string propertyName)
    {
        foreach (var group in propertyGroups)
        {
            var property = group.Elements().FirstOrDefault(element => element.Name.LocalName == propertyName);
            if (property != null && !string.IsNullOrWhiteSpace(property.Value))
            {
                return property.Value.Trim();
            }
        }

        return null;
    }
}
