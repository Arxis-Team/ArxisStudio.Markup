using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A form whose markup places its own class: its data context is another copy of itself.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia's XAML compiler builds it, which is the point of it. Constructed with nothing standing in
/// for its compiled markup, the form constructs itself inside its own population, and that copy does
/// the same, for as long as there is stack — what an application written this way does at run time.
/// </para>
/// <para>
/// So the constructor counts how deep it is on its thread and throws past <see cref="DepthLimit"/>. A
/// regression that hands an instance to the compiled markup becomes an exception a test can see, not
/// a stack overflow that takes the test process with it.
/// </para>
/// </remarks>
public partial class SelfPlacingView : UserControl
{
    /// <summary>How deep construction may nest on one thread before the constructor gives up.</summary>
    public const int DepthLimit = 32;

    [ThreadStatic]
    private static int t_depth;

    /// <summary>Creates the view and populates it, the way a generated partial would.</summary>
    /// <exception cref="InvalidOperationException">The view is being constructed deeper than <see cref="DepthLimit"/> inside itself.</exception>
    public SelfPlacingView()
    {
        t_depth++;

        try
        {
            if (t_depth > DepthLimit)
            {
                throw new InvalidOperationException(
                    $"{nameof(SelfPlacingView)} was constructed {DepthLimit} times inside itself.");
            }

            AvaloniaXamlLoader.Load(this);
        }
        finally
        {
            t_depth--;
        }
    }
}
