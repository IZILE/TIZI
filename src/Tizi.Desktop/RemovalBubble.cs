using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace Tizi.Desktop;

internal sealed class RemovalBubble : Decorator
{
    internal static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<RemovalBubble, double>(nameof(Progress));
    internal double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _offset = new();
    private bool _open;
    static RemovalBubble() => AffectsMeasure<RemovalBubble>(ProgressProperty);
    internal RemovalBubble(Button button)
    {
        Child = button; IsVisible = false; ClipToBounds = false;
        var transforms = new TransformGroup(); transforms.Children.Add(_scale); transforms.Children.Add(_offset);
        button.RenderTransform = transforms; button.RenderTransformOrigin = new(0, .5, RelativeUnit.Relative);
        Transitions = new() { new DoubleTransition { Property = ProgressProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new SplineEasing(.16, 1, .3, 1) } };
        PropertyChanged += (_, e) => { if (e.Property == ProgressProperty) { InvalidateVisual(); if (!_open && Progress < .001) IsVisible = false; } };
    }
    internal void SetOpen(bool open)
    {
        if (_open == open) return; _open = open;
        if (open) IsVisible = true;
        Motion.Set(this, ProgressProperty, open ? 1d : 0d);
        IsHitTestVisible = open;
    }
    protected override Size MeasureOverride(Size available)
    {
        Child?.Measure(new(double.PositiveInfinity, available.Height));
        var size = Child?.DesiredSize ?? default; return new(size.Width * Math.Clamp(Progress, 0, 1), size.Height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        var p = Math.Clamp(Progress, 0, 1); var width = Child?.DesiredSize.Width ?? 0;
        Child?.Arrange(new Rect(0, 0, width, final.Height)); _scale.ScaleX = _scale.ScaleY = .45 + .55 * p; _offset.X = -28 * (1-p);
        if (Child is { } child) child.Opacity = p;
        return final;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); var p = Math.Clamp(Progress, 0, 1);
        if (p is > .05 and < .95) context.DrawEllipse(new SolidColorBrush(Color.Parse("#D74756")), null, new Point(-4, Bounds.Height/2), 17*(1-p), 11*Math.Sin(p*Math.PI));
    }
}
