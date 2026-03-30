using System.Collections.Generic;

namespace ArxisStudio.Markup;

/// <summary>
/// Корневая модель документа <c>.arxui</c>.
/// </summary>
/// <param name="SchemaVersion">Версия схемы документа.</param>
/// <param name="Kind">Семантический тип документа.</param>
/// <param name="Class">Полное имя CLR-типа, если привязка к типу требуется.</param>
/// <param name="Root">Корневой узел визуального дерева.</param>
public sealed record UiDocument(
    int SchemaVersion,
    UiDocumentKind Kind,
    string? Class,
    UiNode Root);

/// <summary>
/// Семантический тип документа.
/// </summary>
public enum UiDocumentKind
{
    /// <summary>
    /// Документ приложения.
    /// </summary>
    Application,

    /// <summary>
    /// Документ пользовательского контрола.
    /// </summary>
    Control,

    /// <summary>
    /// Документ окна.
    /// </summary>
    Window,

    /// <summary>
    /// Документ со стилями.
    /// </summary>
    Styles,

    /// <summary>
    /// Документ словаря ресурсов.
    /// </summary>
    ResourceDictionary
}

/// <summary>
/// Узел дерева пользовательского интерфейса.
/// </summary>
/// <param name="TypeName">Имя типа элемента.</param>
/// <param name="Properties">Набор свойств узла.</param>
/// <param name="Styles">Коллекция стилей узла.</param>
/// <param name="Resources">Коллекция ресурсов узла.</param>
public sealed record UiNode(
    string TypeName,
    IReadOnlyDictionary<string, UiValue> Properties,
    UiStyles? Styles = null,
    UiResources? Resources = null);

/// <summary>
/// Коллекция стилей, привязанная к узлу.
/// </summary>
/// <param name="Items">Элементы коллекции стилей.</param>
public sealed record UiStyles(IReadOnlyList<UiStyleValue> Items);

/// <summary>
/// Базовый тип значения в коллекции стилей.
/// </summary>
public abstract record UiStyleValue;

/// <summary>
/// Подключение внешнего словаря стилей.
/// </summary>
/// <param name="Source">Путь к ресурсу стилей.</param>
public sealed record StyleIncludeValue(string Source) : UiStyleValue;

/// <summary>
/// Вложенный узел стиля.
/// </summary>
/// <param name="Node">Узел, представляющий стиль.</param>
public sealed record StyleNodeValue(UiNode Node) : UiStyleValue;

/// <summary>
/// Коллекция ресурсов, привязанная к узлу.
/// </summary>
/// <param name="MergedDictionaries">Подключенные словари ресурсов.</param>
/// <param name="Values">Локальные значения ресурсов.</param>
public sealed record UiResources(
    IReadOnlyList<UiResourceDictionaryInclude> MergedDictionaries,
    IReadOnlyDictionary<string, UiValue> Values);

/// <summary>
/// Подключение внешнего словаря ресурсов.
/// </summary>
/// <param name="Source">Путь к словарю ресурсов.</param>
public sealed record UiResourceDictionaryInclude(string Source);

/// <summary>
/// Базовый тип значения свойства узла.
/// </summary>
public abstract record UiValue;

/// <summary>
/// Скалярное значение свойства.
/// </summary>
/// <param name="Value">Фактическое значение.</param>
public sealed record ScalarValue(object? Value) : UiValue;

/// <summary>
/// Значение свойства, содержащее вложенный узел.
/// </summary>
/// <param name="Node">Вложенный узел.</param>
public sealed record NodeValue(UiNode Node) : UiValue;

/// <summary>
/// Коллекционное значение свойства.
/// </summary>
/// <param name="Items">Элементы коллекции.</param>
public sealed record CollectionValue(IReadOnlyList<UiValue> Items) : UiValue;

/// <summary>
/// Значение свойства в виде binding-выражения.
/// </summary>
/// <param name="Binding">Описание привязки.</param>
public sealed record BindingValue(BindingSpec Binding) : UiValue;

/// <summary>
/// Ссылка на ресурс по ключу.
/// </summary>
/// <param name="Key">Ключ ресурса.</param>
public sealed record ResourceValue(string Key) : UiValue;

/// <summary>
/// Ссылка на ресурс по URI/пути.
/// </summary>
/// <param name="Path">Путь к ресурсу.</param>
/// <param name="Assembly">Имя сборки, если ресурс внешний.</param>
public sealed record UriReferenceValue(string Path, string? Assembly = null) : UiValue;

/// <summary>
/// Описание параметров привязки.
/// </summary>
/// <param name="Path">Путь привязки.</param>
/// <param name="Mode">Режим привязки.</param>
/// <param name="ConverterKey">Ключ конвертера ресурса.</param>
/// <param name="StringFormat">Строковый формат результата.</param>
/// <param name="ElementName">Имя элемента-источника.</param>
/// <param name="FallbackValue">Значение при ошибке разрешения пути.</param>
/// <param name="TargetNullValue">Значение, подставляемое при <see langword="null"/>.</param>
/// <param name="ConverterParameter">Параметр конвертера.</param>
/// <param name="RelativeSource">Относительный источник данных.</param>
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

/// <summary>
/// Режим привязки данных.
/// </summary>
public enum BindingMode
{
    /// <summary>
    /// Режим по умолчанию.
    /// </summary>
    Default,

    /// <summary>
    /// Односторонняя привязка от источника к цели.
    /// </summary>
    OneWay,

    /// <summary>
    /// Двусторонняя привязка.
    /// </summary>
    TwoWay,

    /// <summary>
    /// Однократная привязка.
    /// </summary>
    OneTime,

    /// <summary>
    /// Односторонняя привязка от цели к источнику.
    /// </summary>
    OneWayToSource
}

/// <summary>
/// Описание относительного источника данных для binding.
/// </summary>
/// <param name="Mode">Режим поиска источника.</param>
/// <param name="AncestorType">Тип предка при режиме поиска предка.</param>
/// <param name="AncestorLevel">Уровень предка при режиме поиска предка.</param>
/// <param name="Tree">Тип дерева для поиска (при необходимости).</param>
public sealed record RelativeSourceSpec(
    RelativeSourceMode Mode,
    string? AncestorType = null,
    int? AncestorLevel = null,
    string? Tree = null);

/// <summary>
/// Режим разрешения относительного источника данных.
/// </summary>
public enum RelativeSourceMode
{
    /// <summary>
    /// Используется текущий контекст данных.
    /// </summary>
    DataContext,

    /// <summary>
    /// Используется шаблонный родитель.
    /// </summary>
    TemplatedParent,

    /// <summary>
    /// Используется сам элемент.
    /// </summary>
    Self,

    /// <summary>
    /// Используется поиск предка в визуальном/логическом дереве.
    /// </summary>
    FindAncestor
}
