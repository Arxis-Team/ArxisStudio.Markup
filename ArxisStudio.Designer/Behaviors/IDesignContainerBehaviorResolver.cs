using ArxisStudio.Markup;

namespace ArxisStudio.Designer.Behaviors;

/// <summary>
/// Разрешает design-time поведение контейнера для указанного узла.
/// </summary>
public interface IDesignContainerBehaviorResolver
{
    /// <summary>
    /// Возвращает подходящее поведение контейнера.
    /// </summary>
    /// <param name="containerNode">Узел контейнера.</param>
    IDesignContainerBehavior Resolve(UiNode? containerNode);
}
