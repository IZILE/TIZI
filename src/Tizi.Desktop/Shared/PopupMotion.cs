using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using System.Reflection;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

/// <summary>Open from the trigger surface; cancel queued work when a popup closes.</summary>
internal sealed class PopupMotion : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty = AvaloniaProperty.RegisterAttached<PopupMotion, Popup, bool>("Enabled");
    private static readonly ConditionalWeakTable<Popup, State> States = new();
    static PopupMotion() => EnabledProperty.Changed.AddClassHandler<Popup>((popup, change) =>
    {
        if (change.NewValue is true) States.GetValue(popup, item => new State(item));
        else if (States.TryGetValue(popup, out var state)) { state.Detach(); States.Remove(popup); }
    });
    public static bool GetEnabled(Popup popup) => popup.GetValue(EnabledProperty);
    public static void SetEnabled(Popup popup, bool enabled) => popup.SetValue(EnabledProperty, enabled);
    internal static void CloseImmediately(ComboBox choice)
    {
        var popup = choice.GetVisualDescendants().OfType<Popup>().FirstOrDefault();
        if (popup is not null)
        {
            if (States.TryGetValue(popup, out var state)) state.CloseImmediately(); else popup.Close();
        }
        choice.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
    }

    private sealed class State
    {
        private readonly Popup _popup;
        private Avalonia.Controls.Control? _child;
        private Transitions? _previous;
        private double _opacity;
        private PopupRevealSurface? _reveal;
        private int _generation;
        private bool _closing, _allowClose;
        private bool _passThrough;
        // Avalonia 12.1.3 keeps the cancellable event internal. Only this event is
        // bridged; the pinned public Close() still owns disposal and input cleanup.
        private static readonly EventInfo? ClosingEvent = typeof(Popup).GetEvent("Closing", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly EventHandler<CancelEventArgs> _closingHandler;
        internal State(Popup popup)
        {
            _popup = popup; _closingHandler = Closing; popup.Opened += Opened; popup.Closed += Closed;
            ClosingEvent?.GetAddMethod(true)?.Invoke(popup, [_closingHandler]);
            popup.PropertyChanged += Changed;
        }
        private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Popup.IsOpenProperty && _popup.IsOpen && _closing && _reveal is { } reveal)
            { ++_generation; _closing = false; _popup.OverlayDismissEventPassThrough = _passThrough; Position(reveal); reveal.IsHitTestVisible = true; reveal.Progress = 1; }
        }
        private void Closing(object? sender, CancelEventArgs args)
        {
            if (_allowClose || _reveal is not { Trigger: ComboBox choice } reveal || TopLevel.GetTopLevel(choice) is not Window { IsVisible: true }) return;
            args.Cancel = true;
            if (_closing) return;
            _closing = true; _passThrough = _popup.OverlayDismissEventPassThrough; _popup.OverlayDismissEventPassThrough = true; var generation = ++_generation;
            reveal.IsHitTestVisible = false; reveal.Progress = 0;
            choice.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
            _ = CompleteCloseAsync(generation);
        }
        private async Task CompleteCloseAsync(int generation)
        {
            await Task.Delay(140);
            if (generation != _generation || !_closing) return;
            _allowClose = true;
            try { _popup.Close(); } finally { _allowClose = false; }
        }
        internal void CloseImmediately()
        {
            ++_generation; _allowClose = true;
            try { _popup.Close(); } finally { _allowClose = false; }
        }
        private void Position(PopupRevealSurface reveal)
        {
            if (reveal.Trigger is not { } trigger || TopLevel.GetTopLevel(trigger) is not { } root
                || trigger.TranslatePoint(default, root) is not { } origin) return;
            var below = root.Bounds.Height - origin.Y - 4;
            var above = origin.Y + trigger.Bounds.Height - 4;
            var requested = trigger is ComboBox choice ? choice.MaxDropDownHeight : 420;
            var menuHeight = reveal.Child?.DesiredSize.Height ?? 0;
            var needed = trigger.Bounds.Height + Math.Min(requested, menuHeight > 0 ? menuHeight : 180);
            reveal.OpensUp = below < needed && above > below;
            _popup.Placement = reveal.OpensUp ? PlacementMode.TopEdgeAlignedLeft : PlacementMode.BottomEdgeAlignedLeft;
            _popup.VerticalOffset = reveal.OpensUp ? trigger.Bounds.Height : -trigger.Bounds.Height;
            _popup.MaxHeight = Math.Max(trigger.Bounds.Height, Math.Min(requested + trigger.Bounds.Height, reveal.OpensUp ? above : below));
        }
        private void Opened(object? sender, EventArgs args)
        {
            Restore();
            if (_popup.Child is not { } child) return;
            var generation = ++_generation;
            if (child is PopupRevealSurface reveal)
            {
                Position(reveal);
                _reveal = reveal; reveal.SetImmediately(0);
                Dispatcher.UIThread.Post(() => { if (_generation == generation && _popup.IsOpen) reveal.Progress = 1; }, DispatcherPriority.Loaded);
                return;
            }
            _child = child; _previous = child.Transitions; _opacity = child.Opacity;
            child.Transitions = null; child.Opacity = 0;
            var transitions = new Transitions();
            if (_previous is not null)
                foreach (var transition in _previous.Where(item => item is not TransitionBase known || known.Property != Avalonia.Visual.OpacityProperty)) transitions.Add(transition);
            transitions.Add(new DoubleTransition { Property = Avalonia.Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(80), Easing = new SplineEasing(0, 0, 0.58, 1) });
            child.Transitions = transitions;
            Dispatcher.UIThread.Post(() =>
            {
                if (_generation == generation && _popup.IsOpen && ReferenceEquals(_child, child)) Motion.Set(child, Avalonia.Visual.OpacityProperty, _opacity);
            }, DispatcherPriority.Loaded);
        }
        private void Closed(object? sender, EventArgs args) { ++_generation; if (_closing) _popup.OverlayDismissEventPassThrough = _passThrough; _closing = false; Restore(); }
        private void Restore()
        {
            if (_reveal is { } reveal) { reveal.SetImmediately(1); reveal.IsHitTestVisible = true; _reveal = null; }
            if (_child is not { } child) return;
            child.Transitions = null; child.Opacity = _opacity; child.Transitions = _previous;
            _child = null; _previous = null;
        }
        internal void Detach()
        {
            _popup.Opened -= Opened; _popup.Closed -= Closed; _popup.PropertyChanged -= Changed;
            ClosingEvent?.GetRemoveMethod(true)?.Invoke(_popup, [_closingHandler]); ++_generation; Restore();
        }
    }
}

/// <summary>A choice grows as one painted surface, retaining the trigger's unscaled content.</summary>
public sealed class PopupRevealSurface : Avalonia.Controls.Decorator
{
    public static readonly StyledProperty<Control?> TriggerProperty = AvaloniaProperty.Register<PopupRevealSurface, Control?>(nameof(Trigger));
    public static readonly StyledProperty<Control?> HeaderProperty = AvaloniaProperty.Register<PopupRevealSurface, Control?>(nameof(Header));
    public static readonly StyledProperty<CornerRadius> SourceCornerRadiusProperty = AvaloniaProperty.Register<PopupRevealSurface, CornerRadius>(nameof(SourceCornerRadius), new(18));
    public Control? Trigger { get => GetValue(TriggerProperty); set => SetValue(TriggerProperty, value); }
    public Control? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public CornerRadius SourceCornerRadius { get => GetValue(SourceCornerRadiusProperty); set => SetValue(SourceCornerRadiusProperty, value); }
    internal bool OpensUp { get; set; }
    internal double TriggerHeight => Trigger?.Bounds.Height ?? 0;
    internal Rect VisibleBounds
    {
        get
        {
            var height = TriggerHeight + Math.Max(0, Bounds.Height - TriggerHeight) * Math.Clamp(Progress, 0, 1);
            return new Rect(0, OpensUp ? Bounds.Height - height : 0, Bounds.Width, height);
        }
    }
    private VisualBrush? _headerBrush;
    private static readonly IBrush SurfaceFill = Brush.Parse("#353535");
    private static readonly IPen SurfaceEdge = new Pen(Brush.Parse("#25666666"), 0.5);
    public static readonly StyledProperty<bool> RevealOnAttachProperty = AvaloniaProperty.Register<PopupRevealSurface, bool>(nameof(RevealOnAttach));
    public bool RevealOnAttach { get => GetValue(RevealOnAttachProperty); set => SetValue(RevealOnAttachProperty, value); }
    public static readonly StyledProperty<bool> GentleMenuMotionProperty = AvaloniaProperty.Register<PopupRevealSurface,bool>(nameof(GentleMenuMotion));
    public bool GentleMenuMotion { get => GetValue(GentleMenuMotionProperty); set => SetValue(GentleMenuMotionProperty,value); }
    private int _attachGeneration;
    public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<PopupRevealSurface, double>(nameof(Progress), 1);
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public PopupRevealSurface()
    {
        ClipToBounds = true;
        Transitions = new() { new DoubleTransition { Property = ProgressProperty, Duration = TimeSpan.FromMilliseconds(120), Easing = new SplineEasing(0, 0, 0.58, 1) } };
        PropertyChanged += (_, change) =>
        {
            if (change.Property == HeaderProperty) _headerBrush = Header is { } header ? new VisualBrush(header) { Stretch = Stretch.Fill } : null;
            if (change.Property == TriggerProperty || change.Property == GentleMenuMotionProperty)
            {
                Transitions = new() { new DoubleTransition { Property = ProgressProperty,
                    Duration = TimeSpan.FromMilliseconds(GentleMenuMotion ? 200 : Trigger is null ? 120 : 140),
                    Easing = GentleMenuMotion ? new SplineEasing(.2,0,.2,1) : new SplineEasing(0.16, 1, 0.3, 1) } };
                InvalidateMeasure();
            }
            if (change.Property == ProgressProperty || change.Property == BoundsProperty || change.Property == TriggerProperty || change.Property == SourceCornerRadiusProperty) RefreshClip();
        };
        AttachedToVisualTree += (_, _) =>
        {
            if (!RevealOnAttach) return;
            var generation = ++_attachGeneration; SetImmediately(0);
            Dispatcher.UIThread.Post(() => { if (generation == _attachGeneration && VisualRoot is not null) Progress = 1; }, DispatcherPriority.Loaded);
        };
        DetachedFromVisualTree += (_, _) => { ++_attachGeneration; if (RevealOnAttach) SetImmediately(1); };
        RefreshClip();
    }
    private void RefreshClip()
    {
        var progress = Math.Clamp(Progress, 0, 1);
        // Publish the clip to the compositor on every frame. Mutating the same geometry
        // can leave a native popup using its original empty clipping snapshot.
        var rect = VisibleBounds;
        var radius = Trigger is null ? 0 : CornerRadiusAt(progress);
        Clip = progress >= 1 ? null : new RectangleGeometry(rect, radius, radius);
        InvalidateVisual();
    }
    private double CornerRadiusAt(double progress) => Math.Min(TriggerHeight / 2, SourceCornerRadius.TopLeft) * (1 - progress) + 12 * progress;
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Trigger is null) return base.MeasureOverride(availableSize);
        Child?.Measure(new Size(availableSize.Width, Math.Max(0, availableSize.Height - TriggerHeight)));
        return new Size(Child?.DesiredSize.Width ?? 0, TriggerHeight + (Child?.DesiredSize.Height ?? 0));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Trigger is null) return base.ArrangeOverride(finalSize);
        Child?.Arrange(new Rect(0, OpensUp ? 0 : TriggerHeight, finalSize.Width, Math.Max(0, finalSize.Height - TriggerHeight)));
        RefreshClip();
        return finalSize;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Trigger is null) return;
        var rect = VisibleBounds;
        var radius = CornerRadiusAt(Math.Clamp(Progress, 0, 1));
        context.DrawRectangle(SurfaceFill, SurfaceEdge, rect.Deflate(0.25), radius, radius);
        // Only the live content is copied, never the popup/background itself. This keeps
        // flags, two-line labels and the rotating chevron at their original coordinates.
        if (Header is { } header && _headerBrush is { } brush)
        {
            var content = header.Bounds;
            context.DrawRectangle(brush, null, new Rect(content.X,
                content.Y + (OpensUp ? Bounds.Height - TriggerHeight : 0), content.Width, content.Height));
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (Trigger is ComboBox choice)
        {
            var y = e.GetPosition(this).Y;
            if (OpensUp ? y >= Bounds.Height - TriggerHeight : y < TriggerHeight)
            { choice.IsDropDownOpen = false; choice.Focus(); e.Handled = true; }
        }
        base.OnPointerPressed(e);
    }
    internal void SetImmediately(double value) { var transitions = Transitions; Transitions = null; Progress = value; Transitions = transitions; }
}
