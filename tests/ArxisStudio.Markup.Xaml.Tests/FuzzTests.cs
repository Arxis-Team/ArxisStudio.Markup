using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// The other exit criterion of this milestone: the parser terminates on invalid input.
/// </summary>
/// <remarks>
/// A lexer that fails to advance loops forever, and a recursive-descent parser that recurses
/// without consuming overflows the stack. Both are reachable from input a user can type, so
/// neither can be left to chance.
/// </remarks>
public sealed class FuzzTests
{
    /// <summary>Characters that carry syntactic weight, so the generator produces real edge cases.</summary>
    private const string Alphabet = "<>/=\"'&;:!?-[]xX aA\t\r\n{}.#_é\u2028";

    private static string Generate(Random random, int maxLength)
    {
        var builder = new StringBuilder();
        int length = random.Next(maxLength);

        for (var index = 0; index < length; index++)
        {
            builder.Append(Alphabet[random.Next(Alphabet.Length)]);
        }

        return builder.ToString();
    }

    [Fact]
    public void RandomInputTerminatesAndRoundTrips()
    {
        var random = new Random(20260731);
        var failures = new List<string>();

        for (var iteration = 0; iteration < 20_000; iteration++)
        {
            string source = Generate(random, 120);

            try
            {
                XamlDocument document = XamlDocument.Parse(source);

                // Termination is necessary but not sufficient: whatever the parser made of the
                // input, every character of it has to still be there.
                if (!string.Equals(document.GetText(), source, StringComparison.Ordinal))
                {
                    failures.Add($"round-trip: {Describe(source)}");
                }
            }
            catch (Exception error)
            {
                failures.Add($"{error.GetType().Name}: {Describe(source)}");
            }

            if (failures.Count > 5)
            {
                break;
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void FragmentsOfRealDocumentsTerminateAndRoundTrip()
    {
        // Truncation is what a half-typed document actually looks like, and it reaches states
        // uniformly random text almost never does.
        var failures = new List<string>();

        foreach (string name in Fixtures.Names)
        {
            string full = Fixtures.Read(name).ToString();

            for (var length = 0; length <= full.Length; length++)
            {
                string prefix = full[..length];

                try
                {
                    if (!string.Equals(XamlDocument.Parse(prefix).GetText(), prefix, StringComparison.Ordinal))
                    {
                        failures.Add($"round-trip: {name} truncated to {length}");
                    }
                }
                catch (Exception error)
                {
                    failures.Add($"{error.GetType().Name}: {name} truncated to {length}");
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void SingleCharacterMutationsOfRealDocumentsTerminate()
    {
        var random = new Random(42);
        var failures = new List<string>();

        foreach (string name in Fixtures.Names)
        {
            char[] characters = Fixtures.Read(name).ToString().ToCharArray();

            for (var attempt = 0; attempt < 200 && characters.Length > 0; attempt++)
            {
                char[] mutated = (char[])characters.Clone();
                mutated[random.Next(mutated.Length)] = Alphabet[random.Next(Alphabet.Length)];

                var source = new string(mutated);

                try
                {
                    if (!string.Equals(XamlDocument.Parse(source).GetText(), source, StringComparison.Ordinal))
                    {
                        failures.Add($"round-trip: mutated {name}");
                    }
                }
                catch (Exception error)
                {
                    failures.Add($"{error.GetType().Name}: mutated {name}");
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task DeeplyNestedInputDoesNotOverflowTheStack()
    {
        // Recursive descent has a real depth limit. This documents where it currently is, so a
        // regression that makes the parser far more stack-hungry is caught rather than shipped.
        var source = string.Concat(Enumerable.Repeat("<a>", 500)) + string.Concat(Enumerable.Repeat("</a>", 500));

        // Run on a thread with a known stack so the result does not depend on the host's.
        string? error = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    XamlDocument document = XamlDocument.Parse(source);

                    if (!string.Equals(document.GetText(), source, StringComparison.Ordinal))
                    {
                        error = "the document did not round-trip";
                    }
                }
                catch (Exception exception)
                {
                    error = exception.GetType().Name;
                }
            },
            maxStackSize: 1024 * 1024);

        thread.Start();

        await Task.Run(thread.Join, TestContext.Current.CancellationToken);

        Assert.Null(error);
    }

    [Fact]
    public void UnbalancedTagsAtDepthTerminate()
    {
        // Every open tag looking for a close that never comes: the worst case for the
        // "does this end tag belong to me" walk.
        var source = string.Concat(Enumerable.Repeat("<a>", 300)) + "</zzz>";

        Assert.Equal(source, XamlDocument.Parse(source).GetText());
    }

    private static string Describe(string source) =>
        string.Concat(source.Select(static c => c switch
        {
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            _ => c.ToString(),
        }));
}
