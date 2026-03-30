using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArxisStudio.Markup.Metadata;

/// <summary>
/// Базовая реализация валидатора реестровых скалярных метаданных.
/// </summary>
public sealed class SimpleMetadataValidator : IMetadataValidator
{
    /// <inheritdoc />
    public IReadOnlyList<MetadataDiagnostic> Validate(DesignOverlay overlay, IDesignPropertyRegistry registry)
    {
        var diagnostics = new List<MetadataDiagnostic>();

        if (overlay.Document != null)
        {
            ValidatePropertyBag(
                overlay.Document.Properties,
                registry,
                diagnostics,
                null);
        }

        foreach (var node in overlay.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Key.Value))
            {
                diagnostics.Add(new MetadataDiagnostic(
                    MetadataDiagnosticCodes.EmptyNodeRef,
                    "NodeRef must not be empty.",
                    node.Key.Value,
                    null));
            }

            ValidatePropertyBag(
                node.Value.Properties,
                registry,
                diagnostics,
                node.Key.Value);
        }

        return diagnostics;
    }

    private static void ValidatePropertyBag(
        IReadOnlyDictionary<string, DesignValue> properties,
        IDesignPropertyRegistry registry,
        ICollection<MetadataDiagnostic> diagnostics,
        string? nodeRef)
    {
        foreach (var property in properties)
        {
            if (!registry.TryResolve(property.Key, out var descriptor))
            {
                diagnostics.Add(new MetadataDiagnostic(
                    MetadataDiagnosticCodes.UnknownProperty,
                    $"Unknown design property '{property.Key}'.",
                    nodeRef,
                    property.Key));
                continue;
            }

            if (property.Value is not DesignScalarValue scalar)
            {
                diagnostics.Add(new MetadataDiagnostic(
                    MetadataDiagnosticCodes.NonScalarProperty,
                    $"Property '{property.Key}' must be scalar.",
                    nodeRef,
                    property.Key));
                continue;
            }

            if (!IsCompatible(scalar.Value, descriptor.ValueType))
            {
                diagnostics.Add(new MetadataDiagnostic(
                    MetadataDiagnosticCodes.InvalidPropertyType,
                    $"Property '{property.Key}' value is incompatible with expected type '{descriptor.ValueType.FullName}'.",
                    nodeRef,
                    property.Key));
            }
        }
    }

    private static bool IsCompatible(object? value, Type targetType)
    {
        if (value == null)
        {
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
        }

        var effectiveTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveTarget.IsInstanceOfType(value))
        {
            return true;
        }

        if (effectiveTarget.IsEnum)
        {
            if (value is string enumName)
            {
                foreach (var name in Enum.GetNames(effectiveTarget))
                {
                    if (string.Equals(name, enumName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            try
            {
                _ = Enum.ToObject(effectiveTarget, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            _ = Convert.ChangeType(value, effectiveTarget, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
