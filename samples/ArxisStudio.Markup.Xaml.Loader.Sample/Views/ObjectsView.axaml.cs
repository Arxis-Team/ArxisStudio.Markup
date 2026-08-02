using System;
using System.Globalization;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.Sample.Reporting;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Views;

/// <summary>
/// Which markup each loaded object came from, and what kind of thing produced it.
/// </summary>
internal sealed partial class ObjectsView : UserControl
{
    private readonly Report _objects = new();
    private readonly Report _diagnostics = new();

    private bool _started;

    public ObjectsView()
    {
        InitializeComponent();

        Objects.ItemsSource = _objects.Rows;
        DiagnosticList.ItemsSource = _diagnostics.Rows;
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (!_started)
        {
            _started = true;

            _ = FillAsync();
        }
    }

    private async Task FillAsync()
    {
        (XamlLoadEnvironment environment, _) = ShowcaseEnvironment.Create();

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri }),
            environment,
            new XamlLoadOptions { Mode = XamlLoadMode.Runtime });

        _diagnostics.Clear().Diagnostics(result.Diagnostics);

        if (session is null)
        {
            Summary.Text = "THE DOCUMENT PRODUCED NO OBJECTS";

            return;
        }

        int anonymous = 0;

        foreach (object target in session.Objects.Objects)
        {
            XamlElement? element = session.GetElement(target);
            Uri? source = session.GetSourceUri(target);

            // Every control carries a Resources dictionary whether the document mentions one or
            // not, and the walk reaches all of them. Listing a dozen empty dictionaries nothing
            // declared buries the rows that say something.
            if (element is null && source is null)
            {
                anonymous++;

                continue;
            }

            _objects.Mapped(new ObjectRow(
                target.GetType().Name,
                session.GetOrigin(target).ToString(),
                source?.Segments[^1] ?? "this document",
                element is null ? "declared there" : $"line {Line(session, element)}: <{element.Name}>"));
        }

        Summary.Text =
            $"{session.Objects.Objects.Count.ToString(CultureInfo.InvariantCulture)} OBJECTS REACHED; " +
            "THOSE WITH MARKUP BEHIND THEM, IN WALK ORDER";

        Footnote.Text =
            $"and {anonymous.ToString(CultureInfo.InvariantCulture)} more the walk reached that no " +
            "markup declares — the empty Resources dictionary every control carries, and the " +
            "default template the theme gave the button.";

        await session.DisposeAsync();
    }

    private static string Line(XamlLoadSession session, XamlElement element) =>
        (session.Document.SourceText.Lines.GetPosition(element.Span.Start).Line + 1)
            .ToString(CultureInfo.InvariantCulture);
}
