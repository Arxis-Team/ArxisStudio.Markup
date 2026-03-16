using ArxisStudio.Markup;

namespace ArxisStudio.Designer.Behaviors;

/// <summary>
/// Разрешатель контейнерного поведения по умолчанию.
/// </summary>
public sealed class DefaultDesignContainerBehaviorResolver : IDesignContainerBehaviorResolver
{
    /// <summary>
    /// Экземпляр разрешателя по умолчанию.
    /// </summary>
    public static DefaultDesignContainerBehaviorResolver Instance { get; } = new();

    private DefaultDesignContainerBehaviorResolver()
    {
    }

    /// <inheritdoc />
    public IDesignContainerBehavior Resolve(UiNode? containerNode) => DefaultDesignContainerBehavior.Instance;
}
