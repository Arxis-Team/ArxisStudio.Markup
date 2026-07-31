using System;
using System.Collections.Immutable;
using System.Linq;
using ArxisStudio.Markup.Xaml;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// What the two lower packages do, none of which needs Avalonia to be present at all.
/// </summary>
internal static class SyntaxShowcase
{
    /// <summary>Parses a document and proves it writes back byte for byte.</summary>
    internal static XamlDocument RoundTrip()
    {
        Report.Section(1, "Parsing, and the round-trip guarantee");
        Report.Note(
            "A document keeps the exact snapshot it was parsed from, and every node points into " +
            "it. Writing an unchanged document back is a copy, not a reformat — comments, blank " +
            "lines, the two spaces before Spacing, all of it.");

        var document = XamlDocument.Parse(
            Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri });

        Report.Value("characters", document.SourceText.Length);
        Report.Value("lines", document.SourceText.Lines.Count);
        Report.Value("tokens", document.Tokens.Length);
        Report.Value("elements", document.DescendantElements().Count());
        Report.Check("GetText() == the original text", document.GetText() == Fixtures.View);
        Report.Check("well formed", document.IsWellFormed);

        return document;
    }

    /// <summary>Shows that a document which does not parse is still a document.</summary>
    internal static void Malformed()
    {
        Report.Section(2, "Malformed input recovers instead of throwing");
        Report.Note(
            "Parsing never throws. A file caught mid-keystroke still has a tree, still covers all " +
            "of its text, and still writes back exactly — what it also has is diagnostics with " +
            "stable codes and the spans they concern.");

        var document = XamlDocument.Parse(Fixtures.Malformed);

        Report.Block("as written", Fixtures.Malformed);
        Report.Value("root element", document.Root?.Name.ToString());
        Report.Check("still round-trips exactly", document.GetText() == Fixtures.Malformed);
        Report.Diagnostics("diagnostics", document.GetDiagnostics(), document.SourceText);
    }

    /// <summary>Edits one attribute and shows that nothing else moved.</summary>
    internal static void Edit(XamlDocument document)
    {
        Report.Section(3, "Editing one attribute disturbs nothing else");
        Report.Note(
            "An edit is expressed as text changes against the document's own spans, so the rest " +
            "of the file is not rewritten. Several edits batched through one editor are computed " +
            "against the same text and applied together.");

        XamlElement button = document.DescendantElements()
            .First(static element => element.Name.LocalName == "Button");
        XamlElement panel = document.DescendantElements()
            .First(static element => element.Name.LocalName == "StackPanel");

        XamlDocumentEditor editor = document.Edit()
            .SetAttribute(button, XamlQualifiedName.Parse("Width"), "160")
            .SetAttribute(button, XamlQualifiedName.Parse("IsDefault"), "True")
            .InsertElement(panel, 2, "<TextBlock Text=\"Added by the sample\" />");

        ImmutableArray<TextChange> changes = editor.GetTextChanges();
        XamlDocument edited = editor.Apply();

        Report.Value("text changes", changes.Length);
        Report.Value("characters added", edited.SourceText.Length - document.SourceText.Length);
        Report.Check(
            "the binding on Title is untouched",
            edited.GetText().Contains("Text=\"{Binding Customer.Name}\"", StringComparison.Ordinal));
        Report.Check(
            "the odd spacing before Spacing is untouched",
            edited.GetText().Contains("Orientation=\"Vertical\"     Spacing", StringComparison.Ordinal));
        Report.Check(
            "the comment is untouched",
            edited.GetText().Contains("<!-- The resources this view uses", StringComparison.Ordinal));

        Report.Block("the edited region", Region(edited, "StackPanel"));
    }

    /// <summary>Shows what the syntax model can say about values without resolving anything.</summary>
    internal static void Values(XamlDocument document)
    {
        Report.Section(4, "Values, namespaces and directives");
        Report.Note(
            "The syntax package reads what a value is — a literal, or a markup extension with its " +
            "arguments — and resolves prefixes by scope. It never decides what a name means: no " +
            "CLR type is touched here, and none is available.");

        foreach (XamlElement element in document.DescendantElements()
            .Where(static element => element.Attributes.Any(static a => a.GetValue() is XamlMarkupExtensionValue)))
        {
            foreach (XamlAttribute attribute in element.Attributes)
            {
                if (attribute.GetValue() is XamlMarkupExtensionValue extension)
                {
                    Report.Value(
                        $"{element.Name}.{attribute.Name}",
                        $"{extension.TypeName} with {extension.Arguments.Length} argument(s): " +
                        string.Join(", ", extension.Arguments.Select(static a => a.ToString())));
                }
            }
        }

        XamlElement deepest = document.DescendantElements().Last();

        Report.Value("deepest element", deepest.Name);
        Report.Value("default namespace there", deepest.NamespaceContext.LookupNamespace(null));
        Report.Value("'d' resolves to", deepest.NamespaceContext.LookupNamespace("d"));
        Report.Value(
            "design-time attributes",
            document.DescendantElements().Sum(static e => e.DesignTimeAttributes.Count()));
        Report.Value("root x:Class", document.Root?.GetDirective("Class") ?? "<none>");
    }

    /// <summary>Prints the text of the first element with a given name, with its indentation.</summary>
    private static string Region(XamlDocument document, string localName)
    {
        XamlElement element = document.DescendantElements()
            .First(candidate => candidate.Name.LocalName == localName);

        return element.GetSourceText();
    }
}
