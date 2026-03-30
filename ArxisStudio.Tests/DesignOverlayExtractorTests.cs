using System.Collections.Generic;
using Avalonia.Controls;
using ArxisStudio.Attached;
using ArxisStudio.Markup.DesignEditorBridge;
using ArxisStudio.Markup.Metadata;
using Xunit;

namespace ArxisStudio.Markup.Generator.Tests;

/// <summary>
/// Тесты извлечения оверлея метаданных из контролов.
/// </summary>
public class DesignOverlayExtractorTests
{
    /// <summary>
    /// Проверяет извлечение зарегистрированных свойств из контрола.
    /// </summary>
    [Fact]
    public void Extract_should_capture_registered_properties()
    {
        var propertyRegistry = new DesignPropertyRegistry();
        propertyRegistry.RegisterKnownProperties();

        var readerRegistry = new DesignPropertyReaderRegistry();
        readerRegistry.RegisterKnownReaders();

        var extractor = new DesignOverlayExtractor(propertyRegistry, readerRegistry);

        var nodeRef = new NodeRef("/Root");
        var control = new Border { IsHitTestVisible = false };
        Layout.SetX(control, 120);
        Layout.SetY(control, 360);
        DesignInteraction.SetMovePolicy(control, MovePolicy.X);

        var overlay = extractor.Extract(new Dictionary<NodeRef, Control>
        {
            [nodeRef] = control
        });

        var node = Assert.Single(overlay.Nodes);
        Assert.Equal("/Root", node.Key.Value);
        var hitTest = Assert.IsType<DesignScalarValue>(node.Value.Properties[KnownDesignProperties.IsHitTestVisible]);
        Assert.Equal(false, hitTest.Value);

        var x = Assert.IsType<DesignScalarValue>(node.Value.Properties[KnownDesignProperties.LayoutX]);
        Assert.Equal(120d, x.Value);

        var y = Assert.IsType<DesignScalarValue>(node.Value.Properties[KnownDesignProperties.LayoutY]);
        Assert.Equal(360d, y.Value);

        var movePolicy = Assert.IsType<DesignScalarValue>(node.Value.Properties[KnownDesignProperties.MovePolicy]);
        Assert.Equal("X", movePolicy.Value);
    }
}
