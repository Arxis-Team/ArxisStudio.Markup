using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ArxisStudio.Designer.Abstractions;
using ArxisStudio.Designer.Behaviors;
using ArxisStudio.Designer.Models;
using ArxisStudio.Designer.Services;
using ArxisStudio.Markup;

namespace ArxisStudio.Designer.Controls;

/// <summary>
/// Базовый контрол design surface для визуального конструктора.
/// </summary>
/// <remarks>
/// Контрол разделяет production preview и design-time chrome:
/// preview отображается в отдельном слое, а рамки выбора и handles рисуются поверх
/// через <see cref="DesignerAdornerLayer"/>.
/// </remarks>
public sealed class DesignerSurfaceControl : UserControl
{
    /// <summary>
    /// Определяет свойство <see cref="Document"/>.
    /// </summary>
    public static readonly StyledProperty<UiDocument?> DocumentProperty =
        AvaloniaProperty.Register<DesignerSurfaceControl, UiDocument?>(nameof(Document));

    /// <summary>
    /// Определяет свойство <see cref="SelectedNode"/>.
    /// </summary>
    public static readonly StyledProperty<UiNode?> SelectedNodeProperty =
        AvaloniaProperty.Register<DesignerSurfaceControl, UiNode?>(nameof(SelectedNode));

    /// <summary>
    /// Определяет свойство <see cref="Zoom"/>.
    /// </summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<DesignerSurfaceControl, double>(nameof(Zoom), 1.0);

    /// <summary>
    /// Определяет свойство <see cref="PreviewBuilder"/>.
    /// </summary>
    public static readonly StyledProperty<IDesignerPreviewBuilder?> PreviewBuilderProperty =
        AvaloniaProperty.Register<DesignerSurfaceControl, IDesignerPreviewBuilder?>(nameof(PreviewBuilder));

    /// <summary>
    /// Определяет свойство <see cref="SelectionService"/>.
    /// </summary>
    public static readonly StyledProperty<IDesignerSelectionService?> SelectionServiceProperty =
        AvaloniaProperty.Register<DesignerSurfaceControl, IDesignerSelectionService?>(nameof(SelectionService));

    /// <summary>
    /// Определяет свойство <see cref="BehaviorResolver"/>.
    /// </summary>
    public static readonly StyledProperty<IDesignContainerBehaviorResolver?> BehaviorResolverProperty =
        AvaloniaProperty.Register<DesignerSurfaceControl, IDesignContainerBehaviorResolver?>(nameof(BehaviorResolver));

    private readonly Border _surfaceBorder;
    private readonly ContentControl _previewPresenter;
    private readonly DesignerAdornerLayer _adornerLayer;
    private readonly Grid _rootGrid;
    private DesignerPreviewScene? _scene;
    private bool _selectionSyncInProgress;

    static DesignerSurfaceControl()
    {
        DocumentProperty.Changed.AddClassHandler<DesignerSurfaceControl>((control, _) => control.RebuildScene());
        ZoomProperty.Changed.AddClassHandler<DesignerSurfaceControl>((control, _) => control.RebuildScene());
        SelectedNodeProperty.Changed.AddClassHandler<DesignerSurfaceControl>((control, _) => control.OnSelectedNodeChanged());
        PreviewBuilderProperty.Changed.AddClassHandler<DesignerSurfaceControl>((control, _) => control.RebuildScene());
        SelectionServiceProperty.Changed.AddClassHandler<DesignerSurfaceControl>((control, args) => control.OnSelectionServiceChanged(args));
    }

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="DesignerSurfaceControl"/>.
    /// </summary>
    public DesignerSurfaceControl()
    {
        _previewPresenter = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top
        };

        _surfaceBorder = new Border
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Child = _previewPresenter
        };

        _adornerLayer = new DesignerAdornerLayer();
        _adornerLayer.IsHitTestVisible = false;

        _rootGrid = new Grid
        {
            Background = Brushes.Transparent,
            Children =
            {
                _surfaceBorder,
                _adornerLayer
            }
        };
        _rootGrid.AddHandler(PointerPressedEvent, OnSurfacePointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        Content = _rootGrid;

        PreviewBuilder = EmptyDesignerPreviewBuilder.Instance;
        SelectionService = new DesignerSelectionService();
        BehaviorResolver = DefaultDesignContainerBehaviorResolver.Instance;
    }

    /// <summary>
    /// Возвращает или задаёт документ, отображаемый на design surface.
    /// </summary>
    public UiDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>
    /// Возвращает или задаёт текущий выбранный узел.
    /// </summary>
    public UiNode? SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    /// <summary>
    /// Возвращает или задаёт текущий масштаб preview.
    /// </summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// Возвращает или задаёт builder, отвечающий за построение preview сцены.
    /// </summary>
    public IDesignerPreviewBuilder? PreviewBuilder
    {
        get => GetValue(PreviewBuilderProperty);
        set => SetValue(PreviewBuilderProperty, value);
    }

    /// <summary>
    /// Возвращает или задаёт сервис выбора узлов.
    /// </summary>
    public IDesignerSelectionService? SelectionService
    {
        get => GetValue(SelectionServiceProperty);
        set => SetValue(SelectionServiceProperty, value);
    }

    /// <summary>
    /// Возвращает или задаёт разрешатель container behavior.
    /// </summary>
    public IDesignContainerBehaviorResolver? BehaviorResolver
    {
        get => GetValue(BehaviorResolverProperty);
        set => SetValue(BehaviorResolverProperty, value);
    }

    /// <summary>
    /// Принудительно перестраивает preview сцену.
    /// </summary>
    public void RebuildScene()
    {
        if (Document == null)
        {
            _scene = null;
            _previewPresenter.Content = null;
            _surfaceBorder.Width = double.NaN;
            _surfaceBorder.Height = double.NaN;
            _adornerLayer.Scene = null;
            _adornerLayer.SelectedNode = null;
            return;
        }

        var builder = PreviewBuilder ?? EmptyDesignerPreviewBuilder.Instance;
        _scene = builder.Build(Document, new DesignerSurfaceContext(Zoom));

        _previewPresenter.Content = _scene.RootControl;

        var surfaceSize = _scene.SurfaceSize ??
                          new Size(
                              Document.Design?.SurfaceWidth ?? double.NaN,
                              Document.Design?.SurfaceHeight ?? double.NaN);

        _surfaceBorder.Width = double.IsNaN(surfaceSize.Width) ? double.NaN : surfaceSize.Width;
        _surfaceBorder.Height = double.IsNaN(surfaceSize.Height) ? double.NaN : surfaceSize.Height;

        _adornerLayer.Scene = _scene;
        _adornerLayer.SelectedNode = SelectedNode;
    }

    private void OnSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_scene == null)
        {
            return;
        }

        var position = e.GetPosition(_previewPresenter);
        var hitNode = _scene.HitTest(position);
        if (hitNode == null)
        {
            return;
        }

        SelectedNode = hitNode;
        e.Handled = true;
    }

    private void OnSelectedNodeChanged()
    {
        _adornerLayer.SelectedNode = SelectedNode;

        if (SelectedNode != null)
        {
            _scene?.BringIntoView(SelectedNode);
        }

        if (_selectionSyncInProgress || SelectionService == null)
        {
            return;
        }

        try
        {
            _selectionSyncInProgress = true;
            SelectionService.Select(SelectedNode);
        }
        finally
        {
            _selectionSyncInProgress = false;
        }
    }

    private void OnSelectionServiceChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (args.OldValue is IDesignerSelectionService oldService)
        {
            oldService.SelectedNodeChanged -= OnSelectionServiceSelectedNodeChanged;
        }

        if (args.NewValue is IDesignerSelectionService newService)
        {
            newService.SelectedNodeChanged += OnSelectionServiceSelectedNodeChanged;
            SelectedNode = newService.SelectedNode;
        }
    }

    private void OnSelectionServiceSelectedNodeChanged(object? sender, UiNode? node)
    {
        if (_selectionSyncInProgress)
        {
            return;
        }

        try
        {
            _selectionSyncInProgress = true;
            SelectedNode = node;
        }
        finally
        {
            _selectionSyncInProgress = false;
        }
    }
}
