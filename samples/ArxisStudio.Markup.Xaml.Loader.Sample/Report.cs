using System;
using System.Collections.Generic;
using System.Linq;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// Console formatting, kept in one place so the showcase itself reads as library calls.
/// </summary>
internal static class Report
{
    private const int Width = 78;

    /// <summary>Writes a numbered section heading.</summary>
    /// <param name="number">The section's number.</param>
    /// <param name="title">What the section demonstrates.</param>
    internal static void Section(int number, string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', Width));
        Console.WriteLine($" {number}. {title}");
        Console.WriteLine(new string('=', Width));
    }

    /// <summary>Writes a sentence explaining what is about to be shown.</summary>
    /// <param name="text">The explanation.</param>
    internal static void Note(string text)
    {
        Console.WriteLine();

        foreach (string line in Wrap(text))
        {
            Console.WriteLine("  " + line);
        }
    }

    /// <summary>Writes a labelled result.</summary>
    /// <param name="label">What the value is.</param>
    /// <param name="value">The value.</param>
    internal static void Value(string label, object? value) =>
        Console.WriteLine($"    {label,-28} {value ?? "<null>"}");

    /// <summary>Writes a claim and whether it held.</summary>
    /// <param name="claim">The claim being demonstrated.</param>
    /// <param name="held">Whether it held.</param>
    internal static void Check(string claim, bool held) =>
        Console.WriteLine($"    [{(held ? "ok" : "!!")}] {claim}");

    /// <summary>Writes a block of markup or text, indented and line-numbered.</summary>
    /// <param name="caption">What the block is.</param>
    /// <param name="text">The text to show.</param>
    internal static void Block(string caption, string text)
    {
        Console.WriteLine();
        Console.WriteLine($"    --- {caption} " + new string('-', Math.Max(0, Width - 9 - caption.Length)));

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            // A trailing newline produces an empty final entry that is not a line of the file.
            if (index == lines.Length - 1 && lines[index].Length == 0)
            {
                break;
            }

            Console.WriteLine($"    {index + 1,3} | {lines[index]}");
        }
    }

    /// <summary>Writes diagnostics the way a host would show them.</summary>
    /// <param name="caption">What produced them.</param>
    /// <param name="diagnostics">The diagnostics to show.</param>
    /// <param name="text">The text their spans point into, when they share one.</param>
    internal static void Diagnostics(
        string caption,
        IEnumerable<MarkupDiagnostic> diagnostics,
        SourceText? text = null)
    {
        MarkupDiagnostic[] all = [.. diagnostics];

        Console.WriteLine();
        Console.WriteLine($"    {caption}: {all.Length}");

        foreach (MarkupDiagnostic diagnostic in all)
        {
            string where = text is not null && diagnostic.Span is { } span && span.End <= text.Length
                ? $" line {text.Lines.GetPosition(span.Start).Line + 1}"
                : string.Empty;

            Console.WriteLine($"      {diagnostic.Severity,-7} {diagnostic.Code}{where}  {diagnostic.Message}");
        }
    }

    /// <summary>Wraps text to the console width without breaking words.</summary>
    private static IEnumerable<string> Wrap(string text)
    {
        var line = new List<string>();
        int length = 0;

        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (length > 0 && length + word.Length + 1 > Width - 4)
            {
                yield return string.Join(' ', line);

                line.Clear();
                length = 0;
            }

            line.Add(word);
            length += word.Length + 1;
        }

        if (line.Count > 0)
        {
            yield return string.Join(' ', line);
        }
    }
}
