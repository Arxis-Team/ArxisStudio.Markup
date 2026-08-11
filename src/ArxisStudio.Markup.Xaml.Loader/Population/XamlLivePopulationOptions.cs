namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// How instances are populated from live documents.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="XamlLoadOptions"/>. A load has a mode and a local assembly; live
/// population has neither — the assembly is the registered type's own, and the mode is always the
/// runtime one, because an embedded instance must behave the way its compiled markup would and a
/// design-time width applied to an embedded control is a layout bug, not a preview.
/// </remarks>
public sealed class XamlLivePopulationOptions
{
    /// <summary>Gets the defaults.</summary>
    public static XamlLivePopulationOptions Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether a plain <c>{Binding}</c> compiles as a compiled binding.
    /// </summary>
    /// <remarks>
    /// What the project's <c>AvaloniaUseCompiledBindingsByDefault</c> said when the assembly was
    /// built, if the caller knows it. The compiled markup was produced under that setting, and
    /// population is trying to be the compiled markup with newer text.
    /// </remarks>
    public bool UseCompiledBindingsByDefault { get; init; }
}
