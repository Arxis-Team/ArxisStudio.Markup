namespace ArxisStudio.Markup.Xaml;

/// <summary>Which of the two style declarations an element is.</summary>
public enum XamlStyleKind
{
    /// <summary>A <c>&lt;Style&gt;</c>, which applies wherever its selector matches.</summary>
    Style,

    /// <summary>
    /// A <c>&lt;ControlTheme&gt;</c>: a control's whole look, applied by its target type rather
    /// than by a selector.
    /// </summary>
    ControlTheme,
}
