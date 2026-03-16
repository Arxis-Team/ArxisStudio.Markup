namespace ArxisStudio.Markup.Workspace.Models;

/// <summary>
/// Описывает публичное свойство типа проекта.
/// </summary>
/// <param name="Name">Имя свойства.</param>
/// <param name="TypeName">Полное имя типа свойства.</param>
/// <param name="CanWrite">Указывает, доступна ли запись в свойство.</param>
/// <param name="IsCollection">Указывает, представляет ли свойство коллекцию.</param>
public sealed record PropertyMetadata(
    string Name,
    string TypeName,
    bool CanWrite,
    bool IsCollection);
