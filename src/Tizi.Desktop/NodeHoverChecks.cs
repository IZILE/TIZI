using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

internal static class NodeHoverChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);var checks=new List<string>();
        try
        {
            Verify(output,(okay,label)=>{if(!okay)throw new InvalidDataException(label);checks.Add(label);});
            File.WriteAllText(Path.Combine(output,"node-hover-checks.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,binary=VerificationStamp.Current()},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception failure){File.WriteAllText(Path.Combine(output,"node-hover-failure.txt"),string.Join('\n',checks)+"\n"+failure);return 1;}
    }
    internal static void Verify(string output,Action<bool,string> check)
    {
        static void Layout(Control top){top.UpdateLayout();Dispatcher.UIThread.RunJobs();top.UpdateLayout();}
        static Bitmap Frame(Window top){Layout(top);AvaloniaHeadlessPlatform.ForceRenderTimerTick();return top.CaptureRenderedFrame()!;}
        static byte[] Pixel(Bitmap image,Point point,double scale)
        {
            var pixel=new byte[4];var pin=GCHandle.Alloc(pixel,GCHandleType.Pinned);
            try{image.CopyPixels(new PixelRect((int)Math.Floor(point.X*scale),(int)Math.Floor(point.Y*scale),1,1),pin.AddrOfPinnedObject(),4,4);}finally{pin.Free();}return pixel;
        }
        var old=Appearance.Current;var samples=new List<object>();
        try
        {
            foreach(var theme in new[]{"Dark","Light"})
            {
                Appearance.Set(theme,false);var rows=new MovingRows(true){Margin=new(16)};
                rows.SetRows([new("fixture","Node fixture","SS · TCP / UDP","80 ms","JP",Latency:true)],null,nodeCount:4);
                var window=new Window{Width=360,Height=160,Background=Skin.PasteboardBrush,Content=rows};window.Show();Layout(window);
                if(window.Content is DialogSurface surface){using var settle=new UiAnimationClock(surface,surface.Scale,surface.Offset);surface.Reveal();settle.Pulse(0);settle.Pulse(200);}
                try
                {
                    var card=rows.For("fixture");var face=card.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="buttonFace");
                    using var clock=new UiAnimationClock(face);clock.Pulse(0);Layout(window);
                    var sample=card.TranslatePoint(new(.5,card.Bounds.Height/2),window)!.Value;
                    using var idleFrame=Frame(window);var idle=Pixel(idleFrame,sample,window.RenderScaling);
                    window.MouseMove(card.TranslatePoint(new(card.Bounds.Width/2,card.Bounds.Height/2),window)!.Value);clock.Pulse(1);clock.Pulse(181);Layout(window);
                    using var hoveredFrame=Frame(window);var hovered=Pixel(hoveredFrame,sample,window.RenderScaling);
                    var leaveAt=182;window.MouseMove(new(2,2));clock.Pulse(leaveAt);
                    for(var ms=0;ms<=184;ms+=8)
                    {
                        clock.Pulse(leaveAt+ms);Layout(window);using var frame=Frame(window);var color=Pixel(frame,sample,window.RenderScaling);
                        for(var c=0;c<3;c++)check(color[c]>=Math.Min(idle[c],hovered[c])-2&&color[c]<=Math.Max(idle[c],hovered[c])+2,
                            theme+" node outline fades inside its endpoint colors without a white halo at "+ms+" ms channel "+c+" ("+color[c]+", "+idle[c]+".."+hovered[c]+")");
                        samples.Add(new{theme,milliseconds=ms,pixel=color});
                        if(ms is 0 or 24 or 64 or 120 or 184)frame.Save(Path.Combine(output,theme.ToLowerInvariant()+"-node-leave-"+ms+".png"),new PngBitmapEncoderOptions());
                    }
                    check(face.BorderBrush is ISolidColorBrush border&&border.Color.A==0,"Leaving the "+theme+" node removes its border completely");
                    window.MouseMove(card.TranslatePoint(new(80,40),window)!.Value);clock.Pulse(370);clock.Pulse(410);var before=((ISolidColorBrush)face.BorderBrush!).Color;
                    window.MouseMove(new(2,2));check(((ISolidColorBrush)face.BorderBrush!).Color==before,"Interrupted "+theme+" node hover retains the displayed border color at reversal");clock.Pulse(411);clock.Pulse(600);
                }
                finally{window.Close();}
            }
        }
        finally{Appearance.Set(old.Theme,false);}
        File.WriteAllText(Path.Combine(output,"node-hover-samples.json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions{WriteIndented=true}));
    }
}
