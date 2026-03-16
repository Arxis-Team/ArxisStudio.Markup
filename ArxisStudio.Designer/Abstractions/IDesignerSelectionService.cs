using System;
using ArxisStudio.Markup;

namespace ArxisStudio.Designer.Abstractions;

/// <summary>
/// Определяет контракт сервиса выбора узлов в дизайнере.
/// </summary>
public interface IDesignerSelectionService
{
    /// <summary>
    /// Возвращает текущий выбранный узел.
    /// </summary>
    UiNode? SelectedNode { get; }

    /// <summary>
    /// Возникает при изменении выбранного узла.
    /// </summary>
    event EventHandler<UiNode?>? SelectedNodeChanged;

    /// <summary>
    /// Выбирает указанный узел.
    /// </summary>
    /// <param name="node">Узел, который должен стать выбранным.</param>
    void Select(UiNode? node);
}
