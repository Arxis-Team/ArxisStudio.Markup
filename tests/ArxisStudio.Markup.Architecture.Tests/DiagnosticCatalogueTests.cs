using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ArxisStudio.Markup.Architecture.Tests;

/// <summary>
/// Guards the diagnostic codes, which the contract makes a stable machine-readable interface.
/// </summary>
/// <remarks>
/// Callers suppress and route on codes, so a code that changes meaning, or two that share a
/// number, breaks something downstream silently. The ranges matter for the same reason: a code
/// says which package produced it before anybody reads the message.
/// </remarks>
public sealed class DiagnosticCatalogueTests
{
    /// <summary>The number range each package's codes must fall in, per the code classes' own remarks.</summary>
    private static readonly (string Package, string Type, int Low, int High)[] Ranges =
    [
        (RepositoryLayout.SyntaxPackage, "ArxisStudio.Markup.Xaml.XamlDiagnosticCodes", 1000, 1999),
        (RepositoryLayout.LoaderPackage, "ArxisStudio.Markup.Xaml.Loader.XamlLoaderDiagnosticCodes", 2000, 3999),
    ];

    /// <summary>
    /// The loader diagnostics the contract requires, by the name each is declared under.
    /// </summary>
    private static readonly string[] Required =
    [
        "UnresolvedType",
        "UnresolvedAssembly",
        "UnresolvedMember",
        "IncompatibleValue",
        "ReadOnlyMember",
        "UnresolvedResource",
        "IncludeCycle",
        "IncompatibleRootType",
        "MissingEventHandler",
        "InvalidRootFactoryResult",
        "TypeConverterFailure",
        "MarkupExtensionFailure",
        "ObjectCreationFailure",
        "InvalidThreadAccess",
    ];

    [Fact]
    public void EveryCodeIsUniqueAcrossThePackages()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var clashes = new List<string>();

        foreach ((string package, string type, _, _) in Ranges)
        {
            foreach ((string name, string code) in Codes(package, type))
            {
                if (!seen.TryAdd(code, name))
                {
                    clashes.Add($"{code} is both {seen[code]} and {name}");
                }
            }
        }

        Assert.True(
            clashes.Count == 0,
            "Diagnostic codes are a stable contract and cannot be shared: " + string.Join(", ", clashes));
    }

    [Fact]
    public void EveryCodeSitsInItsPackagesRange()
    {
        var strays = new List<string>();

        foreach ((string package, string type, int low, int high) in Ranges)
        {
            foreach ((string name, string code) in Codes(package, type))
            {
                Match match = Regex.Match(code, "^AXM([0-9]{4})$", RegexOptions.CultureInvariant);

                if (!match.Success)
                {
                    strays.Add($"{name} = '{code}' is not of the form AXMnnnn");

                    continue;
                }

                int number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

                if (number < low || number > high)
                {
                    strays.Add($"{name} = '{code}' is outside {low}-{high}, which {package} owns");
                }
            }
        }

        Assert.True(strays.Count == 0, string.Join(", ", strays));
    }

    [Fact]
    public void EveryCodeIsDocumented()
    {
        var undocumented = new List<string>();

        foreach ((string package, string type, _, _) in Ranges)
        {
            IReadOnlySet<string> documented = DocumentedMembers(package);

            undocumented.AddRange(
                Codes(package, type)
                    .Where(entry => !documented.Contains($"F:{type}.{entry.Name}"))
                    .Select(entry => $"{type}.{entry.Name}"));
        }

        // The message is what a person reads once; the documentation is what tells them what the
        // code means before they have ever seen one.
        Assert.True(
            undocumented.Count == 0,
            "Diagnostic codes with no documentation: " + string.Join(", ", undocumented));
    }

    [Fact]
    public void EveryDiagnosticTheContractRequiresExists()
    {
        IReadOnlySet<string> declared = Codes(RepositoryLayout.LoaderPackage, Ranges[1].Type)
            .Select(static entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

        string[] missing = [.. Required.Where(name => !declared.Contains(name))];

        Assert.True(
            missing.Length == 0,
            "The contract's runtime-diagnostics list names these and the loader declares no code for them: " +
            string.Join(", ", missing));
    }

    private static IEnumerable<(string Name, string Code)> Codes(string package, string typeName)
    {
        Type type = RepositoryLayout.LoadAssembly(package).GetType(typeName)
            ?? throw new InvalidOperationException($"'{package}' has no type '{typeName}'.");

        return type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field is { IsLiteral: true, FieldType: { } fieldType }
                && fieldType == typeof(string))
            .Select(static field => (field.Name, (string)field.GetRawConstantValue()!));
    }

    /// <summary>Reads the member names the package's XML documentation file covers.</summary>
    private static IReadOnlySet<string> DocumentedMembers(string package)
    {
        string path = Path.ChangeExtension(RepositoryLayout.LoadAssembly(package).Location, ".xml");

        return XDocument.Load(path)
            .Descendants("member")
            .Select(static member => member.Attribute("name")?.Value)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
