using Avalonia.Controls;

namespace ArxisStudio.Markup.Json.Loader.Abstractions;

/// <summary>
/// Создаёт preview-представление top-level документов, которые нельзя вставлять в визуальное дерево напрямую.
/// </summary>
public interface ITopLevelControlFactory
{
    /// <summary>
    /// Создаёт представление top-level контрола.
    /// </summary>
    TopLevelControlBuildResult? Create(UiNode node, System.Type resolvedType, ArxuiLoadContext context);
}

/// <summary>
/// Содержит результат построения preview-представления top-level контрола.
/// </summary>
/// <param name="Root">Корневой preview-контрол.</param>
/// <param name="ContentHost">Хост содержимого top-level документа.</param>
public sealed record TopLevelControlBuildResult(Control Root, ContentControl? ContentHost);
