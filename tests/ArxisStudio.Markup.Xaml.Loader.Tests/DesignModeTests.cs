using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// Design-time values, and the fact that carrying them costs nothing in run mode.
/// </summary>
/// <remarks>
/// Avalonia's runtime loader understands four names in the design namespace and has no emitter
/// for any other, so a single <c>d:Text</c> fails the whole document — in run mode as much as in
/// design mode. Most of what these cover is that a document written the way real ones are
/// written loads at all, and that the document itself is never the thing that changed.
/// </remarks>
public sealed class DesignModeTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string DesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";
    private const string CompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string TestControlsNamespace = "https://arxis.studio/test-controls";

    private static readonly Uri ViewUri = new("file:///Views/View.axaml");

    private static ValueTask<(XamlLoadSession? Session, XamlLoadResult Result)> Load(
        string xaml,
        XamlLoadMode mode) =>
        XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri }),
            XamlLoadEnvironment.CreateDefault([typeof(CustomBadge).Assembly]),
            new XamlLoadOptions { Mode = mode },
            TestContext.Current.CancellationToken);

    /// <summary>A view with the declarations a real design-time document carries.</summary>
    private static string View(string attributes, string content = "") =>
        $"<StackPanel xmlns=\"{AvaloniaNamespace}\"\n" +
        "            xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
        $"            xmlns:d=\"{DesignNamespace}\"\n" +
        $"            xmlns:mc=\"{CompatibilityNamespace}\"\n" +
        "            mc:Ignorable=\"d\"\n" +
        $"            {attributes}>\n" +
        $"{content}" +
        "</StackPanel>";

    [AvaloniaFact]
    public async Task DesignSizeReachesTheControlInDesignMode()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await Load(
            View("d:DesignWidth=\"400\" d:DesignHeight=\"300\""), XamlLoadMode.Design);

        await using (session)
        {
            Assert.True(session is not null, string.Join(" | ", result.Diagnostics));

            var panel = session.GetRoot<StackPanel>();

            // Avalonia records these on Design.Width and Design.Height and stops; honouring
            // them, as the contract asks, is applying them to the control.
            Assert.Equal(400d, panel.Width);
            Assert.Equal(300d, panel.Height);
        }
    }

    [AvaloniaFact]
    public async Task DesignSizeIsIgnoredInRunMode()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await Load(
            View("d:DesignWidth=\"400\" d:DesignHeight=\"300\""), XamlLoadMode.Runtime);

        await using (session)
        {
            Assert.True(session is not null, string.Join(" | ", result.Diagnostics));

            var panel = session.GetRoot<StackPanel>();

            Assert.True(double.IsNaN(panel.Width));
            Assert.True(double.IsNaN(panel.Height));
        }
    }

    [AvaloniaFact]
    public async Task ADesignDataContextIsAppliedAsTheDataContext()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await Load(
            View("d:DataContext=\"a design-time customer\""), XamlLoadMode.Design);

        await using (session)
        {
            Assert.True(session is not null, string.Join(" | ", result.Diagnostics));
            Assert.Equal("a design-time customer", session.GetRoot<StackPanel>().DataContext);
        }
    }

    [AvaloniaFact]
    public async Task ADesignDataContextWrittenAsAnElementIsAppliedTheSameWay()
    {
        // The Design.DataContext form the contract names separately. Reading the value back off
        // the attached property rather than out of the document is what makes it the same code.
        (XamlLoadSession? session, XamlLoadResult result) = await Load(
            View(
                string.Empty,
                "  <Design.DataContext><x:String>from an element</x:String></Design.DataContext>\n"),
            XamlLoadMode.Design);

        await using (session)
        {
            Assert.True(session is not null, string.Join(" | ", result.Diagnostics));
            Assert.Equal("from an element", session.GetRoot<StackPanel>().DataContext);
        }
    }

    [AvaloniaFact]
    public async Task ADesignDataContextIsIgnoredInRunMode()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await Load(
            View("d:DataContext=\"a design-time customer\""), XamlLoadMode.Runtime);

        await using (session)
        {
            Assert.True(session is not null, string.Join(" | ", result.Diagnostics));
            Assert.Null(session.GetRoot<StackPanel>().DataContext);
        }
    }

    [AvaloniaFact]
    public async Task AShadowValueOverridesTheRealOneInDesignModeOnly()
    {
        const string Content = "  <TextBlock d:Text=\"design copy\" Text=\"{Binding Customer.Name}\" />\n";

        (XamlLoadSession? design, XamlLoadResult designResult) =
            await Load(View(string.Empty, Content), XamlLoadMode.Design);

        await using (design)
        {
            Assert.True(design is not null, string.Join(" | ", designResult.Diagnostics));
            Assert.Equal(
                "design copy",
                ((TextBlock)design.GetRoot<StackPanel>().Children[0]).Text);
        }

        (XamlLoadSession? runtime, XamlLoadResult runtimeResult) =
            await Load(View(string.Empty, Content), XamlLoadMode.Runtime);

        await using (runtime)
        {
            // The load has to survive the attribute either way — Avalonia fails the whole
            // document on it — and in run mode the binding is what the property is left to.
            Assert.True(runtime is not null, string.Join(" | ", runtimeResult.Diagnostics));
            Assert.Null(((TextBlock)runtime.GetRoot<StackPanel>().Children[0]).Text);
        }
    }

    [AvaloniaFact]
    public async Task AShadowValueIsConvertedToTheMembersType()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await Load(
            View(string.Empty, "  <Border d:Width=\"123\" />\n"), XamlLoadMode.Design);

        await using (session)
        {
            Assert.True(session is not null, string.Join(" | ", result.Diagnostics));
            Assert.Equal(123d, ((Border)session.GetRoot<StackPanel>().Children[0]).Width);
        }
    }

    [AvaloniaFact]
    public async Task AShadowValueOnACustomControlReachesItsOwnProperty()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await Load(
            $"<local:CustomBadge xmlns=\"{AvaloniaNamespace}\" xmlns:local=\"{TestControlsNamespace}\"\n" +
            $"                   xmlns:d=\"{DesignNamespace}\" d:Caption=\"designed\" />",
            XamlLoadMode.Design);

        await using (session)
        {
            Assert.True(session is not null, string.Join(" | ", result.Diagnostics));
            Assert.Equal("designed", session.GetRoot<CustomBadge>().Caption);
        }
    }

    [AvaloniaFact]
    public async Task AShadowValueNamingNoMemberIsReportedRatherThanFailingTheLoad()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await Load(
            View(string.Empty, "  <Border d:NoSuchThing=\"1\" />\n"), XamlLoadMode.Design);

        await using (session)
        {
            Assert.NotNull(session);
            Assert.Contains(
                result.Diagnostics,
                static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.UnresolvedDesignMember
                    && diagnostic.Severity == MarkupDiagnosticSeverity.Warning);
        }
    }

    [AvaloniaFact]
    public async Task AShadowValueWrittenAsAnExpressionIsReportedRatherThanWrittenAsText()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await Load(
            View(string.Empty, "  <TextBlock d:Text=\"{Binding Preview}\" />\n"), XamlLoadMode.Design);

        await using (session)
        {
            Assert.NotNull(session);
            Assert.Null(((TextBlock)session.GetRoot<StackPanel>().Children[0]).Text);
            Assert.Contains(
                result.Diagnostics,
                static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.DesignValueNotApplied);
        }
    }

    [AvaloniaFact]
    public async Task AnIgnorablePrefixIsKeptFromTheLoaderWhateverItIsCalled()
    {
        // The prefix is not 'd', and the namespace is not one anything here knows. mc:Ignorable
        // says a reader that does not understand it should carry on, and Avalonia's reader is
        // exactly such a reader.
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(
                $"<Border xmlns=\"{AvaloniaNamespace}\"\n" +
                $"        xmlns:mc=\"{CompatibilityNamespace}\"\n" +
                "        xmlns:tooling=\"https://example.invalid/some-tool\"\n" +
                "        mc:Ignorable=\"tooling\"\n" +
                "        tooling:Note=\"only this tool knows what this means\"\n" +
                "        Tag=\"real\" />",
                new XamlParseOptions { DocumentUri = ViewUri }),
            XamlLoadEnvironment.CreateDefault(),
            new XamlLoadOptions { Mode = XamlLoadMode.Runtime },
            TestContext.Current.CancellationToken);

        await using (session)
        {
            Assert.True(session is not null, string.Join(" | ", result.Diagnostics));
            Assert.Equal("real", session.GetRoot<Border>().Tag);
        }
    }

    [AvaloniaFact]
    public async Task TheDocumentKeepsEveryDesignTimeAttributeItWasWrittenWith()
    {
        string xaml = View(
            "d:DesignWidth=\"400\"",
            "  <TextBlock d:Text=\"design copy\" Text=\"{Binding Customer.Name}\" />\n");

        (XamlLoadSession? session, _) = await Load(xaml, XamlLoadMode.Design);

        await using (session)
        {
            Assert.NotNull(session);

            // Rule 2 and rule 3. Only the text handed to Avalonia lost them.
            Assert.Equal(xaml, session.Document.GetText());
            Assert.DoesNotContain("d:Text", session.Projection.Text.ToString(), StringComparison.Ordinal);
            Assert.Contains("Text=\"{Binding Customer.Name}\"", xaml, StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public async Task StrippingADesignAttributeDoesNotMoveTheElementsAroundIt()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await Load(
            View(
                string.Empty,
                "  <TextBlock d:Text=\"a rather long design-time value\" Text=\"real\" />\n" +
                "  <Border Name=\"After\" />\n"),
            XamlLoadMode.Design);

        await using (session)
        {
            Assert.True(session is not null, string.Join(" | ", result.Diagnostics));

            var panel = session.GetRoot<StackPanel>();
            XamlElement element = Assert.IsType<XamlElement>(session.GetElement(panel.Children[1]));

            // The projection is shorter than the document from the strip onwards, and the map
            // has to undo that or the element after it comes back as the one before.
            Assert.Contains(
                "Name=\"After\"",
                session.Document.SourceText.GetText(element.Span),
                StringComparison.Ordinal);
        }
    }
}
