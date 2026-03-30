using System.Collections.Generic;
using ArxisStudio.Markup.DesignEditorBridge;
using ArxisStudio.Markup.Metadata;
using Xunit;

namespace ArxisStudio.Markup.Generator.Tests;

/// <summary>
/// Тесты валидатора метаданных дизайнера.
/// </summary>
public class MetadataValidatorTests
{
    /// <summary>
    /// Проверяет диагностику неизвестного свойства.
    /// </summary>
    [Fact]
    public void Validate_should_report_unknown_property()
    {
        var overlay = new DesignOverlay(
            null,
            new Dictionary<NodeRef, NodeDesignData>
            {
                [new NodeRef("/Root")] = new(new Dictionary<string, DesignValue>
                {
                    ["UnknownProperty"] = new DesignScalarValue(true)
                })
            });

        var registry = new DesignPropertyRegistry();
        var validator = new SimpleMetadataValidator();

        var diagnostics = validator.Validate(overlay, registry);

        Assert.Contains(diagnostics, d => d.Code == MetadataDiagnosticCodes.UnknownProperty);
    }

    /// <summary>
    /// Проверяет диагностику несовместимого типа значения свойства.
    /// </summary>
    [Fact]
    public void Validate_should_report_type_mismatch()
    {
        var overlay = new DesignOverlay(
            null,
            new Dictionary<NodeRef, NodeDesignData>
            {
                [new NodeRef("/Root")] = new(new Dictionary<string, DesignValue>
                {
                    ["IsHitTestVisible"] = new DesignScalarValue("not-bool")
                })
            });

        var registry = new DesignPropertyRegistry();
        registry.Register(new DesignPropertyDescriptor(
            "Avalonia.Input.InputElement.IsHitTestVisible",
            typeof(bool),
            new[] { "IsHitTestVisible" }));

        var validator = new SimpleMetadataValidator();
        var diagnostics = validator.Validate(overlay, registry);

        Assert.Contains(diagnostics, d => d.Code == MetadataDiagnosticCodes.InvalidPropertyType);
    }
}
