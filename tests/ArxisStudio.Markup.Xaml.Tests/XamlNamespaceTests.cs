using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

public sealed class XamlNamespaceTests
{
    private static XamlElement Find(XamlDocument document, string localName) =>
        document.DescendantElements().First(element => element.Name.LocalName == localName);

    [Fact]
    public void XmlnsAttributesBecomeNamespaceDeclarations()
    {
        XamlElement root = XamlDocument.Parse(
            "<a xmlns=\"urn:default\" xmlns:p=\"urn:p\" Width=\"1\" />").Root!;

        Assert.Equal(3, root.Attributes.Length);

        XamlNamespaceDeclaration[] declarations = [.. root.NamespaceDeclarations];

        Assert.Equal(2, declarations.Length);
        Assert.True(declarations[0].IsDefault);
        Assert.Equal("urn:default", declarations[0].GetNamespaceUri());
        Assert.Equal("p", declarations[1].Prefix);
        Assert.Equal("urn:p", declarations[1].GetNamespaceUri());
    }

    [Fact]
    public void DeclarationsStayInTheAttributeListInSourceOrder()
    {
        // They are attributes, and writing the tag back has to reproduce them where they were.
        XamlElement root = XamlDocument.Parse("<a Width=\"1\" xmlns=\"urn:d\" Height=\"2\" />").Root!;

        Assert.Equal(
            ["Width", "xmlns", "Height"],
            root.Attributes.Select(static attribute => attribute.Name.LocalName));
    }

    [Fact]
    public void PrefixesResolveThroughEnclosingElements()
    {
        XamlDocument document = XamlDocument.Parse(
            "<a xmlns:p=\"urn:p\"><b><p:c /></b></a>");

        Assert.Equal("urn:p", Find(document, "c").NamespaceUri);
    }

    [Fact]
    public void AnInnerDeclarationShadowsAnOuterOne()
    {
        XamlDocument document = XamlDocument.Parse(
            "<a xmlns:p=\"urn:outer\"><p:outer /><b xmlns:p=\"urn:inner\"><p:inner /></b></a>");

        Assert.Equal("urn:outer", Find(document, "outer").NamespaceUri);
        Assert.Equal("urn:inner", Find(document, "inner").NamespaceUri);
    }

    [Fact]
    public void TheDefaultNamespaceAppliesToUnprefixedElements()
    {
        XamlDocument document = XamlDocument.Parse("<a xmlns=\"urn:d\"><b /></a>");

        Assert.Equal("urn:d", Find(document, "b").NamespaceUri);
    }

    [Fact]
    public void AnUnprefixedAttributeIsInNoNamespaceEvenWithADefaultDeclared()
    {
        // The XML namespaces specification is explicit about this asymmetry, and getting it
        // wrong would silently reinterpret every attribute in an Avalonia document.
        XamlElement root = XamlDocument.Parse("<a xmlns=\"urn:d\" Width=\"1\" />").Root!;
        XamlAttribute width = root.GetAttribute("Width")!;

        Assert.False(root.NamespaceContext.TryResolveAttributeName(width.Name, out _));
        Assert.True(root.NamespaceContext.TryResolveElementName(root.Name, out string? elementNamespace));
        Assert.Equal("urn:d", elementNamespace);
    }

    [Fact]
    public void APrefixedAttributeResolvesThroughItsPrefix()
    {
        XamlElement root = XamlDocument.Parse("<a xmlns:x=\"urn:x\" x:Key=\"k\" />").Root!;
        XamlAttribute key = root.Attributes.Last();

        Assert.True(root.NamespaceContext.TryResolveAttributeName(key.Name, out string? uri));
        Assert.Equal("urn:x", uri);
    }

    [Fact]
    public void AnUndeclaredPrefixResolvesToNothingRatherThanThrowing()
    {
        XamlDocument document = XamlDocument.Parse("<a><missing:b /></a>");

        Assert.Null(Find(document, "b").NamespaceUri);
    }

    [Fact]
    public void UnknownNamespaceUrisAreKeptVerbatim()
    {
        // The package has no list of namespaces it accepts. A URI it has never seen is a URI.
        XamlDocument document = XamlDocument.Parse(
            "<a xmlns:future=\"urn:not-invented-yet\"><future:b /></a>");

        Assert.Equal("urn:not-invented-yet", Find(document, "b").NamespaceUri);
    }

    [Fact]
    public void PrefixesAreNeverAssumedToBeNamedXOrDOrMc()
    {
        // Nothing obliges a document to use the conventional prefixes.
        XamlDocument document = XamlDocument.Parse(
            $"<a xmlns:whatever=\"{XamlNamespaces.Xaml}\"><whatever:b /></a>");

        Assert.Equal(XamlNamespaces.Xaml, Find(document, "b").NamespaceUri);
        Assert.Equal("whatever", Find(document, "b").NamespaceContext.LookupPrefix(XamlNamespaces.Xaml));
    }

    [Fact]
    public void TheXmlPrefixResolvesWithoutBeingDeclared()
    {
        XamlElement root = XamlDocument.Parse("<a xml:space=\"preserve\" />").Root!;

        Assert.Equal(XamlNamespaces.Xml, root.NamespaceContext.LookupNamespace("xml"));
    }

    [Fact]
    public void AnEmptyUriUndeclaresANamespace()
    {
        XamlDocument document = XamlDocument.Parse(
            "<a xmlns=\"urn:d\"><b xmlns=\"\"><c /></b></a>");

        Assert.Null(Find(document, "c").NamespaceUri);
    }

    [Fact]
    public void InScopeDeclarationsReportTheInnermostBinding()
    {
        XamlDocument document = XamlDocument.Parse(
            "<a xmlns:p=\"urn:outer\" xmlns:q=\"urn:q\"><b xmlns:p=\"urn:inner\"><c /></b></a>");

        var inScope = Find(document, "c").NamespaceContext.GetInScopeDeclarations();

        Assert.Equal("urn:inner", inScope["p"]);
        Assert.Equal("urn:q", inScope["q"]);
    }

    [Fact]
    public void ARepeatedPrefixOnOneElementIsReported()
    {
        XamlDocument document = XamlDocument.Parse("<a xmlns:p=\"urn:1\" xmlns:p=\"urn:2\" />");

        Assert.Contains(
            document.Diagnostics,
            static diagnostic => diagnostic.Code == XamlDiagnosticCodes.DuplicateNamespacePrefix);
    }

    [Fact]
    public void ElementsThatDeclareNothingShareTheirParentsContext()
    {
        // Not adding a link per element keeps lookup cheap in deeply nested documents.
        XamlDocument document = XamlDocument.Parse("<a xmlns:p=\"urn:p\"><b><c /></b></a>");

        Assert.Same(Find(document, "b").NamespaceContext, Find(document, "c").NamespaceContext);
    }
}
