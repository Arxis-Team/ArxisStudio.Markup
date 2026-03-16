using System.Collections.Generic;

namespace ArxisStudio.Markup.Workspace.Models;

/// <summary>
/// Описывает CLR-тип, обнаруженный в проекте.
/// </summary>
/// <param name="FullName">Полное имя типа.</param>
/// <param name="Name">Короткое имя типа.</param>
/// <param name="BaseTypeName">Полное имя базового типа.</param>
/// <param name="IsControl">Указывает, наследуется ли тип от <c>Avalonia.Controls.Control</c>.</param>
/// <param name="IsTopLevel">Указывает, наследуется ли тип от <c>Avalonia.Controls.TopLevel</c> или <c>Avalonia.Controls.Window</c>.</param>
/// <param name="Properties">Публичные свойства типа.</param>
public sealed record TypeMetadata(
    string FullName,
    string Name,
    string? BaseTypeName,
    bool IsControl,
    bool IsTopLevel,
    IReadOnlyList<PropertyMetadata> Properties);
