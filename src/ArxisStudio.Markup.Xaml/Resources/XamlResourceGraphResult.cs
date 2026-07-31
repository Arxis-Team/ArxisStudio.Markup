using System;
using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup.Xaml;

/// <summary>What a walk of the resource graph found.</summary>
/// <param name="Documents">Every document reached, including the one the walk started from.</param>
/// <param name="Cycles">Each cycle found, as the documents forming it in dependency order.</param>
/// <param name="Diagnostics">Everything noticed along the way, in the order it was noticed.</param>
public sealed record XamlResourceGraphResult(
    ImmutableArray<Uri> Documents,
    ImmutableArray<ImmutableArray<Uri>> Cycles,
    ImmutableArray<MarkupDiagnostic> Diagnostics)
{
    /// <summary>Gets a value indicating whether any chain of includes leads back on itself.</summary>
    public bool HasCycles => !Cycles.IsEmpty;

    /// <summary>Gets a value indicating whether the walk completed without errors.</summary>
    /// <remarks>
    /// Warnings do not count. An include pointing at a document no provider knows is ordinary
    /// in an editor, where the file may simply not have been written yet.
    /// </remarks>
    public bool Success => !Diagnostics.Any(static diagnostic => diagnostic.IsError);
}
