using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// A binding written on an element, checked against what the document declares: its source, its path, and
/// whether a build that compiles it takes it.
/// </summary>
public sealed class BindingCheckTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string ControlsNamespace = "https://arxis.studio/test-controls";

    private static string Head(string root, string attributes = "") =>
        $"<{root} xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\" xmlns:tc=\"{ControlsNamespace}\" {attributes}>";

    [AvaloniaFact]
    public async Task ACompiledBindingIsReadAgainstTheDataTypeInScopeAndABrokenPathBreaksTheBuild()
    {
        await using XamlLoadSession session = await LoadAsync(
            Head("UserControl", "x:DataType=\"tc:GreetingModel\"") +
            "<StackPanel>" +
            "<TextBlock x:Name=\"Good\" Text=\"{Binding Name}\" />" +
            "<TextBlock x:Name=\"Bad\" Text=\"{Binding Nmae}\" />" +
            "</StackPanel></UserControl>");

        XamlBindingCheck good = await CheckAsync(session, "Good", "Text", compilesByDefault: true);

        Assert.Equal(XamlBindingSource.DataContext, good.Source);
        Assert.Equal(XamlBindingPathStatus.Resolved, good.Status);
        Assert.True(good.Compiles);
        Assert.Equal(typeof(GreetingModel), good.SourceType);
        Assert.Equal(typeof(string), good.ResultType);
        Assert.False(good.BreaksBuild);

        XamlBindingCheck bad = await CheckAsync(session, "Bad", "Text", compilesByDefault: true);

        Assert.Equal(XamlBindingPathStatus.Broken, bad.Status);
        Assert.Equal("Nmae", bad.Step);
        Assert.True(bad.BreaksBuild);

        // The same path in a build that does not compile it shows nothing, and the build goes through.
        XamlBindingCheck reflected = await CheckAsync(session, "Bad", "Text", compilesByDefault: false);

        Assert.Equal(XamlBindingPathStatus.Broken, reflected.Status);
        Assert.False(reflected.BreaksBuild);
    }

    [AvaloniaFact]
    public async Task ACompiledBindingWithNoDataTypeInScopeBreaksTheBuildAndAReflectionOneReadsTheDesignData()
    {
        // The session loads forgivingly — its own bindings are reflection bindings — and the build's default is asked
        // about separately: a {CompiledBinding} with nothing to compile against would not load at all.
        await using XamlLoadSession session = await LoadAsync(
            Head("UserControl") +
            "<Design.DataContext><tc:GreetingModel /></Design.DataContext>" +
            "<StackPanel>" +
            "<TextBlock x:Name=\"Plain\" Text=\"{Binding Name}\" />" +
            "<TextBlock x:Name=\"Reflected\" Text=\"{ReflectionBinding Name}\" />" +
            "</StackPanel></UserControl>");

        XamlBindingCheck compiled = await CheckAsync(session, "Plain", "Text", compilesByDefault: true);

        Assert.True(compiled.Compiles);
        Assert.Equal(XamlBindingPathStatus.Broken, compiled.Status);
        Assert.Null(compiled.Step);
        Assert.True(compiled.BreaksBuild);
        Assert.Contains("x:DataType", compiled.Message, StringComparison.Ordinal);

        XamlBindingCheck plain = await CheckAsync(session, "Plain", "Text", compilesByDefault: false);

        Assert.Equal(XamlBindingPathStatus.Resolved, plain.Status);
        Assert.Equal(typeof(GreetingModel), plain.SourceType);

        XamlBindingCheck reflected = await CheckAsync(session, "Reflected", "Text", compilesByDefault: true);

        Assert.False(reflected.Compiles);
        Assert.Equal(XamlBindingPathStatus.Resolved, reflected.Status);
        Assert.Equal(typeof(GreetingModel), reflected.SourceType);
    }

    [AvaloniaFact]
    public async Task ABindingTheBuildDoesNotCompileReadsTheDataTheObjectsHaveBeforeTheDataType()
    {
        // The root's x:DataType names its model, and the card hands its content a string: a reflection binding reads
        // the string when the form runs, while a compiled one is checked against the model and refused.
        await using XamlLoadSession session = await LoadAsync(
            Head("UserControl", "x:DataType=\"tc:GreetingModel\"") +
            "<UserControl.DataContext><tc:GreetingModel /></UserControl.DataContext>" +
            "<Border DataContext=\"{Binding Name}\">" +
            "<TextBlock x:Name=\"Inner\" Text=\"{Binding Length}\" />" +
            "</Border></UserControl>");

        var window = new Window { Content = session.RootObject };

        window.Show();

        try
        {
            XamlBindingCheck reflected = await CheckAsync(session, "Inner", "Text", compilesByDefault: false);

            Assert.Equal(XamlBindingPathStatus.Resolved, reflected.Status);
            Assert.Equal(typeof(string), reflected.SourceType);
            Assert.Equal(typeof(int), reflected.ResultType);

            XamlBindingCheck compiled = await CheckAsync(session, "Inner", "Text", compilesByDefault: true);

            Assert.Equal(XamlBindingPathStatus.Broken, compiled.Status);
            Assert.Equal(typeof(GreetingModel), compiled.SourceType);
            Assert.True(compiled.BreaksBuild);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ADataContextBindingReadsTheDataItsParentHas()
    {
        await using XamlLoadSession session = await LoadAsync(
            Head("UserControl", "x:DataType=\"tc:GreetingModel\"") +
            "<Border x:Name=\"Card\" x:DataType=\"tc:CustomBadge\" DataContext=\"{Binding Name}\">" +
            "<TextBlock x:Name=\"Inner\" Text=\"{Binding Caption}\" />" +
            "</Border></UserControl>");

        // The element's own x:DataType is about the data it gives its content: its data context binding reads the root's.
        XamlBindingCheck card = await CheckAsync(session, "Card", "DataContext", compilesByDefault: true);

        Assert.Equal(XamlBindingPathStatus.Resolved, card.Status);
        Assert.Equal(typeof(GreetingModel), card.SourceType);

        XamlBindingCheck inner = await CheckAsync(session, "Inner", "Text", compilesByDefault: true);

        Assert.Equal(typeof(CustomBadge), inner.SourceType);
        Assert.Equal(XamlBindingPathStatus.Resolved, inner.Status);
    }

    [AvaloniaFact]
    public async Task AnElementByNameIsFoundInTheDocumentAndANameNothingHasBreaksTheBinding()
    {
        await using XamlLoadSession session = await LoadAsync(
            Head("UserControl") +
            "<StackPanel>" +
            "<CheckBox x:Name=\"Check\" />" +
            "<TextBlock x:Name=\"Hashed\" IsVisible=\"{Binding !#Check.IsChecked}\" />" +
            "<TextBlock x:Name=\"Named\" Text=\"{Binding Content, ElementName=Check}\" />" +
            "<TextBlock x:Name=\"Missing\" Text=\"{Binding Text, ElementName=Nowhere}\" />" +
            "<TextBlock x:Name=\"Outside\" Text=\"{Binding Text, ElementName=Inside}\" />" +
            "<ItemsControl><ItemsControl.ItemTemplate><DataTemplate>" +
            "<TextBlock x:Name=\"Inside\" />" +
            "</DataTemplate></ItemsControl.ItemTemplate></ItemsControl>" +
            "</StackPanel></UserControl>");

        XamlBindingCheck hashed = await CheckAsync(session, "Hashed", "IsVisible", compilesByDefault: true);

        Assert.Equal(XamlBindingSource.Element, hashed.Source);
        Assert.Equal(typeof(CheckBox), hashed.SourceType);
        Assert.Equal("!IsChecked", hashed.Path);
        Assert.Equal(typeof(bool), hashed.ResultType);

        XamlBindingCheck named = await CheckAsync(session, "Named", "Text", compilesByDefault: true);

        Assert.Equal(XamlBindingPathStatus.Resolved, named.Status);
        Assert.Equal(typeof(CheckBox), named.SourceType);

        XamlBindingCheck missing = await CheckAsync(session, "Missing", "Text", compilesByDefault: true);

        Assert.Equal(XamlBindingPathStatus.Broken, missing.Status);
        Assert.Equal("Nowhere", missing.Step);
        Assert.True(missing.BreaksBuild);

        // A template is a name scope of its own: what is named inside it is not seen from outside.
        Assert.Equal(XamlBindingPathStatus.Broken, (await CheckAsync(session, "Outside", "Text", compilesByDefault: true)).Status);
    }

    [AvaloniaFact]
    public async Task ItselfAndAnAncestorAreReadFromTheDocumentAndAnAncestorBeyondItFromItsType()
    {
        await using XamlLoadSession session = await LoadAsync(
            Head("UserControl") +
            "<Grid>" +
            "<StackPanel x:Name=\"Panel\">" +
            "<TextBlock x:Name=\"Self\" Text=\"{Binding $self.Name}\" />" +
            "<TextBlock x:Name=\"Parent\" Text=\"{Binding $parent[StackPanel].Orientation}\" />" +
            "<TextBlock x:Name=\"Found\" Text=\"{Binding Name, RelativeSource={RelativeSource FindAncestor, AncestorType={x:Type UserControl}}}\" />" +
            "<TextBlock x:Name=\"Window\" Text=\"{Binding $parent[Window].Title}\" />" +
            "<TextBlock x:Name=\"Templated\" Text=\"{Binding Name, RelativeSource={RelativeSource TemplatedParent}}\" />" +
            "<TextBlock x:Name=\"Second\" Text=\"{Binding $parent[Panel;2].ColumnDefinitions}\" />" +
            "</StackPanel></Grid></UserControl>");

        XamlBindingCheck self = await CheckAsync(session, "Self", "Text", compilesByDefault: true);

        Assert.Equal(XamlBindingSource.Self, self.Source);
        Assert.Equal(typeof(TextBlock), self.SourceType);
        Assert.Equal(XamlBindingPathStatus.Resolved, self.Status);

        XamlBindingCheck parent = await CheckAsync(session, "Parent", "Text", compilesByDefault: true);

        Assert.Equal(XamlBindingSource.Ancestor, parent.Source);
        Assert.Equal(typeof(StackPanel), parent.SourceType);
        Assert.Equal(typeof(Orientation), parent.ResultType);

        Assert.Equal(typeof(UserControl), (await CheckAsync(session, "Found", "Text", compilesByDefault: true)).SourceType);

        // The window the form will stand in is not in the document: a build reads the type the binding names.
        XamlBindingCheck window = await CheckAsync(session, "Window", "Text", compilesByDefault: true);

        Assert.Equal(typeof(Window), window.SourceType);
        Assert.Equal(XamlBindingPathStatus.Resolved, window.Status);

        XamlBindingCheck templated = await CheckAsync(session, "Templated", "Text", compilesByDefault: true);

        Assert.Equal(XamlBindingSource.Other, templated.Source);
        Assert.Equal(XamlBindingPathStatus.NotUnderstood, templated.Status);

        // The level counts ancestors of the type: the second panel above is the grid.
        XamlBindingCheck second = await CheckAsync(session, "Second", "Text", compilesByDefault: true);

        Assert.Equal(typeof(Grid), second.SourceType);
        Assert.Equal(typeof(ColumnDefinitions), second.ResultType);
    }

    [AvaloniaFact]
    public async Task ADataTemplateDoesNotInheritTheDataTypeAroundIt()
    {
        await using XamlLoadSession session = await LoadAsync(
            Head("UserControl", "x:DataType=\"tc:GreetingModel\"") +
            "<StackPanel>" +
            "<ItemsControl><ItemsControl.ItemTemplate><DataTemplate>" +
            "<TextBlock x:Name=\"Untyped\" Text=\"{Binding Name}\" />" +
            "</DataTemplate></ItemsControl.ItemTemplate></ItemsControl>" +
            "<ItemsControl><ItemsControl.ItemTemplate><DataTemplate x:DataType=\"tc:CustomBadge\">" +
            "<TextBlock x:Name=\"Typed\" Text=\"{Binding Caption}\" />" +
            "</DataTemplate></ItemsControl.ItemTemplate></ItemsControl>" +
            "</StackPanel></UserControl>");

        XamlBindingCheck untyped = await CheckAsync(session, "Untyped", "Text", compilesByDefault: true);

        Assert.Equal(XamlBindingPathStatus.Broken, untyped.Status);
        Assert.Null(untyped.Step);

        XamlBindingCheck typed = await CheckAsync(session, "Typed", "Text", compilesByDefault: true);

        Assert.Equal(XamlBindingPathStatus.Resolved, typed.Status);
        Assert.Equal(typeof(CustomBadge), typed.SourceType);
    }

    [AvaloniaFact]
    public async Task DataTheRootInheritsFromItsHostIsNotTheDocumentsData()
    {
        await using XamlLoadSession session = await LoadAsync(
            Head("UserControl") + "<TextBlock x:Name=\"Text\" Text=\"{Binding Length}\" /></UserControl>");

        var window = new Window { Content = new ContentControl { DataContext = "the host's", Content = session.RootObject } };

        window.Show();

        try
        {
            XamlBindingCheck text = await CheckAsync(session, "Text", "Text", compilesByDefault: false);

            // "Length" is a member of the host's string; claiming it resolved would be reading the host.
            Assert.Equal(XamlBindingPathStatus.NotUnderstood, text.Status);
            Assert.Null(text.SourceType);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AnElementOfAnotherParseIsRefused()
    {
        string text = Head("UserControl") + "<TextBlock Text=\"{Binding Name}\" /></UserControl>";

        await using XamlLoadSession session = await LoadAsync(text);

        XamlElement foreign = XamlDocument.Parse(text).Root!.ContentElements.Single();

        await Assert.ThrowsAsync<ArgumentException>(() => session.CheckBindingAsync(
            foreign, "Text", (XamlMarkupExtensionValue)XamlValue.Parse("{Binding Name}"), compilesByDefault: true,
            TestContext.Current.CancellationToken).AsTask());
    }

    private static async Task<XamlLoadSession> LoadAsync(string text) =>
        await XamlLoadSession.CreateAsync(
            XamlDocument.Parse(text, new XamlParseOptions { DocumentUri = new Uri("file:///Views/Bindings.axaml") }),
            XamlLoadEnvironment.CreateDefault([typeof(GreetingModel).Assembly], new InMemoryMarkupSourceProvider()),
            new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

    private static ValueTask<XamlBindingCheck> CheckAsync(XamlLoadSession session, string name, string member, bool compilesByDefault)
    {
        XamlElement element = session.Document.DescendantElements().Single(candidate => candidate.Identity == name);
        var binding = (XamlMarkupExtensionValue)element.GetAttribute(member)!.GetValue()!;

        return session.CheckBindingAsync(element, member, binding, compilesByDefault, TestContext.Current.CancellationToken);
    }
}
