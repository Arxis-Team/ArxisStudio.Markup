using System.IO;
using System.Collections.Generic;

using ArxisStudio.Markup.Json;
using ArxisStudio.Markup.Json.Loader.Abstractions;

namespace ArxisStudio.Markup.Json.Loader.Services;

/// <summary>
/// Разрешает корни <c>.arxui</c>-документов в пределах открытого проекта.
/// </summary>
public sealed class ProjectMarkupDocumentResolver : IMarkupDocumentResolver
{
    private readonly IReadOnlyList<string> _arxuiFilePaths;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ProjectMarkupDocumentResolver"/>.
    /// </summary>
    public ProjectMarkupDocumentResolver(IReadOnlyList<string> arxuiFilePaths)
    {
        _arxuiFilePaths = arxuiFilePaths;
    }

    /// <inheritdoc />
    public UiNode? ResolveRootByClass(string className)
    {
        foreach (var filePath in _arxuiFilePaths)
        {
            try
            {
                var document = ArxuiSerializer.Deserialize(File.ReadAllText(filePath));
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
