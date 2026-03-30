using System.Collections.Generic;
using Avalonia.Controls;
using ArxisStudio.Markup.DesignEditorBridge;
using ArxisStudio.Markup.Metadata;
using Xunit;

namespace ArxisStudio.Markup.Generator.Tests;

/// <summary>
/// Тесты фасада <see cref="DesignEditorBridgeRuntime"/>.
/// </summary>
public class DesignEditorBridgeRuntimeTests
{
    /// <summary>
    /// Проверяет, что конфигурация по умолчанию регистрирует встроенные маппинги.
    /// </summary>
    [Fact]
    public void CreateDefault_should_register_known_mappings()
    {
        var runtime = DesignEditorBridgeRuntime.CreateDefault();
        var nodeRef = new NodeRef("/Root");
        var control = new Border { IsHitTestVisible = true };
        var overlay = new DesignOverlay(
            null,
            new Dictionary<NodeRef, NodeDesignData>
            {
                [nodeRef] = new(new Dictionary<string, DesignValue>
                {
                    ["IsHitTestVisible"] = new DesignScalarValue(false)
                })
            });

        var diagnostics = runtime.Apply(overlay, new Dictionary<NodeRef, Control> { [nodeRef] = control });

        Assert.Empty(diagnostics);
        Assert.False(control.IsHitTestVisible);
    }

    /// <summary>
    /// Проверяет, что пустая конфигурация не содержит встроенных свойств.
    /// </summary>
    [Fact]
    public void CreateEmpty_should_not_register_known_mappings()
    {
        var runtime = DesignEditorBridgeRuntime.CreateEmpty();
        var overlay = new DesignOverlay(
            null,
            new Dictionary<NodeRef, NodeDesignData>
            {
                [new NodeRef("/Root")] = new(new Dictionary<string, DesignValue>
                {
                    ["IsHitTestVisible"] = new DesignScalarValue(false)
                })
            });

        var diagnostics = runtime.Validate(overlay);

        Assert.Contains(diagnostics, d => d.Code == MetadataDiagnosticCodes.UnknownProperty);
    }
}
