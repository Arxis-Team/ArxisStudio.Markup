using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ArxisStudio.Markup.Tests;

public sealed class MarkupSourceProviderTests
{
    private static readonly Uri Uri = new("file:///Themes/Colors.axaml");

    [Fact]
    public async Task InMemoryProvider_ReturnsWhatItHolds()
    {
        var provider = new InMemoryMarkupSourceProvider();
        provider.Update(Uri, "<ResourceDictionary />");

        MarkupSource? source = await provider.TryGetSourceAsync(Uri, TestContext.Current.CancellationToken);

        Assert.NotNull(source);
        Assert.Equal(Uri, source.Uri);
        Assert.Equal("<ResourceDictionary />", (await source.GetTextAsync(TestContext.Current.CancellationToken)).ToString());
    }

    [Fact]
    public async Task Provider_ReturnsNullForAUriItDoesNotKnow()
    {
        // Not knowing a URI is an ordinary answer. Only a caller that required the document
        // gets to decide that its absence is an error.
        var provider = new InMemoryMarkupSourceProvider();

        Assert.Null(await provider.TryGetSourceAsync(Uri, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InMemoryProvider_ReplacesAndRemovesEntries()
    {
        var provider = new InMemoryMarkupSourceProvider();

        provider.Update(Uri, "first");
        provider.Update(Uri, "second");

        MarkupSource? source = await provider.TryGetSourceAsync(Uri, TestContext.Current.CancellationToken);
        Assert.Equal("second", (await source!.GetTextAsync(TestContext.Current.CancellationToken)).ToString());

        Assert.True(provider.Contains(Uri));
        Assert.True(provider.Remove(Uri));
        Assert.False(provider.Contains(Uri));
        Assert.Null(await provider.TryGetSourceAsync(Uri, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompositeProvider_HonoursItsOrdering()
    {
        // The precedence rule the contract calls out: an unsaved in-memory document overrides
        // the file of the same URI.
        var unsaved = new InMemoryMarkupSourceProvider();
        var onDisk = new InMemoryMarkupSourceProvider();

        unsaved.Update(Uri, "unsaved edit");
        onDisk.Update(Uri, "committed to disk");

        var composite = new CompositeMarkupSourceProvider(unsaved, onDisk);
        MarkupSource? source = await composite.TryGetSourceAsync(Uri, TestContext.Current.CancellationToken);

        Assert.Equal("unsaved edit", (await source!.GetTextAsync(TestContext.Current.CancellationToken)).ToString());
    }

    [Fact]
    public async Task CompositeProvider_FallsThroughToLaterProviders()
    {
        var empty = new InMemoryMarkupSourceProvider();
        var fallback = new InMemoryMarkupSourceProvider();
        fallback.Update(Uri, "from the fallback");

        var composite = new CompositeMarkupSourceProvider(empty, fallback);
        MarkupSource? source = await composite.TryGetSourceAsync(Uri, TestContext.Current.CancellationToken);

        Assert.Equal("from the fallback", (await source!.GetTextAsync(TestContext.Current.CancellationToken)).ToString());
    }

    [Fact]
    public async Task CompositeProvider_ReturnsNullWhenNoProviderKnowsTheUri()
    {
        var composite = new CompositeMarkupSourceProvider(
            new InMemoryMarkupSourceProvider(),
            new InMemoryMarkupSourceProvider());

        Assert.Null(await composite.TryGetSourceAsync(Uri, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemovingAnInMemoryOverride_RestoresTheProviderBehindIt()
    {
        var unsaved = new InMemoryMarkupSourceProvider();
        var onDisk = new InMemoryMarkupSourceProvider();

        unsaved.Update(Uri, "unsaved edit");
        onDisk.Update(Uri, "committed to disk");

        var composite = new CompositeMarkupSourceProvider(unsaved, onDisk);
        unsaved.Remove(Uri);

        MarkupSource? source = await composite.TryGetSourceAsync(Uri, TestContext.Current.CancellationToken);

        Assert.Equal("committed to disk", (await source!.GetTextAsync(TestContext.Current.CancellationToken)).ToString());
    }

    [Fact]
    public void CompositeProvider_RejectsNullProviders()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeMarkupSourceProvider((IMarkupSourceProvider[])null!));
        Assert.Throws<ArgumentNullException>(() => new CompositeMarkupSourceProvider(new InMemoryMarkupSourceProvider(), null!));
    }

    [Fact]
    public async Task FileProvider_ReadsAnExistingFileAndIgnoresAMissingOne()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".axaml");
        await File.WriteAllTextAsync(path, "<Styles />", TestContext.Current.CancellationToken);

        try
        {
            var provider = new FileMarkupSourceProvider();
            var uri = new Uri(path);

            MarkupSource? source = await provider.TryGetSourceAsync(uri, TestContext.Current.CancellationToken);
            Assert.NotNull(source);
            Assert.Equal("<Styles />", (await source.GetTextAsync(TestContext.Current.CancellationToken)).ToString());

            var missing = new Uri(path + ".missing");
            Assert.Null(await provider.TryGetSourceAsync(missing, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FileProvider_IgnoresNonFileUris()
    {
        var provider = new FileMarkupSourceProvider();

        Assert.Null(await provider.TryGetSourceAsync(
            new Uri("avares://Controls/Themes/Generic.axaml"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void FileSource_RejectsANonFileUri()
    {
        Assert.Throws<ArgumentException>(
            () => new FileMarkupSource(new Uri("avares://Controls/Themes/Generic.axaml")));
    }

    [Fact]
    public async Task StreamSource_ReadsOnceAndCachesTheResult()
    {
        // A stream generally cannot be rewound, so the second read must return the captured
        // snapshot rather than an empty one.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<Styles />"));
        using var source = new StreamMarkupSource(new Uri("avares://Controls/Styles.axaml"), stream);

        SourceText first = await source.GetTextAsync(TestContext.Current.CancellationToken);
        SourceText second = await source.GetTextAsync(TestContext.Current.CancellationToken);

        Assert.Equal("<Styles />", first.ToString());
        Assert.Same(first, second);
    }

    [Fact]
    public async Task CancellationIsObserved()
    {
        var provider = new InMemoryMarkupSourceProvider();
        provider.Update(Uri, "<Grid />");

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await provider.TryGetSourceAsync(Uri, cancellation.Token));
    }
}
