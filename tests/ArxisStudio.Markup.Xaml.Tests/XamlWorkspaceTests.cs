using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// The seam a designer is built on: a structured XAML edit that goes into the workspace's history
/// and comes back out of it unchanged.
/// </summary>
public sealed class XamlWorkspaceTests
{
    private static readonly Uri ViewUri = new("file:///Views/View.axaml");
    private static readonly Uri ThemeUri = new("file:///Themes/Theme.axaml");

    private const string View =
        "<StackPanel xmlns=\"https://github.com/avaloniaui\"\n" +
        "            xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
        "  <!-- a comment nobody asked to move -->\n" +
        "  <TextBlock x:Name=\"Title\" Text=\"Before\" />\n" +
        "  <Button x:Name=\"Save\" Content=\"Save\" />\n" +
        "</StackPanel>\n";

    private const string Theme =
        "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"\n" +
        "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
        "  <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
        "</ResourceDictionary>\n";

    private static XamlWorkspace Workspace()
    {
        var provider = new InMemoryMarkupSourceProvider();

        provider.Update(ViewUri, View);
        provider.Update(ThemeUri, Theme);

        return new XamlWorkspace(new MarkupWorkspace(provider));
    }

    private static XamlElement Named(XamlDocument document, string name) =>
        document.DescendantElements().Single(element => element.GetDirective(XamlDirectives.Name) == name);

    [Fact]
    public async Task AStructuredEditBecomesOneUndoableAction()
    {
        XamlWorkspace workspace = Workspace();
        XamlDocument document = await workspace.OpenAsync(ViewUri, TestContext.Current.CancellationToken);

        XamlDocument edited = workspace.Apply(
            document.Edit()
                .SetAttribute(Named(document, "Title"), XamlQualifiedName.Parse("Text"), "After")
                .SetAttribute(Named(document, "Save"), XamlQualifiedName.Parse("Width"), "120"),
            "Правка заголовка");

        Assert.Contains("Text=\"After\"", edited.GetText(), StringComparison.Ordinal);
        Assert.Contains("Width=\"120\"", edited.GetText(), StringComparison.Ordinal);

        // Two attributes, one action: the user asked for one thing and undoes one thing.
        Assert.True(workspace.CanUndo);
        Assert.Equal("Правка заголовка", workspace.UndoDescription);

        Assert.True(workspace.Undo());
        Assert.Equal(View, workspace.GetDocument(Id(workspace)).GetText());
    }

    [Fact]
    public async Task EditsToTwoDocumentsAreOneActionOrNone()
    {
        XamlWorkspace workspace = Workspace();
        XamlDocument view = await workspace.OpenAsync(ViewUri, TestContext.Current.CancellationToken);
        XamlDocument theme = await workspace.OpenAsync(ThemeUri, TestContext.Current.CancellationToken);

        MarkupDocumentId viewId = Id(workspace);
        MarkupDocumentId themeId = Id(workspace, ThemeUri);

        workspace.Apply(
            "Перекрасить акцент",
            view.Edit().SetAttribute(
                Named(view, "Title"), XamlQualifiedName.Parse("Foreground"), "{StaticResource Accent}"),
            theme.Edit().SetAttribute(
                theme.DescendantElements().First(element => element.Name.LocalName == "SolidColorBrush"),
                XamlQualifiedName.Parse("Color"),
                "Blue"));

        Assert.Contains("Foreground=\"{StaticResource Accent}\"", workspace.GetDocument(viewId).GetText(), StringComparison.Ordinal);
        Assert.Contains("Color=\"Blue\"", workspace.GetDocument(themeId).GetText(), StringComparison.Ordinal);

        // One command, both files: undoing half of it would leave the view pointing at a colour
        // the theme no longer has.
        Assert.True(workspace.Undo());
        Assert.Equal(View, workspace.GetDocument(viewId).GetText());
        Assert.Equal(Theme, workspace.GetDocument(themeId).GetText());
    }

    [Fact]
    public async Task TwoEditorsForOneDocumentAreRefused()
    {
        XamlWorkspace workspace = Workspace();
        XamlDocument document = await workspace.OpenAsync(ViewUri, TestContext.Current.CancellationToken);

        // Both were computed against the same text, so the second one's spans do not describe
        // the document the first one leaves behind.
        Assert.Throws<InvalidOperationException>(
            () => workspace.Apply(
                "Две правки одного файла",
                document.Edit().RemoveElement(Named(document, "Save")),
                document.Edit().RemoveElement(Named(document, "Title"))));
    }

    [Fact]
    public async Task UndoingRestoresTheDocumentCharacterForCharacter()
    {
        XamlWorkspace workspace = Workspace();
        XamlDocument document = await workspace.OpenAsync(ViewUri, TestContext.Current.CancellationToken);
        MarkupDocumentId id = Id(workspace);

        workspace.Apply(
            document.Edit().RemoveElement(Named(document, "Save")),
            "Удалить Save");

        Assert.DoesNotContain("x:Name=\"Save\"", workspace.GetDocument(id).GetText(), StringComparison.Ordinal);
        Assert.True(workspace.Undo());

        // Not "close enough": the comment, the indentation and the line endings are all back.
        Assert.Equal(View, workspace.GetDocument(id).GetText());
    }

    [Fact]
    public async Task RedoingPutsTheEditBack()
    {
        XamlWorkspace workspace = Workspace();
        XamlDocument document = await workspace.OpenAsync(ViewUri, TestContext.Current.CancellationToken);
        MarkupDocumentId id = Id(workspace);

        XamlDocument edited = workspace.Apply(
            document.Edit().WrapElement(Named(document, "Save"), "<Border></Border>"),
            "Обернуть Save");

        string wrapped = edited.GetText();

        Assert.True(workspace.Undo());
        Assert.Equal(View, workspace.GetDocument(id).GetText());

        Assert.True(workspace.Redo());
        Assert.Equal(wrapped, workspace.GetDocument(id).GetText());
    }

    [Fact]
    public async Task AnEditorOpenedOnAVersionTheWorkspaceHasMovedPastIsRefused()
    {
        XamlWorkspace workspace = Workspace();
        XamlDocument document = await workspace.OpenAsync(ViewUri, TestContext.Current.CancellationToken);

        workspace.Apply(
            document.Edit().SetAttribute(Named(document, "Title"), XamlQualifiedName.Parse("Text"), "After"),
            "Первая правка");

        // The spans in this editor point into text the workspace no longer holds. Applying them
        // would cut the document in the wrong places, so it is refused rather than approximated.
        Assert.Throws<InvalidOperationException>(
            () => workspace.Apply(
                document.Edit().SetAttribute(Named(document, "Save"), XamlQualifiedName.Parse("Width"), "120"),
                "Вторая правка"));
    }

    [Fact]
    public void ADocumentThatIsNotOpenIsRefused()
    {
        XamlWorkspace workspace = Workspace();
        var loose = XamlDocument.Parse(View, new XamlParseOptions { DocumentUri = new Uri("file:///Elsewhere.axaml") });

        Assert.Throws<InvalidOperationException>(
            () => workspace.Apply(loose.Edit().RemoveElement(Named(loose, "Save")), "Правка чужого документа"));
    }

    [Fact]
    public async Task AChangeIsAnnouncedWithTheDocumentItProduced()
    {
        XamlWorkspace workspace = Workspace();
        XamlDocument document = await workspace.OpenAsync(ViewUri, TestContext.Current.CancellationToken);

        XamlDocumentChangedEventArgs? announced = null;

        workspace.DocumentChanged += (_, e) => announced = e;

        workspace.Apply(
            document.Edit().SetAttribute(Named(document, "Title"), XamlQualifiedName.Parse("Text"), "After"),
            "Правка");

        Assert.NotNull(announced);
        Assert.Equal(DocumentChangeKind.Changed, announced!.Kind);
        Assert.Contains("Text=\"After\"", announced.Document!.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AskingTwiceForAnUnchangedDocumentParsesItOnce()
    {
        XamlWorkspace workspace = Workspace();

        await workspace.OpenAsync(ViewUri, TestContext.Current.CancellationToken);

        MarkupDocumentId id = Id(workspace);

        // The same object, not an equal one: a tool redrawing a tree asks constantly, and every
        // ask must not be a parse.
        Assert.Same(workspace.GetDocument(id), workspace.GetDocument(id));
    }

    private static MarkupDocumentId Id(XamlWorkspace workspace, Uri? uri = null) =>
        workspace.Workspace.Documents.Single(document => document.Uri == (uri ?? ViewUri)).Id;
}
