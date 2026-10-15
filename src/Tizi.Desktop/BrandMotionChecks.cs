using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

internal static class BrandMotionChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);File.Delete(Path.Combine(output,"failure.txt"));File.Delete(Path.Combine(output,"checks.json"));
        var checks=new List<string>();
        void Check(bool value,string label) { if(!value)throw new InvalidOperationException(label);checks.Add(label); }
        try
        {
            Capture(output,Check);
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new { count=checks.Count,checks,scope="Production controls rendered in an isolated headless window; no personal profile, network operation, or native desktop timing test.",binary=VerificationStamp.Current() },new JsonSerializerOptions { WriteIndented=true }));
            return 0;
        }
        catch(Exception e) { File.WriteAllText(Path.Combine(output,"failure.txt"),string.Join("\n",checks)+"\n"+e);return 1; }
    }
    private static void Capture(string output,Action<bool,string> check)
    {
        static void Layout(Window top) { top.UpdateLayout();Dispatcher.UIThread.RunJobs(); }
        static double Distance(Point a,Point b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
        static object Box(Rect r)=>new { r.X,r.Y,r.Width,r.Height };
        static void Save(Window top,string path) { Layout(top);AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=top.CaptureRenderedFrame()??throw new IOException("Missing motion capture.");image.Save(path,new PngBitmapEncoderOptions()); }
        static void Settle(Window top)
        {
            Layout(top);if(top.Content is DialogSurface surface) { using var clock=new UiAnimationClock(surface,surface.Scale,surface.Offset);surface.Reveal();clock.Pulse(0);clock.Pulse(200); }Layout(top);
        }
        static void Click(Window top,Control target)
        {
            var point=target.TranslatePoint(new(target.Bounds.Width/2,target.Bounds.Height/2),top)!.Value;
            top.MouseMove(point);top.MouseDown(point,MouseButton.Left);top.MouseUp(point,MouseButton.Left);Layout(top);
        }
        UiText.Language="zh-CN";
        var anchorCalls=0;var removeCalls=0;
        var anchor=Ui.Button("More",()=>anchorCalls++);var remove=Ui.Button("Remove",()=>removeCalls++);
        remove.Background=new SolidColorBrush(Color.Parse("#D74756"));remove.BorderThickness=default;
        var bubble=new RemovalBubble(remove);bubble.AttachAnchor(anchor);
        var bar=new WrapPanel { Orientation=Orientation.Horizontal,ItemSpacing=8,LineSpacing=8,Margin=new(24),VerticalAlignment=VerticalAlignment.Center,
            Children={Ui.Button("Import nodes",()=>{}),Ui.Button("Test latency",()=>{}),bubble} };
        var top=new Window { Width=640,Height=140,Background=Skin.PasteboardBrush,Content=bar };top.Show();Settle(top);
        var fission=Path.Combine(output,"fission-frames");Directory.CreateDirectory(fission);
        var samples=new List<object>();
        try
        {
            var original=anchor.Bounds;check(!remove.IsEffectivelyVisible && anchor.IsEffectivelyVisible,"Normal selection keeps only the original toolbar button visible");
            using(var clock=new UiAnimationClock(bubble))
            {
                clock.Pulse(0);bubble.SetOpen(true);clock.Pulse(1);
                for(var frame=0;frame<=90;frame++)
                {
                    clock.Pulse(1+frame*8);Layout(top);var s=bubble.Shape();
                    samples.Add(new { milliseconds=frame*8,bubble.Progress,bud=Box(s.Bud),s.Bulge,s.Neck,s.Joined,anchor=Box(anchor.Bounds),remove=Box(remove.Bounds),remove.Opacity,remove.IsHitTestVisible });
                    Save(top,Path.Combine(fission,frame.ToString("D3")+".png"));
                    if(frame==12)check(s.Bulge>7 && s.Joined && s.Neck>5 && anchor.Bounds==original,"The original button's surface stretches while its text box and hit target stay fixed");
                    if(frame==31)check(s.Joined && s.Neck is > 1 and < 2 && !remove.IsHitTestVisible,"A full-sized new lobe remains attached by a thin neck before release and cannot be activated early");
                    if(frame==33)check(!s.Joined && s.Neck==0 && s.Bud.X>anchor.Bounds.Right+8,"The next separation frame drops the neck and recoils into a real gap");
                }
                check(bubble.Progress==1 && remove.Opacity==1 && remove.IsHitTestVisible && Math.Abs(remove.Bounds.X-anchor.Bounds.Right-8)<.01,"The two settled buttons have normal-size text, an eight-pixel gap and independent hit targets");
                Click(top,anchor);Click(top,remove);check(anchorCalls==1 && removeCalls==1,"The original and new button still dispatch their own actions exactly once");
                bubble.SetOpen(false);clock.Pulse(722);clock.Pulse(850);Layout(top);var progress=bubble.Progress;
                bubble.SetOpen(true);check(bubble.Progress==progress,"An interrupted retraction reopens from the displayed fission frame");clock.Pulse(851);clock.Pulse(1550);Layout(top);
                top.Width=310;Layout(top);check(Math.Abs(anchor.Bounds.Y-remove.Bounds.Y)<.01 && bubble.Bounds.Width<=bar.Bounds.Width,"A narrow toolbar wraps the connected pair together without detaching it onto different rows");
                top.Width=640;bubble.SetOpen(false);clock.Pulse(1551);clock.Pulse(2250);Layout(top);
                check(!remove.IsEffectivelyVisible && anchor.IsEffectivelyVisible && !remove.IsHitTestVisible,"Reverse motion merges the red lobe and leaves no invisible removal input target");
            }
        }
        finally { top.Close(); }
        File.WriteAllText(Path.Combine(output,"fission-samples.json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions { WriteIndented=true }));
        var button=new ConnectionButton { HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
        top=new Window { Width=240,Height=100,Background=Skin.ChromeBrush,Content=button };top.Show();Settle(top);
        var assembly=Path.Combine(output,"assembly-frames");Directory.CreateDirectory(assembly);var pieces=new List<object>();
        try
        {
            button.Logo.PreviewTime=0;
            using var clock=new UiAnimationClock(button.Logo,button.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="buttonFace"));
            clock.Pulse(0);button.SetState(ConnectionPhase.Connecting,true);clock.Pulse(1);clock.Pulse(1200);button.Logo.PreviewTime=1.2;
            var spinning=button.Logo.Curves();var velocity=button.Logo.Velocities();
            check(spinning.Length==6 && spinning.Chunk(2).All(pair=>Distance(pair[0].End,pair[1].Start)<.01),"The spinner consists of three continuous arcs whose seams are ready to split into six construction pieces");
            Save(top,Path.Combine(output,"connecting.png"));
            button.SetState(ConnectionPhase.Connected,false);
            check(button.Logo.Curves().SequenceEqual(spinning) && button.Logo.Velocities().SequenceEqual(velocity),"Success keeps all visible arc positions and velocities at the moment assembly starts");
            clock.Pulse(1201);
            for(var frame=0;frame<=50;frame++)
            {
                clock.Pulse(1201+frame*20);Layout(top);var curves=button.Logo.Curves();
                pieces.Add(new { milliseconds=frame*20,button.Logo.MorphProgress,curves });
                Save(top,Path.Combine(assembly,frame.ToString("D3")+".png"));
                if(frame==6)check(curves.Chunk(2).Any(pair=>Distance(pair[0].End,pair[1].Start)>2),"The three original arcs have visibly separated pieces before the ladder is complete");
                if(frame==14)check(Math.Abs(curves[1].Start.X-curves[1].End.X)<.6 && Math.Abs(curves[2].Start.X-curves[2].End.X)<.6 && Math.Abs(curves[5].Start.Y-curves[5].End.Y)>.7,"Both rails dock before the final bottom rung, so the result reads as construction rather than one simultaneous morph");
            }
            check(button.Logo.MorphProgress==1 && button.Logo.Curves().Length==6 && ConnectionLogo.ArtworkBounds.Height==32 && !button.Logo.Waiting,"The settled success state keeps the same six source-derived vector strokes and stops the spinner timer");
            Save(top,Path.Combine(output,"connected.png"));
            button.SetState(ConnectionPhase.Connecting,true);clock.Pulse(2202);clock.Pulse(3202);button.Logo.PreviewTime=2.2;
            button.SetState(ConnectionPhase.Connected,false);clock.Pulse(3203);clock.Pulse(3443);var current=button.Logo.Curves();velocity=button.Logo.Velocities();
            button.SetState(ConnectionPhase.Disconnected,false);
            check(button.Logo.Curves().SequenceEqual(current) && button.Logo.Velocities().SequenceEqual(velocity),"Cancelling during staged assembly retains the current pieces and velocity before reversing");
            clock.Pulse(3444);clock.Pulse(4444);check(button.Logo.Assembly==0 && button.Logo.MorphProgress==1 && !button.Logo.Waiting,"Interrupted assembly finishes at the original play symbol without a hidden active timer");
        }
        finally { top.Close(); }
        File.WriteAllText(Path.Combine(output,"assembly-samples.json"),JsonSerializer.Serialize(pieces,new JsonSerializerOptions { WriteIndented=true }));
        using var iconStream=typeof(MainWindow).Assembly.GetManifestResourceStream("Tizi.Desktop.AppIcon.png")!;
        using var icon=new Bitmap(iconStream);
        var sizes=new[] { 16,24,32,48,64,128 };var row=new StackPanel { Orientation=Orientation.Horizontal,Spacing=20,Margin=new(24),VerticalAlignment=VerticalAlignment.Center };
        foreach(var size in sizes)
        {
            var image=new Image { Source=icon,Width=size,Height=size };
            RenderOptions.SetBitmapInterpolationMode(image,BitmapInterpolationMode.HighQuality);
            row.Children.Add(new StackPanel { Spacing=10,VerticalAlignment=VerticalAlignment.Center,Children={ image,Ui.Value(size+" px",true,11) } });
        }
        top=new Window { Width=590,Height=210,Background=Skin.ChromeBrush,Content=row };top.Show();Settle(top);
        try
        {
            Save(top,Path.Combine(output,"app-icon-sizes.png"));
            check(icon.PixelSize==new PixelSize(1024,1024),"The app icon renderer uses the approved 1024-pixel embedded artwork at multiple shell sizes");
            var ladder=new BrandRaster("ConnectionLadder",32);var mark=new BrandRaster("BrandMark",28);
            check(ladder.IsVector&&mark.IsVector,"Both toolbar brand marks use source-derived vector geometry at every display scale");
            foreach(var scale in new[] {1d,1.25,1.5,1.75,2,2.5,3,4})
                check(ladder.AtScale(scale).PixelSize.Height==Math.Round(32*scale) && mark.AtScale(scale).PixelSize.Height==Math.Round(28*scale)
                    && ladder.Bounds(new(48,18),scale).Center==new Point(48,18),
                    "Source-derived supersampled assets match native pixels and remain centered at display scale "+scale);
        }
        finally { top.Close(); }
        var dpiButton=new ConnectionButton { VerticalAlignment=VerticalAlignment.Center };
        var dpiMark=new WordmarkHost { VerticalAlignment=VerticalAlignment.Center };
        top=new Window { Width=310,Height=110,Background=Skin.ChromeBrush,Content=new StackPanel
            { Orientation=Orientation.Horizontal,Spacing=20,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Children={dpiMark,dpiButton} } };
        top.Show();Settle(top);
        try
        {
            using(var clock=new UiAnimationClock(dpiButton.Logo))
            {clock.Pulse(0);dpiButton.SetState(ConnectionPhase.Connected,false);clock.Pulse(1);clock.Pulse(1000);}
            foreach(var scale in new[] {1d,1.25,1.5,1.75,2,2.5,3,4})
            {
                top.SetRenderScaling(scale);Layout(top);AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var image=top.CaptureRenderedFrame()??throw new IOException("Missing DPI capture");
                image.Save(Path.Combine(output,"dpi-"+(int)Math.Round(scale*100)+".png"),new PngBitmapEncoderOptions());
                check(top.RenderScaling==scale && image.PixelSize.Width==(int)Math.Ceiling(top.ClientSize.Width*scale),
                    "Production toolbar mark and connected button render at physical pixel density "+scale);
            }
        }
        finally { top.Close(); }
    }
}
