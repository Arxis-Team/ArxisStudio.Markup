using Avalonia;
using ArxisStudio.Markup;

namespace ArxisStudio.Designer.Models;

/// <summary>
/// Контекст операции перемещения узла.
/// </summary>
/// <param name="Node">Перемещаемый узел.</param>
/// <param name="Delta">Смещение относительно исходной позиции.</param>
public sealed record DesignMoveContext(UiNode Node, Vector Delta);

/// <summary>
/// Контекст операции изменения размеров узла.
/// </summary>
/// <param name="Node">Узел, размеры которого изменяются.</param>
/// <param name="Bounds">Новые границы узла.</param>
public sealed record DesignResizeContext(UiNode Node, Rect Bounds);

/// <summary>
/// Контекст операции drag-and-drop в контейнере.
/// </summary>
/// <param name="ContainerNode">Узел контейнера.</param>
/// <param name="PointerPosition">Позиция указателя в координатах surface.</param>
public sealed record DesignDropContext(UiNode ContainerNode, Point PointerPosition);

/// <summary>
/// Определяет цель drop-операции в дизайнере.
/// </summary>
/// <param name="ContainerNode">Контейнер, принимающий узел.</param>
/// <param name="SlotName">Имя слота или свойства контейнера.</param>
/// <param name="InsertIndex">Индекс вставки в коллекцию, если применимо.</param>
public sealed record DesignerDropTarget(UiNode ContainerNode, string SlotName, int? InsertIndex = null);

/// <summary>
/// Контекст построения design surface.
/// </summary>
/// <param name="Zoom">Текущий масштаб surface.</param>
public sealed record DesignerSurfaceContext(double Zoom = 1.0);
