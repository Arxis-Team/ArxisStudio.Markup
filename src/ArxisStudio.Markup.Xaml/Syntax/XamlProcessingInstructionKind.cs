namespace ArxisStudio.Markup.Xaml;

/// <summary>Which prolog construct a <see cref="XamlProcessingInstruction"/> is.</summary>
public enum XamlProcessingInstructionKind
{
    /// <summary>An ordinary <c>&lt;?target ?&gt;</c> instruction.</summary>
    ProcessingInstruction,

    /// <summary>The <c>&lt;?xml version="..." ?&gt;</c> declaration.</summary>
    XmlDeclaration,

    /// <summary>A <c>&lt;!DOCTYPE ...&gt;</c> declaration.</summary>
    DocumentType,
}
