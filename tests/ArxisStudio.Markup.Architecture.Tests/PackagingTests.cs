using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace ArxisStudio.Markup.Architecture.Tests;

/// <summary>
/// Guards what the preview packages carry.
/// </summary>
/// <remarks>
/// Package metadata is the kind of thing that is set once and then quietly lost to a refactor of
/// the build files, and nothing fails when it is: the packages still build, they are just worse.
/// A licence nobody declared or a description nobody wrote is found by whoever installs it.
/// </remarks>
public sealed class PackagingTests
{
    /// <summary>Properties every shipping package must carry, and why each is worth failing over.</summary>
    private static readonly (string Name, string Reason)[] Required =
    [
        ("IsPackable", "a package that is not packable ships as nothing at all"),
        ("GenerateDocumentationFile", "the contract requires XML documentation on all public APIs"),
        ("IncludeSymbols", "a preview with no symbols cannot be stepped into by whoever reports a bug"),
        ("PackageLicenseExpression", "an unlicensed package cannot legally be used"),
        ("PackageReadmeFile", "a package with no readme says nothing on its gallery page"),
        ("VersionPrefix", "an unversioned package cannot be referenced"),
    ];

    [Fact]
    public void EveryPackageDeclaresTheMetadataAPreviewNeeds()
    {
        IReadOnlyDictionary<string, string> shared = Properties(
            Path.Combine(RepositoryLayout.RepositoryRoot, "src", "Directory.Build.props"));

        var missing = new List<string>();

        foreach (string package in RepositoryLayout.Packages)
        {
            IReadOnlyDictionary<string, string> own = Properties(
                Path.Combine(RepositoryLayout.RepositoryRoot, "src", package, package + ".csproj"));

            missing.AddRange(
                Required
                    .Where(entry => !own.ContainsKey(entry.Name) && !shared.ContainsKey(entry.Name))
                    .Select(entry => $"{package} declares no {entry.Name}: {entry.Reason}"));
        }

        Assert.True(missing.Count == 0, string.Join("; ", missing));
    }

    [Fact]
    public void EveryPackageDescribesItself()
    {
        foreach (string package in RepositoryLayout.Packages)
        {
            IReadOnlyDictionary<string, string> own = Properties(
                Path.Combine(RepositoryLayout.RepositoryRoot, "src", package, package + ".csproj"));

            Assert.True(
                own.TryGetValue("Description", out string? description) && description.Length > 40,
                $"'{package}' has no description of its own. The three are told apart by what they " +
                "say they are for, and a shared one tells a reader nothing.");
        }
    }

    [Fact]
    public void TheFilesThePackagesCarryExist()
    {
        foreach (string name in new[] { "README.md", "LICENSE" })
        {
            Assert.True(
                File.Exists(Path.Combine(RepositoryLayout.RepositoryRoot, name)),
                $"src/Directory.Build.props packs '{name}', and it is not there.");
        }
    }

    [Fact]
    public void TheKnownLimitationsAreWrittenDown()
    {
        // The milestone asks for them, and a preview that does not say what it cannot do makes
        // whoever tries it find out one at a time.
        Assert.True(
            File.Exists(Path.Combine(RepositoryLayout.RepositoryRoot, "docs", "limitations.md")),
            "docs/limitations.md is missing.");
    }

    private static IReadOnlyDictionary<string, string> Properties(string path) =>
        !File.Exists(path)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : XDocument.Load(path)
                .Descendants("PropertyGroup")
                .Elements()
                .GroupBy(static element => element.Name.LocalName, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Last().Value,
                    StringComparer.Ordinal);
}
