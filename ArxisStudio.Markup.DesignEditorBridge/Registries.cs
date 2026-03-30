using System;
using System.Collections.Generic;
using System.Linq;
using ArxisStudio.Markup.Metadata;

namespace ArxisStudio.Markup.DesignEditorBridge;

/// <summary>
/// Реестр дескрипторов свойств дизайнера с поддержкой алиасов.
/// </summary>
public sealed class DesignPropertyRegistry : IDesignPropertyRegistry
{
    private readonly Dictionary<string, DesignPropertyDescriptor> _canonical =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _aliases =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Регистрирует дескриптор свойства дизайнера.
    /// </summary>
    /// <param name="descriptor">Дескриптор свойства.</param>
    public void Register(DesignPropertyDescriptor descriptor)
    {
        _canonical[descriptor.CanonicalKey] = descriptor;

        if (descriptor.Aliases == null)
        {
            return;
        }

        foreach (var alias in descriptor.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
        {
            _aliases[alias] = descriptor.CanonicalKey;
        }
    }

    /// <inheritdoc />
    public bool TryGetByCanonicalKey(string canonicalKey, out DesignPropertyDescriptor descriptor)
    {
        return _canonical.TryGetValue(canonicalKey, out descriptor!);
    }

    /// <inheritdoc />
    public bool TryResolve(string keyOrAlias, out DesignPropertyDescriptor descriptor)
    {
        if (_canonical.TryGetValue(keyOrAlias, out descriptor!))
        {
            return true;
        }

        if (_aliases.TryGetValue(keyOrAlias, out var canonical))
        {
            return _canonical.TryGetValue(canonical, out descriptor!);
        }

        descriptor = null!;
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<DesignPropertyDescriptor> GetAll()
    {
        return _canonical.Values.ToArray();
    }
}

/// <summary>
/// Реестр обработчиков применения значений свойств дизайнера.
/// </summary>
public sealed class DesignPropertyApplierRegistry : IDesignPropertyApplierRegistry
{
    private readonly Dictionary<string, IDesignPropertyApplier> _appliers =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Регистрирует обработчик применения.
    /// </summary>
    /// <param name="canonicalKey">Канонический ключ свойства.</param>
    /// <param name="applier">Обработчик применения.</param>
    public void Register(string canonicalKey, IDesignPropertyApplier applier)
    {
        _appliers[canonicalKey] = applier;
    }

    /// <inheritdoc />
    public bool TryGet(string canonicalKey, out IDesignPropertyApplier applier)
    {
        return _appliers.TryGetValue(canonicalKey, out applier!);
    }
}

/// <summary>
/// Реестр обработчиков чтения значений свойств дизайнера.
/// </summary>
public sealed class DesignPropertyReaderRegistry : IDesignPropertyReaderRegistry
{
    private readonly Dictionary<string, IDesignPropertyReader> _readers =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Регистрирует обработчик чтения.
    /// </summary>
    /// <param name="canonicalKey">Канонический ключ свойства.</param>
    /// <param name="reader">Обработчик чтения.</param>
    public void Register(string canonicalKey, IDesignPropertyReader reader)
    {
        _readers[canonicalKey] = reader;
    }

    /// <inheritdoc />
    public bool TryGet(string canonicalKey, out IDesignPropertyReader reader)
    {
        return _readers.TryGetValue(canonicalKey, out reader!);
    }
}
