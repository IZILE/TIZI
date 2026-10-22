using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Tizi.Desktop;

/// <summary>A monotone damped follower of startup readiness; positions and velocities stay continuous.</summary>
internal sealed class StartupTiming
{
    internal double Position {get;private set;}
    internal double Velocity {get;private set;}
    internal double Elapsed {get;private set;}
    internal double? ReadyAt {get;private set;}
    internal bool Finished=>Position>=1;
    internal void Advance(double seconds,bool ready)
    {
        if(Finished)return;if(ready&&ReadyAt is null)ReadyAt=Elapsed;
        for(var remaining=Math.Max(0,seconds);remaining>0;)
        {
            var dt=Math.Min(1/240d,remaining);remaining-=dt;Elapsed+=dt;
            // Never keep a decorative overlay in front of a stalled/error recovery screen.
            if(ReadyAt is null&&Elapsed>=3.5)ReadyAt=3.5;
            var release=ReadyAt??double.PositiveInfinity;
            var readiness=Smooth((Elapsed-Math.Max(.5,release))/.4);
            var before=.10+.46*(1-Math.Exp(-Elapsed/.8));var target=before+(1-before)*readiness;
            var omega=8+4*readiness;var acceleration=omega*omega*(target-Position)-2*omega*Velocity;
            Velocity=Math.Max(0,Velocity+acceleration*dt);Position=Math.Clamp(Position+Velocity*dt,0,1);
            if(readiness==1&&1-Position<.0001&&Velocity<.002){Position=1;Velocity=0;break;}
        }
    }
    internal static double Smooth(double value){var x=Math.Clamp(value,0,1);return x*x*x*(x*(x*6-15)+10);}
}

/// <summary>Eleven live strokes merge into the existing brand and dock at its actual toolbar position.</summary>
internal sealed class StartupAnimation : Control
{
    internal static bool AllowVerification {get;set;}
    private static readonly (Point[][] Lines,double Stroke) Mark=BrandGeometry.Lines("BrandMark",28);
    internal static readonly Point[][] Letters=new Point[][]
    {
        [new(-90,-22),new(-58,-22)],[new(-74,-22),new(-74,22)],
        [new(-42,-22),new(-16,-22)],[new(-29,-22),new(-29,22)],[new(-42,22),new(-16,22)],
        [new(0,-22),new(32,-22)],[new(32,-22),new(0,22)],[new(0,22),new(32,22)],
        [new(50,-22),new(76,-22)],[new(63,-22),new(63,22)],[new(50,22),new(76,22)]
    }.Select(line=>line.Select(p=>new Point(p.X*.68,p.Y*.68)).ToArray()).ToArray();
    internal static readonly int[] TopRails=[0,2,5,8],BottomRails=[4,7,10];
    private static Point[] Rail(Point[] rail,int index,int count,int letter)
    {
        var length=Letters[letter][1].X-Letters[letter][0].X;
        var center=rail[0].X+length/2+(rail[1].X-rail[0].X-length)*index/(count-1);
        return[new(center-length/2,rail[0].Y),new(center+length/2,rail[0].Y)];
    }
    private static readonly Point[][] Targets=[Rail(Mark.Lines[2],0,4,0),Mark.Lines[5],Rail(Mark.Lines[2],1,4,2),Mark.Lines[4],Rail(Mark.Lines[1],0,3,4),
        Rail(Mark.Lines[2],2,4,5),Mark.Lines[0],Rail(Mark.Lines[1],1,3,7),Rail(Mark.Lines[2],3,4,8),Mark.Lines[3],Rail(Mark.Lines[1],2,3,10)];
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private readonly Stopwatch _watch=new();
    private double _previous;
    private readonly Func<bool> _ready;
    private readonly Func<Point> _destination;
    private readonly Action<double> _applyContentOpacity;
    internal StartupTiming Timing {get;}=new();
    internal double? PreviewProgress {get;set;}
    internal double Progress=>PreviewProgress??Timing.Position;
    internal double Frost=>1-StartupTiming.Smooth((Progress-.50)/.44);
    internal double ContentOpacity=>RevealOpacity(Progress);
    internal static double RevealOpacity(double progress)=>StartupTiming.Smooth((progress-.80)/.18);
    internal double StrokeWidth=>Mark.Stroke;
    internal bool Running=>_timer.IsEnabled;
    internal event Action? Completed;
    internal StartupAnimation(Func<bool> ready,Func<Point> destination,Action<double> applyContentOpacity)
    {
        _ready=ready;_destination=destination;_applyContentOpacity=applyContentOpacity;UseLayoutRounding=false;Focusable=false;
        Avalonia.Automation.AutomationProperties.SetName(this,"TIZI");
        _timer.Tick+=(_,_)=>Tick();DetachedFromVisualTree+=(_,_)=>_timer.Stop();
        PointerPressed+=(_,e)=>e.Handled=true;
    }
    internal void Start()
    {
        if(!AnimationsEnabled()){Finish();return;}
        _previous=0;_watch.Restart();_timer.Start();Tick();
    }
    private void Tick()
    {
        var now=_watch.Elapsed.TotalSeconds;Timing.Advance(Math.Max(0,now-_previous),_ready());_previous=now;
        _applyContentOpacity(ContentOpacity);InvalidateVisual();if(Timing.Finished)Finish();
    }
    internal void Finish(){_timer.Stop();_watch.Stop();_applyContentOpacity(1);Completed?.Invoke();}
    internal Point[][] Strokes(double progress)
    {
        var morph=StartupTiming.Smooth((progress-.12)/.34);
        return Letters.Select((line,i)=>line.Select((p,j)=>new Point(p.X+(Targets[i][j].X-p.X)*morph,p.Y+(Targets[i][j].Y-p.Y)*morph)).ToArray()).ToArray();
    }
    internal (Point Center,double Scale) Placement(double progress)
    {
        var travel=StartupTiming.Smooth((progress-.56)/.38);var target=_destination();
        return(new(Bounds.Width/2+(target.X-Bounds.Width/2)*travel,Bounds.Height/2+(target.Y-Bounds.Height/2)*travel-Math.Sin(Math.PI*travel)*12),3.1+(1-3.1)*travel);
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);var p=Progress;var frost=Frost;
        using(context.PushOpacity(.84*frost))context.FillRectangle(Skin.WindowBackdrop,new Rect(Bounds.Size));
        var place=Placement(p);using(context.PushTransform(Matrix.CreateScale(place.Scale,place.Scale)*Matrix.CreateTranslation(place.Center.X,place.Center.Y)))
        using(context.PushOpacity(Skin.Label.A/255d))
            context.DrawGeometry(null,new Pen(Skin.BrandInk,Mark.Stroke,lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round),BrandGeometry.Path(Strokes(p)));
    }
    internal static bool AnimationsEnabled()=>!OperatingSystem.IsWindows()||!SystemParametersInfo(0x1042,0,out var enabled,0)||enabled;
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SystemParametersInfo(uint action,uint parameter,[MarshalAs(UnmanagedType.Bool)]out bool value,uint flags);
}
