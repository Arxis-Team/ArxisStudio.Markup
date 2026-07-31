namespace ArxisStudio.Markup.Xaml;

/// <summary>What kind of include one document uses to pull in another.</summary>
public enum XamlResourceReferenceKind
{
    /// <summary>A <c>&lt;ResourceInclude Source="..." /&gt;</c>.</summary>
    ResourceInclude,

    /// <summary>A <c>&lt;StyleInclude Source="..." /&gt;</c>.</summary>
    StyleInclude,
}
