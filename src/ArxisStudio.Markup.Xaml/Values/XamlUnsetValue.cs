namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// The absence of a value, as distinct from an empty one.
/// </summary>
/// <remarks>
/// <c>Width=""</c> sets an empty string; a missing <c>Width</c> sets nothing at all. Later
/// milestones have to tell those apart to decide whether an edit is adding a member or
/// changing one.
/// </remarks>
public sealed record XamlUnsetValue : XamlValue
{
    /// <inheritdoc />
    public override string ToXamlText() => string.Empty;

    /// <summary>Returns a marker rather than an empty string, so an unset value is visible in logs.</summary>
    /// <returns>A readable representation.</returns>
    public override string ToString() => "<unset>";
}
