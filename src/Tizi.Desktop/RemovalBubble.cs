using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace Tizi.Desktop;

/// <summary>One deformable surface grows a second lobe, holds a neck, then releases it.</summary>
internal sealed class RemovalBubble : Control
{
    internal static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<RemovalBubble, double>(nameof(Progress));
    internal double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    internal Button? Anchor { get; private set; }
    internal Button Remove { get; }
    private readonly DoubleTransition _transition;
    private bool _open;
    private double _anchorWidth, _removeWidth;
    internal const double BreakPoint = .8;
    internal readonly record struct Surface(Rect Bud, double Bulge, double Neck, bool Joined);
    static RemovalBubble() { AffectsMeasure<RemovalBubble>(ProgressProperty); AffectsArrange<RemovalBubble>(ProgressProperty); AffectsRender<RemovalBubble>(ProgressProperty); }
    internal RemovalBubble(Button button)
    {
        Remove = button; VisualChildren.Add(button); LogicalChildren.Add(button); ClipToBounds = false;
        button.Classes.Add("fission-part"); button.IsVisible = false;
        _transition = new DoubleTransition { Property = ProgressProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new LinearEasing() };
        Transitions = new() { _transition };
        PropertyChanged += (_, e) =>
        {
            if (e.Property != ProgressProperty) return;
            button.IsVisible = Progress > .001;
            button.IsHitTestVisible = _open && Progress >= .9;
        };
    }
    internal void AttachAnchor(Button anchor)
    {
        Anchor = anchor; anchor.Classes.Add("fission-part"); VisualChildren.Insert(0, anchor); LogicalChildren.Insert(0, anchor);
    }
    internal void SetOpen(bool open)
    {
        if (_open == open) return; _open = open;
        if (open) Remove.IsVisible = true;
        else Remove.IsHitTestVisible = false;
        _transition.Duration = TimeSpan.FromMilliseconds(open ? 320 : 180);
        Motion.Set(this, ProgressProperty, open ? 1d : 0d);
    }
    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    internal Surface Shape()
    {
        var p = Math.Clamp(Progress, 0, 1); var h = Math.Max(36, Bounds.Height);
        var birth = Smooth(p / .78); var pull = Smooth(p / BreakPoint);
        var release = p >= BreakPoint ? Math.Sin(Math.Clamp((p - BreakPoint) / (1 - BreakPoint), 0, 1) * Math.PI) : 0;
        var budWidth = _removeWidth * birth;
        var budHeight = h * Math.Pow(birth,.75);
        var center = _anchorWidth - 2 + (10 + _removeWidth / 2) * pull + 2.5 * release;
        var left = center - budWidth / 2;
        var bulge = p < BreakPoint ? 10 * Math.Sin(Math.PI * p / BreakPoint)*Smooth(p/.25) : -1.5 * release;
        var neck = p is > 0 and < BreakPoint ? (h * .34 * (1 - Smooth((p - .4) / .4)) + 1.1) * Smooth(p / .15) : 0;
        return new(new Rect(left, (h - budHeight) / 2, Math.Max(0, budWidth), budHeight), bulge, neck, p is > .001 and < BreakPoint);
    }
    protected override Size MeasureOverride(Size available)
    {
        // This panel owns both placements. A Canvas could independently rearrange a
        // changed count label back to (0,0) without rerunning the outer arrangement.
        Anchor?.Measure(new(double.PositiveInfinity, available.Height));
        Remove.Measure(new(double.PositiveInfinity, available.Height));
        _anchorWidth = Anchor?.DesiredSize.Width ?? 0;
        if (Remove.IsVisible) _removeWidth = Remove.DesiredSize.Width;
        var height = Math.Max(Anchor?.DesiredSize.Height ?? 36, Remove.DesiredSize.Height);
        return new(_anchorWidth + (8 + _removeWidth) * Smooth(Progress / .82), height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        Anchor?.Arrange(new Rect(0, 0, _anchorWidth, final.Height));
        var surface = Shape(); Remove.Arrange(new Rect(surface.Bud.X, 0, _removeWidth, final.Height));
        Remove.Opacity = Smooth((Progress - .64) / .28);
        var radius = Math.Min(surface.Bud.Width, surface.Bud.Height) / 2;
        Remove.Clip = new RectangleGeometry(new Rect(0, surface.Bud.Y, surface.Bud.Width, surface.Bud.Height), radius, radius);
        return final;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var h = Bounds.Height; if (h <= 0 || _anchorWidth <= 0) return;
        var s = Shape(); var p=Math.Clamp(Progress,0,1);
        var source = (Anchor?.Background as ISolidColorBrush)?.Color ?? Color.Parse("#3A3A3A");
        var red = (Remove.Background as ISolidColorBrush)?.Color ?? Color.Parse("#D74756");
        var tint=Smooth((p-.22)/.5);
        Color Mix(Color a,Color b,double t)=>Color.FromRgb((byte)Math.Round(a.R+(b.R-a.R)*t),(byte)Math.Round(a.G+(b.G-a.G)*t),(byte)Math.Round(a.B+(b.B-a.B)*t));
        var budColor=Mix(source,red,tint);
        Geometry shape=SourceOutline(_anchorWidth,h,s.Bulge);
        Geometry? bud=null;
        if(s.Bud.Width>.01&&s.Bud.Height>.01)
        {
            var radius=Math.Min(s.Bud.Width,s.Bud.Height)/2;
            bud=new RectangleGeometry(s.Bud,radius,radius);
            if(s.Joined)shape=new CombinedGeometry(GeometryCombineMode.Union,shape,bud);
        }
        if (s.Joined && s.Neck > 0)
        {
            var x1 = _anchorWidth - 8; var x2 = s.Bud.X + Math.Min(s.Bud.Width / 3, 10); var middle = (x1 + x2) / 2;
            var cy = h / 2; var attach = Math.Min(h * .34, s.Bud.Height * .34);
            var bridge = new StreamGeometry();
            using (var path = bridge.Open())
            {
                path.BeginFigure(new(x1, cy - attach), true);
                path.CubicBezierTo(new(x1 + 8, cy - attach), new(middle - 3, cy - s.Neck / 2), new(middle, cy - s.Neck / 2));
                path.CubicBezierTo(new(middle + 3, cy - s.Neck / 2), new(x2 - 8, cy - attach), new(x2, cy - attach));
                path.LineTo(new(x2, cy + attach));
                path.CubicBezierTo(new(x2 - 8, cy + attach), new(middle + 3, cy + s.Neck / 2), new(middle, cy + s.Neck / 2));
                path.CubicBezierTo(new(middle - 3, cy + s.Neck / 2), new(x1 + 8, cy + attach), new(x1, cy + attach));
                path.EndFigure(true);
            }
            shape=new CombinedGeometry(GeometryCombineMode.Union,shape,bridge);
        }
        if(s.Joined)
        {
            var start=_anchorWidth-6+12*Smooth((p-.42)/.38);
            var paint=new LinearGradientBrush { StartPoint=new(start,h/2,RelativeUnit.Absolute),EndPoint=new(start+22,h/2,RelativeUnit.Absolute),
                GradientStops=new() { new(source,0),new(budColor,1) } };
            using(context.PushOpacity(Anchor?.Opacity??1))context.DrawGeometry(paint,null,shape);
        }
        else
        {
            using(context.PushOpacity(Anchor?.Opacity??1))context.DrawGeometry(new SolidColorBrush(source),null,shape);
            if(bud is not null)
            {
                var left=Mix(source,red,Smooth((p-BreakPoint)/(1-BreakPoint)));
                var paint=new LinearGradientBrush { StartPoint=new(s.Bud.Left,h/2,RelativeUnit.Absolute),EndPoint=new(s.Bud.Left+18,h/2,RelativeUnit.Absolute),
                    GradientStops=new() { new(left,0),new(budColor,1) } };
                context.DrawGeometry(paint,null,bud);
            }
        }
    }
    private static StreamGeometry SourceOutline(double width, double height, double bulge)
    {
        var r = height / 2; var end = width - r; const double k = .55228475;
        var shape = new StreamGeometry();
        using (var p = shape.Open())
        {
            p.BeginFigure(new(r, 0), true); p.LineTo(new(end, 0));
            p.CubicBezierTo(new(end + r * k, 0), new(width + bulge, r * (1 - k)), new(width + bulge, r));
            p.CubicBezierTo(new(width + bulge, r * (1 + k)), new(end + r * k, height), new(end, height));
            p.LineTo(new(r, height)); p.CubicBezierTo(new(r * (1 - k), height), new(0, r * (1 + k)), new(0, r));
            p.CubicBezierTo(new(0, r * (1 - k)), new(r * (1 - k), 0), new(r, 0)); p.EndFigure(true);
        }
        return shape;
    }
}
