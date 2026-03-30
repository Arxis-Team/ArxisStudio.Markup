using System.Collections.Generic;
using Avalonia.Controls;
using ArxisStudio.Markup.DesignEditorBridge;
using ArxisStudio.Markup.Metadata;
using Xunit;

namespace ArxisStudio.Markup.Generator.Tests;

/// <summary>
/// Тесты применения оверлея метаданных к контролам.
/// </summary>
public class DesignOverlayApplierTests
{
    /// <summary>
    /// Проверяет применение зарегистрированного скалярного свойства.
    /// </summary>
    [Fact]
    public void Apply_should_set_registered_scalar_property()
    {
        var propertyRegistry = new DesignPropertyRegistry();
        propertyRegistry.RegisterKnownProperties();

        var applierRegistry = new DesignPropertyApplierRegistry();
        applierRegistry.RegisterKnownAppliers();

        var bridge = new DesignOverlayApplier(propertyRegistry, applierRegistry);

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

        var diagnostics = bridge.Apply(
            overlay,
            new Dictionary<NodeRef, Control> { [nodeRef] = control });

        Assert.Empty(diagnostics);
        Assert.False(control.IsHitTestVisible);
    }
}
