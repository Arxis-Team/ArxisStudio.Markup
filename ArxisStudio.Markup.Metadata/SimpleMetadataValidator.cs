using System.Collections.Generic;

namespace ArxisStudio.Markup.Metadata;

/// <summary>
/// Минимальная реализация валидатора metadata.
/// </summary>
public sealed class SimpleMetadataValidator : IMetadataValidator
{
    /// <inheritdoc />
    public IReadOnlyList<MetadataDiagnostic> Validate(DesignMetadata metadata)
    {
        var diagnostics = new List<MetadataDiagnostic>();

        if (metadata.Document != null)
        {
            ValidatePropertyBag(metadata.Document.Properties, diagnostics, null);
        }

        foreach (var node in metadata.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Key.Value))
            {
                diagnostics.Add(new MetadataDiagnostic(
                    MetadataDiagnosticCodes.EmptyNodeRef,
                    "NodeRef must not be empty.",
                    node.Key.Value,
                    null));
            }

            ValidatePropertyBag(node.Value.Properties, diagnostics, node.Key.Value);
        }

        return diagnostics;
    }

    private static void ValidatePropertyBag(
        IReadOnlyDictionary<string, DesignValue> properties,
        ICollection<MetadataDiagnostic> diagnostics,
        string? nodeRef)
    {
        foreach (var property in properties)
        {
            if (string.IsNullOrWhiteSpace(property.Key))
            {
                diagnostics.Add(new MetadataDiagnostic(
                    MetadataDiagnosticCodes.EmptyPropertyKey,
                    "Design property key must not be empty.",
                    nodeRef,
                    property.Key));
            }
        }
    }
}
