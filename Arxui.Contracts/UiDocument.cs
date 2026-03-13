using System.Collections.Generic;

namespace ArxisStudio.Markup.Json;

public sealed record UiDocument(
    int SchemaVersion,
    UiDocumentKind Kind,
    UiNode Root);

public enum UiDocumentKind
{
    UserControl,
    Window,
    Styles,
    ResourceDictionary,
    CustomControl
}

public sealed record UiNode(
    string TypeName,
    IReadOnlyDictionary<string, UiValue> Properties);

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
