using System;
using System.Globalization;
using Avalonia.Controls;
using ArxisStudio.Attached;
using ArxisStudio.Markup.Metadata;

namespace ArxisStudio.Markup.DesignEditorBridge;

/// <summary>
/// Регистрация стандартных сопоставлений bridge-слоя для известных свойств дизайнера.
/// </summary>
public static class DefaultDesignEditorMappings
{
    /// <summary>
    /// Регистрирует встроенные дескрипторы свойств дизайнера.
    /// </summary>
    /// <param name="registry">Реестр дескрипторов свойств.</param>
    public static void RegisterKnownProperties(this DesignPropertyRegistry registry)
    {
        foreach (var descriptor in KnownDesignProperties.Descriptors)
        {
            registry.Register(descriptor);
        }
    }

    /// <summary>
    /// Регистрирует встроенные обработчики применения значений к контролам.
    /// </summary>
    /// <param name="registry">Реестр обработчиков применения.</param>
    public static void RegisterKnownAppliers(this DesignPropertyApplierRegistry registry)
    {
        registry.Register(KnownDesignProperties.IsHitTestVisible, new DelegateApplier((control, value) =>
        {
            control.IsHitTestVisible = ToBoolean(value);
        }));

        registry.Register(KnownDesignProperties.LayoutX, new DelegateApplier((control, value) =>
        {
            Layout.SetX(control, ToDouble(value));
        }));

        registry.Register(KnownDesignProperties.LayoutY, new DelegateApplier((control, value) =>
        {
            Layout.SetY(control, ToDouble(value));
        }));

        registry.Register(KnownDesignProperties.MovePolicy, new DelegateApplier((control, value) =>
        {
            DesignInteraction.SetMovePolicy(control, ToMovePolicy(value));
        }));

        registry.Register(KnownDesignProperties.ResizePolicy, new DelegateApplier((control, value) =>
        {
            DesignInteraction.SetResizePolicy(control, ToResizePolicy(value));
        }));
    }

    /// <summary>
    /// Регистрирует встроенные обработчики чтения значений из контролов.
    /// </summary>
    /// <param name="registry">Реестр обработчиков чтения.</param>
    public static void RegisterKnownReaders(this DesignPropertyReaderRegistry registry)
    {
        registry.Register(KnownDesignProperties.IsHitTestVisible, new DelegateReader(control =>
            (true, control.IsHitTestVisible)));

        registry.Register(KnownDesignProperties.LayoutX, new DelegateReader(control =>
            (true, Layout.GetX(control))));

        registry.Register(KnownDesignProperties.LayoutY, new DelegateReader(control =>
            (true, Layout.GetY(control))));

        registry.Register(KnownDesignProperties.MovePolicy, new DelegateReader(control =>
            (true, DesignInteraction.GetMovePolicy(control).ToString())));

        registry.Register(KnownDesignProperties.ResizePolicy, new DelegateReader(control =>
            (true, DesignInteraction.GetResizePolicy(control).ToString())));
    }

    private static bool ToBoolean(object? value)
    {
        if (value is bool b)
        {
            return b;
        }

        if (value is string s)
        {
            return bool.Parse(s);
        }

        return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    private static double ToDouble(object? value)
    {
        if (value is double d)
        {
            return d;
        }

        if (value is string s)
        {
            return double.Parse(s, CultureInfo.InvariantCulture);
        }

        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static MovePolicy ToMovePolicy(object? value)
    {
        if (value is MovePolicy movePolicy)
        {
            return movePolicy;
        }

        if (value is string name)
        {
            return (MovePolicy)Enum.Parse(typeof(MovePolicy), name, true);
        }

        return (MovePolicy)Enum.ToObject(typeof(MovePolicy), value!);
    }

    private static ResizePolicy ToResizePolicy(object? value)
    {
        if (value is ResizePolicy resizePolicy)
        {
            return resizePolicy;
        }

        if (value is string name)
        {
            return (ResizePolicy)Enum.Parse(typeof(ResizePolicy), name, true);
        }

        return (ResizePolicy)Enum.ToObject(typeof(ResizePolicy), value!);
    }

    private sealed class DelegateApplier : IDesignPropertyApplier
    {
        private readonly Action<Control, object?> _apply;

        internal DelegateApplier(Action<Control, object?> apply)
        {
            _apply = apply;
        }

        void IDesignPropertyApplier.Apply(Control control, object? value)
        {
            _apply(control, value);
        }
    }

    private sealed class DelegateReader : IDesignPropertyReader
    {
        private readonly Func<Control, (bool Success, object? Value)> _read;

        internal DelegateReader(Func<Control, (bool Success, object? Value)> read)
        {
            _read = read;
        }

        bool IDesignPropertyReader.TryRead(Control control, out object? value)
        {
            var result = _read(control);
            value = result.Value;
            return result.Success;
        }
    }
}
