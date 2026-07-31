using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// Building live Avalonia objects from a document, and keeping them and the document in step.
/// </summary>
/// <remarks>
/// Nothing here shows a window. The library's job is to produce the objects and to say which
/// markup each one came from; putting them on screen is the host's, and a sample that grew a
/// selection adorner or a property inspector would be the visual designer the contract rules out.
/// </remarks>
internal static class LoaderShowcase
{
    /// <summary>Builds the environment every load in this sample goes through.</summary>
    internal static (XamlLoadEnvironment Environment, InMemoryResourceResolver Resources) Environment()
    {
        var resources = new InMemoryResourceResolver();

        resources.Update(Fixtures.PaletteUri, Fixtures.Palette("Red"));
        resources.Update(Fixtures.BrandUri, Fixtures.Brand);

        XamlLoadEnvironment defaults = XamlLoadEnvironment.CreateDefault();

        return (
            new XamlLoadEnvironment
            {
                SourceProvider = defaults.SourceProvider,
                AssemblyResolver = defaults.AssemblyResolver,
                TypeResolver = defaults.TypeResolver,

                // In front of the defaults, so an unsaved edit shadows whatever is on disk.
                ResourceResolver = new CompositeResourceResolver(resources, defaults.ResourceResolver),
            },
            resources);
    }

    /// <summary>Loads the view and shows what came of it.</summary>
    internal static async Task<XamlLoadSession> LoadAsync(
        XamlLoadEnvironment environment,
        XamlLoadMode mode,
        CancellationToken cancellationToken)
    {
        Report.Section(7, "Loading: real objects, and includes through the caller's resolver");
        Report.Note(
            "Avalonia resolves an include's Source through its own asset loader and throws when " +
            "it cannot. So the document is projected — the includes resolved through the " +
            "environment and spliced in — and Avalonia is handed that. The document itself is " +
            "never touched.");

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri }),
            environment,
            new XamlLoadOptions { Mode = mode },
            cancellationToken);

        if (session is null)
        {
            Report.Diagnostics("the load produced nothing", result.Diagnostics);

            throw new InvalidOperationException("The sample's own document failed to load.");
        }

        var view = session.GetRoot<UserControl>();
        var panel = (StackPanel)view.Content!;
        var title = (TextBlock)panel.Children[0];
        var border = (Border)panel.Children[1];

        Report.Value("root object", view.GetType().Name);
        Report.Value("children built", panel.Children.Count);
        Report.Value("Surface came from", Describe(border.Background));
        Report.Check(
            "a brush from an included file reached the control",
            border.Background is ISolidColorBrush);
        Report.Check(
            "the document is unchanged",
            session.Document.GetText() == Fixtures.View);
        Report.Check(
            "the projection is not the document",
            session.Projection.Text.ToString() != Fixtures.View);
        Report.Value(
            "projection grew by",
            session.Projection.Text.Length - session.Projection.Source.Length);

        Report.Note("The binding was not evaluated into a value and never will be written back as one:");
        Report.Value("Title.Text", title.Text ?? "<unset>");
        Report.Value(
            "what the document says",
            session.Document.DescendantElements()
                .First(static e => e.Name.LocalName == "TextBlock")
                .GetAttribute("Text")?.GetValueText());

        Report.Diagnostics("diagnostics", result.Diagnostics);

        return session;
    }

    /// <summary>Shows the map between elements and objects, and where each object came from.</summary>
    internal static void Mapping(XamlLoadSession session)
    {
        Report.Section(8, "Which markup each object came from");
        Report.Note(
            "Built from the source information Avalonia records, read back through the " +
            "projection. That is what keeps an object declared in an included file attributed to " +
            "that file rather than to whichever line of this one sits at the same number.");

        var view = session.GetRoot<UserControl>();
        var panel = (StackPanel)view.Content!;

        foreach (object candidate in new object[] { view, panel, panel.Children[0], panel.Children[1] })
        {
            XamlElement? element = session.GetElement(candidate);

            Report.Value(
                candidate.GetType().Name,
                $"{session.GetOrigin(candidate),-16} " +
                (element is null ? "<no declaration here>" : $"line {Line(session, element)}: {element.Name}"));
        }

        object? brush = view.Resources.MergedDictionaries
            .OfType<IResourceDictionary>()
            .SelectMany(static dictionary => dictionary.Values)
            .FirstOrDefault();

        if (brush is not null)
        {
            Report.Note("And for something the view did not declare at all:");
            Report.Value(brush.GetType().Name, session.GetOrigin(brush));
            Report.Value("declared in", session.GetSourceUri(brush)?.Segments[^1] ?? "<unknown>");
            Report.Check("no element of this document is offered for it", session.GetElement(brush) is null);
        }
    }

    /// <summary>Sets a property through the session and shows both sides move together.</summary>
    internal static void Edit(XamlLoadSession session)
    {
        Report.Section(9, "Editing through the session");
        Report.Note(
            "One operation changes the object and the document, in that order, and puts the " +
            "object back if writing the document fails. Replacing a binding is allowed, because " +
            "a caller may mean exactly that — but it is never allowed to happen unnoticed.");

        var view = session.GetRoot<UserControl>();
        var panel = (StackPanel)view.Content!;
        var title = (TextBlock)panel.Children[0];
        var button = (Button)((Border)panel.Children[1]).Child!;

        XamlEditResult width = session.SetValue(button, Layoutable.WidthProperty, 220d);

        Report.Value("Button.Width applied", width.Applied);
        Report.Value("the object now", button.Width);
        Report.Check(
            "and the document now",
            session.Document.GetText().Contains("Width=\"220\"", StringComparison.Ordinal));

        XamlValueInfo info = session.GetValueInfo(title, TextBlock.TextProperty);

        Report.Note("Before touching a bound property, a caller can ask what it would cost:");
        Report.Value("source value", info.SourceValue.ToXamlText());
        Report.Value("has a binding", info.HasBinding);
        Report.Value("would destroy it", info.WouldDestroyExpression);

        XamlEditResult replaced = session.SetValue(title, TextBlock.TextProperty, "literal now");

        Report.Diagnostics("what the edit reported", replaced.Diagnostics);
        Report.Check("it was applied anyway", replaced.Applied);
    }

    private static int Line(XamlLoadSession session, XamlElement element) =>
        session.Document.SourceText.Lines.GetPosition(element.Span.Start).Line + 1;

    private static string Describe(IBrush? brush) =>
        brush is ISolidColorBrush solid ? solid.Color.ToString() : brush?.ToString() ?? "<unset>";
}
