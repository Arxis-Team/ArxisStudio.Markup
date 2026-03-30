using System;
using System.IO;

namespace ArxisStudio.Markup.Json.Loader.Services;

internal static class ProjectPathResolver
{
    internal static string? ResolveProjectRelativePath(
        string path,
        string? assemblyName,
        string? projectDirectory,
        string? projectAssemblyName)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
        {
            if (!string.Equals(absoluteUri.Scheme, "avares", StringComparison.OrdinalIgnoreCase))
            {
                return absoluteUri.IsFile ? absoluteUri.LocalPath : null;
            }

            if (!string.IsNullOrWhiteSpace(assemblyName) &&
                !string.Equals(assemblyName, projectAssemblyName, StringComparison.Ordinal))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(absoluteUri.Host) &&
                !string.Equals(absoluteUri.Host, projectAssemblyName, StringComparison.Ordinal))
            {
                return null;
            }

            var relativePath = absoluteUri.AbsolutePath.TrimStart('/');
            return Path.Combine(projectDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        return Path.Combine(projectDirectory, path.TrimStart('/', '\\'));
    }
}
