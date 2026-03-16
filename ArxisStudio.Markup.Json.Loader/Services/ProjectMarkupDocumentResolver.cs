using System.IO;

using ArxisStudio.Markup.Json;
using ArxisStudio.Markup.Json.Loader.Abstractions;
using ArxisStudio.Markup.Json.Loader.Models;

namespace ArxisStudio.Markup.Json.Loader.Services;

/// <summary>
/// Разрешает корни <c>.arxui</c>-документов в пределах открытого проекта.
/// </summary>
public sealed class ProjectMarkupDocumentResolver : IMarkupDocumentResolver
{
    private readonly ProjectContext _projectContext;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ProjectMarkupDocumentResolver"/>.
    /// </summary>
    public ProjectMarkupDocumentResolver(ProjectContext projectContext)
    {
        _projectContext = projectContext;
    }

    /// <inheritdoc />
    public UiNode? ResolveRootByClass(string className)
    {
        foreach (var file in _projectContext.ArxuiFiles)
        {
            try
            {
                var document = ArxuiSerializer.Deserialize(File.ReadAllText(file.FullPath));
                if (document != null && string.Equals(document.Class, className, System.StringComparison.Ordinal))
                {
                    return document.Root;
                }
            }
            catch
            {
                // Ignore invalid documents while scanning preview metadata.
            }
        }

        return null;
    }
}
