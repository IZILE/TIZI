using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Tizi.Desktop;

/// <summary>Approved play/C1 artwork with critically damped motion that retains velocity on interruption.</summary>
internal sealed class ConnectionLogo : Control
{
    internal static readonly StyledProperty<double> AssemblyProperty = AvaloniaProperty.Register<ConnectionLogo,double>(nameof(Assembly));
    internal static readonly StyledProperty<double> MorphProgressProperty = AvaloniaProperty.Register<ConnectionLogo,double>(nameof(MorphProgress),1);
    internal double Assembly { get=>GetValue(AssemblyProperty); set=>SetValue(AssemblyProperty,value); }
    internal double MorphProgress { get=>GetValue(MorphProgressProperty); set=>SetValue(MorphProgressProperty,value); }
    internal readonly record struct Curve(Point Start,Point Control1,Point Control2,Point End,double Opacity=1);
    private static readonly Curve[] Play = [
        new Curve(new Point(42.1620592580931, 5.1), new Point(48.0287259247598, 9.4), new Point(53.8953925914264, 13.7), new Point(59.7620592580931, 18)),
        new Curve(new Point(55.9712711404049, 20.7784753817146), new Point(51.3682005129676, 24.1523169211431), new Point(46.7651298855304, 27.5261584605715), new Point(42.1620592580931, 30.9)),
        new Curve(new Point(42.1620592580931, 25.1), new Point(42.1620592580931, 20.3666666666667), new Point(42.1620592580931, 15.6333333333333), new Point(42.1620592580931, 10.9))
    ];
    private static readonly Curve[] Ladder = [
        new Curve(new Point(40, 11.9), new Point(45.3333333333333, 14.0666666666667), new Point(50.6666666666667, 16.2333333333333), new Point(56, 18.4)),
        new Curve(new Point(56, 33.2), new Point(56, 23.5333333333333), new Point(56, 13.8666666666667), new Point(56, 4.2)),
        new Curve(new Point(40, 33.2), new Point(40, 23.5333333333333), new Point(40, 13.8666666666667), new Point(40, 4.2))
    ];
    private static readonly double[] RungY = [8.5,21.8,29];
    internal static readonly Point[] CrackPoints = [new(36,18.4),new(40,17.3),new(44,19.5),new(48,17.3),new(52,19.5),new(56,17.3),new(60,18.4)];
    private static readonly Pen Stroke = new(Brushes.White,3.6,lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round);
    private static readonly SplineEasing Ease = new(.22,.7,.2,1);
    private readonly DispatcherTimer _waiting = new() { Interval=TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _elapsed = new();
    private readonly Spring[] _coordinates = Flatten(Play).Select(v=>new Spring(v)).ToArray();
    private readonly Spring[] _rungs = [new(0),new(0),new(0)];
    private readonly Spring _speed = new(0);
    private double[] _initialX = [], _initialV = [];
    private double _angle=-Math.PI/2, _initialAngle, _evaluated, _previewStarted, _fromFracture, _formationEnd;
    private ConnectionPhase _phase;
    private bool _attached;
    internal bool Waiting => _waiting.IsEnabled;
    internal double? PreviewTime { get; set; }
    internal Func<IBrush?>? SurfaceBrush { get; set; }
    internal double Fracture => _phase==ConnectionPhase.Failed
        ? _fromFracture+(1-_fromFracture)*Ease.Ease(Math.Clamp((MorphProgress-_formationEnd)/(1-_formationEnd),0,1))
        : _fromFracture*(1-Ease.Ease(Math.Clamp(MorphProgress,0,1)));
    internal double[] Velocities() { Evaluate(); return _coordinates.Select(s=>s.V).ToArray(); }
    private IEnumerable<Spring> Springs => _coordinates.Concat(_rungs).Append(_speed);
    static ConnectionLogo() => AffectsRender<ConnectionLogo>(AssemblyProperty,MorphProgressProperty);
    internal ConnectionLogo()
    {
        Width=96;Height=36;IsHitTestVisible=false;
        HorizontalAlignment=HorizontalAlignment.Center;VerticalAlignment=VerticalAlignment.Center;
        Transitions=new()
        {
            new DoubleTransition { Property=AssemblyProperty,Duration=TimeSpan.FromMilliseconds(900),Easing=Ease },
            new DoubleTransition { Property=MorphProgressProperty,Duration=TimeSpan.FromMilliseconds(900),Easing=new LinearEasing() }
        };
        Snapshot(); _waiting.Tick+=(_,_)=>InvalidateVisual();
    }
    internal void SetPhase(ConnectionPhase phase)
    {
        var fracture=Fracture; Evaluate(); Snapshot();
        _fromFracture=fracture; _phase=phase;
        _formationEnd=phase==ConnectionPhase.Failed && fracture==0
            && Flatten(Ladder).Zip(_coordinates,(v,s)=>Math.Abs(v-s.X)).Max()>1 ? .38 : 0;
        var transitions=Transitions;var remaining=new Transitions();
        if(transitions is not null)foreach(var item in transitions.Where(t=>t is not TransitionBase known || known.Property!=MorphProgressProperty))remaining.Add(item);
        Transitions=remaining; MorphProgress=0; Transitions=transitions;
        Motion.Set(this,MorphProgressProperty,1d);
        Motion.Set(this,AssemblyProperty,phase switch { ConnectionPhase.Connected=>1d,ConnectionPhase.Connecting=>.78,ConnectionPhase.Disconnecting=>.12,ConnectionPhase.Failed=>.5,_=>0 });
        _previewStarted=PreviewTime??0; _elapsed.Restart(); UpdateWaiting();
    }
    private void Snapshot()
    {
        _initialX=Springs.Select(s=>s.X).ToArray(); _initialV=Springs.Select(s=>s.V).ToArray();
        _initialAngle=_angle; _evaluated=0;
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
        // The native compositor and the deterministic capture clock share these substeps.
        while(_evaluated+1e-8<seconds)
        {
            var dt=Math.Min(1d/240,seconds-_evaluated);_evaluated+=dt;
            _speed.Step(_phase==ConnectionPhase.Connecting ? Math.PI*2/1.4 : 0,10,dt);
            _angle+=_speed.X*dt;
            var target=_phase==ConnectionPhase.Connecting ? Spinner(_angle) : _phase is ConnectionPhase.Connected or ConnectionPhase.Failed ? Ladder : Play;
            var values=Flatten(target);for(var i=0;i<_coordinates.Length;i++)_coordinates[i].Step(values[i],11,dt);
            for(var i=0;i<3;i++) _rungs[i].Step(_phase is ConnectionPhase.Connected or ConnectionPhase.Failed && _evaluated>.27+i*.06 ? 1 : 0,20,dt);
        }
        if(MorphProgress>=1 && _phase!=ConnectionPhase.Connecting)
        {
            var target=Flatten(_phase is ConnectionPhase.Connected or ConnectionPhase.Failed ? Ladder : Play);
            for(var i=0;i<_coordinates.Length;i++) { _coordinates[i].X=target[i];_coordinates[i].V=0; }
            foreach(var s in _rungs) { s.X=_phase is ConnectionPhase.Connected or ConnectionPhase.Failed ? 1 : 0;s.V=0; }
        }
    }
    private void UpdateWaiting()
    {
        if(_phase==ConnectionPhase.Connecting && _attached) { if(!_elapsed.IsRunning)_elapsed.Start();_waiting.Start(); }
        else { _waiting.Stop();_elapsed.Stop(); }
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e);_attached=true;UpdateWaiting(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _attached=false;_waiting.Stop();_elapsed.Stop();base.OnDetachedFromVisualTree(e); }
    internal Curve[] Curves()
    {
        Evaluate();var curves=new Curve[6];
        for(var i=0;i<3;i++)
        {
            Point P(int j)=>new(_coordinates[i*8+j*2].X,_coordinates[i*8+j*2+1].X);
            curves[i]=new(P(0),P(1),P(2),P(3));
        }
        for(var i=0;i<3;i++)
        {
            var parameter=(33.2-RungY[i])/29;
            var left=At(curves[2],parameter);var right=At(curves[1],parameter);var grow=Math.Clamp(_rungs[i].X,0,1);
            curves[i+3]=Line(left,Mix(left,right,grow),grow>.008 ? 1 : 0);
        }
        return curves;
    }
    internal (Point Start,Point End)[] Strokes()=>Curves().Where(c=>c.Opacity>.001).Select(c=>(c.Start,c.End)).ToArray();
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        foreach(var curve in Curves().Where(c=>c.Opacity>.001))
        {
            var geometry=new StreamGeometry();
            using(var path=geometry.Open()) { path.BeginFigure(curve.Start,false);path.CubicBezierTo(curve.Control1,curve.Control2,curve.End);path.EndFigure(false); }
            context.DrawGeometry(null,Stroke,geometry);
        }
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
    private static Point Mix(Point a,Point b,double t)=>a+(b-a)*t;
    private static Curve Line(Point a,Point b,double opacity=1)=>new(a,Mix(a,b,1d/3),Mix(a,b,2d/3),b,opacity);
    private static Point At(Curve c,double t)
    {var u=1-t;return new(u*u*u*c.Start.X+3*u*u*t*c.Control1.X+3*u*t*t*c.Control2.X+t*t*t*c.End.X,u*u*u*c.Start.Y+3*u*u*t*c.Control1.Y+3*u*t*t*c.Control2.Y+t*t*t*c.End.Y);}
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
