using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

internal static class DialogReturnChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);var checks=new List<string>();
        try
        {
            Verify(output,(okay,label)=>{if(!okay)throw new InvalidDataException(label);checks.Add(label);});
            File.WriteAllText(Path.Combine(output,"dialog-return-checks.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,binary=VerificationStamp.Current(),syntheticData=true,personalConfigurationAccessed=false},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception error){File.WriteAllText(Path.Combine(output,"dialog-return-failure.txt"),string.Join('\n',checks)+"\n"+error);return 1;}
    }
    internal static void Verify(string output,Action<bool,string> check)
    {
        Directory.CreateDirectory(output);
        foreach(var scale in new[]{1d,1.25,1.5,2})VerifyFixture("Dark",scale,output,check);
    }

    private static void VerifyFixture(string theme,double scale,string output,Action<bool,string> check)
    {
        static void Layout(Control control){control.UpdateLayout();Dispatcher.UIThread.RunJobs();control.UpdateLayout();}
        var name=theme+"-"+(int)(scale*100);var directory=Path.Combine(output,"dialog-return-"+name);Directory.CreateDirectory(directory);
        var button=Ui.Button(UiText.Get("Log settings"),()=>{});button.HorizontalAlignment=HorizontalAlignment.Left;button.VerticalAlignment=VerticalAlignment.Top;button.Margin=new(120,80,0,0);
        var panel=new Grid{Children={button}};
        var window=new Window{Width=900,Height=650,Background=Skin.PasteboardBrush,Content=panel};window.Show();window.SetRenderScaling(scale);Layout(window);
        if(window.Content is DialogSurface surface){using var settle=new UiAnimationClock(surface,surface.Scale,surface.Offset);surface.Reveal();settle.Pulse(0);settle.Pulse(220);Layout(window);}
        try
        {
            var sourceLabel=(TextBlock)button.Content!;var position=button.PointToScreen(default);
            var origin=new DialogOrigin(button,new(position.X,position.Y,button.Bounds.Width*scale,button.Bounds.Height*scale),DialogOrigin.SurfaceColor(button),button.CornerRadius.TopLeft,sourceLabel.Text!);
            button.Opacity=0;
            var content=Ui.Value("Synthetic settings content");
            var overlay=new DialogOverlay(content,sourceLabel.Text!,new(24),500,330,double.PositiveInfinity,origin,()=>{});panel.Children.Add(overlay);Layout(window);
            var caption=overlay.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text==sourceLabel.Text);
            using var clock=new UiAnimationClock(overlay);overlay.SetImmediately(1);Layout(window);
            var contentOffset=content.TranslatePoint(default,overlay)!.Value-overlay.VisibleBounds.Position;
            overlay.Dismiss();clock.Pulse(0);var samples=new List<object>();double previous=caption.FontSize;var smooth=true;var bodyDrift=0d;
            for(var ms=0;ms<=240;ms+=8)
            {
                clock.Pulse(ms);Layout(window);
                var point=caption.TranslatePoint(default,window)!.Value;
                var body=content.TranslatePoint(default,overlay)!.Value-overlay.VisibleBounds.Position;
                smooth &= caption.FontSize<=previous+.001&&Math.Abs(caption.FontSize-previous)<2;previous=caption.FontSize;
                bodyDrift=Math.Max(bodyDrift,Math.Abs(body.Y-contentOffset.Y));
                samples.Add(new{ms,progress=overlay.Progress,fontSize=caption.FontSize,fontWeight=(int)caption.FontWeight,x=point.X,y=point.Y,bodyY=body.Y,initialBodyY=contentOffset.Y});
                if(ms is 0 or 64 or 128 or 176 or 208 or 224 or 240)Capture(window,Path.Combine(directory,"close-"+ms+".png")).Dispose();
            }
            check(smooth&&caption.FontSize==sourceLabel.FontSize,name+" closing title changes size continuously and finishes at the actual source button font size");
            var target=sourceLabel.TranslatePoint(default,window)!.Value;var actual=caption.TranslatePoint(default,window)!.Value;
            check(Math.Abs(target.X-actual.X)<.01&&Math.Abs(target.Y-actual.Y)<.01&&caption.FontWeight==sourceLabel.FontWeight,
                name+" last dialog text frame matches the source button's position and font weight before handoff");
            File.WriteAllText(Path.Combine(directory,"samples.json"),JsonSerializer.Serialize(new{samples,bodyDrift},new JsonSerializerOptions{WriteIndented=true}));
            check(bodyDrift<=1/scale&&overlay.GetVisualChildren().Single() is Grid{Opacity:1,RenderTransform:null}&&!overlay.HasSourcePreview,
                name+" title return keeps settings content layout within one raster row on one opaque live page (drift "+bodyDrift+")");
            using var final=Capture(window,Path.Combine(directory,"before-handoff.png"));
            var rect=sourceLabel.TranslatePoint(default,window)!.Value;
            var before=Pixels(final,rect,sourceLabel.Bounds.Size,scale);panel.Children.Remove(overlay);button.Opacity=1;Layout(window);
            using var restored=Capture(window,Path.Combine(directory,"after-handoff.png"));var after=Pixels(restored,rect,sourceLabel.Bounds.Size,scale);
            var max=before.Zip(after,(a,b)=>Math.Abs(a-b)).Max();
            check(max<=2,name+" rendered text region has no abrupt glyph change when the overlay is removed (max channel difference "+max+")");
            File.WriteAllText(Path.Combine(directory,"samples.json"),JsonSerializer.Serialize(new{samples,maximumHandoffChannelDifference=max},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{window.Close();}
    }
    private static Bitmap Capture(Window window,string file)
    {AvaloniaHeadlessPlatform.ForceRenderTimerTick();var frame=window.CaptureRenderedFrame()??throw new IOException("Missing dialog frame.");frame.Save(file,new PngBitmapEncoderOptions());return frame;}
    private static byte[] Pixels(Bitmap image,Point position,Size size,double scale)
    {
        var rect=new PixelRect((int)Math.Floor(position.X*scale),(int)Math.Floor(position.Y*scale),(int)Math.Ceiling(size.Width*scale),(int)Math.Ceiling(size.Height*scale));
        var data=new byte[rect.Width*rect.Height*4];var pin=GCHandle.Alloc(data,GCHandleType.Pinned);
        try{image.CopyPixels(rect,pin.AddrOfPinnedObject(),data.Length,rect.Width*4);}finally{pin.Free();}return data;
    }
}
