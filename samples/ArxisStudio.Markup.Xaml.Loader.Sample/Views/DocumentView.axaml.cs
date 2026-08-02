using System;
using System.Globalization;
using System.Linq;
using ArxisStudio.Markup.Xaml.Loader.Sample.Reporting;
using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Views;

/// <summary>
/// The syntax packages on their own: round-trip, recovery, and an edit that disturbs nothing.
/// </summary>
internal sealed partial class DocumentView : UserControl
{
    public DocumentView()
    {
        InitializeComponent();

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

        Facts.ItemsSource = new Report()
            .Field("characters", Count(document.SourceText.Length))
            .Field("lines", Count(document.SourceText.Lines.Count))
            .Field("tokens", Count(document.Tokens.Length))
            .Field("elements", Count(document.DescendantElements().Count()))
            .Verdict("GetText() returns the original text, byte for byte", document.GetText() == Fixtures.View)
            .Rows;

        EditedRegion.Text = Region(edited, "StackPanel");

        EditFacts.ItemsSource = new Report()
            .Verdict(
                "the binding is untouched",
                edited.GetText().Contains("Text=\"{Binding Customer.Name}\"", StringComparison.Ordinal))
            .Verdict(
                "the odd spacing before Spacing is untouched",
                edited.GetText().Contains("Orientation=\"Vertical\"     Spacing", StringComparison.Ordinal))
            .Verdict(
                "the comment is untouched",
                edited.GetText().Contains("<!-- The resources this view uses", StringComparison.Ordinal))
            .Rows;

        MalformedText.Text = Fixtures.Malformed;

        RecoveryFacts.ItemsSource = new Report()
            .Verdict("parsing did not throw", true)
            .Verdict("it still round-trips exactly", malformed.GetText() == Fixtures.Malformed)
            .Caption("DIAGNOSTICS")
            .Diagnostics(malformed.GetDiagnostics(), malformed.SourceText)
            .Rows;

        var extensions = new Report();

        foreach (XamlElement element in document.DescendantElements())
        {
            foreach (XamlAttribute attribute in element.Attributes)
            {
                if (attribute.GetValue() is XamlMarkupExtensionValue extension)
                {
                    extensions.Field($"{element.Name}.{attribute.Name}", Describe(extension));
                }
            }
        }

        Extensions.ItemsSource = extensions.Rows;
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Describe(XamlMarkupExtensionValue extension) =>
        $"{extension.TypeName} — " +
        string.Join(", ", extension.Arguments.Select(static argument => argument.ToString()));

    private static string Region(XamlDocument document, string localName) =>
        document.DescendantElements().First(element => element.Name.LocalName == localName).GetSourceText();
}
