using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace ArxisStudio.Markup.Architecture.Tests;

/// <summary>
/// Guards the few things the three libraries say about themselves.
/// </summary>
/// <remarks>
/// This used to guard package metadata as well, back when the libraries were published. They are
/// consumed by project reference now, so licence expressions, readmes, symbol formats and Source
/// Link went with the packaging. What is left is not about distribution: an assembly still carries
/// a description, and the limitations are still the thing whoever builds on this has to read.
/// </remarks>
public sealed class ProjectMetadataTests
{
    [Fact]
    public void EveryLibraryDescribesItself()
    {
        foreach (string package in RepositoryLayout.Packages)
        {
            IReadOnlyDictionary<string, string> own = Properties(
                Path.Combine(RepositoryLayout.RepositoryRoot, "src", package, package + ".csproj"));

            // Reaches the assembly as its description, and reaches a reader long before that:
            // the three are told apart by what they say they are for, and a shared sentence
            // would tell nobody anything.
            Assert.True(
                own.TryGetValue("Description", out string? description) && description.Length > 40,
                $"'{package}' has no description of its own.");
        }
    }

    [Fact]
    public void TheKnownLimitationsAreWrittenDown()
    {
        // A library that does not say what it cannot do makes whoever builds on it find out one
        // at a time, and the honest list is what the guides point at.
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
