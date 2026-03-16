namespace ArxisStudio.Markup.Json.Loader.Abstractions;

/// <summary>
/// Разрешает корневой узел другого <c>.arxui</c>-документа по значению <see cref="UiDocument.Class"/>.
/// </summary>
public interface IMarkupDocumentResolver
{
    /// <summary>
    /// Пытается найти корневой узел документа по имени класса.
    /// </summary>
    UiNode? ResolveRootByClass(string className);
}
