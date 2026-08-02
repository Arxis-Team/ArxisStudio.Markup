using System;
using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// A reference to an element that survives the document being edited, which is what a tool keeps
/// a selection in.
/// </summary>
public sealed class XamlElementPathTests
{
    private const string Source =
        "<StackPanel xmlns=\"https://github.com/avaloniaui\"\n" +
        "            xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
        "  <StackPanel.Resources>\n" +
        "    <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
        "    <SolidColorBrush x:Key=\"Line\" Color=\"Gray\" />\n" +
        "  </StackPanel.Resources>\n" +
        "  <TextBlock x:Name=\"Title\" Text=\"Title\" />\n" +
        "  <Border x:Name=\"Card\">\n" +
        "    <Button x:Name=\"Save\" Content=\"Save\" />\n" +
        "  </Border>\n" +
        "</StackPanel>";

    private static XamlDocument Parse(string text = Source) => XamlDocument.Parse(text);

    private static XamlElement Named(XamlDocument document, string name) =>
        document.DescendantElements().Single(element => element.Identity == name);

    [Fact]
    public void APathLeadsBackToTheElementItDescribes()
    {
        XamlDocument document = Parse();
        XamlElement save = Named(document, "Save");

        Assert.Same(save, XamlElementPath.Of(save).Resolve(document));
    }

    [Fact]
    public void APathReachesThroughAMember()
    {
        XamlDocument document = Parse();

        XamlElement line = document.DescendantElements()
            .Single(element => element.GetDirective(XamlDirectives.Key) == "Line");

        XamlElementPath path = XamlElementPath.Of(line);

        // A property element is the name of the step rather than a step of its own, so the brush
        // is the second child of the member called Resources.
        Assert.Equal(new XamlPathStep("Resources", 1), path.Steps.Single());
        Assert.Same(line, path.Resolve(document));
    }

    [Fact]
    public void APathSurvivesAnEditSomewhereElse()
    {
        XamlDocument document = Parse();
        XamlElementPath path = XamlElementPath.Of(Named(document, "Save"));

        XamlDocument edited = document.SetAttribute(
            Named(document, "Title"), XamlQualifiedName.Parse("Text"), "Something longer than before");

        // Every element in the new document is a different object at a different offset. The
        // path is the same path, and it still means the button.
        XamlElement? save = path.Resolve(edited);

        Assert.NotNull(save);
        Assert.Equal("Save", save!.Identity);
    }

    [Fact]
    public void APathToTheRootIsNoStepsAtAll()
    {
        XamlDocument document = Parse();

        Assert.Empty(XamlElementPath.Of(document.Root!).Steps);
        Assert.Same(document.Root, XamlElementPath.Root.Resolve(document));
    }

    [Fact]
    public void APathDoesNotResolveWhereTheDocumentNoLongerHasOne()
    {
        XamlDocument document = Parse();
        XamlElementPath path = XamlElementPath.Of(Named(document, "Save"));

        XamlDocument emptied = document.RemoveElement(Named(document, "Save"));

        Assert.Null(path.Resolve(emptied));
    }

    [Fact]
    public void PathsAreEqualByWhatTheySay()
    {
        XamlDocument document = Parse();
        XamlDocument other = Parse();

        XamlElementPath one = XamlElementPath.Of(Named(document, "Save"));
        XamlElementPath two = XamlElementPath.Of(Named(other, "Save"));

        // Two parses of the same text, two sets of elements, one path — which is what lets a path
        // key the expanded nodes of a tree across an edit.
        Assert.Equal(one, two);
        Assert.Equal(one.GetHashCode(), two.GetHashCode());
        Assert.NotEqual(one, XamlElementPath.Of(Named(document, "Title")));
    }

    [Fact]
    public void ContentAndMembersAreToldApart()
    {
        XamlDocument document = Parse();
        XamlElement root = document.Root!;

        Assert.Equal(["TextBlock", "Border"], root.ContentElements.Select(element => element.Name.LocalName));
        Assert.Equal(["StackPanel.Resources"], root.MemberElements.Select(element => element.Name.ToString()));

        // The index a caller means: the border is the second control, whatever the member
        // written above it.
        Assert.Equal(1, Named(document, "Card").IndexInContent);
        Assert.Equal(-1, root.IndexInContent);
    }
}
