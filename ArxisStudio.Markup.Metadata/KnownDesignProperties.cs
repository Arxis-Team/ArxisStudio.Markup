using System;
using System.Collections.Generic;

namespace ArxisStudio.Markup.Metadata;

/// <summary>
/// Канонические ключи встроенных свойств дизайнера.
/// </summary>
public static class KnownDesignProperties
{
    /// <summary>
    /// Канонический ключ свойства доступности hit-test.
    /// </summary>
    public const string IsHitTestVisible = "Avalonia.Input.InputElement.IsHitTestVisible";
    /// <summary>
    /// Канонический ключ координаты X в макете.
    /// </summary>
    public const string LayoutX = "ArxisStudio.Attached.Layout.X";
    /// <summary>
    /// Канонический ключ координаты Y в макете.
    /// </summary>
    public const string LayoutY = "ArxisStudio.Attached.Layout.Y";
    /// <summary>
    /// Канонический ключ политики перемещения.
    /// </summary>
    public const string MovePolicy = "ArxisStudio.Attached.DesignInteraction.MovePolicy";
    /// <summary>
    /// Канонический ключ политики изменения размера.
    /// </summary>
    public const string ResizePolicy = "ArxisStudio.Attached.DesignInteraction.ResizePolicy";

    /// <summary>
    /// Набор встроенных дескрипторов свойств дизайнера.
    /// </summary>
    public static IReadOnlyList<DesignPropertyDescriptor> Descriptors { get; } =
        new[]
        {
            new DesignPropertyDescriptor(
                IsHitTestVisible,
                typeof(bool),
                new[] { "IsHitTestVisible" }),
            new DesignPropertyDescriptor(
                LayoutX,
                typeof(double),
                new[] { "Layout.X" }),
            new DesignPropertyDescriptor(
                LayoutY,
                typeof(double),
                new[] { "Layout.Y" }),
            new DesignPropertyDescriptor(
                MovePolicy,
                typeof(string),
                new[] { "DesignInteraction.MovePolicy" }),
            new DesignPropertyDescriptor(
                ResizePolicy,
                typeof(string),
                new[] { "DesignInteraction.ResizePolicy" })
        };
}
