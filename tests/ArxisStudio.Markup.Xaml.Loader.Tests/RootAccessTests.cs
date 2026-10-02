using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// A session writes into a root whose parts a host has borrowed only while the host has given them
/// back — <see cref="IXamlRootAccess"/>.
/// </summary>
/// <remarks>
/// A host showing a window takes the window's content out of it, because a window cannot be put
/// anywhere. An update that wrote into that root, or rebuilt the map by walking it, met a window with
/// nothing in it: the writes went where nobody was looking and the map paired nothing below the root.
/// </remarks>
public sealed class RootAccessTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string XamlNamespace = XamlNamespaces.Xaml;
    private const string ControlsNamespace = "https://arxis.studio/test-controls";

    private static readonly Uri ViewUri = new("file:///Views/CountedView.axaml");

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri });

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault([typeof(CountedView).Assembly], new InMemoryMarkupSourceProvider());

    private static string View(string content) =>
        $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\" xmlns:tc=\"{ControlsNamespace}\"\n" +
        $"             x:Class=\"{typeof(CountedView).FullName}\">\n" +
        content +
        "</UserControl>";

    private static string Panel(string text) =>
        "  <StackPanel>\n" +
        $"    <TextBlock x:Name=\"Title\" Text=\"{text}\" />\n" +
        "  </StackPanel>\n";

    private static async Task<(XamlLoadSession Session, BorrowingHost Host)> LoadAsync(string xaml)
    {
        var host = new BorrowingHost();

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse(xaml),
            Environment(),
            new XamlLoadOptions { Mode = XamlLoadMode.Design, RootAccess = host },
            TestContext.Current.CancellationToken);

        Assert.True(
            session is not null,
            "The document produced no object: " + string.Join(" | ", result.Diagnostics));

        host.Borrow((ContentControl)session.RootObject);

        return (session, host);
    }

    private static string Describe(XamlUpdateResult result) =>
        $"{result.Outcome} / {result.Strategy}: " + string.Join(" | ", result.Diagnostics);

    [AvaloniaFact]
    public async Task TheMapIsRebuiltWhileTheRootIsLent()
    {
        (XamlLoadSession session, BorrowingHost host) = await LoadAsync(View(Panel("one")));

        await using (session)
        {
            XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
                Parse(View(Panel("two"))), TestContext.Current.CancellationToken);

            Assert.True(result.Applied, Describe(result));

            var panel = Assert.IsType<StackPanel>(host.Borrowed);
            var title = Assert.IsType<TextBlock>(panel.Children.Single());

            Assert.Equal("two", title.Text);

            XamlElement titleElement = session.Document.DescendantElements()
                .Single(static element => element.Identity == "Title");

            Assert.Same(title, session.GetObject(titleElement));
            Assert.Null(((ContentControl)session.RootObject).Content);
            Assert.Equal(1, host.Lent);
            Assert.Equal(0, host.WrittenWhileBorrowed);
        }
    }

    [AvaloniaFact]
    public async Task EveryLiveWriteHappensInsideTheRootAccess()
    {
        (XamlLoadSession session, BorrowingHost host) = await LoadAsync(View(Panel("one")));

        await using (session)
        {
            // The root's content rebuilt, which is the write that puts a new tree into the root.
            XamlUpdateResult rebuilt = await session.ApplyDocumentUpdateAsync(
                Parse(View(
                    "  <UserControl.Resources>\n" +
                    "    <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
                    "  </UserControl.Resources>\n" +
                    Panel("one"))),
                TestContext.Current.CancellationToken);

            Assert.True(rebuilt.Applied, Describe(rebuilt));

            var title = (TextBlock)((StackPanel)host.Borrowed!).Children.Single();

            Assert.True(session.SetValue(title, TextBlock.FontSizeProperty, 21d).Applied);
            Assert.True(session.SetXamlValue(title, TextBlock.TextProperty, new XamlLiteralValue("three")).Applied);

            Assert.Equal(0, host.WrittenWhileBorrowed);
            Assert.Equal(3, host.Lent);
            Assert.Equal(host.Lent, host.Returned);
            Assert.Null(((ContentControl)session.RootObject).Content);
            Assert.Contains("FontSize=\"21\"", session.Document.GetText(), StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public async Task TheLeaseIsReturnedWhenAnUpdateDoesNotLand()
    {
        (XamlLoadSession session, BorrowingHost host) = await LoadAsync(View(
            "  <tc:ThrowingControl />\n"));

        await using (session)
        {
            // Refused before anything is written: text the member cannot hold.
            XamlUpdateResult refused = await session.ApplyDocumentUpdateAsync(
                Parse(View("  <tc:ThrowingControl Width=\"wide\" />\n")),
                TestContext.Current.CancellationToken);

            Assert.False(refused.Applied, Describe(refused));

            // Stopped after writing: a setter that assigns and then throws.
            XamlUpdateResult broken = await session.ApplyDocumentUpdateAsync(
                Parse(View("  <tc:ThrowingControl AssignsThenThrows=\"any\" />\n")),
                TestContext.Current.CancellationToken);

            Assert.Equal(XamlUpdateOutcome.RequiresNewSession, broken.Outcome);
            Assert.Equal(host.Lent, host.Returned);
            Assert.True(host.Lent >= 1);
        }
    }

    [AvaloniaFact]
    public async Task AWindowCopyThatWillNotCloseIsReported()
    {
        string Window(string content) =>
            $"<tc:StubbornWindow xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\" xmlns:tc=\"{ControlsNamespace}\"\n" +
            $"                   x:Class=\"{typeof(StubbornClassWindow).FullName}\">\n" +
            content +
            "</tc:StubbornWindow>";

        (XamlLoadSession? session, XamlLoadResult loaded) = await XamlLoadSession.TryCreateAsync(
            Parse(Window("  <StackPanel />\n")),
            Environment(),
            new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

        Assert.True(session is not null, string.Join(" | ", loaded.Diagnostics));

        var root = (StubbornWindow)session.RootObject;

        try
        {
            XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
                Parse(Window(
                    "  <tc:StubbornWindow.Resources>\n" +
                    "    <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
                    "  </tc:StubbornWindow.Resources>\n" +
                    "  <StackPanel />\n")),
                TestContext.Current.CancellationToken);

            Assert.True(result.Applied, Describe(result));
            Assert.Contains(
                result.Diagnostics,
                static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.TopLevelCopyNotClosed
                    && diagnostic.Severity == MarkupDiagnosticSeverity.Warning);
        }
        finally
        {
            await session.DisposeAsync();

            foreach (StubbornWindow window in TrackedWindow.Open().OfType<StubbornWindow>())
            {
                window.MayClose = true;
                window.Close();
            }

            Assert.DoesNotContain(root, TrackedWindow.Open());
        }
    }

    /// <summary>
    /// A host that shows a root by taking its content out of it, the way a form designer shows a
    /// window, and gives the content back only when the session asks.
    /// </summary>
    private sealed class BorrowingHost : IXamlRootAccess
    {
        private ContentControl? _root;
        private bool _lent;

        public object? Borrowed { get; private set; }

        public int Lent { get; private set; }

        public int Returned { get; private set; }

        /// <summary>How often the root's content was set while this host was holding it.</summary>
        public int WrittenWhileBorrowed { get; private set; }

        public void Borrow(ContentControl root)
        {
            _root ??= root;

            if (_root.Content is { } content)
            {
                Borrowed = content;
                _root.Content = null;
            }

            _root.PropertyChanged -= Watch;
            _root.PropertyChanged += Watch;
        }

        public IDisposable Lend(object root)
        {
            Assert.False(_lent, "A lease was asked for while another was open.");
            Assert.Same(_root, root);

            _lent = true;
            Lent++;

            _root!.Content = Borrowed;
            Borrowed = null;

            return new Lease(this);
        }

        private void Watch(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (!_lent && e.Property == ContentControl.ContentProperty && e.NewValue is not null)
            {
                WrittenWhileBorrowed++;
            }
        }

        private sealed class Lease(BorrowingHost host) : IDisposable
        {
            public void Dispose()
            {
                host._lent = false;
                host.Returned++;
                host.Borrow(host._root!);
            }
        }
    }
}
