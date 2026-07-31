namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// The diagnostic codes this package produces.
/// </summary>
/// <remarks>
/// <para>
/// Codes are a stable contract. Consumers suppress, route and test on them, so a code's
/// meaning never changes and a retired code is never reused for something else. Messages, by
/// contrast, are for people and may be reworded freely.
/// </para>
/// <para>
/// <c>AXM1xxx</c> is reserved for syntax; the loader owns later ranges.
/// </para>
/// </remarks>
public static class XamlDiagnosticCodes
{
    /// <summary>A comment ran to the end of the document without <c>--&gt;</c>.</summary>
    public const string UnterminatedComment = "AXM1001";

    /// <summary>A CDATA section ran to the end of the document without <c>]]&gt;</c>.</summary>
    public const string UnterminatedCData = "AXM1002";

    /// <summary>A processing instruction ran to the end of the document without <c>?&gt;</c>.</summary>
    public const string UnterminatedProcessingInstruction = "AXM1003";

    /// <summary>An attribute value was not closed by its opening quote character.</summary>
    public const string UnterminatedAttributeValue = "AXM1004";

    /// <summary>A character appeared where the grammar does not allow one.</summary>
    public const string UnexpectedCharacter = "AXM1005";

    /// <summary>An <c>&amp;</c> did not begin a well-formed entity or character reference.</summary>
    public const string InvalidEntityReference = "AXM1006";

    /// <summary>A DOCTYPE declaration ran to the end of the document without <c>&gt;</c>.</summary>
    public const string UnterminatedDocumentType = "AXM1007";

    /// <summary>A tag opened but no element name followed.</summary>
    public const string ExpectedElementName = "AXM1010";

    /// <summary>A tag ran to the end of the document without <c>&gt;</c> or <c>/&gt;</c>.</summary>
    public const string UnterminatedTag = "AXM1011";

    /// <summary>An element was never closed.</summary>
    public const string UnclosedElement = "AXM1012";

    /// <summary>An end tag appeared with no element open.</summary>
    public const string UnexpectedEndTag = "AXM1013";

    /// <summary>An end tag named a different element than the one it closed.</summary>
    public const string MismatchedEndTag = "AXM1014";

    /// <summary>An attribute name was not followed by <c>=</c> and a quoted value.</summary>
    public const string MissingAttributeValue = "AXM1015";

    /// <summary>An element declared the same attribute name twice.</summary>
    public const string DuplicateAttribute = "AXM1016";

    /// <summary>An element bound the same namespace prefix twice.</summary>
    public const string DuplicateNamespacePrefix = "AXM1017";

    /// <summary>The document has more than one root element.</summary>
    public const string MultipleRootElements = "AXM1018";

    /// <summary>The document has no root element.</summary>
    public const string MissingRootElement = "AXM1019";

    /// <summary>A markup extension ran to the end of the value without <c>}</c>.</summary>
    public const string UnterminatedMarkupExtension = "AXM1030";

    /// <summary>A markup extension opened but named no type.</summary>
    public const string ExpectedMarkupExtensionName = "AXM1031";

    /// <summary>A quoted markup-extension argument was not closed by its opening quote.</summary>
    public const string UnterminatedQuotedArgument = "AXM1032";

    /// <summary>Text follows a markup extension's closing brace.</summary>
    public const string MalformedMarkupExtension = "AXM1033";

    /// <summary>An edit targeted a node that belongs to a different document.</summary>
    public const string ForeignNode = "AXM1040";

    /// <summary>Two edits in one operation would change overlapping regions of the document.</summary>
    public const string ConflictingEdits = "AXM1041";

    /// <summary>An include element has no <c>Source</c> attribute.</summary>
    public const string MissingIncludeSource = "AXM1050";

    /// <summary>An include's <c>Source</c> could not be resolved to a URI.</summary>
    public const string UnresolvedIncludeUri = "AXM1051";

    /// <summary>A chain of includes leads back to a document already in the chain.</summary>
    public const string ResourceIncludeCycle = "AXM1052";

    /// <summary>An include points at a URI no source provider knows.</summary>
    public const string UnresolvedIncludeDocument = "AXM1053";
}
