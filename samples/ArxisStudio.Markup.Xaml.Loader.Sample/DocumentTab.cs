using System;
using System.Linq;
using ArxisStudio.Markup.Xaml;
using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// The syntax packages on their own: round-trip, recovery, and an edit that disturbs nothing.
/// </summary>
/// <remarks>
/// None of this needs Avalonia to be present. It is the half of the library a tool can use
/// without ever building an object.
/// </remarks>
internal static class DocumentTab
{
    /// <summary>Builds the tab.</summary>
    /// <returns>The tab's content.</returns>
    internal static Control Build()
    {
        var document = XamlDocument.Parse(
            Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri });

        var malformed = XamlDocument.Parse(Fixtures.Malformed);

        XamlElement button = document.DescendantElements()
            .First(static element => element.Name.LocalName == "Button");
        XamlElement panel = document.DescendantElements()
            .First(static element => element.Name.LocalName == "StackPanel");

        XamlDocument edited = document.Edit()
            .SetAttribute(button, XamlQualifiedName.Parse("Width"), "160")
            .InsertElement(panel, 2, "<TextBlock Text=\"Added by an edit\" />")
            .Apply();

        return Ui.Page(Ui.Stack(
            Ui.Heading("The document is the source of truth"),
            Ui.Body(
                "A document keeps the exact snapshot it was parsed from, and every node points " +
                "into it. Writing an unchanged one back is a copy rather than a reformat, and an " +
                "edit is expressed as changes against its own spans so nothing else is rewritten."),

            Ui.Field("characters", document.SourceText.Length.ToString()),
            Ui.Field("lines", document.SourceText.Lines.Count.ToString()),
            Ui.Field("tokens", document.Tokens.Length.ToString()),
            Ui.Field("elements", document.DescendantElements().Count().ToString()),
            Ui.Verdict("GetText() returns the original text, byte for byte", document.GetText() == Fixtures.View),

            Ui.Caption("two edits, batched, applied together"),
            Ui.Code(Region(edited, "StackPanel")),
            Ui.Verdict(
                "the binding is untouched",
                edited.GetText().Contains("Text=\"{Binding Customer.Name}\"", StringComparison.Ordinal)),
            Ui.Verdict(
                "the odd spacing before Spacing is untouched",
                edited.GetText().Contains("Orientation=\"Vertical\"     Spacing", StringComparison.Ordinal)),
            Ui.Verdict(
                "the comment is untouched",
                edited.GetText().Contains("<!-- The resources this view uses", StringComparison.Ordinal)),

            Ui.Caption("a document caught mid-keystroke"),
            Ui.Code(Fixtures.Malformed),
            Ui.Verdict("parsing did not throw", true),
            Ui.Verdict("it still round-trips exactly", malformed.GetText() == Fixtures.Malformed),
            Ui.Diagnostics(malformed.GetDiagnostics(), malformed.SourceText),

            Ui.Caption("what the syntax model can say about a value, with no CLR type in sight"),
            Ui.Stack([.. document.DescendantElements()
                .SelectMany(static element => element.Attributes
                    .Where(static attribute => attribute.GetValue() is XamlMarkupExtensionValue)
                    .Select(attribute => Ui.Field(
                        $"{element.Name}.{attribute.Name}",
                        Describe((XamlMarkupExtensionValue)attribute.GetValue()))))])));
    }

    private static string Describe(XamlMarkupExtensionValue extension) =>
        $"{extension.TypeName} — " +
        string.Join(", ", extension.Arguments.Select(static argument => argument.ToString()));

    private static string Region(XamlDocument document, string localName) =>
        document.DescendantElements().First(element => element.Name.LocalName == localName).GetSourceText();
}
