namespace ArxisStudio.Markup;

/// <summary>How seriously a <see cref="MarkupDiagnostic"/> should be taken.</summary>
public enum MarkupDiagnosticSeverity
{
    /// <summary>Worth reporting, but nothing is wrong.</summary>
    Info,

    /// <summary>Something is suspicious, but the operation produced a usable result.</summary>
    Warning,

    /// <summary>The operation could not produce a correct result.</summary>
    Error,
}
