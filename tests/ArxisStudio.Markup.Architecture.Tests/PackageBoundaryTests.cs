using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Architecture.Tests;

/// <summary>
/// The executable form of the contract's "Explicit non-responsibilities" and
/// "Terminology" sections, and of milestone 0's exit criterion that the package
/// dependency direction is enforced.
/// <para>
/// When one of these tests fails, the code is in the wrong package. Move the code;
/// never relax the test.
/// </para>
/// </summary>
public sealed class PackageBoundaryTests
{
    private const string AvaloniaPrefix = "Avalonia";

    /// <summary>
    /// The only permitted direction: Markup ← Markup.Xaml ← Markup.Xaml.Loader.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExpectedProjectReferences = new(StringComparer.Ordinal)
    {
        [RepositoryLayout.BasePackage] = [],
        [RepositoryLayout.SyntaxPackage] = [RepositoryLayout.BasePackage],
        [RepositoryLayout.LoaderPackage] = [RepositoryLayout.SyntaxPackage],
    };

    [Fact]
    public void DependencyDirection_MatchesTheContract()
    {
        foreach ((string package, string[] expected) in ExpectedProjectReferences)
        {
            IReadOnlySet<string> actual = RepositoryLayout.ProjectReferencesOf(package);

            Assert.Equal(
                expected.OrderBy(static name => name, StringComparer.Ordinal),
                actual.OrderBy(static name => name, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void DependencyGraph_IsAcyclic()
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (string package in RepositoryLayout.Packages)
        {
            Visit(package, visiting, visited);
        }

        static void Visit(string package, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(package))
            {
                return;
            }

            Assert.True(
                visiting.Add(package),
                $"Circular dependency detected while walking '{package}'. The contract forbids cycles.");

            foreach (string dependency in RepositoryLayout.ProjectReferencesOf(package))
            {
                Visit(dependency, visiting, visited);
            }

            visiting.Remove(package);
            visited.Add(package);
        }
    }

    /// <summary>
    /// "ArxisStudio.Markup.Xaml understands XAML syntax and document structure.
    /// It must not require Avalonia runtime assemblies."
    /// </summary>
    [Theory]
    [InlineData(RepositoryLayout.BasePackage)]
    [InlineData(RepositoryLayout.SyntaxPackage)]
    public void SyntaxLayer_DeclaresNoAvaloniaPackageReference(string package)
    {
        string[] avalonia = RepositoryLayout.PackageReferencesOf(package)
            .Where(static id => id.StartsWith(AvaloniaPrefix, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            avalonia.Length == 0,
            $"'{package}' declares Avalonia package references ({string.Join(", ", avalonia)}). " +
            "Avalonia may only be referenced by ArxisStudio.Markup.Xaml.Loader.");
    }

    /// <summary>
    /// Catches Avalonia reaching the syntax layer transitively, which the
    /// project-file check above cannot see.
    /// </summary>
    [Theory]
    [InlineData(RepositoryLayout.BasePackage)]
    [InlineData(RepositoryLayout.SyntaxPackage)]
    public void SyntaxLayer_CompilesWithoutAvaloniaAssemblies(string package)
    {
        string[] avalonia = RepositoryLayout.LoadAssembly(package)
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .Where(static name => name.StartsWith(AvaloniaPrefix, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            avalonia.Length == 0,
            $"The compiled '{package}' assembly references Avalonia ({string.Join(", ", avalonia)}). " +
            "Syntax and runtime semantics must remain separated.");
    }

    /// <summary>
    /// "The .axaml extension identifies Avalonia XAML files, but public types in this
    /// project should use Xaml, not Axaml."
    /// </summary>
    [Fact]
    public void PublicTypes_DoNotUseAxamlNaming()
    {
        string[] offenders = RepositoryLayout.Packages
            .SelectMany(static package => RepositoryLayout.LoadAssembly(package).GetExportedTypes())
            .Where(static type => UsesAxamlNaming(type.Name))
            .Select(static type => type.FullName ?? type.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Public types use 'Axaml' naming ({string.Join(", ", offenders)}). " +
            "Follow Avalonia's own terminology and use 'Xaml'.");
    }

    [Theory]
    [InlineData("AxamlDocument", true)]
    [InlineData("AXamlLoader", true)]
    [InlineData("AXAMLDocument", true)]
    [InlineData("XamlDocument", false)]
    [InlineData("AvaloniaXamlDispatcher", false)]
    [InlineData("AvaloniaXamlLoaderShim", false)]
    public void TheAxamlNamingRule_MatchesTheThreeSpellingsAndNothingElse(string name, bool expected)
    {
        // The rule has to be exact. A case-insensitive search matches the "aXaml" inside
        // "AvaloniaXaml", which is not what the contract forbids and would push perfectly good
        // names out of the codebase.
        Assert.Equal(expected, UsesAxamlNaming(name));
    }

    /// <summary>
    /// Decides whether a type name uses the naming the contract rules out.
    /// </summary>
    /// <remarks>
    /// Matched ordinally against the three spellings the contract names, so a lower-case
    /// <c>a</c> — the one that appears mid-word in <c>Avalonia</c> — is not a match.
    /// </remarks>
    private static bool UsesAxamlNaming(string name) =>
        name.Contains("Axaml", StringComparison.Ordinal)
        || name.Contains("AXaml", StringComparison.Ordinal)
        || name.Contains("AXAML", StringComparison.Ordinal);
}
