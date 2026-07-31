using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// The few control shapes the window is built from, so each tab reads as what it shows.
/// </summary>
internal static class Ui
{
    internal static FontFamily Mono { get; } = new("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace");

    /// <summary>A tab's title line.</summary>
    internal static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 0, 0, 4),
    };

    /// <summary>A sentence of explanation, wrapped.</summary>
    internal static TextBlock Body(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.8,
        Margin = new Thickness(0, 0, 0, 8),
    };

    /// <summary>A small caption above something.</summary>
    internal static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Opacity = 0.6,
        Margin = new Thickness(0, 6, 0, 2),
    };

    /// <summary>Text shown as text: monospaced, selectable, and never interpreted.</summary>
    internal static TextBox Code(string text, bool editable = false) => new()
    {
        Text = text,
        FontFamily = Mono,
        FontSize = 12,
        AcceptsReturn = true,
        IsReadOnly = !editable,
        TextWrapping = TextWrapping.NoWrap,
        VerticalContentAlignment = VerticalAlignment.Top,
    };

    /// <summary>A framed area, used for the one place a loaded control is put on screen.</summary>
    internal static Border Frame(Control? content = null) => new()
    {
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(8),
        Child = content,
    };

    /// <summary>A verdict line: what was claimed, and whether it held.</summary>
    internal static Control Verdict(string claim, bool held) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children =
        {
            new TextBlock
            {
                Text = held ? "✓" : "✗",
                Foreground = held ? Brushes.SeaGreen : Brushes.IndianRed,
                FontWeight = FontWeight.Bold,
            },
            new TextBlock { Text = claim, TextWrapping = TextWrapping.Wrap },
        },
    };

    /// <summary>A label and a value on one line.</summary>
    internal static Control Field(string label, string value) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            new TextBlock { Text = label, Opacity = 0.6, Width = 190 },
            new TextBlock { Text = value, FontFamily = Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap },
        },
    };

    /// <summary>A vertical stack with the window's usual spacing.</summary>
    internal static StackPanel Stack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 4 };

        foreach (Control child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    /// <summary>Puts a tab's content in a scroll viewer with a margin.</summary>
    internal static Control Page(Control content) => new ScrollViewer
    {
        Padding = new Thickness(16),
        Content = content,
    };

    /// <summary>Renders diagnostics as the list a host would show.</summary>
    internal static Control Diagnostics(IEnumerable<MarkupDiagnostic> diagnostics, SourceText? text = null)
    {
        var panel = new StackPanel { Spacing = 2 };
        int count = 0;

        foreach (MarkupDiagnostic diagnostic in diagnostics)
        {
            count++;

            string where = text is not null && diagnostic.Span is { } span && span.End <= text.Length
                ? $"line {text.Lines.GetPosition(span.Start).Line + 1}"
                : string.Empty;

            panel.Children.Add(new TextBlock
            {
                Text = $"{diagnostic.Severity,-7} {diagnostic.Code} {where}  {diagnostic.Message}",
                FontFamily = Mono,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = diagnostic.Severity switch
                {
                    MarkupDiagnosticSeverity.Error => Brushes.IndianRed,
                    MarkupDiagnosticSeverity.Warning => Brushes.DarkGoldenrod,
                    _ => Brushes.SteelBlue,
                },
            });
        }

        if (count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "nothing reported", Opacity = 0.5, FontSize = 11 });
        }

        return panel;
    }
}
