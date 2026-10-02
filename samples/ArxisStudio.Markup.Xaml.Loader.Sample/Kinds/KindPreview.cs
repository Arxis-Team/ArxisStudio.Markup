using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.Sample.Reporting;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Kinds;

/// <summary>
/// How a host shows a document, decided by what the document is.
/// </summary>
/// <remarks>
/// <para>
/// This is the question the classification exists to answer before anything is built. A control
/// goes into the preview as it is. A set of styles or a dictionary draws nothing by itself, so what
/// is shown is the document's own <c>Design.PreviewWith</c>, wearing the document. A window cannot
/// be put inside anything at all, and an application has nothing to show — for those the host
/// decides not to build, and says why.
/// </para>
/// <para>
/// Everything here is the host's: the packages classify and load, and none of them hosts a root.
/// </para>
/// </remarks>
internal static class KindPreview
{
    /// <summary>Builds what the preview shows for a document, and notes saying why.</summary>
    /// <param name="document">The document.</param>
    /// <param name="what">What the classifier said it is.</param>
    /// <param name="environment">The environment to load it in, when it is loaded at all.</param>
    /// <param name="notes">Collects what the preview says about itself.</param>
    /// <returns>The control to show, if any, and the session that built it, if one did.</returns>
    internal static async Task<(Control? Content, XamlLoadSession? Session)> BuildAsync(
        XamlDocument document,
        XamlDocumentClassification what,
        XamlLoadEnvironment environment,
        Report notes)
    {
        if (what.Kind == XamlDocumentKind.Unknown)
        {
            notes.Note("Корень не разрешился — строить нечего. Почему, сказано в диагностике справа.");

            return (null, null);
        }

        if (!what.IsResolved)
        {
            notes.Note(
                "Без сборки проекта контрола ещё не существует, и превью строить не из чего. " +
                "Но что это за файл, уже известно: имена сказали это до сборки.");

            return (null, null);
        }

        switch (what.Kind)
        {
            case XamlDocumentKind.Application:
                notes.Note(
                    "Приложению нечего показать само: App.axaml отдаёт стили и ресурсы всем окнам. " +
                    "Что именно, видно и без загрузки — из синтаксиса:");

                foreach (XamlResourceReference reference in XamlResourceAnalyzer.Discover(document))
                {
                    notes.Field(reference.Kind.ToString(), reference.SourceText);
                }

                return (null, null);

            case XamlDocumentKind.Window:
                notes.Note(
                    "Окно — TopLevel: Avalonia не даст вложить его в другой контрол, поэтому витрина " +
                    "его не строит. Хост показывает окно заместителем — в ArxisStudio это " +
                    "UiDesignerFormItem из ArxisStudio.Surface.");

                notes.Field("заголовок", what.Root?.GetAttribute("Title")?.GetValueText() ?? "—")
                    .Field("размер", $"{Attribute(what, "Width")} × {Attribute(what, "Height")}");

                return (null, null);

            case XamlDocumentKind.UserControl or XamlDocumentKind.Control:
                return await ControlAsync(document, environment, notes);

            case XamlDocumentKind.Other:
                return await OtherAsync(document, environment, notes);

            default:
                return await LookAsync(document, environment, notes);
        }
    }

    /// <summary>A control is put in the preview as it is.</summary>
    private static async Task<(Control?, XamlLoadSession?)> ControlAsync(
        XamlDocument document,
        XamlLoadEnvironment environment,
        Report notes)
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            document, environment, new XamlLoadOptions { Mode = XamlLoadMode.Runtime });

        if (session is null)
        {
            notes.Caption("ЗАГРУЗКА НЕ УДАЛАСЬ").Diagnostics(result.Diagnostics, document.SourceText);

            return (null, null);
        }

        notes.Note("Корень — контрол: хост кладёт его в превью как есть.");

        return (SampleData.Attach(session.RootObject), session);
    }

    /// <summary>
    /// Styles, a theme or a dictionary draw nothing by themselves: the preview is the document's
    /// own <c>Design.PreviewWith</c>, with the document applied to the control that hosts it.
    /// </summary>
    /// <remarks>
    /// Loaded in design mode, because that is the mode in which Avalonia keeps the preview at all.
    /// </remarks>
    private static async Task<(Control?, XamlLoadSession?)> LookAsync(
        XamlDocument document,
        XamlLoadEnvironment environment,
        Report notes)
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            document, environment, new XamlLoadOptions { Mode = XamlLoadMode.Design });

        if (session is null)
        {
            notes.Caption("ЗАГРУЗКА НЕ УДАЛАСЬ").Diagnostics(result.Diagnostics, document.SourceText);

            return (null, null);
        }

        Control? preview = session.RootObject switch
        {
            IStyle style => Design.GetPreviewWith(style),
            ResourceDictionary dictionary => Design.GetPreviewWith(dictionary),
            _ => null,
        };

        if (preview is null)
        {
            notes.Note("Стили сами ничего не рисуют, а показать их не на чем: в документе нет Design.PreviewWith.");

            return (null, session);
        }

        var host = new Border { Child = preview };

        if (session.RootObject is IStyle applied)
        {
            host.Styles.Add(applied);
        }
        else if (session.RootObject is IResourceProvider resources)
        {
            host.Resources.MergedDictionaries.Add(resources);
        }

        notes.Note(
            "Сами стили ничего не рисуют. Превью — то, что документ положил в Design.PreviewWith, " +
            "а документ приложен к контролу, который это превью держит: стили — в его Styles, " +
            "словарь — в его ресурсы.");

        return (host, session);
    }

    /// <summary>Anything else is shown when it is something that can be — a brush as a swatch.</summary>
    private static async Task<(Control?, XamlLoadSession?)> OtherAsync(
        XamlDocument document,
        XamlLoadEnvironment environment,
        Report notes)
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            document, environment, new XamlLoadOptions { Mode = XamlLoadMode.Runtime });

        if (session is null)
        {
            notes.Caption("ЗАГРУЗКА НЕ УДАЛАСЬ").Diagnostics(result.Diagnostics, document.SourceText);

            return (null, null);
        }

        if (session.RootObject is IBrush brush)
        {
            notes.Note("Документ описывает кисть, а не контрол — витрина показывает её образцом.");

            return (new Border { Width = 240, Height = 72, CornerRadius = new CornerRadius(8), Background = brush }, session);
        }

        notes.Note($"Корень — {session.RootObject.GetType().Name}: показать его витрине нечем.");

        return (null, session);
    }

    private static string Attribute(XamlDocumentClassification what, string name) =>
        what.Root?.GetAttribute(name)?.GetValueText() is { Length: > 0 } text
            ? text
            : "авто";
}
