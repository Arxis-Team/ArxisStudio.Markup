using System.Collections.Generic;

namespace ArxisStudio.Markup.Json;

public sealed record UiDocument(
    int SchemaVersion,
    UiDocumentKind Kind,
    string? Class,
    UiNode Root,
    UiDocumentDesign? Design = null);

public enum UiDocumentKind
{
    Application,
    Control,
    Window,
    Styles,
    ResourceDictionary
}

public sealed record UiNode(
    string TypeName,
    IReadOnlyDictionary<string, UiValue> Properties,
    UiStyles? Styles = null,
    UiResources? Resources = null,
    UiNodeDesign? Design = null);

public sealed record UiDocumentDesign(
    double? SurfaceWidth = null,
    double? SurfaceHeight = null);

public sealed record UiNodeDesign(
    bool? Locked = null,
    bool? Hidden = null,
    bool? IgnorePreviewInput = null,
    bool? AllowMove = null,
    bool? AllowResize = null);

public sealed record UiStyles(
    IReadOnlyList<UiStyleValue> Items);

public abstract record UiStyleValue;

public sealed record StyleIncludeValue(string Source) : UiStyleValue;

public sealed record StyleNodeValue(UiNode Node) : UiStyleValue;

public sealed record UiResources(
    IReadOnlyList<UiResourceDictionaryInclude> MergedDictionaries,
    IReadOnlyDictionary<string, UiValue> Values);

public sealed record UiResourceDictionaryInclude(string Source);

public abstract record UiValue;

public sealed record ScalarValue(object? Value) : UiValue;

public sealed record NodeValue(UiNode Node) : UiValue;

public sealed record CollectionValue(IReadOnlyList<UiValue> Items) : UiValue;

public sealed record BindingValue(BindingSpec Binding) : UiValue;

public sealed record ResourceValue(string Key) : UiValue;

public sealed record UriReferenceValue(string Path, string? Assembly = null) : UiValue;

public sealed record BindingSpec(
    string Path,
    BindingMode? Mode = null,
    string? ConverterKey = null,
    string? StringFormat = null,
    string? ElementName = null,
    object? FallbackValue = null,
    object? TargetNullValue = null,
    object? ConverterParameter = null,
    RelativeSourceSpec? RelativeSource = null);

public enum BindingMode
{
    Default,
    OneWay,
    TwoWay,
    OneTime,
    OneWayToSource
}

public sealed record RelativeSourceSpec(
    RelativeSourceMode Mode,
    string? AncestorType = null,
    int? AncestorLevel = null,
    string? Tree = null);

public enum RelativeSourceMode
{
    DataContext,
    TemplatedParent,
    Self,
    FindAncestor
}
