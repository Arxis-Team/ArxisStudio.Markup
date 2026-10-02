using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// A value taken out and an expression written in apply on the objects where the member allows it,
/// rather than rebuilding the element — and, at the root, rather than asking for a new session.
/// </summary>
/// <remarks>
/// An inspector writes bindings and takes values out all day. Both used to be structural: the
/// element was rebuilt for a binding on a text block, and a value taken off the root of a form
/// with a class meant a new session — a new instance of the author's class — every time.
/// </remarks>
public sealed class InPlaceUpdateTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string XamlNamespace = XamlNamespaces.Xaml;
    private const string ControlsNamespace = "https://arxis.studio/test-controls";

    private static readonly Uri ViewUri = new("file:///Views/View.axaml");

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri });

    private static string Form(string rootAttributes, string text, string textAttributes = "") =>
        $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\" xmlns:tc=\"{ControlsNamespace}\"\n" +
        $"             x:Class=\"{typeof(CountedView).FullName}\"{rootAttributes}>\n" +
        "  <UserControl.Resources>\n" +
        "    <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
        "  </UserControl.Resources>\n" +
        "  <StackPanel>\n" +
        $"    <TextBlock x:Name=\"Shown\" Text=\"{text}\"{textAttributes} />\n" +
        "  </StackPanel>\n" +
        "</UserControl>";

    private static async Task<XamlLoadSession> LoadAsync(string xaml)
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse(xaml),
            XamlLoadEnvironment.CreateDefault([typeof(CountedView).Assembly], new InMemoryMarkupSourceProvider()),
            new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

        Assert.True(session is not null, "The document produced no object: " + string.Join(" | ", result.Diagnostics));

        return session;
    }

    private static Task<XamlUpdateResult> UpdateAsync(XamlLoadSession session, string xaml) =>
        session.ApplyDocumentUpdateAsync(Parse(xaml), TestContext.Current.CancellationToken).AsTask();

    private static string Describe(XamlUpdateResult result) =>
        $"{result.Outcome} / {result.Strategy}: " + string.Join(" | ", result.Diagnostics);

    private static TextBlock Shown(XamlLoadSession session) =>
        ((StackPanel)session.GetRoot<UserControl>().Content!).Children.OfType<TextBlock>().Single();

    [AvaloniaFact]
    public async Task ABindingWrittenOnAChildAppliesInPlace()
    {
        await using XamlLoadSession session = await LoadAsync(Form(string.Empty, "plain"));

        TextBlock text = Shown(session);
        int constructed = CountedView.Constructed;

        XamlUpdateResult result = await UpdateAsync(session, Form(string.Empty, "{Binding Name}"));

        Assert.True(result.Applied, Describe(result));
        Assert.Equal(XamlUpdateStrategy.SetExpression, result.Strategy);
        Assert.Same(text, Shown(session));
        Assert.Equal(constructed, CountedView.Constructed);

        text.DataContext = new GreetingModel { Name = "Grace" };
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Grace", text.Text);
        Assert.True(session.GetValueInfo(text, TextBlock.TextProperty).HasBinding);
    }

    [AvaloniaTheory]
    [InlineData("{x:Static tc:Greetings.Morning}", "Good morning")]
    [InlineData("{x:Static tc:Greetings.Evening}", "Good evening")]
    [InlineData("{x:Null}", null)]
    public async Task AStaticOrNullWrittenOnAChildAppliesInPlace(string written, string? shown)
    {
        await using XamlLoadSession session = await LoadAsync(Form(string.Empty, "plain"));

        TextBlock text = Shown(session);

        XamlUpdateResult result = await UpdateAsync(session, Form(string.Empty, written));

        Assert.True(result.Applied, Describe(result));
        Assert.Equal(XamlUpdateStrategy.SetExpression, result.Strategy);
        Assert.Same(text, Shown(session));
        Assert.Equal(shown, text.Text);
    }

    [AvaloniaFact]
    public async Task ADynamicResourceWrittenOnTheRootAppliesInPlaceAndFollowsTheResource()
    {
        await using XamlLoadSession session = await LoadAsync(Form(string.Empty, "plain"));

        object root = session.RootObject;

        XamlUpdateResult result = await UpdateAsync(
            session, Form(" Background=\"{DynamicResource Accent}\"", "plain"));

        Assert.True(result.Applied, Describe(result));
        Assert.Same(root, session.RootObject);

        var view = session.GetRoot<UserControl>();

        Assert.Equal(Colors.Red, Assert.IsAssignableFrom<ISolidColorBrush>(view.Background).Color);

        view.Resources["Accent"] = new SolidColorBrush(Colors.Blue);

        Assert.Equal(Colors.Blue, Assert.IsAssignableFrom<ISolidColorBrush>(view.Background).Color);
    }

    [AvaloniaFact]
    public async Task RemovingAnAttributeFromTheRootDoesNotNeedANewSession()
    {
        await using XamlLoadSession session = await LoadAsync(Form(" Width=\"120\"", "plain"));

        object root = session.RootObject;
        int constructed = CountedView.Constructed;

        XamlUpdateResult result = await UpdateAsync(session, Form(string.Empty, "plain"));

        Assert.True(result.Applied, Describe(result));
        Assert.Equal(XamlUpdateStrategy.ClearProperty, result.Strategy);
        Assert.Same(root, session.RootObject);
        Assert.Equal(constructed, CountedView.Constructed);
        Assert.True(double.IsNaN(session.GetRoot<UserControl>().Width));
    }

    [AvaloniaFact]
    public async Task RemovingABindingEndsIt()
    {
        await using XamlLoadSession session = await LoadAsync(Form(string.Empty, "{Binding Name}"));

        TextBlock text = Shown(session);
        var model = new GreetingModel { Name = "first" };

        text.DataContext = model;
        Dispatcher.UIThread.RunJobs();

        XamlUpdateResult result = await UpdateAsync(
            session,
            Form(string.Empty, "plain").Replace(" Text=\"plain\"", string.Empty, StringComparison.Ordinal));

        Assert.True(result.Applied, Describe(result));
        Assert.Same(text, Shown(session));
        Assert.False(session.GetValueInfo(text, TextBlock.TextProperty).HasBinding);
        Assert.True(string.IsNullOrEmpty(text.Text), $"\"{text.Text}\" is still shown.");
    }

    [AvaloniaFact]
    public async Task AnExpressionThatCannotBeAppliedInPlaceRebuildsItsElement()
    {
        await using XamlLoadSession session = await LoadAsync(Form(string.Empty, "plain"));

        TextBlock text = Shown(session);

        // A static resource is read from the dictionaries in scope when the element is built, and
        // the honest way to read it is to build the element.
        XamlUpdateResult result = await UpdateAsync(
            session, Form(string.Empty, "plain", " Foreground=\"{StaticResource Accent}\""));

        Assert.True(result.Applied, Describe(result));
        Assert.Equal(XamlUpdateStrategy.ReloadSubtree, result.Strategy);
        Assert.NotSame(text, Shown(session));
        Assert.Equal(Colors.Red, Assert.IsAssignableFrom<ISolidColorBrush>(Shown(session).Foreground).Color);
    }

    [AvaloniaFact]
    public async Task AStaticReferenceNestedInABindingIsReadFromTheDictionaryAboveIt()
    {
        await using XamlLoadSession session = await LoadAsync(Form(string.Empty, "plain"));

        XamlElement root = session.Document.Root!;

        // A fallback written as a static reference: the binding cannot be set in place, and the
        // text block built on its own would have no dictionary to read the brush from.
        XamlUpdateResult result = await UpdateAsync(
            session, Form(string.Empty, "plain", " Tag=\"{Binding Missing, FallbackValue={StaticResource Accent}}\""));

        Assert.True(result.Applied, Describe(result));
        Assert.Contains(
            result.Changes,
            change => change.Strategy == XamlUpdateStrategy.ReloadSubtree && ReferenceEquals(change.OldElement, root));

        TextBlock text = Shown(session);

        text.DataContext = new GreetingModel();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Colors.Red, Assert.IsAssignableFrom<ISolidColorBrush>(text.Tag).Color);
    }

    [AvaloniaFact]
    public async Task ABindingWhereBindingsCompileIsBuiltAsTheLoadWouldBuildIt()
    {
        await using XamlLoadSession session = await LoadAsync(
            Form(" x:CompileBindings=\"True\" x:DataType=\"tc:GreetingModel\"", "plain"));

        TextBlock text = Shown(session);

        // Compiled, a binding is checked against its data type when it is built — a reflection
        // binding set in its place would take a path the load refuses.
        XamlUpdateResult result = await UpdateAsync(
            session, Form(" x:CompileBindings=\"True\" x:DataType=\"tc:GreetingModel\"", "{Binding Name}"));

        Assert.True(result.Applied, Describe(result));
        Assert.NotEqual(XamlUpdateStrategy.SetExpression, result.Strategy);
        Assert.NotSame(text, Shown(session));

        session.GetRoot<UserControl>().DataContext = new GreetingModel { Name = "Grace" };
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Grace", Shown(session).Text);
    }

    [AvaloniaFact]
    public async Task ABindingToAPathTheDataTypeLacksIsRefusedWhereBindingsCompile()
    {
        await using XamlLoadSession session = await LoadAsync(
            Form(" x:CompileBindings=\"True\" x:DataType=\"tc:GreetingModel\"", "plain"));

        TextBlock text = Shown(session);

        XamlUpdateResult result = await UpdateAsync(
            session, Form(" x:CompileBindings=\"True\" x:DataType=\"tc:GreetingModel\"", "{Binding Nickname}"));

        Assert.Equal(XamlUpdateOutcome.RejectedCleanly, result.Outcome);
        Assert.Same(text, Shown(session));
        Assert.Equal("plain", text.Text);
    }

    [AvaloniaFact]
    public async Task RemovingAPlainClrValueRebuildsItsElement()
    {
        string Xaml(string note) =>
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\" xmlns:tc=\"{ControlsNamespace}\">\n" +
            $"  <tc:MemberMatrixControl{note} />\n" +
            "</StackPanel>";

        (XamlLoadSession? session, XamlLoadResult loaded) = await XamlLoadSession.TryCreateAsync(
            Parse(Xaml(" Note=\"kept\"")),
            XamlLoadEnvironment.CreateDefault([typeof(MemberMatrixControl).Assembly], new InMemoryMarkupSourceProvider()),
            new XamlLoadOptions(),
            TestContext.Current.CancellationToken);

        Assert.True(session is not null, string.Join(" | ", loaded.Diagnostics));

        await using (session)
        {
            // A CLR property has no value of its own to clear: nothing but building the object
            // again says what it holds when nobody wrote it.
            XamlUpdateResult result = await UpdateAsync(session, Xaml(string.Empty));

            Assert.True(result.Applied, Describe(result));
            Assert.Equal(XamlUpdateStrategy.ReloadSubtree, result.Strategy);
            Assert.Null(session.GetRoot<StackPanel>().Children.OfType<MemberMatrixControl>().Single().Note);
        }
    }

    [AvaloniaFact]
    public async Task MakingANamespaceIgnorableThatNothingUsedIsNoChange()
    {
        string Xaml(string design) =>
            $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"{design}\n" +
            $"             x:Class=\"{typeof(CountedView).FullName}\">\n" +
            "  <TextBlock Text=\"plain\" />\n" +
            "</UserControl>";

        await using XamlLoadSession session = await LoadAsync(Xaml(string.Empty));

        object root = session.RootObject;

        // What QualifyAttribute and EnsureIgnorable write for a first design value: a declaration,
        // mc:Ignorable naming it, and the value itself — and nothing in the document used the prefix.
        XamlUpdateResult result = await UpdateAsync(session, Xaml(
            $" xmlns:d=\"{XamlNamespaces.Design}\" xmlns:mc=\"{XamlNamespaces.MarkupCompatibility}\"" +
            " mc:Ignorable=\"d\" d:DesignWidth=\"300\""));

        Assert.True(result.Applied, Describe(result));
        Assert.NotEqual(XamlUpdateStrategy.RecreateSession, result.Strategy);
        Assert.Same(root, session.RootObject);
    }

    [AvaloniaFact]
    public async Task NoLongerIgnoringANamespaceIsStructural()
    {
        string Xaml(string ignorable) =>
            $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"\n" +
            $"             xmlns:d=\"{XamlNamespaces.Design}\" xmlns:mc=\"{XamlNamespaces.MarkupCompatibility}\"{ignorable}\n" +
            $"             x:Class=\"{typeof(CountedView).FullName}\" d:DesignWidth=\"300\">\n" +
            "  <TextBlock Text=\"plain\" />\n" +
            "</UserControl>";

        await using XamlLoadSession session = await LoadAsync(Xaml(" mc:Ignorable=\"d\""));

        XamlUpdateResult result = await UpdateAsync(session, Xaml(string.Empty));

        Assert.Equal(XamlUpdateStrategy.RecreateSession, result.Strategy);
    }

    [AvaloniaFact]
    public async Task MakingANamespaceIgnorableThatMarkupIsWrittenInIsStructural()
    {
        string Xaml(string ignorable) =>
            $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\" xmlns:tc=\"{ControlsNamespace}\"\n" +
            $"             xmlns:mc=\"{XamlNamespaces.MarkupCompatibility}\"{ignorable}\n" +
            $"             x:Class=\"{typeof(CountedView).FullName}\">\n" +
            "  <tc:CustomBadge />\n" +
            "</UserControl>";

        await using XamlLoadSession session = await LoadAsync(Xaml(string.Empty));

        // A reader skips markup in an ignorable namespace, and the loaded document has some.
        XamlUpdateResult result = await UpdateAsync(session, Xaml(" mc:Ignorable=\"tc\""));

        Assert.Equal(XamlUpdateStrategy.RecreateSession, result.Strategy);
    }
}
