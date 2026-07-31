namespace ArxisStudio.Markup;

/// <summary>
/// Which stage of the pipeline a <see cref="MarkupDiagnostic"/> came from.
/// </summary>
/// <remarks>
/// Consumers routinely need to treat these differently — a syntax error means the document
/// cannot be trusted, while an unresolved type only means the runtime tree is incomplete — so
/// the stage is carried explicitly rather than inferred from a code prefix or a message.
/// </remarks>
public enum MarkupDiagnosticCategory
{
    /// <summary>The document's text could not be read as well-formed markup.</summary>
    Parse,

    /// <summary>A name, type, member or resource could not be resolved.</summary>
    Resolution,

    /// <summary>An object could not be created or initialised from the document.</summary>
    Load,

    /// <summary>The document and the objects created from it could not be kept in step.</summary>
    Synchronization,
}
