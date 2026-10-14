using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Tizi.Desktop;

/// <summary>The existing sidebar outlines acquire a solid body when selected.</summary>
internal sealed class NavigationGlyph : Canvas
{
    public static readonly StyledProperty<double> SelectionFillProperty =
        AvaloniaProperty.Register<NavigationGlyph, double>(nameof(SelectionFill));
    public double SelectionFill { get => GetValue(SelectionFillProperty); set => SetValue(SelectionFillProperty, value); }
    internal string Page { get; }
    internal Geometry? FilledBody { get; }
    internal IBrush OutlineBrush { get; }
    internal const double StrokeWidth = 1.5;
    internal const int DurationMilliseconds = 240;

    static NavigationGlyph()
    {
        SelectionFillProperty.Changed.AddClassHandler<NavigationGlyph>((glyph, _) =>
        {
            if (glyph.Children.Count > 0) glyph.Children[0].InvalidateVisual();
        });
    }

    internal NavigationGlyph(string page, IBrush? outlineBrush = null)
    {
        Page = page;
        OutlineBrush = outlineBrush ?? Skin.LabelBrush;
        Width = Height = 24;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        var (outline, details, body) = page switch
        {
            "Notice" => ("M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 Z", "M12,10 L12,17 M12,6.5 L12,6.6", ""),
            "Warning" => ("M10.3,3.8 Q12,1 13.7,3.8 L22,18.4 Q23.5,21 20.5,21 L3.5,21 Q0.5,21 2,18.4 Z", "M12,9 L12,14 M12,17.5 L12,17.6", ""),
            "Error" => ("M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 Z", "M8.5,8.5 L15.5,15.5 M15.5,8.5 L8.5,15.5", ""),
            "Nodes" => (
                "M4,2 L20,2 L20,10 L4,10 Z M4,14 L20,14 L20,22 L4,22 Z M12,10 L12,14",
                "M7,6 L9,6 M7,18 L9,18",
                "F0 M4,2 L20,2 L20,10 L4,10 Z M4,14 L20,14 L20,22 L4,22 Z M6.25,5.25 L9.75,5.25 L9.75,6.75 L6.25,6.75 Z M6.25,17.25 L9.75,17.25 L9.75,18.75 L6.25,18.75 Z"),
            "Subscriptions" => (
                "M20,7 A9,9 0 1 0 21,15 M20,2 L20,7 L15,7 M9,12 L15,12 M12,9 L12,15", "", ""),
            "Providers" => (
                "M2,7 A10,10 0 0 1 7,2 M22,17 A10,10 0 0 1 17,22 M12,2 C10.8,2 10.8,4 10.8,6 L10.2,9.4 L2,14 L2,16 L10.2,13.5 L10.2,18 L7.7,20 L7.7,22 L12,20.5 L16.3,22 L16.3,20 L13.8,18 L13.8,13.5 L22,16 L22,14 L13.2,9.4 L13.2,6 C13.2,4 13.2,2 12,2 Z", "",
                "M12,2 C10.8,2 10.8,4 10.8,6 L10.2,9.4 L2,14 L2,16 L10.2,13.5 L10.2,18 L7.7,20 L7.7,22 L12,20.5 L16.3,22 L16.3,20 L13.8,18 L13.8,13.5 L22,16 L22,14 L13.2,9.4 L13.2,6 C13.2,4 13.2,2 12,2 Z"),
            "Rules" => (
                "M3,3 L10,3 L10,10 L3,10 Z M14,14 L21,14 L21,21 L14,21 Z M6.5,10 L6.5,17.5 L14,17.5 M10,6.5 L17.5,6.5 L17.5,14", "",
                "M3,3 L10,3 L10,10 L3,10 Z M14,14 L21,14 L21,21 L14,21 Z"),
            // A waveform has no enclosed body: retain its precise path and brighten it with selection.
            "Connections" => ("M2,12 L6,12 L9,3 L14,21 L17,12 L22,12", "", ""),
            "Overview" => (
                "M2,2 L10,2 L10,10 L2,10 Z M14,2 L22,2 L22,10 L14,10 Z M2,14 L10,14 L10,22 L2,22 Z M14,19 L17,15 L20,21 L22,17", "",
                "M2,2 L10,2 L10,10 L2,10 Z M14,2 L22,2 L22,10 L14,10 Z M2,14 L10,14 L10,22 L2,22 Z"),
            "Logs" => (
                "M4,2 L20,2 L20,22 L4,22 Z", "M8,7 L16,7 M8,12 L16,12 M8,17 L13,17",
                "F0 M4,2 L20,2 L20,22 L4,22 Z M7.25,6.25 L16.75,6.25 L16.75,7.75 L7.25,7.75 Z M7.25,11.25 L16.75,11.25 L16.75,12.75 L7.25,12.75 Z M7.25,16.25 L13.75,16.25 L13.75,17.75 L7.25,17.75 Z"),
            "Settings" => (
                "M9.5,2 L14.5,2 L15,5 L17,6 L20,5 L22,9 L19.5,11 L19.5,13 L22,15 L20,19 L17,18 L15,19 L14.5,22 L9.5,22 L9,19 L7,18 L4,19 L2,15 L4.5,13 L4.5,11 L2,9 L4,5 L7,6 L9,5 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12", "",
                "F0 M9.5,2 L14.5,2 L15,5 L17,6 L20,5 L22,9 L19.5,11 L19.5,13 L22,15 L20,19 L17,18 L15,19 L14.5,22 L9.5,22 L9,19 L7,18 L4,19 L2,15 L4.5,13 L4.5,11 L2,9 L4,5 L7,6 L9,5 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12 Z"),
            _ => ("M11,7 A4,4 0 1 1 10.99,7 M11,1 L11,4 M11,18 L11,21 M1,11 L4,11 M18,11 L21,11 M4,4 L6,6 M16,16 L18,18 M4,18 L6,16 M16,6 L18,4", "", ""),
        };
        FilledBody = body.Length == 0 ? null : Geometry.Parse(body);
        Children.Add(new GlyphSurface(this, Geometry.Parse(outline), details.Length == 0 ? null : Geometry.Parse(details), OutlineBrush) { Width = 24, Height = 24 });
        Transitions = new Transitions
        {
            new DoubleTransition { Property = SelectionFillProperty, Duration = TimeSpan.FromMilliseconds(DurationMilliseconds), Easing = new SplineEasing(.16, 1, .3, 1) },
        };
    }

    internal void Select(bool selected, bool animate = true)
    {
        var transitions = Transitions;
        if (!animate) Transitions = null;
        Motion.Set(this, SelectionFillProperty, selected ? 1d : 0d);
        if (!animate) Transitions = transitions;
    }

    private sealed class GlyphSurface(NavigationGlyph owner, Geometry outline, Geometry? details, IBrush brush) : Control
    {
        private readonly Pen OutlinePen = new(brush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        private static readonly Pen SelectedPen = new(Brushes.White, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        public override void Render(DrawingContext context)
        {
            var progress = Math.Clamp(owner.SelectionFill, 0, 1);
            if (progress > 0)
                using (context.PushOpacity(progress))
                    if (owner.FilledBody is { } body) context.DrawGeometry(Brushes.White, null, body);
            context.DrawGeometry(null, OutlinePen, outline);
            if (progress > 0)
                using (context.PushOpacity(progress)) context.DrawGeometry(null, SelectedPen, outline);
            if (details is not null && progress < 1)
                using (context.PushOpacity(1 - progress)) context.DrawGeometry(null, OutlinePen, details);
        }
    }
}
