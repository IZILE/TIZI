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
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool okay,string label){if(!okay)throw new InvalidDataException(label);checks.Add(label);}
        try
        {
            Capture(output,Check);
            File.WriteAllText(Path.Combine(output,"appearance-checks.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,syntheticData=true,personalConfigurationAccessed=false,nativeBackdropPixelsVerified=false,binary=VerificationStamp.Current()},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception failure){File.WriteAllText(Path.Combine(output,"appearance-failure.txt"),string.Join('\n',checks)+"\n"+failure);return 1;}
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
            return VerificationPixels.Bgra(image);
        }
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
            foreach(var theme in new[]{"Dark"})
            {
                Layout(window);
                var about=new AboutPage(typeof(MainWindow).Assembly.GetName().Version!.ToString(3));
                var aboutWindow=new Window{Width=520,Height=370,Background=Skin.PasteboardBrush,Content=about};
                aboutWindow.Show();about.Reveal();Layout(aboutWindow);
                if(aboutWindow.Content is DialogSurface aboutSurface)
                {
                    using var settle=new UiAnimationClock(aboutSurface,aboutSurface.Scale,aboutSurface.Offset);
                    aboutSurface.Reveal();settle.Pulse(0);settle.Pulse(220);Layout(aboutWindow);
                }
                try
                {
                    var logo=about.GetVisualDescendants().OfType<Border>().Single(b=>b.Tag is "about-logo");
                    using var visible=Frame(aboutWindow);var logoOpacity=logo.Opacity;logo.Opacity=0;
                    using var hidden=Frame(aboutWindow);logo.Opacity=logoOpacity;
                    var front=Pixels(visible);var back=Pixels(hidden);var origin=logo.TranslatePoint(default,aboutWindow)!.Value;
                    var changed=0;var maximum=0;
                    for(var y=(int)origin.Y;y<(int)(origin.Y+logo.Bounds.Height);y++)
                    for(var x=(int)origin.X;x<(int)(origin.X+logo.Bounds.Width);x++)
                    {
                        var index=(y*visible.PixelSize.Width+x)*4;
                        var delta=Enumerable.Range(0,3).Max(c=>Math.Abs(front[index+c]-back[index+c]));
                        if(delta>40)changed++;maximum=Math.Max(maximum,delta);
                    }
                    check(changed>200&&maximum>100,theme+" About logo renders visible contrasting ink from the original artwork (pixels "+changed+")");
                    visible.Save(Path.Combine(output,theme.ToLowerInvariant()+"-about.png"),new PngBitmapEncoderOptions());
                }
                finally{aboutWindow.Close();}
                check(((ISolidColorBrush)TrafficChart.DownloadColor).Color==Color.Parse("#58D6B2")&&((ISolidColorBrush)TrafficChart.UploadColor).Color==Color.Parse("#82B4FF")
                    &&((ISolidColorBrush)TrafficChart.LatencyColor).Color==Color.Parse("#F6C76C"),theme+" preserves the original colored metrics and chart palette");
                MenuAppearanceChecks.Run(window,theme,output,check);
                var capture=window.GetVisualDescendants().OfType<StableChoice>().Single(c=>c.Classes.Contains("capture-mode"));
                var enabled=capture.IsEnabled;var selected=capture.SelectedIndex;
                capture.IsEnabled=true;Layout(window);
                check(ReferenceEquals(window.ModeControl.Background,Skin.ToolbarFill)&&ReferenceEquals(capture.Background,Skin.ToolbarFill),
                    theme+" routing and capture controls share the same toolbar fill");
                capture.SelectedIndex=1;capture.IsEnabled=false;Layout(window);
                check(capture.Opacity==1&&ReferenceEquals(capture.Background,Skin.ToolbarDisabledFill)
                      &&capture.GetVisualDescendants().OfType<Grid>().Single(g=>g.Name=="choiceContent").Opacity==.4,
                    theme+" disabled capture control keeps its backplate visible while its label retains the disabled cue");
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
                }
                window.ShowPage("Settings");Layout(window);
                check(!window.PageView.GetVisualDescendants().OfType<StableChoice>().Any(c=>c.Tag is "settings-appearance"),
                    "Settings contain no appearance picker");
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
            var preference=UserData.File("appearance.json");
            Directory.CreateDirectory(UserData.DirectoryPath);
            foreach(var legacy in new[]{"{\"Theme\":\"Light\"}","{\"Theme\":\"System\"}"})
            {
                File.WriteAllText(preference,legacy);Appearance.Initialize(Application.Current!);
                check(Application.Current!.RequestedThemeVariant==Avalonia.Styling.ThemeVariant.Dark
                      &&window.ActualThemeVariant==Avalonia.Styling.ThemeVariant.Dark&&File.ReadAllText(preference)==legacy,
                      "Legacy appearance settings are ignored without rewriting user preferences: "+legacy);
            }
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
