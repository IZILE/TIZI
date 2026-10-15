using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Tizi.Desktop;

/// <summary>Three rotating arcs split into six pieces that straighten, travel, and dock into the approved ladder.</summary>
internal sealed class ConnectionLogo : Control
{
    internal static readonly StyledProperty<double> AssemblyProperty = AvaloniaProperty.Register<ConnectionLogo,double>(nameof(Assembly));
    internal static readonly StyledProperty<double> MorphProgressProperty = AvaloniaProperty.Register<ConnectionLogo,double>(nameof(MorphProgress),1);
    internal double Assembly { get=>GetValue(AssemblyProperty); set=>SetValue(AssemblyProperty,value); }
    internal double MorphProgress { get=>GetValue(MorphProgressProperty); set=>SetValue(MorphProgressProperty,value); }
    internal readonly record struct Curve(Point Start,Point Control1,Point Control2,Point End,double Opacity=1);
    private static readonly Curve[] Play = Split([
        Line(new(42.1620592580931,5.1),new(59.7620592580931,18)),
        Line(new(55.9712711404049,20.7784753817146),new(42.1620592580931,30.9)),
        Line(new(42.1620592580931,25.1),new(42.1620592580931,10.9))
    ]);
    // Docking coordinates are measured from the approved raster, which renders the settled state.
    private static readonly (Curve[] Curves,double Stroke) Docking = LoadDocking();
    private static readonly Curve[] Ladder = Docking.Curves;
    private static (Curve[],double) LoadDocking()
    {
        using var source=typeof(ConnectionLogo).Assembly.GetManifestResourceStream("Tizi.Desktop.ConnectionLadder.geometry.json")!;
        using var document=System.Text.Json.JsonDocument.Parse(source);
        Point P(System.Text.Json.JsonElement value)=>new(value[0].GetDouble(),value[1].GetDouble());
        return (document.RootElement.GetProperty("curves").EnumerateArray().Select(c=>Line(P(c[0]),P(c[1]))).ToArray(),
            document.RootElement.GetProperty("strokeWidth").GetDouble());
    }
    private static readonly double[] DockDelays = [.14,.02,0,.10,.20,.28];
    internal static readonly Point[] CrackPoints = [new(36,18.4),new(40,17.3),new(44,19.5),new(48,17.3),new(52,19.5),new(56,17.3),new(60,18.4)];
    private static readonly BrandRaster ApprovedLadder = new("ConnectionLadder",32);
    internal static Rect ArtworkBounds => ApprovedLadder.Bounds(new(48,18));
    private static readonly SplineEasing Ease = new(.22,.7,.2,1);
    private readonly DispatcherTimer _waiting = new() { Interval=TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _elapsed = new();
    private readonly Spring[] _coordinates = Flatten(Play).Select(v=>new Spring(v)).ToArray();
    private readonly Spring _speed = new(0);
    private double[] _initialX = [], _initialV = [];
    private Curve[] _pieces = Play;
    private double _angle=-Math.PI/2, _initialAngle, _evaluated, _previewStarted, _fromFracture, _formationEnd, _fromAsset, _fromStroke=3.6;
    private ConnectionPhase _phase;
    private bool _attached, _assemble;
    internal bool Waiting => _waiting.IsEnabled;
    internal double? PreviewTime { get; set; }
    internal Func<IBrush?>? SurfaceBrush { get; set; }
    internal double Fracture => _phase==ConnectionPhase.Failed
        ? _fromFracture+(1-_fromFracture)*Ease.Ease(Math.Clamp((MorphProgress-_formationEnd)/(1-_formationEnd),0,1))
        : _fromFracture*(1-Ease.Ease(Math.Clamp(MorphProgress,0,1)));
    internal double ArtworkOpacity => _phase is ConnectionPhase.Connected or ConnectionPhase.Failed
        ? _fromAsset+(1-_fromAsset)*Smooth((MorphProgress-.78)/.22)
        : _fromAsset*(1-Smooth(MorphProgress/.18));
    private double StrokeWidth => _fromStroke+((_phase is ConnectionPhase.Connected or ConnectionPhase.Failed ? Docking.Stroke : 3.6)-_fromStroke)*Smooth(MorphProgress/.6);
    internal double[] Velocities() { Evaluate(); return _coordinates.Select(s=>s.V).ToArray(); }
    private IEnumerable<Spring> Springs => _coordinates.Append(_speed);
    static ConnectionLogo() => AffectsRender<ConnectionLogo>(AssemblyProperty,MorphProgressProperty);
    internal ConnectionLogo()
    {
        RenderOptions.SetBitmapInterpolationMode(this,BitmapInterpolationMode.HighQuality);
        Width=96;Height=36;IsHitTestVisible=false;
        HorizontalAlignment=HorizontalAlignment.Center;VerticalAlignment=VerticalAlignment.Center;
        Transitions=new()
        {
            new DoubleTransition { Property=AssemblyProperty,Duration=TimeSpan.FromMilliseconds(540),Easing=Ease },
            new DoubleTransition { Property=MorphProgressProperty,Duration=TimeSpan.FromMilliseconds(540),Easing=new LinearEasing() }
        };
        Snapshot(); _waiting.Tick+=(_,_)=>InvalidateVisual();
    }
    internal void SetPhase(ConnectionPhase phase)
    {
        var fracture=Fracture;var asset=ArtworkOpacity;var stroke=StrokeWidth;Evaluate();Snapshot();_pieces=ReadCurves();
        _assemble=phase is ConnectionPhase.Connected or ConnectionPhase.Failed && Flatten(Ladder).Zip(_coordinates,(v,s)=>Math.Abs(v-s.X)).Max()>.15;
        _fromFracture=fracture;_fromAsset=asset;_fromStroke=stroke;_phase=phase;
        _formationEnd=phase==ConnectionPhase.Failed && fracture==0 && _assemble ? .68 : 0;
        var transitions=Transitions;var remaining=new Transitions();
        if(transitions is not null)foreach(var item in transitions.Where(t=>t is not TransitionBase known || known.Property!=MorphProgressProperty))remaining.Add(item);
        Transitions=remaining;MorphProgress=0;Transitions=transitions;
        Motion.Set(this,MorphProgressProperty,1d);
        Motion.Set(this,AssemblyProperty,phase switch { ConnectionPhase.Connected=>1d,ConnectionPhase.Connecting=>.78,ConnectionPhase.Disconnecting=>.12,ConnectionPhase.Failed=>.5,_=>0 });
        _previewStarted=PreviewTime??0;_elapsed.Restart();UpdateWaiting();
    }
    private void Snapshot()
    {
        _initialX=Springs.Select(s=>s.X).ToArray();_initialV=Springs.Select(s=>s.V).ToArray();
        _initialAngle=_angle;_evaluated=0;
    }
    private void Evaluate()
    {
        var seconds=Math.Clamp(MorphProgress,0,1)*.9;
        if(_phase==ConnectionPhase.Connecting)
            seconds=PreviewTime is { } preview ? Math.Max(seconds,preview-_previewStarted) : Math.Max(seconds,_elapsed.Elapsed.TotalSeconds);
        if(seconds<_evaluated)
        {
            var n=0;foreach(var s in Springs) { s.X=_initialX[n];s.V=_initialV[n++]; }
            _angle=_initialAngle;_evaluated=0;
        }
        while(_evaluated+1e-8<seconds)
        {
            var dt=Math.Min(1d/240,seconds-_evaluated);_evaluated+=dt;
            _speed.Step(_phase==ConnectionPhase.Connecting ? Math.PI*2/1.4 : 0,10,dt);_angle+=_speed.X*dt;
            var isLadder=_phase is ConnectionPhase.Connected or ConnectionPhase.Failed;
            var target=_phase==ConnectionPhase.Connecting ? Split(Spinner(_angle))
                : isLadder ? _assemble ? Enumerable.Range(0,6).Select(i=>AssemblyTarget(i,_evaluated*(_phase==ConnectionPhase.Failed ? 1.7 : 1))).ToArray() : Ladder : Play;
            var values=Flatten(target);for(var i=0;i<_coordinates.Length;i++)_coordinates[i].Step(values[i],isLadder ? 32 : 11,dt);
        }
        if(MorphProgress>=1 && _phase!=ConnectionPhase.Connecting)
        {
            var target=Flatten(_phase is ConnectionPhase.Connected or ConnectionPhase.Failed ? Ladder : Play);
            for(var i=0;i<_coordinates.Length;i++) { _coordinates[i].X=target[i];_coordinates[i].V=0; }
        }
    }
    private Curve AssemblyTarget(int i,double seconds)
    {
        var source=_pieces[i];var destination=Ladder[i];
        var center=At(source,.5);var vector=new Vector(source.End.X-source.Start.X,source.End.Y-source.Start.Y);var length=vector.Length;
        var direction=length>.01 ? vector/length : new Vector(1,0);
        var straight=Line(center-direction*(length/2),center+direction*(length/2));
        var unroll=Smooth(seconds/.10);
        if(seconds<=.10)return Blend(source,straight,unroll);
        var travel=Smooth((seconds-.10-DockDelays[i])/.20);
        var extend=Smooth((seconds-.24-DockDelays[i])/.13);
        var targetVector=new Vector(destination.End.X-destination.Start.X,destination.End.Y-destination.Start.Y);var targetLength=targetVector.Length;
        var originAngle=Math.Atan2(direction.Y,direction.X);var targetAngle=Math.Atan2(targetVector.Y,targetVector.X);
        var delta=Math.Atan2(Math.Sin(targetAngle-originAngle),Math.Cos(targetAngle-originAngle));
        var angle=originAngle+delta*travel;var heading=new Vector(Math.Cos(angle),Math.Sin(angle));
        // A small separation at the former arc seam makes the pieces legible before they travel.
        var seam=new Vector(-direction.Y,direction.X)*(i%2==0 ? -1.3 : 1.3)*(1-travel);
        center=Mix(center,At(destination,.5),travel)+seam*Smooth((seconds-.10)/.06);
        length+= (targetLength-length)*extend;
        return Line(center-heading*(length/2),center+heading*(length/2));
    }
    private void UpdateWaiting()
    {
        if(_phase==ConnectionPhase.Connecting && _attached) { if(!_elapsed.IsRunning)_elapsed.Start();_waiting.Start(); }
        else { _waiting.Stop();_elapsed.Stop(); }
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e);_attached=true;UpdateWaiting(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _attached=false;_waiting.Stop();_elapsed.Stop();base.OnDetachedFromVisualTree(e); }
    private Curve[] ReadCurves()=>Enumerable.Range(0,6).Select(i=>
    {
        Point P(int j)=>new(_coordinates[i*8+j*2].X,_coordinates[i*8+j*2+1].X);
        return new Curve(P(0),P(1),P(2),P(3));
    }).ToArray();
    internal Curve[] Curves() { Evaluate();return ReadCurves(); }
    internal (Point Start,Point End)[] Strokes()=>Curves().Where(c=>c.Opacity>.001).Select(c=>(c.Start,c.End)).ToArray();
    public override void Render(DrawingContext context)
    {
        base.Render(context);var curves=Curves();var asset=ArtworkOpacity;
        // Reveal the approved pixels across the docked structure without an opacity dip from
        // compositing two partially transparent white shapes over the blue action surface.
        var edge=ArtworkBounds.Left+ArtworkBounds.Width*asset;
        using(context.PushClip(asset<=0 ? new Rect(0,0,96,36) : new Rect(edge,0,96-edge,36)))
        foreach(var group in curves.Chunk(2))
        {
            var continuous=group.Length==2 && Math.Abs(group[0].End.X-group[1].Start.X)<.01 && Math.Abs(group[0].End.Y-group[1].Start.Y)<.01;
            foreach(var stroke in continuous ? new[] {group} : group.Select(curve=>new[] {curve}))
            {
                var geometry=new StreamGeometry();
                using(var path=geometry.Open())
                {
                    path.BeginFigure(stroke[0].Start,false);
                    foreach(var curve in stroke)path.CubicBezierTo(curve.Control1,curve.Control2,curve.End);
                    path.EndFigure(false);
                }
                context.DrawGeometry(null,new Pen(Brushes.White,StrokeWidth,lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round),geometry);
            }
        }
        if(asset>0)using(context.PushClip(new Rect(0,0,edge,36)))ApprovedLadder.Draw(context,new(48,18),this);
        if(Fracture>.001)
        {
            var cut=new StreamGeometry();var position=Math.Clamp(Fracture*1.6,0,1)*(CrackPoints.Length-1);var count=(int)position;
            using(var path=cut.Open())
            {
                path.BeginFigure(CrackPoints[0],false);foreach(var p in CrackPoints.Skip(1).Take(count))path.LineTo(p);
                if(count<CrackPoints.Length-1)path.LineTo(Mix(CrackPoints[count],CrackPoints[count+1],position-count));path.EndFigure(false);
            }
            context.DrawGeometry(null,new Pen(SurfaceBrush?.Invoke()??Brushes.Transparent,2.4*Fracture,lineCap:PenLineCap.Flat,lineJoin:PenLineJoin.Miter),cut);
        }
    }
    private static double Smooth(double t) { t=Math.Clamp(t,0,1);return t*t*(3-2*t); }
    private static Point Mix(Point a,Point b,double t)=>a+(b-a)*t;
    private static Curve Blend(Curve a,Curve b,double t)=>new(Mix(a.Start,b.Start,t),Mix(a.Control1,b.Control1,t),Mix(a.Control2,b.Control2,t),Mix(a.End,b.End,t));
    private static Curve Line(Point a,Point b,double opacity=1)=>new(a,Mix(a,b,1d/3),Mix(a,b,2d/3),b,opacity);
    private static Point At(Curve c,double t)
    {var u=1-t;return new(u*u*u*c.Start.X+3*u*u*t*c.Control1.X+3*u*t*t*c.Control2.X+t*t*t*c.End.X,u*u*u*c.Start.Y+3*u*u*t*c.Control1.Y+3*u*t*t*c.Control2.Y+t*t*t*c.End.Y);}
    private static Curve[] Split(Curve[] curves)=>curves.SelectMany(c=>
    {
        var a=Mix(c.Start,c.Control1,.5);var b=Mix(c.Control1,c.Control2,.5);var d=Mix(c.Control2,c.End,.5);
        var e=Mix(a,b,.5);var f=Mix(b,d,.5);var m=Mix(e,f,.5);
        return new[] { new Curve(c.Start,a,e,m),new Curve(m,f,d,c.End) };
    }).ToArray();
    private static double[] Flatten(Curve[] curves)=>curves.SelectMany(c=>new[]{c.Start.X,c.Start.Y,c.Control1.X,c.Control1.Y,c.Control2.X,c.Control2.Y,c.End.X,c.End.Y}).ToArray();
    private static Curve[] Spinner(double angle)=>Enumerable.Range(0,3).Select(i=>
    {
        var a=angle+i*Math.PI*2/3;var b=a+Math.PI/2;const double radius=11.7;var k=radius*4/3*Math.Tan((b-a)/4);
        var start=new Point(48+radius*Math.Cos(a),18+radius*Math.Sin(a));var end=new Point(48+radius*Math.Cos(b),18+radius*Math.Sin(b));
        return new Curve(start,start+new Vector(-Math.Sin(a),Math.Cos(a))*k,end-new Vector(-Math.Sin(b),Math.Cos(b))*k,end);
    }).ToArray();
    private sealed class Spring(double value)
    {
        internal double X=value,V;
        internal void Step(double target,double omega,double dt)
        {var offset=X-target;var impulse=V+omega*offset;var decay=Math.Exp(-omega*dt);X=target+(offset+impulse*dt)*decay;V=(V-omega*impulse*dt)*decay;}
    }
}
