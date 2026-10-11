using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Diagnostics;
using Avalonia.Markup.Xaml.Templates;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// The root of an <c>x:Class</c> document records no position of its own, and the map still knows it
/// and still tells this document's markup from everybody else's.
/// </summary>
/// <remarks>
/// A designer found the gap: a form-window with a class, dressed for the canvas by a window chrome whose
/// compiled template records positions in its own file, was described by its inspector as that
/// template. With no position on the root the map did not know what its own text was called, read the
/// template's position as one in the form, and let the template take the root element before the root
/// could.
/// </remarks>
public sealed class ClassRootMapTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";

    /// <summary>What the runtime loader names every text it loads: it becomes <c>file://runtimexaml0/</c>.</summary>
    private const string RuntimeText = "runtimexaml0";

    /// <summary>A file of compiled markup somebody else wrote: a theme, a canvas's window chrome.</summary>
    private const string ForeignText = "C:/Themes/WindowChrome.axaml";

    private static XamlDocument Form(string content) =>
        XamlDocument.Parse(
            $"<UserControl xmlns=\"{AvaloniaNamespace}\">\n" +
            content +
            "</UserControl>",
            new XamlParseOptions { DocumentUri = new Uri("file:///Views/Form.axaml") });

    /// <summary>A template set by somebody else's style, recording where it was written.</summary>
    private static ControlTemplate Foreign(int line, int position)
    {
        var template = new ControlTemplate();

        XamlSourceInfo.SetXamlSourceInfo(template, new XamlSourceInfo(line, position, ForeignText));

        return template;
    }

    [AvaloniaFact]
    public void AnEmptyRootIsItsElementsEvenWhenAForeignTemplateRecordsThatPosition()
    {
        XamlDocument document = Form(string.Empty);

        // Constructed, not loaded: the class's instance carries no position.
        var root = new UserControl { Template = Foreign(1, 2) };

        XamlObjectMap map = XamlObjectMap.Build(document, root);

        Assert.Same(root, map.GetObject(document.Root!));
        Assert.Null(map.GetElement(root.Template!));
    }

    [AvaloniaFact]
    public void AForeignPositionInsideTheFormIsNotTakenForTheFormsMarkup()
    {
        XamlDocument document = Form("  <TextBlock Text=\"declared\" />\n");

        var declared = new TextBlock { Text = "declared" };

        // What the session's own load records on what the document declared.
        XamlSourceInfo.SetXamlSourceInfo(declared, new XamlSourceInfo(2, 4, RuntimeText));

        // Somebody else's template, whose recorded position happens to fall on the text block's line.
        var root = new UserControl { Content = declared, Template = Foreign(2, 4) };

        XamlObjectMap map = XamlObjectMap.Build(document, root);
        XamlElement block = Assert.Single(document.Root!.Elements);

        Assert.Same(root, map.GetObject(document.Root!));
        Assert.Same(declared, map.GetObject(block));
        Assert.Null(map.GetElement(root.Template!));
    }
}
