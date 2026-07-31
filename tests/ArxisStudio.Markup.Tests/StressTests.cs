using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace ArxisStudio.Markup.Tests;

/// <summary>
/// The claims the contract makes about snapshots holding under load rather than in principle.
/// </summary>
/// <remarks>
/// A snapshot never changes, so any number of threads may read one without synchronisation and
/// a span taken against it stays valid. Both are properties nothing else here would notice the
/// loss of until something intermittent went wrong somewhere unrelated.
/// </remarks>
public sealed class StressTests
{
    private static SourceText Large(int lines)
    {
        var text = new StringBuilder();

        for (int index = 0; index < lines; index++)
        {
            text.Append("  <Row Index=\"").Append(index).Append("\" Label=\"Row ").Append(index).Append("\" />\r\n");
        }

        return SourceText.From(text.ToString());
    }

    [Fact]
    public async Task ManyThreadsReadOneSnapshotAndAgree()
    {
        SourceText text = Large(5_000);
        var answers = new ConcurrentBag<(int Lines, int Length, string Middle)>();

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            TextLineCollection lines = text.Lines;

            answers.Add((lines.Count, text.Length, lines[lines.Count / 2].ToString()));
        })));

        // The line table is computed on first access and cached; a race there would give two
        // readers different answers about the same immutable text.
        Assert.Single(answers.Distinct());
        Assert.Equal(32, answers.Count);
    }

    [Fact]
    public void PositionsRoundTripAcrossEveryLineOfALargeSnapshot()
    {
        SourceText text = Large(2_000);
        TextLineCollection lines = text.Lines;

        for (int line = 0; line < lines.Count; line++)
        {
            TextLine current = lines[line];
            TextPosition position = lines.GetPosition(current.Start);

            Assert.Equal(line, position.Line);
            Assert.Equal(0, position.Column);
            Assert.Equal(current.Start, lines.GetOffset(position));
        }
    }

    [Fact]
    public void ThousandsOfSequentialChangesLeaveTheTextExact()
    {
        SourceText text = SourceText.From(string.Empty);
        var expected = new StringBuilder();

        // Each change is applied to the snapshot the last one produced, which is the shape an
        // editor's history has and the one where an off-by-one compounds instead of showing up.
        for (int index = 0; index < 2_000; index++)
        {
            string addition = index + ";";

            text = text.WithChange(new TextChange(new TextSpan(text.Length, 0), addition));
            expected.Append(addition);
        }

        Assert.Equal(expected.ToString(), text.ToString());
        Assert.Equal(expected.Length, text.Length);
    }

    [Fact]
    public void ASnapshotIsUnaffectedByChangesMadeAfterIt()
    {
        SourceText original = Large(500);
        string before = original.ToString();

        SourceText changed = original.WithChange(new TextChange(new TextSpan(0, 0), "<!-- added -->\r\n"));

        Assert.Equal(before, original.ToString());
        Assert.NotEqual(before, changed.ToString());
        Assert.Equal(original.Lines.Count + 1, changed.Lines.Count);
    }
}
