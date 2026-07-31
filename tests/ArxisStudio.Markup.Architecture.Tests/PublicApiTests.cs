using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ArxisStudio.Markup.Architecture.Tests;

/// <summary>
/// Guards the declared public surface of the shipping packages.
/// </summary>
/// <remarks>
/// <para>
/// <c>PublicApiAnalyzers</c> already fails the build on an undeclared member, which is the real
/// enforcement. What it cannot do is notice that it has been switched off — a removed
/// <c>PackageReference</c>, a deleted <c>AdditionalFiles</c> line, a renamed file — and a
/// silently unenforced surface is worse than none, because the files then read as a record that
/// nobody is keeping.
/// </para>
/// <para>
/// So this checks the wiring, and checks the declared files against the compiled assembly at the
/// level the analyzer's own format makes reliable: the type. Member signatures are the
/// analyzer's business and are not reimplemented here, because a second, subtly different
/// rendering of a signature is a source of disagreements rather than of confidence.
/// </para>
/// </remarks>
public sealed class PublicApiTests
{
    private const string Analyzer = "Microsoft.CodeAnalysis.PublicApiAnalyzers";

    [Fact]
    public void Packages_TrackTheirPublicSurface()
    {
        foreach (string package in RepositoryLayout.Packages)
        {
            Assert.True(
                File.Exists(ApiFile(package, "Shipped")),
                $"'{package}' has no PublicAPI.Shipped.txt. It is where the surface moves at release, and " +
                "without it the analyzer treats everything as new for ever.");

            Assert.True(
                File.Exists(ApiFile(package, "Unshipped")),
                $"'{package}' has no PublicAPI.Unshipped.txt, so new public API has nowhere to be declared.");
        }
    }

    [Fact]
    public void Packages_ReferenceTheAnalyzerThatEnforcesIt()
    {
        // Declared once for all three in src/Directory.Build.props, which is why this reads the
        // effective reference set rather than each project file.
        foreach (string package in RepositoryLayout.Packages)
        {
            Assert.Contains(Analyzer, RepositoryLayout.PackageReferencesOf(package), StringComparer.Ordinal);
        }
    }

    [Fact]
    public void EveryPublicTypeIsDeclared()
    {
        var undeclared = new List<string>();

        foreach (string package in RepositoryLayout.Packages)
        {
            IReadOnlySet<string> declared = DeclaredNames(package);

            undeclared.AddRange(
                PublicTypes(package)
                    .Select(static type => type.FullName!.Replace('+', '.'))
                    .Where(name => !declared.Contains(name))
                    .Select(name => $"{package}: {name}"));
        }

        Assert.True(
            undeclared.Count == 0,
            "Public types missing from the declared API: " + string.Join(", ", undeclared) + ". " +
            "Run tools/sync-public-api.py for the package that owns them.");
    }

    [Fact]
    public void EveryDeclaredTypeStillExists()
    {
        var stale = new List<string>();

        foreach (string package in RepositoryLayout.Packages)
        {
            var present = PublicTypes(package)
                .Select(static type => type.FullName!.Replace('+', '.'))
                .ToHashSet(StringComparer.Ordinal);

            // A declaration nothing answers to is a rename or a removal that the files were
            // never told about, which is exactly the kind of drift they exist to prevent.
            stale.AddRange(
                DeclaredNames(package)
                    .Where(name => !present.Contains(name))
                    .Select(name => $"{package}: {name}"));
        }

        Assert.True(
            stale.Count == 0,
            "Declared API that no longer exists: " + string.Join(", ", stale) + ". " +
            "Run tools/sync-public-api.py for the package that owns them.");
    }

    private static string ApiFile(string package, string state) =>
        Path.Combine(RepositoryLayout.RepositoryRoot, "src", package, $"PublicAPI.{state}.txt");

    private static IEnumerable<Type> PublicTypes(string package) =>
        RepositoryLayout.LoadAssembly(package).GetExportedTypes();

    /// <summary>
    /// Reads the type names out of a package's declared API.
    /// </summary>
    /// <remarks>
    /// The analyzer gives every public type a line of its own, holding nothing but its name;
    /// every other line is a member and carries a signature. Reading only the former is what
    /// keeps this from having to parse a signature grammar it would only get subtly wrong.
    /// </remarks>
    private static IReadOnlySet<string> DeclaredNames(string package)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (string state in new[] { "Shipped", "Unshipped" })
        {
            string path = ApiFile(package, state);

            if (!File.Exists(path))
            {
                continue;
            }

            foreach (string line in File.ReadLines(path))
            {
                if (TypeNameOf(line) is { } name)
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    private static string? TypeNameOf(string line)
    {
        string text = line.Trim();

        // "#nullable enable" and the like are instructions to the analyzer, not declarations;
        // a signature belongs to a member of a type that has a line of its own further up.
        if (text.Length == 0
            || text.StartsWith('#')
            || text.Contains('(', StringComparison.Ordinal)
            || text.Contains(" -> ", StringComparison.Ordinal))
        {
            return null;
        }

        int generic = text.IndexOf('<', StringComparison.Ordinal);
        string name = generic < 0 ? text : text[..generic];

        return name.StartsWith("ArxisStudio.Markup", StringComparison.Ordinal) ? name : null;
    }
}
