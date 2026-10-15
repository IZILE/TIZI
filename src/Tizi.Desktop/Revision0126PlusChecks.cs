using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class Revision0126PlusChecks
{
    internal static async Task RunAsync(string output,Action<bool,string> check)
    {
        static void Layout(Control top) { top.UpdateLayout();Dispatcher.UIThread.RunJobs(); }
        static byte[] Save(Control top,string file)
        {
            Layout(top);AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=top.CaptureRenderedFrame()??throw new IOException("Missing revision capture");
            image.Save(file,new PngBitmapEncoderOptions());return SHA256.HashData(File.ReadAllBytes(file));
        }
        static void Click(Control top,Control control)
        { var point=control.TranslatePoint(new(control.Bounds.Width/2,control.Bounds.Height/2),top)!.Value;top.MouseMove(point);top.MouseDown(point,MouseButton.Left);top.MouseUp(point,MouseButton.Left);Layout(top); }
        var subscription=new Subscription { Name="布局示例 · Fixture",Url="https://fixture.example/sub",Usage=new(0,107L*1024*1024*1024,300L*1024*1024*1024,null) };
        var node=NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@fixture.example:443#Fixture").Single();
        var state=new AppState { Nodes=Enumerable.Range(0,96).Select(i=>node with { Id=Guid.NewGuid(),Name="Fixture "+i,SubscriptionId=subscription.Id }).ToList(),Subscriptions=[subscription],AutoUpdate=false,AutoUpdateProviders=false,RestoreConnection=false,CloseToTray=false };
        state=state with { SelectedNode=state.Nodes[0].Id };
        var window=new MainWindow(true,state);window.Show();window.Rail.Select("Nodes");Layout(window);
        try
        {
            var group=window.SubscriptionView.Groups.Single();
            using(var clock=new UiAnimationClock(group.Reveal))
            {
                clock.Pulse(0);Click(window,group.Disclosure);clock.Pulse(1);clock.Pulse(300);Layout(window);
                var width=group.Bounds.Width;var quota=group.GetVisualDescendants().OfType<ProgressBar>().Single();var quotaWidth=quota.Bounds.Width;var value=quota.Value;
                Save(window,Path.Combine(output,"subscription-gutter-closed.png"));
                Click(window,group.Disclosure);clock.Pulse(301);clock.Pulse(440);Layout(window);
                check(group.Reveal.Progress>1 && group.Reveal.Bounds.Height-group.Reveal.Child!.DesiredSize.Height<=12.1,"Subscription expansion has a real but bounded elastic overshoot even for a large node list");
                clock.Pulse(600);Layout(window);
                check(Math.Abs(group.Bounds.Width-width)<.1 && Math.Abs(quota.Bounds.Width-quotaWidth)<.1 && quota.Value==value,"Opening an overflowing subscription cannot shrink its card, quota track or used proportion");
                var scroll=(ScrollViewer)window.PageView;var bar=scroll.GetVisualDescendants().OfType<ScrollBar>().Single(b=>b.Orientation==Avalonia.Layout.Orientation.Vertical);
                check(bar.IsVisible && scroll.Extent.Height>scroll.Viewport.Height && bar.TranslatePoint(default,window)!.Value.X>=group.TranslatePoint(new(group.Bounds.Width,0),window)!.Value.X,"The visible scrollbar occupies the existing right blank strip and remains outside the card");
                Save(window,Path.Combine(output,"subscription-gutter-open.png"));
            }
            window.SearchControl.Text="Fixture";Layout(window);var previous=window.PageView;
            await window.OpenAboutAsync();var about=(AboutPage)window.PageView;
            using(var clock=new UiAnimationClock(about,about.Offset))
            {
                about.Reveal();clock.Pulse(0);clock.Pulse(120);Layout(window);
                check(about.Offset.X<0 && about.Opacity>.9,"The About second page fades in and elastically settles past its destination without scaling the text");clock.Pulse(350);Layout(window);
            }
            check(!window.OwnedWindows.Any() && window.PageHeading.Text==UiText.Get("About TIZI"),"About uses the main content page, not an owned window or a modal input layer");
            foreach(var width in new[] {880d,1180d,1920d})
            { window.Width=width;Layout(window);check(about.Bounds.Width<=window.ClientSize.Width && about.Bounds.Height<=window.ClientSize.Height,"The About second page fits the actual content viewport at width "+width); }
            window.Width=1032;await window.Controller.ApplyAsync(window.Controller.State with { UpdateMinutes=42 });Layout(window);
            check(ReferenceEquals(window.PageView,about),"Background settings refresh keeps About visible instead of restoring the underlying list");
            Save(window,Path.Combine(output,"about-second-page.png"));await window.ReturnFromAboutAsync();Layout(window);
            check(ReferenceEquals(window.PageView,previous) && window.SearchControl.Text=="Fixture" && !window.OwnedWindows.Any(),"Returning from About restores the same page and filter without an invisible input blocker");
            await window.OpenAboutAsync();var returning=window.ReturnFromAboutAsync();window.Rail.Select("Overview");await returning;
            check(window.CurrentPage=="Overview" && window.PageView is ScrollViewer,"Sidebar navigation during About return cannot restore a stale second page");
            var monitor=window.OverviewMonitor;monitor.Update(1_000_000,2_500_000,0,true,"31 ms",TimeSpan.FromMinutes(5));Layout(window);
            var metrics=monitor.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.Text?.EndsWith("MB/S")==true).ToArray();
            check(metrics.Single(t=>t.Text=="2.50 MB/S").Foreground==TrafficChart.DownloadColor && metrics.Single(t=>t.Text=="1.00 MB/S").Foreground==TrafficChart.UploadColor,"Both speed numbers and their units share the actual download-green and upload-blue curve brushes");
            var result=new IpQualityResult("8.8.8.8",[new("Fixture A","Residential","US","AS1","Fixture ISP",12,"OK"),new("Fixture B","Residential","US","AS1","Fixture ISP",24,"OK")],"US",DateTimeOffset.UtcNow);
            monitor.IpQuality.Update(IpQualityPresentation.From(result),true);Layout(window);
            check(monitor.IpQuality.Badges[1].Child is TextBlock score && score.Text=="18/100" && Avalonia.Automation.AutomationProperties.GetHelpText(monitor.IpQuality.Badges[1])!.Contains("12 / 100"),"Real assessment presentation shows its derived median and keeps each provider's raw score in hover details");
            using(var logoClock=new UiAnimationClock(window.ConnectAction.Logo))
            { logoClock.Pulse(0);window.ConnectAction.SetState(ConnectionPhase.Connected,false);logoClock.Pulse(1);logoClock.Pulse(1000);Layout(window);Save(window,Path.Combine(output,"overview-current-design.png")); }
            var brand=window.GetVisualDescendants().OfType<WordmarkHost>().Single();var brandPosition=brand.TranslatePoint(brand.InkBounds.TopLeft,window)!.Value;
            var artwork=ConnectionLogo.ArtworkBounds;var ladderPosition=window.ConnectAction.Logo.TranslatePoint(artwork.TopLeft,window)!.Value;
            File.WriteAllText(Path.Combine(output,"brand-layout.json"),System.Text.Json.JsonSerializer.Serialize(new { brand=new { X=brandPosition.X,Y=brandPosition.Y,brand.InkBounds.Width,brand.InkBounds.Height },ladder=new { X=ladderPosition.X,Y=ladderPosition.Y,artwork.Width,artwork.Height },railCenter=window.Rail.GlyphFor("Overview").TranslatePoint(new(12,12),window)!.Value.X },new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
        }
        finally { window.Close(); }
        var chart=new TrafficChart { Width=880,Height=260,PreviewElapsed=0 };
        var chartWindow=new Window { Width=920,Height=310,Background=Skin.ChromeBrush,Content=chart };chartWindow.Show();Layout(chartWindow);
        if(chartWindow.Content is DialogSurface surface) { using var settle=new UiAnimationClock(surface,surface.Scale,surface.Offset);surface.Reveal();settle.Pulse(0);settle.Pulse(200); }
        var chartFrames=Path.Combine(output,"continuous-chart-frames");Directory.CreateDirectory(chartFrames);
        var origin=DateTimeOffset.UtcNow;
        for(var i=0;i<=60;i++)chart.Add(100000+(long)(80000*Math.Sin(i*.3)),250000+(long)(220000*Math.Pow(Math.Sin(i*.19),8)),origin.AddSeconds(i-60));
        var hashes=new HashSet<string>();var count=chart.SampleCount;var start=chart.DisplayTime;
        for(var i=0;i<=60;i++)
        {
            chart.PreviewElapsed=i/60d;chart.InvalidateVisual();var hash=Save(chartWindow,Path.Combine(chartFrames,i.ToString("D3")+".png"));
            if(i is >=20 and <=50)hashes.Add(Convert.ToHexString(hash));
        }
        check(chart.SampleCount==count && Math.Abs((chart.DisplayTime-start).TotalSeconds-1)<.001 && hashes.Count>=25,"The chart keeps moving between real one-second samples instead of stopping after a brief endpoint tween");
        check(chart.Animating && chart.HoverAt(chart.Plot.Right).Contains(UiFormat.Speed(100000+(long)(80000*Math.Sin(60*.3)))),"The continuous chart retains actual sample values for hover while the render timer is active");
        chartWindow.Close();Dispatcher.UIThread.RunJobs();check(!chart.Animating,"Detaching the chart stops its render timer so hidden pages do not keep repainting");
    }
}
