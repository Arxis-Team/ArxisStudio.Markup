using AvaloniaEdit;
using AvaloniaEdit.Highlighting;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Controls;

/// <summary>
/// Gives an editor the highlighting the showcase's documents deserve.
/// </summary>
/// <remarks>
/// <para>
/// XML rather than anything of this library's own. Colouring a document is a question about
/// characters, and the editor answers it for itself; these packages answer a different one — what
/// the characters mean — and the two are deliberately not the same component.
/// </para>
/// <para>
/// A definition is a shared, immutable object, so every editor is handed the same one.
/// </para>
/// </remarks>
internal static class XamlEditor
{
    private static readonly IHighlightingDefinition? Xml = HighlightingManager.Instance.GetDefinition("XML");

    /// <summary>Turns highlighting on for every editor given.</summary>
    /// <param name="editors">The editors to colour.</param>
    internal static void Highlight(params TextEditor[] editors)
    {
        foreach (TextEditor editor in editors)
        {
            editor.SyntaxHighlighting = Xml;
        }
    }
}
