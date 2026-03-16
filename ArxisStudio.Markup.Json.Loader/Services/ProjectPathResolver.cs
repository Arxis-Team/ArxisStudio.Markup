using System;
using System.IO;

using ArxisStudio.Markup.Json.Loader.Models;

namespace ArxisStudio.Markup.Json.Loader.Services;

internal static class ProjectPathResolver
{
    public static string? ResolveProjectRelativePath(string path, string? assemblyName, ProjectContext? projectContext)
    {
        if (projectContext == null || string.IsNullOrWhiteSpace(path))
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
                !string.Equals(assemblyName, projectContext.AssemblyName, StringComparison.Ordinal))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(absoluteUri.Host) &&
                !string.Equals(absoluteUri.Host, projectContext.AssemblyName, StringComparison.Ordinal))
            {
                return null;
            }

            var relativePath = absoluteUri.AbsolutePath.TrimStart('/');
            return Path.Combine(projectContext.ProjectDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        return Path.Combine(projectContext.ProjectDirectory, path.TrimStart('/', '\\'));
    }
}
