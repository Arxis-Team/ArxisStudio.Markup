using System;
using Xunit;

namespace ArxisStudio.Markup.Tests;

public sealed class MarkupDocumentTests
{
    private static readonly Uri Uri = new("file:///Views/MainView.axaml");

    private static MarkupDocument CreateDocument(string text = "<Grid />") =>
        new(MarkupDocumentId.New(), Uri, SourceText.From(text));

    [Fact]
    public void NewDocument_StartsAtTheInitialVersion()
    {
        MarkupDocument document = CreateDocument();

        Assert.Equal(DocumentVersion.Initial, document.Version);
        Assert.Equal(0, document.Version.Value);
    }

    [Fact]
    public void WithText_KeepsIdentityAndAdvancesTheVersion()
    {
        // "Document identity must not change merely because a new text snapshot is created."
        MarkupDocument original = CreateDocument();
        MarkupDocument edited = original.WithText(SourceText.From("<Border />"));

        Assert.Equal(original.Id, edited.Id);
        Assert.Equal(original.Uri, edited.Uri);
        Assert.Equal(original.Version.Next(), edited.Version);
        Assert.Equal("<Grid />", original.Text.ToString());
        Assert.Equal("<Border />", edited.Text.ToString());
    }

    [Fact]
    public void WithText_OnTheSameSnapshotConsumesNoVersion()
    {
        MarkupDocument document = CreateDocument();

        Assert.Same(document, document.WithText(document.Text));
    }

    [Fact]
    public void Versions_IncreaseMonotonicallyAcrossManyEdits()
    {
        MarkupDocument document = CreateDocument("0");

        for (int index = 1; index <= 25; index++)
        {
            MarkupDocument next = document.WithChanges(
                [new TextChange(new TextSpan(0, document.Text.Length), index.ToString(System.Globalization.CultureInfo.InvariantCulture))]);

            Assert.True(next.Version > document.Version);
            Assert.Equal(document.Id, next.Id);
            document = next;
        }

        Assert.Equal(25, document.Version.Value);
    }

    [Fact]
    public void WithChanges_ProducesTheEditedText()
    {
        MarkupDocument document = CreateDocument("Width=\"320\"");

        MarkupDocument edited = document.WithChanges([new TextChange(new TextSpan(7, 3), "480")]);

        Assert.Equal("Width=\"480\"", edited.Text.ToString());
    }

    [Fact]
    public void WithUri_KeepsIdentity()
    {
        MarkupDocument document = CreateDocument();
        var moved = new Uri("file:///Views/Renamed.axaml");

        MarkupDocument result = document.WithUri(moved);

        Assert.Equal(document.Id, result.Id);
        Assert.Equal(moved, result.Uri);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        MarkupDocument document = CreateDocument();

        Assert.Throws<ArgumentNullException>(() => new MarkupDocument(MarkupDocumentId.New(), null!, SourceText.From("x")));
        Assert.Throws<ArgumentNullException>(() => new MarkupDocument(MarkupDocumentId.New(), Uri, null!));
        Assert.Throws<ArgumentNullException>(() => document.WithText(null!));
        Assert.Throws<ArgumentNullException>(() => document.WithChanges(null!));
    }

    [Fact]
    public void DocumentIds_AreUniqueAndCompareByValue()
    {
        MarkupDocumentId first = MarkupDocumentId.New();
        MarkupDocumentId second = MarkupDocumentId.New();

        Assert.NotEqual(first, second);
        Assert.Equal(first, new MarkupDocumentId(first.Value));
    }

    [Fact]
    public void DocumentVersions_CompareByAge()
    {
        DocumentVersion first = DocumentVersion.Initial;
        DocumentVersion second = first.Next();

        Assert.True(first < second);
        Assert.True(second > first);
        Assert.True(first <= DocumentVersion.Initial);
        Assert.True(first >= DocumentVersion.Initial);
        Assert.Equal(-1, first.CompareTo(second));
    }
}
