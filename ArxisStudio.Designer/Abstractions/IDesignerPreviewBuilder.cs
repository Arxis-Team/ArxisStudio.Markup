using ArxisStudio.Designer.Models;
using ArxisStudio.Markup;

namespace ArxisStudio.Designer.Abstractions;

/// <summary>
/// Строит design-time preview сцену по документу <c>.arxui</c>.
/// </summary>
public interface IDesignerPreviewBuilder
{
    /// <summary>
    /// Строит preview сцену для указанного документа.
    /// </summary>
    /// <param name="document">Документ, для которого требуется preview.</param>
    /// <param name="context">Контекст построения surface.</param>
    /// <returns>Построенная сцена preview.</returns>
    DesignerPreviewScene Build(UiDocument document, DesignerSurfaceContext context);
}
