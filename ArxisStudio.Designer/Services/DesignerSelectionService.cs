using System;
using ArxisStudio.Designer.Abstractions;
using ArxisStudio.Markup;

namespace ArxisStudio.Designer.Services;

/// <summary>
/// Базовая реализация сервиса выбора узлов дизайнера.
/// </summary>
public sealed class DesignerSelectionService : IDesignerSelectionService
{
    /// <inheritdoc />
    public UiNode? SelectedNode { get; private set; }

    /// <inheritdoc />
    public event EventHandler<UiNode?>? SelectedNodeChanged;

    /// <inheritdoc />
    public void Select(UiNode? node)
    {
        if (ReferenceEquals(SelectedNode, node))
        {
            return;
        }

        SelectedNode = node;
        SelectedNodeChanged?.Invoke(this, node);
    }
}
