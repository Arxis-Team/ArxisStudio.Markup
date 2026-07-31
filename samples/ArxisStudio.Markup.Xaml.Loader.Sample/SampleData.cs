using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// Something for the document's bindings to bind to.
/// </summary>
/// <remarks>
/// A host supplies the data; the library never does. Without it the view's bindings resolve to
/// nothing and the preview shows an empty label, which reads as a rendering fault rather than as
/// a binding waiting for a data context — and hides the thing worth seeing, that the binding was
/// kept as a binding rather than written back as whatever it evaluated to.
/// </remarks>
internal static class SampleData
{
    /// <summary>Gives a loaded root something to bind against.</summary>
    /// <param name="root">The object the document produced.</param>
    /// <returns>The root as a control, when it is one.</returns>
    internal static Control? Attach(object root)
    {
        if (root is Control control)
        {
            control.DataContext = new { Customer = new { Name = "Grace Hopper" } };

            return control;
        }

        return null;
    }
}
