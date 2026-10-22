using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

// Measure painted text coverage, not a fixed white threshold that changes with badge color.
internal static class TagAlignmentChecks
{
    private static (double Offset,double Weight,double BoundsOffset) Measure(Bitmap image,Window window,Control surface,TextBlock text,double scale,Color background)
    {
        var p=surface.TranslatePoint(default,window)!.Value;var ink=text.TranslatePoint(default,window)!.Value;
        var bounds=new PixelRect((int)Math.Round(ink.X*scale),(int)Math.Round(p.Y*scale),
            (int)Math.Round(text.Bounds.Width*scale),(int)Math.Round(surface.Bounds.Height*scale));
        var data=new byte[bounds.Width*bounds.Height*4];var pin=GCHandle.Alloc(data,GCHandleType.Pinned);
        try {image.CopyPixels(bounds,pin.AddrOfPinnedObject(),data.Length,bounds.Width*4);}finally {pin.Free();}
        var foreground=((ISolidColorBrush)text.Foreground!).Color;
        var direction=new[]{(double)foreground.R-background.R,(double)foreground.G-background.G,(double)foreground.B-background.B};
        var baseRgb=new[]{background.R,background.G,background.B};var norm=direction.Sum(value=>value*value);
        double weight=0,moment=0;var top=bounds.Height;var bottom=-1;
        for(var y=0;y<bounds.Height;y++)for(var x=0;x<bounds.Width;x++)
        {
            var start=(y*bounds.Width+x)*4;double projection=0;
            for(var c=0;c<3;c++)projection+=(data[start+c]-baseRgb[c])*direction[c];
            var coverage=Math.Clamp(projection/norm,0,1);if(coverage<.02)continue;
            weight+=coverage;moment+=(y+.5)*coverage;
            if(coverage>.45){top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
        }
        return ((moment/weight-bounds.Height/2d)/scale,weight,((top+bottom+1)/2d-bounds.Height/2d)/scale);
    }
    internal static void Run(string output,Action<bool,string> check)
    {
        var folder=Path.Combine(output,"tag-ink-centering");Directory.CreateDirectory(folder);
        var window=new MainWindow(true,new Tizi.Core.AppState {CloseToTray=false,AutoUpdate=false,AutoUpdateProviders=false});
        window.Show();window.UpdateLayout();Dispatcher.UIThread.RunJobs();
        window.ShowPage("Overview");window.OverviewMonitor.Update(1_000_000,2_500_000,0,true);
        var badges=window.OverviewMonitor.IpQuality;
        var samples=new List<object>();
        try
        {
            foreach(var scale in new[]{1d,1.25,1.5,1.75,2,2.5,3,4})
            foreach(var native in new[]{true,false})
            {
                window.SetRenderScaling(scale);
                badges.Update(new("家庭宽带",native?"原生 IP":"注册地不同","Synthetic type","Synthetic native",native),true);
                window.UpdateLayout();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var image=window.CaptureRenderedFrame()!;
                image.Save(Path.Combine(folder,(native?"native":"registration-differs")+"-"+(int)(scale*100)+".png"),new PngBitmapEncoderOptions());
                var badge=badges.Badges[1];var (offset,weight,boundsOffset)=Measure(image,window,badge,(TextBlock)badge.Child!,scale,((ISolidColorBrush)badge.Background!).Color);
                var name=((TextBlock)badge.Child!).Text;
                check(weight>20*scale*scale,"The "+name+" reference has real rendered text ink at scale "+scale);
                check(Math.Abs(boundsOffset)<=.7,"The "+name+" reference's painted text center matches its badge at scale "+scale+" (offset "+boundsOffset.ToString("0.###")+")");
                samples.Add(new {label=name,scale,offsetLogicalPixels=boundsOffset,paintedCoverage=weight});
                if(native)
                {
                    var other=badges.Badges.Where((_,index)=>index!=1).Select(border=>((Control)border,(TextBlock)border.Child!,((ISolidColorBrush)border.Background!).Color))
                        .Concat(window.OverviewMonitor.GetVisualDescendants().OfType<Button>().Where(button=>button.Tag is string tag&&tag.StartsWith("chart-series-"))
                            .Select(button=>((Control)button,button.GetVisualDescendants().OfType<TextBlock>().Single(),DialogOrigin.SurfaceColor(button))));
                    foreach(var (surface,label,background) in other)
                    {
                        var (centroid,coverage,error)=Measure(image,window,surface,label,scale,background);
                        check(coverage>20*scale*scale,"The "+label.Text+" label has real rendered ink at scale "+scale);
                        // CJK ink bounds are quantized to whole raster rows; 150% can contribute 2/3 of a logical pixel.
                        check(Math.Abs(error)<=.7,"The "+label.Text+" label's painted bounds center matches its capsule at scale "+scale+" (offset "+error.ToString("0.###")+")");
                        samples.Add(new {label=label.Text,scale,offsetLogicalPixels=error,paintedCoverage=coverage,centroidOffset=centroid});
                    }
                }
            }
        }
        finally {window.Close();}
        File.WriteAllText(Path.Combine(output,"tag-ink-centering.json"),JsonSerializer.Serialize(new {samples,
            scope="Rendered foreground coverage relative to each actual opaque badge color; optical vertical center, no native frame-time claim."},new JsonSerializerOptions {WriteIndented=true}));
    }
}
