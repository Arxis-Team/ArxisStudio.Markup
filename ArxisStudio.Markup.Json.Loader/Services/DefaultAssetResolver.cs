using System;
using System.IO;

using Avalonia.Media;
using Avalonia.Media.Imaging;

using ArxisStudio.Markup.Json.Loader.Abstractions;

namespace ArxisStudio.Markup.Json.Loader.Services;

/// <summary>
/// Разрешает ссылки на ассеты в рамках текущего проекта.
/// </summary>
public sealed class DefaultAssetResolver : IAssetResolver
{
    /// <inheritdoc />
    public object? Resolve(UriReferenceValue asset, Type targetType, ArxuiLoadContext context)
    {
        var assetPath = ProjectPathResolver.ResolveProjectRelativePath(asset.Path, asset.Assembly, context.ProjectContext);
        if (assetPath == null || !File.Exists(assetPath))
        {
            return null;
        }

        if (typeof(IImage).IsAssignableFrom(targetType))
        {
            return new Bitmap(assetPath);
        }

        return null;
    }
}
