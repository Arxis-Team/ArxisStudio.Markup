using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Controls;

/// <summary>
/// The one place a loaded document's objects are put on screen.
/// </summary>
/// <remarks>
/// <para>
/// A previewed control belongs to the application it was written for, not to this window, and it
/// has to look like it. Its own surface and its own theme variant are what make that true, and
/// both live in the control theme in <c>Themes/Showcase.axaml</c> rather than here.
/// </para>
/// <para>
/// A backdrop and nothing else. Nothing is drawn over the content, nothing in it is selected, and
/// no input aimed at it is intercepted — it behaves exactly as it would in its own application,
/// because it is that application's control.
/// </para>
/// </remarks>
internal sealed class PreviewSurface : ContentControl
{
}
