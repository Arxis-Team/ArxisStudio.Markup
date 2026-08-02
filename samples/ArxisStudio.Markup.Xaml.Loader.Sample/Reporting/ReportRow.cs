using System;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Reporting;

/// <summary>
/// One line of what a tab has to say, as data rather than as controls.
/// </summary>
/// <remarks>
/// The rows carry the facts; <c>App.axaml</c> carries a data template for each kind and decides
/// what one looks like. That is the whole reason the tabs hold no layout code: they answer, and
/// the markup renders the answer.
/// </remarks>
internal abstract class ReportRow
{
}

/// <summary>A small heading above a group of rows.</summary>
internal sealed class CaptionRow(string text) : ReportRow
{
    /// <summary>Gets the caption.</summary>
    public string Text { get; } = text;
}

/// <summary>A remark in the margin, in smaller type.</summary>
internal sealed class NoteRow(string text) : ReportRow
{
    /// <summary>Gets the note.</summary>
    public string Text { get; } = text;
}

/// <summary>A label and the value it names.</summary>
internal sealed class FieldRow(string label, string value) : ReportRow
{
    /// <summary>Gets the label.</summary>
    public string Label { get; } = label;

    /// <summary>Gets the value.</summary>
    public string Value { get; } = value;
}

/// <summary>Something claimed, and whether it held.</summary>
internal sealed class VerdictRow(string claim, bool held) : ReportRow
{
    /// <summary>Gets the claim.</summary>
    public string Claim { get; } = claim;

    /// <summary>Gets a value indicating whether the claim held.</summary>
    public bool Held { get; } = held;

    /// <summary>Gets the mark shown against the claim.</summary>
    public string Mark => Held ? "✓" : "✗";
}

/// <summary>One diagnostic, as a host would list it.</summary>
internal sealed class DiagnosticRow : ReportRow
{
    internal DiagnosticRow(MarkupDiagnostic diagnostic, SourceText? text)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        Code = diagnostic.Code;
        Message = diagnostic.Message;
        Severity = diagnostic.Severity.ToString().ToUpperInvariant();
        IsError = diagnostic.Severity == MarkupDiagnosticSeverity.Error;
        IsWarning = diagnostic.Severity == MarkupDiagnosticSeverity.Warning;

        Where = text is not null && diagnostic.Span is { } span && span.End <= text.Length
            ? $"line {text.Lines.GetPosition(span.Start).Line + 1}"
            : string.Empty;
    }

    /// <summary>Gets the stable machine-readable code.</summary>
    public string Code { get; }

    /// <summary>Gets the message.</summary>
    public string Message { get; }

    /// <summary>Gets the severity, spelled for display.</summary>
    public string Severity { get; }

    /// <summary>Gets the line the diagnostic points at, when it points at one.</summary>
    public string Where { get; }

    /// <summary>Gets a value indicating whether this is an error.</summary>
    public bool IsError { get; }

    /// <summary>Gets a value indicating whether this is a warning.</summary>
    public bool IsWarning { get; }
}

/// <summary>One loaded object and the markup behind it.</summary>
internal sealed class ObjectRow(string type, string origin, string source, string detail) : ReportRow
{
    /// <summary>Gets the object's type name.</summary>
    public string Type { get; } = type;

    /// <summary>Gets what kind of markup produced it.</summary>
    public string Origin { get; } = origin;

    /// <summary>Gets the file it was declared in.</summary>
    public string Source { get; } = source;

    /// <summary>Gets where in that file, when the mapping reaches an element.</summary>
    public string Detail { get; } = detail;
}
