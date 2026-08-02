using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Reporting;

/// <summary>
/// The rows a tab is currently showing.
/// </summary>
/// <remarks>
/// One instance per list, handed to an <c>ItemsControl</c> once and then refilled in place, so a
/// tab that reloads after every keystroke never rebuilds a control.
/// </remarks>
internal sealed class Report
{
    /// <summary>Gets the rows, in the order they were added.</summary>
    public ObservableCollection<ReportRow> Rows { get; } = [];

    /// <summary>Empties the report.</summary>
    /// <returns>This report.</returns>
    internal Report Clear()
    {
        Rows.Clear();

        return this;
    }

    /// <summary>Adds a heading.</summary>
    /// <param name="text">The caption.</param>
    /// <returns>This report.</returns>
    internal Report Caption(string text)
    {
        Rows.Add(new CaptionRow(text));

        return this;
    }

    /// <summary>Adds a remark.</summary>
    /// <param name="text">The note.</param>
    /// <returns>This report.</returns>
    internal Report Note(string text)
    {
        Rows.Add(new NoteRow(text));

        return this;
    }

    /// <summary>Adds a label and its value.</summary>
    /// <param name="label">The label.</param>
    /// <param name="value">The value.</param>
    /// <returns>This report.</returns>
    internal Report Field(string label, string value)
    {
        Rows.Add(new FieldRow(label, value));

        return this;
    }

    /// <summary>Adds a claim and whether it held.</summary>
    /// <param name="claim">What is claimed.</param>
    /// <param name="held">Whether it held.</param>
    /// <returns>This report.</returns>
    internal Report Verdict(string claim, bool held)
    {
        Rows.Add(new VerdictRow(claim, held));

        return this;
    }

    /// <summary>Adds one object and the markup behind it.</summary>
    /// <param name="row">The row.</param>
    /// <returns>This report.</returns>
    internal Report Mapped(ObjectRow row)
    {
        Rows.Add(row);

        return this;
    }

    /// <summary>Adds every diagnostic, or a line saying there were none.</summary>
    /// <param name="diagnostics">The diagnostics.</param>
    /// <param name="text">The text their spans point into, when it is at hand.</param>
    /// <returns>This report.</returns>
    internal Report Diagnostics(IEnumerable<MarkupDiagnostic> diagnostics, SourceText? text = null)
    {
        int count = 0;

        foreach (MarkupDiagnostic diagnostic in diagnostics)
        {
            count++;

            Rows.Add(new DiagnosticRow(diagnostic, text));
        }

        if (count == 0)
        {
            Rows.Add(new NoteRow("ничего не сообщено"));
        }

        return this;
    }
}
