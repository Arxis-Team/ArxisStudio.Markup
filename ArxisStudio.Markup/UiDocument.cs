using System.Collections.Generic;

namespace ArxisStudio.Markup;

/// <summary>
/// Представляет корневой документ <c>.arxui</c>.
/// </summary>
/// <param name="SchemaVersion">Версия схемы документа.</param>
/// <param name="Kind">Семантический тип документа.</param>
/// <param name="Class">Полное CLR-имя целевого partial-типа, если документ генерируется в класс.</param>
/// <param name="Root">Корневой узел документа.</param>
/// <param name="Design">Design-time метаданные документа, используемые редактором.</param>
public sealed record UiDocument(
    int SchemaVersion,
    UiDocumentKind Kind,
    string? Class,
    UiNode Root,
    UiDocumentDesign? Design = null);

/// <summary>
/// Определяет семантический тип документа <c>.arxui</c>.
/// </summary>
public enum UiDocumentKind
{
    /// <summary>
    /// Документ уровня <c>Avalonia.Application</c>.
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
    /// Документ коллекции стилей.
    /// </summary>
    Styles,

    /// <summary>
    /// Документ словаря ресурсов.
    /// </summary>
    ResourceDictionary
}

/// <summary>
/// Представляет узел дерева UI внутри документа <c>.arxui</c>.
/// </summary>
/// <param name="TypeName">Полное CLR-имя типа узла.</param>
/// <param name="Properties">Набор свойств узла.</param>
/// <param name="Styles">Стили, принадлежащие узлу.</param>
/// <param name="Resources">Ресурсы, принадлежащие узлу.</param>
/// <param name="Design">Design-time метаданные узла.</param>
public sealed record UiNode(
    string TypeName,
    IReadOnlyDictionary<string, UiValue> Properties,
    UiStyles? Styles = null,
    UiResources? Resources = null,
    UiNodeDesign? Design = null);

/// <summary>
/// Определяет design-time метаданные документа.
/// </summary>
/// <param name="SurfaceWidth">Желаемая ширина design surface в редакторе.</param>
/// <param name="SurfaceHeight">Желаемая высота design surface в редакторе.</param>
public sealed record UiDocumentDesign(
    double? SurfaceWidth = null,
    double? SurfaceHeight = null);

/// <summary>
/// Определяет design-time метаданные узла, используемые визуальным редактором.
/// </summary>
/// <param name="Locked">Запрещает редактирование узла в дизайнере.</param>
/// <param name="Hidden">Скрывает узел в design-time preview.</param>
/// <param name="IgnorePreviewInput">Запрещает передавать input самому контролу в preview.</param>
/// <param name="AllowMove">Разрешает попытку перемещения узла в дизайнере.</param>
/// <param name="AllowResize">Разрешает попытку изменения размеров узла в дизайнере.</param>
public sealed record UiNodeDesign(
    bool? Locked = null,
    bool? Hidden = null,
    bool? IgnorePreviewInput = null,
    bool? AllowMove = null,
    bool? AllowResize = null);

/// <summary>
/// Представляет коллекцию стилей, принадлежащих узлу.
/// </summary>
/// <param name="Items">Элементы коллекции стилей.</param>
public sealed record UiStyles(
    IReadOnlyList<UiStyleValue> Items);

/// <summary>
/// Базовый тип для элементов коллекции стилей.
/// </summary>
public abstract record UiStyleValue;

/// <summary>
/// Представляет ссылку на внешний style-файл.
/// </summary>
/// <param name="Source">URI источника style-файла.</param>
public sealed record StyleIncludeValue(string Source) : UiStyleValue;

/// <summary>
/// Представляет inline style-узел.
/// </summary>
/// <param name="Node">Узел, описывающий стиль.</param>
public sealed record StyleNodeValue(UiNode Node) : UiStyleValue;

/// <summary>
/// Представляет набор ресурсов, принадлежащих узлу.
/// </summary>
/// <param name="MergedDictionaries">Подключённые merged resource dictionaries.</param>
/// <param name="Values">Локальные ресурсы по ключу.</param>
public sealed record UiResources(
    IReadOnlyList<UiResourceDictionaryInclude> MergedDictionaries,
    IReadOnlyDictionary<string, UiValue> Values);

/// <summary>
/// Представляет подключение внешнего словаря ресурсов.
/// </summary>
/// <param name="Source">URI источника словаря ресурсов.</param>
public sealed record UiResourceDictionaryInclude(string Source);

/// <summary>
/// Базовый тип значения свойства в модели <c>.arxui</c>.
/// </summary>
public abstract record UiValue;

/// <summary>
/// Представляет scalar-значение свойства.
/// </summary>
/// <param name="Value">Значение свойства.</param>
public sealed record ScalarValue(object? Value) : UiValue;

/// <summary>
/// Представляет вложенный узел.
/// </summary>
/// <param name="Node">Вложенный узел.</param>
public sealed record NodeValue(UiNode Node) : UiValue;

/// <summary>
/// Представляет коллекцию значений.
/// </summary>
/// <param name="Items">Элементы коллекции.</param>
public sealed record CollectionValue(IReadOnlyList<UiValue> Items) : UiValue;

/// <summary>
/// Представляет значение привязки.
/// </summary>
/// <param name="Binding">Спецификация привязки.</param>
public sealed record BindingValue(BindingSpec Binding) : UiValue;

/// <summary>
/// Представляет ссылку на ресурс по ключу.
/// </summary>
/// <param name="Key">Ключ ресурса.</param>
public sealed record ResourceValue(string Key) : UiValue;

/// <summary>
/// Представляет ссылку на asset или avares-ресурс.
/// </summary>
/// <param name="Path">Путь к asset внутри сборки.</param>
/// <param name="Assembly">Имя сборки, в которой расположен asset.</param>
public sealed record UriReferenceValue(string Path, string? Assembly = null) : UiValue;

/// <summary>
/// Определяет спецификацию data binding.
/// </summary>
/// <param name="Path">Путь binding.</param>
/// <param name="Mode">Режим binding.</param>
/// <param name="ConverterKey">Ключ ресурса-конвертера.</param>
/// <param name="StringFormat">Строковый формат результата.</param>
/// <param name="ElementName">Имя элемента-источника.</param>
/// <param name="FallbackValue">Fallback value.</param>
/// <param name="TargetNullValue">Значение для <see langword="null"/>.</param>
/// <param name="ConverterParameter">Параметр конвертера.</param>
/// <param name="RelativeSource">Настройки relative source.</param>
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
/// Определяет режим binding.
/// </summary>
public enum BindingMode
{
    /// <summary>
    /// Режим по умолчанию, определяемый свойством назначения.
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
    /// Однократное считывание значения из источника.
    /// </summary>
    OneTime,

    /// <summary>
    /// Односторонняя привязка от цели к источнику.
    /// </summary>
    OneWayToSource
}

/// <summary>
/// Определяет источник для relative binding.
/// </summary>
/// <param name="Mode">Режим relative source.</param>
/// <param name="AncestorType">Тип предка для поиска.</param>
/// <param name="AncestorLevel">Уровень предка.</param>
/// <param name="Tree">Тип дерева для поиска.</param>
public sealed record RelativeSourceSpec(
    RelativeSourceMode Mode,
    string? AncestorType = null,
    int? AncestorLevel = null,
    string? Tree = null);

/// <summary>
/// Определяет режим relative source.
/// </summary>
public enum RelativeSourceMode
{
    /// <summary>
    /// Источником служит текущий <c>DataContext</c>.
    /// </summary>
    DataContext,

    /// <summary>
    /// Источником служит templated parent.
    /// </summary>
    TemplatedParent,

    /// <summary>
    /// Источником служит текущий объект.
    /// </summary>
    Self,

    /// <summary>
    /// Источником служит предок в дереве.
    /// </summary>
    FindAncestor
}
