using System;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// Which markup each loaded object came from, and what kind of thing produced it.
/// </summary>
/// <remarks>
/// A read-only report of what the library answers when asked. It is not a property inspector:
/// nothing here edits anything, and no object is selected in or drawn over the preview — those
/// are the designer's job, and the contract puts the designer outside this repository.
/// </remarks>
internal static class ObjectsTab
{
    /// <summary>Builds the tab.</summary>
    /// <returns>The tab's content.</returns>
    internal static async Task<Control> BuildAsync()
    {
        (XamlLoadEnvironment environment, _) = ShowcaseEnvironment.Create();

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri }),
            environment,
            new XamlLoadOptions { Mode = XamlLoadMode.Runtime });

        if (session is null)
        {
            return Ui.Page(Ui.Stack(
                Ui.Heading("Object map"),
                Ui.Diagnostics(result.Diagnostics)));
        }

        var rows = new StackPanel { Spacing = 2 };
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

            rows.Children.Add(Ui.Field(
                target.GetType().Name,
                $"{session.GetOrigin(target),-10} " +
                (element is null
                    ? $"declared in {source?.Segments[^1]}"
                    : $"line {Line(session, element)} of {source?.Segments[^1] ?? "this document"}: <{element.Name}>")));
        }

        rows.Children.Add(new TextBlock
        {
            Text = $"and {anonymous} more the walk reached that no markup declares — the empty " +
                "Resources dictionary every control carries, and the default template the theme " +
                "gave the button.",
            FontSize = 11,
            Opacity = 0.5,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
        });

        Control content = Ui.Page(Ui.Stack(
            Ui.Heading("Which markup each object came from"),
            Ui.Body(
                "Built from the source information Avalonia records, read back through the " +
                "projection the includes were spliced into. That is what keeps an object " +
                "declared in an included file attributed to that file rather than to whichever " +
                "line of this one sits at the same number — and what stops a template's output " +
                "being passed off as the control's own declaration."),
            Ui.Caption($"{session.Objects.Objects.Count} objects reached; those with markup behind them, in walk order"),
            rows,
            Ui.Caption("diagnostics"),
            Ui.Diagnostics(result.Diagnostics)));

        await session.DisposeAsync();

        return content;
    }

    private static int Line(XamlLoadSession session, XamlElement element) =>
        session.Document.SourceText.Lines.GetPosition(element.Span.Start).Line + 1;
}
