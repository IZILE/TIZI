using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Tizi.Desktop;

internal static class DialogMotion
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window,Task> Closing = new();
    internal static void Prepare(Window window)
    {
        if (window.Content is not Control content || content is DialogSurface) return;
        window.Content = null; window.Content = new DialogSurface { Child = content };
    }
    internal static void Open(Window window)
    {
        if (window is MainWindow || TrayMenuPlacement.IsTrayMenu(window)) return;
        Prepare(window); if (window.Content is not DialogSurface surface) return;
        Dispatcher.UIThread.Post(() => { if (window.IsVisible) surface.Reveal(); }, DispatcherPriority.Loaded);
    }
    internal static Task CloseAsync(Window window, object? result = null) => Closing.GetValue(window, _ => FinishAsync(window,result));
    private static async Task FinishAsync(Window window, object? result)
    {
        if(window.Content is DialogSurface surface)
        {
            surface.IsHitTestVisible=false; surface.Dismiss();
            await Task.Delay(180);
        }
        if(window.IsVisible) window.Close(result);
    }
}
internal sealed class DialogSurface : Decorator
{
    internal ScaleTransform Scale { get; } = new(0.985, 0.985);
    internal TranslateTransform Offset { get; } = new(0, 6);
    internal DialogSurface()
    {
        Opacity = 0; RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        RenderTransform = new TransformGroup { Children = { Scale, Offset } };
        static DoubleTransition Transition(AvaloniaProperty property) => new() { Property = property, Duration = TimeSpan.FromMilliseconds(160), Easing = new SplineEasing(0, 0, 0.58, 1) };
        Transitions = new() { Transition(OpacityProperty) };
        Scale.Transitions = new() { Transition(ScaleTransform.ScaleXProperty), Transition(ScaleTransform.ScaleYProperty) };
        Offset.Transitions = new() { Transition(TranslateTransform.YProperty) };
    }
    internal void Reveal() { Motion.Set(this, OpacityProperty, 1d); Motion.Set(Scale, ScaleTransform.ScaleXProperty, 1d); Motion.Set(Scale, ScaleTransform.ScaleYProperty, 1d); Motion.Set(Offset, TranslateTransform.YProperty, 0d); }
    internal void Dismiss()
    {
        Motion.Set(this,OpacityProperty,0d); Motion.Set(Scale,ScaleTransform.ScaleXProperty,.985d);
        Motion.Set(Scale,ScaleTransform.ScaleYProperty,.985d); Motion.Set(Offset,TranslateTransform.YProperty,6d);
    }
}
