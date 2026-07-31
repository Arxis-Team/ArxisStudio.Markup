using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// What a session keeps hold of, and for how long.
/// </summary>
/// <remarks>
/// A session that is edited all day builds objects all day. The ones it replaces have to become
/// collectable, and the record it keeps of the texts it built them from has to shrink with them;
/// neither is visible from outside until an editor has been open for an hour.
/// </remarks>
public sealed class LeakTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = new Uri("file:///Views/View.axaml") });

    private static string Panel(int rows) =>
        $"<StackPanel xmlns=\"{AvaloniaNamespace}\">\n" + Rows(rows) + "</StackPanel>";

    private static string Rows(int rows)
    {
        var text = new StringBuilder();

        for (int index = 0; index < rows; index++)
        {
            text.Append("  <TextBlock Text=\"row ").Append(index).Append("\" />\n");
        }

        return text.ToString();
    }

    private static ValueTask<XamlLoadSession> Load(string xaml) =>
        XamlLoadSession.CreateAsync(
            Parse(xaml),
            XamlLoadEnvironment.CreateDefault(),
            cancellationToken: TestContext.Current.CancellationToken);

    private static void Collect()
    {
        for (int pass = 0; pass < 3; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    [AvaloniaFact]
    public async Task ObjectsAnUpdateReplacesBecomeCollectable()
    {
        XamlLoadSession session = await Load(Panel(3));

        // The reference is taken and dropped inside a method of its own. An async method's
        // locals live in a state machine that survives until the method returns, so a weak
        // reference examined in the same method as its target is examined while something is
        // still holding it.
        WeakReference discarded = await ReplaceTheContent(session);

        Collect();

        Assert.False(discarded.IsAlive);

        await session.DisposeAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> ReplaceTheContent(XamlLoadSession session)
    {
        var discarded = new WeakReference(session.GetRoot<StackPanel>().Children[0]);

        Assert.True(discarded.IsAlive);

        // A structural change rebuilds the panel's content, so every child it had is dropped.
        Assert.True(
            (await session.ApplyDocumentUpdateAsync(
                Parse(Panel(4)), TestContext.Current.CancellationToken)).Applied);

        return discarded;
    }

    [AvaloniaFact]
    public async Task ASessionDoesNotHoldTheDocumentsItRefused()
    {
        XamlLoadSession session = await Load(Panel(2));
        WeakReference stale = await RefuseSeveralDocuments(session);

        Collect();

        // One refused document is kept so a caller can show it; the twenty before it are not.
        Assert.False(stale.IsAlive);
        Assert.NotNull(session.PendingDocument);

        await session.DisposeAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> RefuseSeveralDocuments(XamlLoadSession session)
    {
        XamlDocument first = Parse($"<StackPanel xmlns=\"{AvaloniaNamespace}\"><TextBlock Text=\"half");

        Assert.False(
            (await session.ApplyDocumentUpdateAsync(first, TestContext.Current.CancellationToken)).Applied);
        Assert.Same(first, session.PendingDocument);

        var stale = new WeakReference(first);

        for (int index = 0; index < 20; index++)
        {
            await session.ApplyDocumentUpdateAsync(
                Parse($"<StackPanel xmlns=\"{AvaloniaNamespace}\"><TextBlock Text=\"{index}"),
                TestContext.Current.CancellationToken);
        }

        return stale;
    }

    [AvaloniaFact]
    public async Task ManyUpdatesInSequenceStayCorrect()
    {
        await using XamlLoadSession session = await Load(Panel(3));

        var panel = session.GetRoot<StackPanel>();

        // Alternating between a property set and a structural change exercises both paths, and
        // the map is rebuilt after each one from positions recorded against the one before.
        for (int index = 0; index < 40; index++)
        {
            string xaml = index % 2 == 0
                ? $"<StackPanel xmlns=\"{AvaloniaNamespace}\" Spacing=\"{index}\">\n" + Rows(3) + "</StackPanel>"
                : $"<StackPanel xmlns=\"{AvaloniaNamespace}\" Spacing=\"{index}\">\n" + Rows(4) + "</StackPanel>";

            XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
                Parse(xaml), TestContext.Current.CancellationToken);

            Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        }

        Assert.Same(panel, session.RootObject);
        Assert.Equal(4, panel.Children.Count);
        Assert.Equal(39d, panel.Spacing);

        // And the map still answers for the children the last update built.
        Assert.NotNull(session.GetElement(panel.Children[3]));
    }

    [AvaloniaFact]
    public async Task ALargeDocumentLoadsAndMapsEveryElementItDeclares()
    {
        await using XamlLoadSession session = await Load(Panel(500));

        var panel = session.GetRoot<StackPanel>();

        Assert.Equal(500, panel.Children.Count);

        for (int index = 0; index < panel.Children.Count; index += 97)
        {
            XamlElement element = Assert.IsType<XamlElement>(session.GetElement(panel.Children[index]));

            Assert.Contains($"row {index}\"", element.GetSourceText(), StringComparison.Ordinal);
        }
    }
}
