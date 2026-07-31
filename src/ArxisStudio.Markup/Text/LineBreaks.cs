using System;
using System.Collections.Generic;

namespace ArxisStudio.Markup;

/// <summary>
/// Line-break recognition shared by the text model.
/// </summary>
/// <remarks>
/// This library never normalises line breaks. XML processors are entitled to report
/// <c>\r\n</c> as <c>\n</c>, but rewriting them would break the round-trip requirement, so the
/// text model records where breaks are and leaves the characters exactly as they were written.
/// </remarks>
internal static class LineBreaks
{
    /// <summary>Determines whether a character terminates a line on its own.</summary>
    /// <param name="value">The character to test.</param>
    /// <returns><see langword="true"/> if the character is a line break.</returns>
    public static bool IsLineBreak(char value) => value switch
    {
        '\r' => true,
        '\n' => true,
        '\u0085' => true, // NEXT LINE
        '\u2028' => true, // LINE SEPARATOR
        '\u2029' => true, // PARAGRAPH SEPARATOR
        _ => false,
    };

    /// <summary>
    /// Computes the offset of the first character of every line, treating <c>\r\n</c> as a
    /// single break.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>
    /// The line starts, always containing at least one entry. Text ending with a line break
    /// yields a final entry at the end of the text, representing the trailing empty line.
    /// </returns>
    public static int[] ComputeLineStarts(ReadOnlySpan<char> text)
    {
        var starts = new List<int> { 0 };

        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];

            if (!IsLineBreak(current))
            {
                continue;
            }

            if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                index++;
            }

            starts.Add(index + 1);
        }

        return [.. starts];
    }
}
