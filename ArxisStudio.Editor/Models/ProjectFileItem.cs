namespace ArxisStudio.Editor.Models;

public sealed class ProjectFileItem
{
    public ProjectFileItem(string fullPath, string relativePath, string kind)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
        Kind = kind;
    }

    public string FullPath { get; }

    public string RelativePath { get; }

    public string Kind { get; }

    public override string ToString() => RelativePath;
}
