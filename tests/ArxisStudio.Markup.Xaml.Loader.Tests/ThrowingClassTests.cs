using System;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// A document whose <c>x:Class</c> resolves but cannot be constructed — its constructor throws — is
/// still shown, as the element its root is written as, and says why with the constructor's own words
/// (ADR 0028).
/// </summary>
/// <remarks>
/// The constructor is the project's code. It throws for reasons that have nothing to do with the
/// document: a generated <c>InitializeComponent</c> in an assembly whose markup was never compiled
/// throws "No precompiled XAML found", and an editor lost every form of such a project over it.
/// </remarks>
public sealed class ThrowingClassTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string XamlNamespace = XamlNamespaces.Xaml;

    private const string Form =
        "  <StackPanel>\n" +
        "    <TextBlock x:Name=\"Title\" Text=\"hello\" />\n" +
        "    <Button Content=\"Save\" Click=\"SaveClicked\" />\n" +
        "  </StackPanel>\n";

    private static readonly Uri ViewUri = new("file:///Views/Throwing.axaml");

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri });

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault([typeof(CustomerView).Assembly], new InMemoryMarkupSourceProvider());

    private static string View(string content, Type type) =>
        $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"\n" +
        $"             x:Class=\"{type.FullName}\">\n" +
        content +
        "</UserControl>";

    private static async Task<XamlLoadSession> LoadAsync(string xaml, XamlLoadMode mode = XamlLoadMode.Design)
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse(xaml),
            Environment(),
            new XamlLoadOptions { Mode = mode },
            TestContext.Current.CancellationToken);

        Assert.True(
            session is not null,
            "The document produced no object: " + string.Join(" | ", result.Diagnostics));

        return session;
    }

    [AvaloniaTheory]
    [InlineData(XamlLoadMode.Design)]
    [InlineData(XamlLoadMode.Runtime)]
    public async Task AClassWhoseConstructorThrowsIsLeftOutAndTheRootIsBuiltAsWritten(XamlLoadMode mode)
    {
        await using XamlLoadSession session = await LoadAsync(View(Form, typeof(UnconstructableView)), mode);

        Assert.Equal(typeof(UserControl), session.RootObject.GetType());

        var panel = Assert.IsType<StackPanel>(session.GetRoot<UserControl>().Content);

        Assert.Equal(2, panel.Children.Count);

        MarkupDiagnostic failure = Assert.Single(
            session.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.RootInstanceCreationFailure);

        // The constructor's own words, not the reflection wrapper's.
        Assert.Contains("This view cannot be constructed, by design.", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("target of an invocation", failure.Message, StringComparison.Ordinal);

        // A warning, because the load went on without the class and lost nothing the markup shows.
        Assert.Equal(MarkupDiagnosticSeverity.Warning, failure.Severity);

        // The class never declared the handler, and that was said once, when the class was asked.
        Assert.Single(session.Diagnostics, static d => d.Code == XamlLoaderDiagnosticCodes.MissingEventHandler);
        Assert.DoesNotContain(session.Diagnostics, static d => d.IsError);
        Assert.Contains("x:Class=", session.Document.GetText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AHandlerTheClassWouldAnswerIsLeftOutWithItAndSaysSoOnce()
    {
        await using XamlLoadSession session = await LoadAsync(View(Form, typeof(UnconstructableHandlingView)));

        var root = session.GetRoot<UserControl>();

        Assert.IsType<TextBlock>(root.FindControl<TextBlock>("Title"));

        MarkupDiagnostic handler = Assert.Single(
            session.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.MissingEventHandler);

        Assert.Contains("SaveClicked", handler.Message, StringComparison.Ordinal);
        Assert.Contains("built as written", handler.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ASessionWhoseClassCouldNotBeConstructedFollowsItsDocument()
    {
        await using XamlLoadSession session = await LoadAsync(View(Form, typeof(UnconstructableHandlingView)));

        var root = session.GetRoot<UserControl>();

        // A rebuilt part projects the document again — without the class and its handler, as the load did.
        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            Parse(View(Form.Replace("StackPanel", "WrapPanel", StringComparison.Ordinal), typeof(UnconstructableHandlingView))),
            TestContext.Current.CancellationToken);

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Same(root, session.RootObject);
        Assert.Equal(2, Assert.IsType<WrapPanel>(root.Content).Children.Count);

        // And says of the handler what the load said: the root is built as written, not classless.
        MarkupDiagnostic handler = Assert.Single(
            result.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.MissingEventHandler);

        Assert.Contains("built as written", handler.Message, StringComparison.Ordinal);
    }
}
