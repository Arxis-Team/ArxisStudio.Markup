using ArxisStudio.Markup.DesignEditorBridge;
using ArxisStudio.Markup.Metadata;
using Xunit;

namespace ArxisStudio.Markup.Generator.Tests;

/// <summary>
/// Тесты реестра свойств дизайнера.
/// </summary>
public class DesignPropertyRegistryTests
{
    /// <summary>
    /// Проверяет разрешение ключа как по алиасу, так и по каноническому имени.
    /// </summary>
    [Fact]
    public void Resolve_should_support_alias_and_canonical_key()
    {
        var registry = new DesignPropertyRegistry();
        registry.RegisterKnownProperties();

        Assert.True(registry.TryResolve("IsHitTestVisible", out var byAlias));
        Assert.Equal(KnownDesignProperties.IsHitTestVisible, byAlias.CanonicalKey);

        Assert.True(registry.TryResolve(KnownDesignProperties.IsHitTestVisible, out var byCanonical));
        Assert.Equal(KnownDesignProperties.IsHitTestVisible, byCanonical.CanonicalKey);
    }
}
