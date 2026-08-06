using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Architecture.Tests;

/// <summary>
/// Guards the contract's "Out of scope" section: no project system, MSBuild evaluation,
/// NuGet management or C# compilation may enter the shipping packages.
/// </summary>
public sealed class ScopeBoundaryTests
{
    /// <summary>
    /// Package-id prefixes that would signal out-of-scope functionality creeping in.
    /// </summary>
    private static readonly string[] ForbiddenPackagePrefixes =
    [
        "Microsoft.Build",
        "Microsoft.CodeAnalysis.CSharp",
        "Microsoft.CodeAnalysis.Common",
        "Microsoft.CodeAnalysis.Workspaces",
        "NuGet.",
        "MSBuild",
    ];

    [Fact]
    public void Packages_DoNotReferenceProjectSystemOrCompilerInfrastructure()
    {
        var offenders = new List<string>();

        foreach (string package in RepositoryLayout.Packages)
        {
            offenders.AddRange(
                RepositoryLayout.PackageReferencesOf(package)
                    .Where(static id => ForbiddenPackagePrefixes.Any(
                        prefix => id.StartsWith(prefix, StringComparison.Ordinal)))
                    .Select(id => $"{package} -> {id}"));
        }

        Assert.True(
            offenders.Count == 0,
            "Out-of-scope dependencies found: " + string.Join(", ", offenders) + ". " +
            "The contract forbids MSBuild evaluation, NuGet management and C# compilation in these packages.");
    }

    [Fact]
    public void Packages_ShipXmlDocumentation()
    {
        foreach (string package in RepositoryLayout.Packages)
        {
            string documentationPath = System.IO.Path.ChangeExtension(
                RepositoryLayout.LoadAssembly(package).Location,
                ".xml");

            Assert.True(
                System.IO.File.Exists(documentationPath),
                $"'{package}' produced no XML documentation file. " +
                "The contract requires XML documentation on all public APIs.");
        }
    }
}
