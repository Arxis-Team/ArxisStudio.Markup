using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// An image or an icon written as text is read through the environment, against the document, by the
/// session — at a load, in place on an update, and again for a rebuilt part (ADR 0031).
/// </summary>
/// <remarks>
/// Avalonia's own loader read these through its asset loader, which knows the resources of the
/// assemblies the process loaded: an image the last build lacked failed the whole document, and the
/// environment's resolvers were never asked.
/// </remarks>
public sealed class ResourceValueTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";

    private static readonly Uri ViewUri = new("avares://Probe/Views/View.axaml");

    /// <summary>Resources by the absolute URI they are found at, recording every URI it is asked for.</summary>
    private sealed class ResourceFiles : IXamlResourceResolver
    {
        private readonly Dictionary<string, byte[]?> _files = new(StringComparer.Ordinal);

        public List<string> Asked { get; } = [];

        public ResourceFiles With(string uri, byte[]? bytes)
        {
            _files[new Uri(uri).AbsoluteUri] = bytes;

            return this;
        }

        public ValueTask<XamlResource?> ResolveAsync(Uri resourceUri, Uri? baseUri, CancellationToken cancellationToken)
        {
            Uri target = resourceUri.IsAbsoluteUri || baseUri is null ? resourceUri : new Uri(baseUri, resourceUri);

            Asked.Add(target.AbsoluteUri);

            if (!_files.TryGetValue(target.AbsoluteUri, out byte[]? bytes))
            {
                return new ValueTask<XamlResource?>((XamlResource?)null);
            }

            return new ValueTask<XamlResource?>(new XamlResource(
                target,
                _ => bytes is null
                    ? throw new IOException("The file is held by another process.")
                    : new ValueTask<Stream>(new MemoryStream(bytes))));
        }
    }

    private static XamlLoadEnvironment Environment(ResourceFiles files, params System.Reflection.Assembly[] assemblies)
    {
        XamlLoadEnvironment defaults = XamlLoadEnvironment.CreateDefault(assemblies);

        return new XamlLoadEnvironment
        {
            SourceProvider = defaults.SourceProvider,
            AssemblyResolver = defaults.AssemblyResolver,
            TypeResolver = defaults.TypeResolver,
            ResourceResolver = new CompositeResourceResolver(files, defaults.ResourceResolver),
        };
    }

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri });

    private static string Panel(string source, string sibling = "") =>
        $"<StackPanel xmlns=\"{AvaloniaNamespace}\">\n" +
        "  <TextBlock Text=\"caption\" />\n" +
        $"  <Image Source=\"{source}\" />{sibling}\n" +
        "</StackPanel>";

    private static async Task<(XamlLoadSession Session, XamlLoadResult Result)> LoadAsync(
        string xaml, XamlLoadEnvironment environment)
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse(xaml), environment, new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

        Assert.True(session is not null, "The document produced no object: " + string.Join(" | ", result.Diagnostics));

        return (session, result);
    }

    private static Image Shown(XamlLoadSession session) =>
        session.GetRoot<StackPanel>().Children.OfType<Image>().Single();

    private static string Describe(XamlUpdateResult result) =>
        $"{result.Outcome} / {result.Strategy}: " + string.Join(" | ", result.Diagnostics);

    [AvaloniaFact]
    public async Task ARelativeImageIsReadThroughTheEnvironmentAgainstTheDocument()
    {
        ResourceFiles files = new ResourceFiles().With("avares://Probe/Assets/dot.png", [1, 2, 3]);

        (XamlLoadSession session, XamlLoadResult result) = await LoadAsync(Panel("../Assets/dot.png"), Environment(files));

        await using (session)
        {
            Assert.Empty(result.Diagnostics);
            Assert.IsType<Bitmap>(Shown(session).Source);
            Assert.Equal([new Uri("avares://Probe/Assets/dot.png").AbsoluteUri], files.Asked);
        }
    }

    [AvaloniaFact]
    public async Task AnImageNamedFromTheAssemblyRootOrAbsolutelyIsReadToo()
    {
        ResourceFiles files = new ResourceFiles()
            .With("avares://Probe/Assets/dot.png", [1, 2, 3])
            .With("avares://Other/Images/logo.png", [4, 5, 6]);
        XamlLoadEnvironment environment = Environment(files);

        (XamlLoadSession rooted, _) = await LoadAsync(Panel("/Assets/dot.png"), environment);
        (XamlLoadSession absolute, _) = await LoadAsync(Panel("avares://Other/Images/logo.png"), environment);

        await using (rooted)
        await using (absolute)
        {
            Assert.IsType<Bitmap>(Shown(rooted).Source);
            Assert.IsType<Bitmap>(Shown(absolute).Source);
        }
    }

    [AvaloniaFact]
    public async Task AWindowsIconIsReadAsAnIcon()
    {
        ResourceFiles files = new ResourceFiles().With("avares://Probe/Assets/app.ico", [0, 0, 1, 0]);

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse($"<Window xmlns=\"{AvaloniaNamespace}\" Icon=\"/Assets/app.ico\" />"),
            Environment(files),
            new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

        Assert.True(session is not null, string.Join(" | ", result.Diagnostics));

        await using (session)
        {
            var window = session.GetRoot<Window>();

            try
            {
                Assert.IsType<WindowIcon>(window.Icon);
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public async Task AnImageNobodyFindsIsAWarningAndTheDocumentStands()
    {
        (XamlLoadSession session, XamlLoadResult result) = await LoadAsync(
            Panel("/Assets/missing.png"), Environment(new ResourceFiles()));

        await using (session)
        {
            MarkupDiagnostic missing = Assert.Single(
                result.Diagnostics, diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.UnresolvedResource);

            Assert.Equal(MarkupDiagnosticSeverity.Warning, missing.Severity);
            Assert.Equal("/Assets/missing.png", session.Document.SourceText.GetText(missing.Span!.Value));
            Assert.Null(Shown(session).Source);
            Assert.Single(session.GetRoot<StackPanel>().Children.OfType<TextBlock>());
        }
    }

    [AvaloniaFact]
    public async Task AnImageThatCannotBeReadIsSaidSoAndTheDocumentStands()
    {
        (XamlLoadSession session, XamlLoadResult result) = await LoadAsync(
            Panel("/Assets/held.png"), Environment(new ResourceFiles().With("avares://Probe/Assets/held.png", null)));

        await using (session)
        {
            MarkupDiagnostic unread = Assert.Single(
                result.Diagnostics, diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.UnreadableResourceValue);

            Assert.Equal(MarkupDiagnosticSeverity.Warning, unread.Severity);
            Assert.Contains("held by another process", unread.Message, StringComparison.Ordinal);
            Assert.Null(Shown(session).Source);
        }
    }

    [AvaloniaFact]
    public async Task AnImageWrittenAnewIsReadAndSetWhereItStands()
    {
        ResourceFiles files = new ResourceFiles()
            .With("avares://Probe/Assets/dot.png", [1, 2, 3])
            .With("avares://Probe/Assets/other.png", [4, 5, 6]);

        (XamlLoadSession session, _) = await LoadAsync(Panel("/Assets/dot.png"), Environment(files));

        await using (session)
        {
            Image image = Shown(session);
            object? before = image.Source;

            XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
                Parse(Panel("/Assets/other.png")), TestContext.Current.CancellationToken);

            Assert.True(result.Applied, Describe(result));
            Assert.Equal(XamlUpdateStrategy.SetProperty, result.Strategy);
            Assert.Same(image, Shown(session));
            Assert.IsType<Bitmap>(image.Source);
            Assert.NotSame(before, image.Source);
        }
    }

    [AvaloniaFact]
    public async Task AnImageWrittenAnewThatNobodyFindsRefusesTheUpdate()
    {
        ResourceFiles files = new ResourceFiles().With("avares://Probe/Assets/dot.png", [1, 2, 3]);

        (XamlLoadSession session, _) = await LoadAsync(Panel("/Assets/dot.png"), Environment(files));

        await using (session)
        {
            Image image = Shown(session);
            object? before = image.Source;

            XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
                Parse(Panel("/Assets/typo.png")), TestContext.Current.CancellationToken);

            Assert.Equal(XamlUpdateOutcome.RejectedCleanly, result.Outcome);
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.UnresolvedResource && diagnostic.IsError);
            Assert.Same(before, image.Source);
        }
    }

    [AvaloniaFact]
    public async Task AnImageInARebuiltPartIsReadForTheRebuiltObject()
    {
        ResourceFiles files = new ResourceFiles().With("avares://Probe/Assets/dot.png", [1, 2, 3]);

        string Xaml(string sibling) =>
            $"<UserControl xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <StackPanel>\n" +
            "    <Image Source=\"/Assets/dot.png\" />\n" +
            $"    {sibling}\n" +
            "  </StackPanel>\n" +
            "</UserControl>";

        (XamlLoadSession session, _) = await LoadAsync(Xaml(string.Empty), Environment(files));

        await using (session)
        {
            Image before = ((StackPanel)session.GetRoot<UserControl>().Content!).Children.OfType<Image>().Single();

            XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
                Parse(Xaml("<Border><TextBlock Text=\"added\" /></Border>")), TestContext.Current.CancellationToken);

            Assert.True(result.Applied, Describe(result));

            Image after = ((StackPanel)session.GetRoot<UserControl>().Content!).Children.OfType<Image>().Single();

            Assert.NotSame(before, after);
            Assert.IsType<Bitmap>(after.Source);
        }
    }

    [AvaloniaFact]
    public async Task TheSameBytesAreDecodedOnceAndChangedBytesAgain()
    {
        ResourceFiles files = new ResourceFiles().With("avares://Probe/Assets/dot.png", [1, 2, 3]);
        XamlLoadEnvironment environment = Environment(files);

        (XamlLoadSession first, _) = await LoadAsync(Panel("/Assets/dot.png"), environment);
        (XamlLoadSession second, _) = await LoadAsync(Panel("/Assets/dot.png"), environment);

        files.With("avares://Probe/Assets/dot.png", [7, 8, 9]);

        (XamlLoadSession third, _) = await LoadAsync(Panel("/Assets/dot.png"), environment);

        await using (first)
        await using (second)
        await using (third)
        {
            Assert.Same(Shown(first).Source, Shown(second).Source);
            Assert.NotSame(Shown(first).Source, Shown(third).Source);
        }
    }

    [AvaloniaFact]
    public async Task AnExpressionIsLeftToAvalonia()
    {
        var files = new ResourceFiles();

        (XamlLoadSession session, XamlLoadResult result) = await LoadAsync(Panel("{x:Null}").Replace(
            "<StackPanel ", "<StackPanel xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" ", StringComparison.Ordinal),
            Environment(files));

        await using (session)
        {
            Assert.Empty(result.Diagnostics);
            Assert.Empty(files.Asked);
            Assert.Null(Shown(session).Source);
        }
    }

    [AvaloniaFact]
    public async Task AMemberSaysItReadsAResourceAndConversionLeavesItToALoad()
    {
        ResourceFiles files = new ResourceFiles().With("avares://Probe/Assets/dot.png", [1, 2, 3]);

        (XamlLoadSession session, _) = await LoadAsync(Panel("/Assets/dot.png"), Environment(files));

        await using (session)
        {
            XamlMemberDescriptor source = session.GetMember(Shown(session), "Source");
            XamlMemberDescriptor text = session.GetMember(session.GetRoot<StackPanel>().Children[0], "Text");

            Assert.True(source.ReadsResource);
            Assert.False(text.ReadsResource);

            XamlValueConversionResult converted = source.ConvertFromText("/Assets/dot.png");

            Assert.False(converted.Succeeded);
            Assert.Contains("resource", converted.Error, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An image the session could not set — a brush that is a property's value, an image in a template — is left
    /// to Avalonia's loader, as it was before ADR 0031, rather than withheld from the text and never set.
    /// </summary>
    /// <remarks>
    /// The object map pairs what the logical tree, the resources and the styles reach. A brush written in a
    /// property element and a template's content are reached by none of them, so the value read for them had
    /// no object to go to, and the image vanished without a word.
    /// </remarks>
    [AvaloniaFact]
    public async Task AnImageTheSessionCannotReachIsLeftToAvalonia()
    {
        string xaml =
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\">\n" +
            "  <Border Width=\"8\" Height=\"8\">\n" +
            "    <Border.Background>\n" +
            $"      <ImageBrush Source=\"{BuiltDot}\" />\n" +
            "    </Border.Background>\n" +
            "  </Border>\n" +
            "  <ContentControl Content=\"x\">\n" +
            "    <ContentControl.ContentTemplate>\n" +
            "      <DataTemplate>\n" +
            $"        <Image Source=\"{BuiltDot}\" />\n" +
            "      </DataTemplate>\n" +
            "    </ContentControl.ContentTemplate>\n" +
            "  </ContentControl>\n" +
            "</StackPanel>";

        var files = new ResourceFiles();

        (XamlLoadSession session, XamlLoadResult result) = await LoadAsync(xaml, Environment(files));

        await using (session)
        {
            Assert.Empty(result.Diagnostics);
            Assert.Empty(files.Asked);

            StackPanel root = session.GetRoot<StackPanel>();
            var brush = Assert.IsType<ImageBrush>(root.Children.OfType<Border>().Single().Background);
            var template = Assert.IsAssignableFrom<IDataTemplate>(root.Children.OfType<ContentControl>().Single().ContentTemplate);

            Assert.NotNull(brush.Source);
            Assert.NotNull(Assert.IsType<Image>(template.Build("x")).Source);
        }
    }

    /// <summary>
    /// An image the environment's resolvers do not know is asked of Avalonia's asset loader, which knows the
    /// resources built into the assemblies the process has loaded — a package's images among them.
    /// </summary>
    /// <remarks>
    /// Before ADR 0031 that loader was the only one asked, and an image a package carries loaded. A host whose
    /// resolvers know only its own projects' files then lost every such image to <c>AXM2005</c>.
    /// </remarks>
    [AvaloniaFact]
    public async Task AnImageTheEnvironmentDoesNotKnowIsAskedOfAvaloniasAssetLoader()
    {
        (XamlLoadSession session, XamlLoadResult result) = await LoadAsync(Panel(BuiltDot), Environment(new ResourceFiles()));

        await using (session)
        {
            Assert.Empty(result.Diagnostics);
            Assert.IsType<Bitmap>(Shown(session).Source);
        }
    }

    /// <summary>
    /// A setter that throws while an image is set through reflection is a warning about that attribute, not an
    /// exception out of the load.
    /// </summary>
    /// <remarks>
    /// A CLR property is written by reflection, which wraps what its setter threw, and the filter named only the
    /// exceptions a setter raises itself: the load threw instead of saying which attribute went wrong.
    /// </remarks>
    [AvaloniaFact]
    public async Task ASetterThatThrowsThroughReflectionIsAWarning()
    {
        ResourceFiles files = new ResourceFiles().With("avares://Probe/Assets/dot.png", [1, 2, 3]);
        string xaml =
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\" xmlns:c=\"using:ArxisStudio.Markup.Xaml.Loader.TestControls\">\n" +
            "  <c:PictureRefusingControl Picture=\"/Assets/dot.png\" />\n" +
            "</StackPanel>";

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse(xaml),
            Environment(files, typeof(PictureRefusingControl).Assembly),
            new XamlLoadOptions { Mode = XamlLoadMode.Design, LocalAssembly = typeof(PictureRefusingControl).Assembly },
            TestContext.Current.CancellationToken);

        Assert.True(session is not null, "The document produced no object: " + string.Join(" | ", result.Diagnostics));

        await using (session)
        {
            MarkupDiagnostic refused = Assert.Single(
                result.Diagnostics, diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.IncompatibleValue);

            Assert.Equal(MarkupDiagnosticSeverity.Warning, refused.Severity);
            Assert.Contains("takes no pictures", refused.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>An image built into the test controls' assembly — what Avalonia's asset loader finds.</summary>
    private const string BuiltDot = "avares://ArxisStudio.Markup.Xaml.Loader.TestControls/Assets/dot.png";
}
