using System.Collections.Generic;
using Avalonia.Controls;
using ArxisStudio.Attached;
using ArxisStudio.Markup.DesignEditorBridge;
using ArxisStudio.Markup.Metadata;
using Xunit;

namespace ArxisStudio.Markup.Generator.Tests;

/// <summary>
/// Тесты стандартных сопоставлений bridge-слоя для DesignEditor.
/// </summary>
public class DefaultDesignEditorMappingsTests
{
    /// <summary>
    /// Проверяет применение свойств позиционирования и взаимодействия.
    /// </summary>
    [Fact]
    public void Apply_should_support_layout_and_interaction_properties()
    {
        var propertyRegistry = new DesignPropertyRegistry();
        propertyRegistry.RegisterKnownProperties();

        var applierRegistry = new DesignPropertyApplierRegistry();
        applierRegistry.RegisterKnownAppliers();

        var applier = new DesignOverlayApplier(propertyRegistry, applierRegistry);

        var nodeRef = new NodeRef("/Root");
        var control = new Border();

        var overlay = new DesignOverlay(
            null,
            new Dictionary<NodeRef, NodeDesignData>
            {
                [nodeRef] = new(new Dictionary<string, DesignValue>
                {
                    ["Layout.X"] = new DesignScalarValue(140),
                    ["Layout.Y"] = new DesignScalarValue("240"),
                    ["DesignInteraction.MovePolicy"] = new DesignScalarValue("X"),
                    ["DesignInteraction.ResizePolicy"] = new DesignScalarValue("None")
                })
            });

        var diagnostics = applier.Apply(overlay, new Dictionary<NodeRef, Control> { [nodeRef] = control });
        Assert.Empty(diagnostics);

        Assert.Equal(140d, Layout.GetX(control));
        Assert.Equal(240d, Layout.GetY(control));
        Assert.Equal(MovePolicy.X, DesignInteraction.GetMovePolicy(control));
        Assert.Equal(ResizePolicy.None, DesignInteraction.GetResizePolicy(control));
    }

    /// <summary>
    /// Проверяет применение свойства по полному каноническому ключу.
    /// </summary>
    [Fact]
    public void Apply_should_support_full_canonical_property_key()
    {
        var propertyRegistry = new DesignPropertyRegistry();
        propertyRegistry.RegisterKnownProperties();

        var applierRegistry = new DesignPropertyApplierRegistry();
        applierRegistry.RegisterKnownAppliers();

        var applier = new DesignOverlayApplier(propertyRegistry, applierRegistry);

        var nodeRef = new NodeRef("/Root");
        var control = new Border { IsHitTestVisible = true };

        var overlay = new DesignOverlay(
            null,
            new Dictionary<NodeRef, NodeDesignData>
            {
                [nodeRef] = new(new Dictionary<string, DesignValue>
                {
                    [KnownDesignProperties.IsHitTestVisible] = new DesignScalarValue(false)
                })
            });

        var diagnostics = applier.Apply(overlay, new Dictionary<NodeRef, Control> { [nodeRef] = control });
        Assert.Empty(diagnostics);
        Assert.False(control.IsHitTestVisible);
    }
}
