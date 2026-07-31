using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// Access to the golden <c>.axaml</c> files.
/// </summary>
/// <remarks>
/// Read as bytes and decoded here rather than through <see cref="File.ReadAllText(string)"/>,
/// because what these files are testing is their exact bytes: their line endings, their
/// trailing whitespace, whether they end with a newline and whether they carry a byte-order
/// mark. Any convenience API that normalises one of those would hide the very regression the
/// fixture exists to catch.
/// </remarks>
internal static class Fixtures
{
    public static string Directory { get; } = Path.Combine(
        Path.GetDirectoryName(typeof(Fixtures).Assembly.Location)!,
        "Fixtures");

    /// <summary>Gets every fixture's file name.</summary>
    public static IEnumerable<string> Names =>
        System.IO.Directory.EnumerateFiles(Directory, "*.axaml")
            .Select(Path.GetFileName)
            .Select(static name => name!)
            .OrderBy(static name => name, StringComparer.Ordinal);

    /// <summary>Gets a fixture's raw bytes.</summary>
    public static byte[] ReadBytes(string name) => File.ReadAllBytes(Path.Combine(Directory, name));

    /// <summary>
    /// Reads a fixture as a snapshot, detecting its encoding and byte-order mark the same way
    /// a real caller would.
    /// </summary>
    public static SourceText Read(string name)
    {
        using var stream = new MemoryStream(ReadBytes(name));

        return SourceText.FromAsync(stream).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Parses a fixture.</summary>
    public static XamlDocument Parse(string name) =>
        XamlDocument.Parse(Read(name), new XamlParseOptions { DocumentUri = UriOf(name) });

    public static Uri UriOf(string name) => new(Path.Combine(Directory, name));
}
