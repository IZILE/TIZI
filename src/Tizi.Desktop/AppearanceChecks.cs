using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class AppearanceChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);var checks=new List<string>();var old=Appearance.Current;
        void Check(bool okay,string label){if(!okay)throw new InvalidDataException(label);checks.Add(label);}
        try
        {
            Capture(output,Check);
            File.WriteAllText(Path.Combine(output,"appearance-checks.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,syntheticData=true,personalConfigurationAccessed=false,nativeBackdropPixelsVerified=false,binary=VerificationStamp.Current()},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception failure){File.WriteAllText(Path.Combine(output,"appearance-failure.txt"),string.Join('\n',checks)+"\n"+failure);return 1;}
        finally{Appearance.Set(old.Theme,false);}
    }
    private static void Capture(string output,Action<bool,string> check)
    {
        static void Layout(Control top){top.UpdateLayout();Dispatcher.UIThread.RunJobs();top.UpdateLayout();Motion.SettlePaint(top);}
        static Bitmap Frame(Window top)
        {
            Layout(top);var moving=top.GetVisualDescendants().OfType<SelectionIndicator>().SelectMany(i=>new Avalonia.Animation.Animatable[]{i,i.Position})
                .Concat(top.GetVisualDescendants().OfType<NavigationGlyph>()).ToArray();
            using var clock=new UiAnimationClock(moving);clock.Pulse(0);clock.Pulse(340);Layout(top);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();return top.CaptureRenderedFrame()??throw new IOException("No appearance frame.");
        }
        static byte[] Pixels(Bitmap image)
        {
            var data=new byte[image.PixelSize.Width*image.PixelSize.Height*4];var pin=GCHandle.Alloc(data,GCHandleType.Pinned);
            try{image.CopyPixels(new PixelRect(image.PixelSize),pin.AddrOfPinnedObject(),data.Length,image.PixelSize.Width*4);}finally{pin.Free();}return data;
        }
        static double Luminance(Color color)
        {
            double C(byte value){var x=value/255d;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}
            return .2126*C(color.R)+.7152*C(color.G)+.0722*C(color.B);
        }
        static double Contrast(Color a,Color b){var x=Luminance(a);var y=Luminance(b);return(Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
        var subscription=new Subscription{Name="示例订阅 · Example",Url="https://fixture.example/subscription",Usage=new(0,89_000_000_000,300_000_000_000,DateTimeOffset.UtcNow.AddDays(30))};
        var node=NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@node.example:443#Tokyo%20%7C%2001").Single();
        var state=new AppState{AutoUpdate=false,AutoUpdateProviders=false,RestoreConnection=false,Subscriptions=[subscription],Nodes=Enumerable.Range(0,48).Select(i=>node with{Id=Guid.NewGuid(),Name="Tokyo | "+(i+1).ToString("D2"),SubscriptionId=subscription.Id,CountryCode="JP"}).ToList(),
            Providers=Enumerable.Range(0,12).Select(i=>new ProviderSite{Name="示例机场 "+(i+1),Url="https://provider"+i+".example/",Region="JP · SG"}).ToList(),
            Rules=Enumerable.Range(0,80).Select(i=>new RoutingRule("DOMAIN","fixture"+i+".example","DIRECT")).ToList()};
        state=state with{SelectedNode=state.Nodes[0].Id};
        var window=new MainWindow(true,state);window.Show();Layout(window);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var logs=(ActivityLog)typeof(MainWindow).GetField("_logs",flags)!.GetValue(window)!;
        for(var i=0;i<80;i++)logs.Add("Synthetic application record "+i+" · This content is isolated test data.");
        typeof(MainWindow).GetField("_snapshot",flags)!.SetValue(window,new ConnectionSnapshot(0,0,Enumerable.Range(0,40).Select(i=>new ConnectionInfo(i.ToString(),"fixture"+i+".example:443","tcp","MATCH","Fixture","fixture.exe",1024,2048)).ToList()));
        var monitor=(NetworkMonitor)typeof(MainWindow).GetField("_monitor",flags)!.GetValue(window)!;
        var originalOrder=window.Controller.State.Nodes.Select(n=>n.Id).ToArray();
        try
        {
            foreach(var theme in new[]{"Dark","Light"})
            {
                Appearance.Set(theme,false);Layout(window);
                check(((ISolidColorBrush)TrafficChart.DownloadColor).Color==Color.Parse("#58D6B2")&&((ISolidColorBrush)TrafficChart.UploadColor).Color==Color.Parse("#82B4FF")
                    &&((ISolidColorBrush)TrafficChart.LatencyColor).Color==Color.Parse("#F6C76C"),theme+" preserves the original colored metrics and chart palette");
                MenuAppearanceChecks.Run(window,theme,output,check);
                var capture=window.GetVisualDescendants().OfType<StableChoice>().Single(c=>c.Classes.Contains("capture-mode"));
                var enabled=capture.IsEnabled;var selected=capture.SelectedIndex;
                capture.IsEnabled=true;Layout(window);
                check(ReferenceEquals(window.ModeControl.Background,Skin.ToolbarFill)&&ReferenceEquals(capture.Background,Skin.ToolbarFill),
                    theme+" routing and capture controls share the same toolbar fill");
                if(theme=="Light")
                {
                    var toolbarSamples=new List<object>();var feedback=capture.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="popupFeedback");
                    using var clock=new UiAnimationClock(feedback);clock.Pulse(0);
                    Color Sample(Bitmap image,Control control,Point point)
                    {
                        var p=control.TranslatePoint(point,window)!.Value;var data=Pixels(image);
                        var index=((int)Math.Floor(p.Y*window.RenderScaling)*image.PixelSize.Width+(int)Math.Floor(p.X*window.RenderScaling))*4;
                        return Color.FromRgb(data[index+2],data[index+1],data[index]);
                    }
                    foreach(var step in new[]{0,1,2})
                    {
                        window.MouseMove(step==1?capture.TranslatePoint(new(capture.Bounds.Width/2,capture.Bounds.Height/2),window)!.Value:new(window.Bounds.Width-5,window.Bounds.Height-5));
                        Layout(window);clock.Pulse(step*102+1);clock.Pulse(step*102+101);
                        using var image=Frame(window);
                        var routing=Sample(image,window.ModeControl.ButtonAt(1),new(5,31));var proxy=Sample(image,capture,new(18,5));
                        check(routing==proxy&&proxy==Skin.ToolbarFill.Color,"Light toolbar controls have identical rendered fill when "+new[]{"idle","hovered","pointer has left"}[step]);
                        toolbarSamples.Add(new{phase=new[]{"idle","hover","leave"}[step],routing=routing.ToString(),capture=proxy.ToString()});
                        if(step==2)image.Save(Path.Combine(output,"light-toolbar-enabled.png"),new PngBitmapEncoderOptions());
                    }
                    File.WriteAllText(Path.Combine(output,"toolbar-fill-samples.json"),JsonSerializer.Serialize(toolbarSamples,new JsonSerializerOptions{WriteIndented=true}));
                }
                capture.SelectedIndex=1;capture.IsEnabled=false;Layout(window);
                check(capture.Opacity==1&&ReferenceEquals(capture.Background,Skin.ToolbarDisabledFill)
                      &&capture.GetVisualDescendants().OfType<Grid>().Single(g=>g.Name=="choiceContent").Opacity==.4,
                    theme+" disabled capture control keeps its backplate visible while its label retains the disabled cue");
                if(theme=="Light")
                {
                    check(Skin.ToolbarFill.Color.R==Skin.ToolbarFill.Color.G&&Skin.ToolbarFill.Color.G==Skin.ToolbarFill.Color.B
                          &&Skin.FieldFill.Color.R-Skin.ToolbarFill.Color.R==6&&Skin.ToolbarFill.Color.A==255,
                        "Light routing and capture controls alone retain their slightly deeper neutral fill");
                    using var image=Frame(window);image.Save(Path.Combine(output,"light-toolbar-disabled.png"),new PngBitmapEncoderOptions());
                    var point=capture.TranslatePoint(new(18,5),window)!.Value;var bytes=Pixels(image);
                    var index=((int)Math.Floor(point.Y*window.RenderScaling)*image.PixelSize.Width+(int)Math.Floor(point.X*window.RenderScaling))*4;
                    check(Color.FromRgb(bytes[index+2],bytes[index+1],bytes[index])==Skin.ToolbarFill.Color,"Light disabled TUN backplate renders the same gray as the routing control");
                }
                capture.IsEnabled=enabled;capture.SelectedIndex=selected;Layout(window);
                foreach(var page in NavigationRail.Pages.Where(p=>p!="Second"))
                {
                    window.Rail.Select(page);window.ShowPage(page);Layout(window);
                    foreach(var cards in window.PageView.GetVisualDescendants().OfType<SettingsCards>()){cards.Transitions=null;cards.Progress=1;}
                    if(page=="Overview")
                    {
                        monitor.Update(125000,320000,4,true,"80 ms",TimeSpan.FromMinutes(17));monitor.SetLatencyMonitoring(true);monitor.Chart.Reset();
                        var now=DateTimeOffset.UtcNow;
                        for(var i=0;i<60;i++){monitor.Chart.Add(125000+(i%8)*9000,320000+(i%9)*14000,now.AddSeconds(i-60));monitor.Chart.AddLatency(80+i%7,now.AddSeconds(i-60));}
                        monitor.Chart.PreviewElapsed=1;
                    }
                    if(page=="Providers")
                    {
                        var cards=window.PageView.GetVisualDescendants().OfType<ProviderCardButton>().ToArray();
                        check(cards.Length>0&&cards.All(card=>card.GetVisualAncestors().OfType<MovingRows>().Single().RowSurfaceColor(card)==Skin.ContentCardFill.Color),
                            theme+" airport cards use the same neutral content-card fill as node cards");
                    }
                    using var image=Frame(window);image.Save(Path.Combine(output,theme.ToLowerInvariant()+"-"+page.ToLowerInvariant()+".png"),new PngBitmapEncoderOptions());
                    check(window.PageHeading.Foreground==Skin.LabelBrush&&window.ContentSurface.Background==Skin.PasteboardBrush&&((ISolidColorBrush)window.ContentSurface.Background).Color.A==255,
                        theme+" "+page+" keeps its functional page opaque and its heading readable");
                    if(theme=="Light")check(Contrast(Skin.Label,Skin.Pasteboard)>=7&&Contrast(Skin.Secondary,Skin.Pasteboard)>=4.5,
                        "Light "+page+" primary and secondary labels meet their contrast targets");
                }
                window.ShowPage("Settings");Layout(window);
                var mode=window.PageView.GetVisualDescendants().OfType<StableChoice>().Single(c=>c.Tag is "settings-appearance");
                check(mode.SelectedIndex==(theme=="Light"?1:0),theme+" is reflected in the whole-field appearance picker");
                check(window.VersionUpdateButton?.Content is TextBlock update&&update.Foreground==Skin.LabelBrush,theme+" neutral update action keeps readable label contrast");
                var oldNode=window.Controller.State.SelectedNode;
                check(window.FrostedChromeVisible&&window.TransparencyLevelHint[0]==WindowTransparencyLevel.AcrylicBlur&&window.TransparencyBackgroundFallback==Skin.WindowBackdrop,
                    theme+" frosted chrome requests compositor blur with an opaque fallback");
                var target=Skin.WindowBackdrop.Color;
                var samples=new List<object>();
                using(var tint=new RenderTargetBitmap(new PixelSize(64,64),new Vector(96,96)))
                {
                    using(var context=tint.CreateDrawingContext())context.FillRectangle(window.ChromeMaterial.Background!,new Rect(0,0,64,64));
                    var pixel=Pixels(tint);var index=(32*64+32)*4;var alpha=pixel[index+3];
                    check(alpha is >=249 and <255,theme+" backdrop retains restrained transparency");
                    foreach(var underlay in new[]{Colors.Black,Colors.White,Colors.Red,Colors.Lime,Colors.Blue})
                    {
                        // CopyPixels is premultiplied BGRA; composite over each extreme desktop color.
                        var red=pixel[index+2]+underlay.R*(255-alpha)/255d;
                        var green=pixel[index+1]+underlay.G*(255-alpha)/255d;
                        var blue=pixel[index]+underlay.B*(255-alpha)/255d;
                        var delta=Math.Max(Math.Abs(red-target.R),Math.Max(Math.Abs(green-target.G),Math.Abs(blue-target.B)));
                        check(delta<=6,theme+" backdrop stays close to its reference on "+underlay);
                        samples.Add(new{underlay=underlay.ToString(),red,green,blue,maximumChannelDelta=delta});
                    }
                }
                File.WriteAllText(Path.Combine(output,theme.ToLowerInvariant()+"-backdrop-samples.json"),JsonSerializer.Serialize(new{target=target.ToString(),samples},new JsonSerializerOptions{WriteIndented=true}));
                check(window.ContentSurface.Background==Skin.PasteboardBrush&&((ISolidColorBrush)window.ContentSurface.Background).Color.A==255&&window.Controller.State.SelectedNode==oldNode,
                    theme+" frosted chrome leaves the functional page and selected node intact");
                using(var image=Frame(window))image.Save(Path.Combine(output,theme.ToLowerInvariant()+"-frosted.png"),new PngBitmapEncoderOptions());
                check(!window.GetVisualDescendants().OfType<CheckBox>().Any(c=>c.Tag is "settings-frosted-chrome"),theme+" uses frosted chrome by default without a material switch");
                window.ShowPage("Overview");Layout(window);
                var shell=(Grid)typeof(MainWindow).GetField("_shellRoot",flags)!.GetValue(window)!;
                var mark=window.GetVisualDescendants().OfType<WordmarkHost>().Single();mark.Opacity=0;
                var blur=new BlurEffect{Radius=16};shell.Effect=blur;
                var animation=new StartupAnimation(()=>true,()=>mark.TranslatePoint(new(mark.Bounds.Width/2,mark.Bounds.Height/2),window)!.Value,f=>blur.Radius=16*f);
                var host=(Grid)window.Content!;host.Children.Add(animation);animation.ZIndex=20;Layout(window);
                try
                {
                    foreach(var p in new[]{0d,.18,.3,.46,.56,.7,.86,.94,1})
                    {
                        animation.PreviewProgress=p;blur.Radius=16*animation.Frost;animation.InvalidateVisual();
                        using var image=Frame(window);image.Save(Path.Combine(output,theme.ToLowerInvariant()+"-startup-"+(int)Math.Round(p*1000)+".png"),new PngBitmapEncoderOptions());
                        check(animation.GetVisualChildren().Count()==0&&animation.Strokes(p).Length==11,theme+" startup draws the same live stroke set at "+p);
                    }
                    animation.PreviewProgress=1;blur.Radius=0;animation.InvalidateVisual();using var before=Frame(window);
                    host.Children.Remove(animation);mark.Opacity=1;shell.Effect=null;using var after=Frame(window);
                    var a=Pixels(before);var b=Pixels(after);var region=new Rect(mark.TranslatePoint(default,window)!.Value,mark.Bounds.Size);
                    var max=0;for(var y=(int)region.Top;y<(int)region.Bottom;y++)for(var x=(int)region.Left;x<(int)region.Right;x++)for(var c=0;c<3;c++)
                    {var index=(y*before.PixelSize.Width+x)*4+c;max=Math.Max(max,Math.Abs(a[index]-b[index]));}
                    check(max<=4,theme+" startup docks without an image/opacity seam (maximum channel delta "+max+")");
                }
                finally{host.Children.Remove(animation);mark.Opacity=1;shell.Effect=null;}
            }
            check(window.Controller.State.Nodes.Select(n=>n.Id).SequenceEqual(originalOrder)&&!window.Controller.Connected&&!window.Controller.Engine.IsRunning,
                "Appearance changes preserve node order and never start a network connection");
            Appearance.Set("Light",true);check(Appearance.Read()==new AppearancePreferences("Light"),"The light preference survives a local interface preference reload");
            Appearance.Set("System",false);check(Application.Current!.RequestedThemeVariant==Avalonia.Styling.ThemeVariant.Default,"Follow system restores the platform theme variant");
            var durations=new List<double>();var timingSamples=new List<object>();
            foreach(var readyAt in new[]{.1,.9,1.8,3.8})
            {
                var timing=new StartupTiming();var previous=0d;var largest=0d;var smallest=double.MaxValue;
                while(!timing.Finished&&timing.Elapsed<8)
                {
                    timing.Advance(1/120d,timing.Elapsed>=readyAt);if(timing.Position<previous)throw new InvalidDataException("Startup reversed.");
                    if(timing.Position is >.12 and <.94){largest=Math.Max(largest,timing.Velocity);smallest=Math.Min(smallest,timing.Velocity);}previous=timing.Position;
                }
                check(timing.Finished&&largest>smallest*1.5,"Startup follows readiness at "+readyAt+" s with acceleration and a settled endpoint");durations.Add(timing.Elapsed);timingSamples.Add(new{readyAt,total=timing.Elapsed,largest,smallest});
            }
            check(durations.SequenceEqual(durations.Order())&&durations[^1]>durations[0]+2,"Slower initialization lengthens startup while fast initialization can finish earlier");
            var stalled=new StartupTiming();while(!stalled.Finished&&stalled.Elapsed<8)stalled.Advance(1/120d,false);
            check(stalled.Finished&&stalled.Elapsed<6,"A stalled initialization cannot trap the user behind the startup overlay");
            var probe=new StartupAnimation(()=>true,()=>new(36,62),_=>{});
            for(var i=0;i<=100;i++)foreach(var rail in StartupAnimation.TopRails.Concat(StartupAnimation.BottomRails))
            {
                var line=probe.Strokes(i/100d)[rail];var original=StartupAnimation.Letters[rail];
                if(Math.Abs(line[1].X-line[0].X-(original[1].X-original[0].X))>.00001||line[1].X<=line[0].X)throw new InvalidDataException("A startup rail shrank or reversed.");
            }
            check(true,"Every top and bottom stroke retains its length and orientation throughout fusion");
            File.WriteAllText(Path.Combine(output,"startup-timing.json"),JsonSerializer.Serialize(timingSamples,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{window.Close();}
    }
}
