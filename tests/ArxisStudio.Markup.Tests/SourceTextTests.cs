using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace ArxisStudio.Markup.Tests;

public sealed class SourceTextTests
{
    private const string Sample = "<Grid>\r\n  <!-- keep me -->\r\n  <Button Width=\"320\" />\r\n</Grid>\r\n";

    [Fact]
    public void From_PreservesTheTextExactly()
    {
        SourceText text = SourceText.From(Sample);

        Assert.Equal(Sample, text.ToString());
        Assert.Equal(Sample.Length, text.Length);
        Assert.False(text.IsEmpty);
    }

    [Fact]
    public void GetText_ReturnsTheSpannedCharacters()
    {
        SourceText text = SourceText.From("<Button Width=\"320\" />");

        Assert.Equal("Width", text.GetText(new TextSpan(8, 5)));
        Assert.Equal(string.Empty, text.GetText(new TextSpan(8, 0)));
    }

    [Fact]
    public void GetText_RejectsASpanBeyondTheSnapshot()
    {
        SourceText text = SourceText.From("abc");

        Assert.Throws<ArgumentOutOfRangeException>(() => text.GetText(new TextSpan(2, 5)));
    }

    [Fact]
    public void WithChange_LeavesTheOriginalSnapshotUntouched()
    {
        SourceText original = SourceText.From("Width=\"320\"");
        SourceText edited = original.WithChange(new TextChange(new TextSpan(7, 3), "480"));

        Assert.Equal("Width=\"320\"", original.ToString());
        Assert.Equal("Width=\"480\"", edited.ToString());
    }

    [Fact]
    public void WithChanges_PreservesEverythingOutsideTheChangedSpans()
    {
        // This is requirement 2 of the contract in miniature: change one value, and every
        // comment, blank line and indent around it must come through untouched.
        SourceText text = SourceText.From(Sample);

        int start = Sample.IndexOf("320", StringComparison.Ordinal);
        SourceText edited = text.WithChange(new TextChange(new TextSpan(start, 3), "480"));

        Assert.Equal(Sample.Replace("320", "480", StringComparison.Ordinal), edited.ToString());
        Assert.Contains("<!-- keep me -->", edited.ToString(), StringComparison.Ordinal);
        Assert.Contains("\r\n", edited.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WithChanges_AppliesSeveralOrderedChanges()
    {
        SourceText text = SourceText.From("aaa bbb ccc");

        SourceText edited = text.WithChanges(
        [
            new TextChange(new TextSpan(0, 3), "xxx"),
            new TextChange(new TextSpan(8, 3), "zzz"),
        ]);

        Assert.Equal("xxx bbb zzz", edited.ToString());
    }

    [Fact]
    public void WithChanges_SupportsInsertionAndDeletion()
    {
        SourceText text = SourceText.From("ac");

        Assert.Equal("abc", text.WithChange(TextChange.Insert(1, "b")).ToString());
        Assert.Equal("c", text.WithChange(TextChange.Delete(new TextSpan(0, 1))).ToString());
    }

    [Fact]
    public void WithChanges_OnAnEmptyListReturnsTheSameSnapshot()
    {
        SourceText text = SourceText.From("abc");

        Assert.Same(text, text.WithChanges([]));
    }

    [Fact]
    public void WithChanges_RejectsUnorderedChanges()
    {
        SourceText text = SourceText.From("aaa bbb ccc");

        ArgumentException error = Assert.Throws<ArgumentException>(() => text.WithChanges(
        [
            new TextChange(new TextSpan(8, 3), "zzz"),
            new TextChange(new TextSpan(0, 3), "xxx"),
        ]));

        Assert.Equal("changes", error.ParamName);
    }

    [Fact]
    public void WithChanges_RejectsOverlappingChanges()
    {
        // Overlapping changes have no single correct interpretation. Guessing at one would
        // silently corrupt the document, so this is invalid API use, not a diagnostic.
        SourceText text = SourceText.From("aaa bbb ccc");

        Assert.Throws<ArgumentException>(() => text.WithChanges(
        [
            new TextChange(new TextSpan(0, 5), "x"),
            new TextChange(new TextSpan(3, 5), "y"),
        ]));
    }

    [Fact]
    public void WithChanges_RejectsAChangeBeyondTheSnapshot()
    {
        SourceText text = SourceText.From("abc");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => text.WithChanges([new TextChange(new TextSpan(2, 5), "x")]));
    }

    [Fact]
    public void WithChanges_RejectsANullList()
    {
        SourceText text = SourceText.From("abc");

        Assert.Throws<ArgumentNullException>(() => text.WithChanges(null!));
    }

    [Fact]
    public void EditedSnapshot_RemapsLinesToTheNewText()
    {
        SourceText text = SourceText.From("a\nb\nc");
        SourceText edited = text.WithChange(new TextChange(new TextSpan(1, 1), "\n\n"));

        Assert.Equal(3, text.Lines.Count);
        Assert.Equal(4, edited.Lines.Count);
    }

    [Fact]
    public void DefaultEncoding_IsUtf8WithoutAByteOrderMark()
    {
        SourceText text = SourceText.From("abc");

        Assert.Equal("utf-8", text.Encoding.WebName);
        Assert.False(text.HasByteOrderMark);
    }

    [Fact]
    public async Task FromAsync_DetectsAByteOrderMark()
    {
        // Whether the file had a mark decides whether writing it back reproduces the original
        // bytes, so it is recorded rather than inferred later.
        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        {
            await writer.WriteAsync("<Grid />".AsMemory(), TestContext.Current.CancellationToken);
        }

        stream.Position = 0;
        SourceText text = await SourceText.FromAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("<Grid />", text.ToString());
        Assert.True(text.HasByteOrderMark);
    }

    [Fact]
    public async Task FromAsync_WithoutAMarkRecordsItsAbsence()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<Grid />"));

        SourceText text = await SourceText.FromAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("<Grid />", text.ToString());
        Assert.False(text.HasByteOrderMark);
    }

    [Fact]
    public void EditingCarriesTheEncodingForward()
    {
        SourceText text = SourceText.From("abc", Encoding.Unicode, hasByteOrderMark: true);
        SourceText edited = text.WithChange(new TextChange(new TextSpan(0, 1), "x"));

        Assert.Equal(Encoding.Unicode.WebName, edited.Encoding.WebName);
        Assert.True(edited.HasByteOrderMark);
    }

    [Fact]
    public void Lines_AreSafeToReadFromManyThreadsAtOnce()
    {
        // Snapshots are immutable and the line table is computed lazily, so the only hazard is
        // the race to build it. Every racing reader must observe an equivalent result.
        SourceText text = SourceText.From(string.Join('\n', new string[500]));
        var results = new int[64];

        Parallel.For(0, results.Length, index => results[index] = text.Lines.Count);

        Assert.All(results, count => Assert.Equal(500, count));
    }

    [Fact]
    public void ConcurrentReadsOfASnapshotAgreeWhileOtherSnapshotsAreBeingProduced()
    {
        SourceText text = SourceText.From(Sample);
        string expected = text.ToString();
        var failures = new List<string>();

        Parallel.For(0, 128, index =>
        {
            // Producing new snapshots must not disturb the one being read.
            string replacement = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            SourceText derived = text.WithChange(new TextChange(new TextSpan(0, 1), replacement));
            string observed = text.ToString();

            if (!string.Equals(observed, expected, StringComparison.Ordinal)
                || derived.Length != expected.Length - 1 + replacement.Length)
            {
                lock (failures)
                {
                    failures.Add(observed);
                }
            }
        });

        Assert.Empty(failures);
    }
}
