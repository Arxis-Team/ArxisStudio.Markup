using System.Collections.Generic;

using ArxisStudio.Markup.Workspace.Models;

namespace ArxisStudio.Markup.Workspace.Services;

/// <summary>
/// Выполняет базовую семантическую валидацию документа <c>.arxui</c> в контексте проекта.
/// </summary>
public sealed class ArxuiSemanticValidator
{
    /// <summary>
    /// Проверяет документ и возвращает список диагностик.
    /// </summary>
    public IReadOnlyList<ArxuiSemanticDiagnostic> Validate(UiDocument document, WorkspaceContext context)
    {
        var diagnostics = new List<ArxuiSemanticDiagnostic>();

        if (!string.IsNullOrWhiteSpace(document.Class) && !context.Types.ContainsKey(document.Class))
        {
            diagnostics.Add(new ArxuiSemanticDiagnostic(
                ArxuiSemanticDiagnosticSeverity.Warning,
                $"Type '{document.Class}' was not found in the indexed project sources."));
        }

        if (!string.IsNullOrWhiteSpace(document.Class) &&
            context.Types.TryGetValue(document.Class, out var typeMetadata) &&
            document.Kind is UiDocumentKind.Control or UiDocumentKind.Window &&
            !typeMetadata.IsControl &&
            !typeMetadata.IsTopLevel)
        {
            diagnostics.Add(new ArxuiSemanticDiagnostic(
                ArxuiSemanticDiagnosticSeverity.Warning,
                $"Type '{document.Class}' is not previewable as Avalonia control or top-level."));
        }

        return diagnostics;
    }
}
