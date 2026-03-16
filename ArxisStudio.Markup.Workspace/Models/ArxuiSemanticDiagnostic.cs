namespace ArxisStudio.Markup.Workspace.Models;

/// <summary>
/// Представляет результат семантической проверки документа <c>.arxui</c>.
/// </summary>
/// <param name="Severity">Уровень серьёзности.</param>
/// <param name="Message">Текст диагностического сообщения.</param>
public sealed record ArxuiSemanticDiagnostic(
    ArxuiSemanticDiagnosticSeverity Severity,
    string Message);

/// <summary>
/// Определяет уровень серьёзности семантической диагностики.
/// </summary>
public enum ArxuiSemanticDiagnosticSeverity
{
    /// <summary>
    /// Информационное сообщение.
    /// </summary>
    Info,

    /// <summary>
    /// Предупреждение.
    /// </summary>
    Warning,

    /// <summary>
    /// Ошибка.
    /// </summary>
    Error
}
