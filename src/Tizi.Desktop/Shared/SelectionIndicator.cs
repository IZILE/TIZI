using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Tizi.Desktop;

/// <summary>One persistent selection surface; interrupted transitions continue at the displayed position.</summary>
internal sealed class SelectionIndicator : Border
{
    internal const int DurationMilliseconds = 160;
    internal TranslateTransform Position { get; } = new();
    internal Rect Destination { get; private set; }
    private bool _placed;

    internal SelectionIndicator()
    {
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        RenderTransform = Position;
        Position.Transitions = new Transitions { Transition(TranslateTransform.XProperty), Transition(TranslateTransform.YProperty) };
        Transitions = new Transitions { Transition(WidthProperty), Transition(HeightProperty) };
        DetachedFromVisualTree += (_, _) => _placed = false;
    }

    internal void MoveTo(Rect bounds, bool animate = true)
    {
        if (_placed && animate && Destination == bounds) return; // Layout must not restart an in-flight transition.
        var motion = Position.Transitions;
        var sizeMotion = Transitions;
        if (!_placed || !animate) { Position.Transitions = null; Transitions = null; }
        Motion.Set(Position, TranslateTransform.XProperty, bounds.X);
        Motion.Set(Position, TranslateTransform.YProperty, bounds.Y);
        Motion.Set(this, WidthProperty, bounds.Width); Motion.Set(this, HeightProperty, bounds.Height);
        Position.Transitions = motion; Transitions = sizeMotion;
        Destination = bounds; _placed = true;
    }

    private static DoubleTransition Transition(AvaloniaProperty property) => new()
    {
        Property = property, Duration = TimeSpan.FromMilliseconds(DurationMilliseconds),
        Easing = new SplineEasing(0, 0, 0.58, 1),
    };
}
