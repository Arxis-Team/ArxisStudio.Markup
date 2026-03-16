using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using ArxisStudio.Markup.Json;
using ArxisStudio.Markup.Workspace.Models;

namespace ArxisStudio.Markup.Workspace.Services;

/// <summary>
/// Индексирует проект с помощью Roslyn и строит design-time модель типов и документов.
/// </summary>
public sealed class RoslynWorkspaceService
{
    /// <summary>
    /// Загружает проект из <c>.csproj</c> или <c>.sln</c> и возвращает индексированный workspace-контекст.
    /// </summary>
    public WorkspaceContext Load(string sourcePath)
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

        return LoadWorkspaceContext(fullSourcePath, projectPath);
    }

    private static WorkspaceContext LoadWorkspaceContext(string sourcePath, string projectPath)
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

        var typeIndex = BuildTypeIndex(projectDirectory, assemblyName);
        var frameworkTypeIndex = new FrameworkTypeCatalogService().Build();
        var documents = BuildDocumentIndex(projectDirectory, typeIndex);

        return new WorkspaceContext(
            sourcePath,
            projectPath,
            projectDirectory,
            assemblyName,
            targetFramework,
            documents,
            typeIndex,
            frameworkTypeIndex);
    }

    private static IReadOnlyDictionary<string, TypeMetadata> BuildTypeIndex(string projectDirectory, string assemblyName)
    {
        var sourceFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = sourceFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))
            .ToList();

        var references = BuildMetadataReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var types = new Dictionary<string, TypeMetadata>(StringComparer.Ordinal);

        foreach (var tree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var declarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
            foreach (var declaration in declarations)
            {
                if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                {
                    continue;
                }

                var fullName = symbol.ToDisplayString();
                if (string.IsNullOrWhiteSpace(fullName) || types.ContainsKey(fullName))
                {
                    continue;
                }

                var properties = symbol.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Where(property => property.DeclaredAccessibility == Accessibility.Public)
                    .Where(property => !property.IsStatic)
                    .Select(property => new PropertyMetadata(
                        property.Name,
                        property.Type.ToDisplayString(),
                        property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public,
                        property.Type.AllInterfaces.Any(i => i.ToDisplayString() == "System.Collections.IEnumerable") &&
                        property.Type.SpecialType != SpecialType.System_String))
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToList();

                types[fullName] = new TypeMetadata(
                    fullName,
                    symbol.Name,
                    symbol.BaseType?.ToDisplayString(),
                    DerivesFrom(symbol, "Avalonia.Controls.Control"),
                    DerivesFrom(symbol, "Avalonia.Controls.TopLevel") || DerivesFrom(symbol, "Avalonia.Controls.Window"),
                    properties);
            }
        }

        return types;
    }

    private static IReadOnlyList<ArxuiDocumentInfo> BuildDocumentIndex(
        string projectDirectory,
        IReadOnlyDictionary<string, TypeMetadata> typeIndex)
    {
        var files = Directory.GetFiles(projectDirectory, "*.arxui", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var documents = new List<ArxuiDocumentInfo>();
        foreach (var path in files)
        {
            try
            {
                var document = ArxuiSerializer.Deserialize(File.ReadAllText(path));
                if (document == null)
                {
                    continue;
                }

                var isPreviewable =
                    document.Kind == UiDocumentKind.Control ||
                    document.Kind == UiDocumentKind.Window ||
                    (!string.IsNullOrWhiteSpace(document.Class) &&
                     typeIndex.TryGetValue(document.Class, out var typeMetadata) &&
                     (typeMetadata.IsControl || typeMetadata.IsTopLevel));

                documents.Add(new ArxuiDocumentInfo(
                    path,
                    Path.GetRelativePath(projectDirectory, path),
                    document.Kind,
                    document.Class,
                    document.Root.TypeName,
                    isPreviewable));
            }
            catch
            {
                // Ignore invalid design documents while indexing workspace.
            }
        }

        return documents;
    }

    private static IReadOnlyList<MetadataReference> BuildMetadataReferences()
    {
        var referencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
        {
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    referencePaths.Add(path);
                }
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(assembly.Location) && File.Exists(assembly.Location))
                {
                    referencePaths.Add(assembly.Location);
                }
            }
            catch
            {
                // Dynamic assemblies do not expose a stable file path.
            }
        }

        return referencePaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
    }

    private static bool DerivesFrom(INamedTypeSymbol symbol, string targetTypeName)
    {
        for (var current = symbol; current != null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), targetTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
