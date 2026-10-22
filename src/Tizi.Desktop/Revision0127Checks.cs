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
using Tizi.Core;

namespace Tizi.Desktop;

internal static class Revision0127Checks
{
    internal static async Task<int> Run(string output)
    {
        Directory.CreateDirectory(output);File.Delete(Path.Combine(output,"failure.txt"));
        var checks=new List<string>();
        try
        {
            await RunAsync(output,(value,label)=> { if(!value)throw new InvalidOperationException(label);checks.Add(label); });
            await Revision0128Checks.RunAsync(output,(value,label)=> { if(!value)throw new InvalidOperationException(label);checks.Add(label); });
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new {count=checks.Count,checks,binary=VerificationStamp.Current()},new JsonSerializerOptions { WriteIndented=true }));
            return 0;
        }
        catch(Exception error) {File.WriteAllText(Path.Combine(output,"failure.txt"),string.Join("\n",checks)+"\n"+error);return 1;}
    }
    internal static async Task RunAsync(string output,Action<bool,string> check)
    {
        static void Layout(Control control) { control.UpdateLayout();Dispatcher.UIThread.RunJobs(); }
        static async Task Until(Func<bool> ready) { using var stop=new CancellationTokenSource(8000);while(!ready())await Task.Delay(10,stop.Token); }
        static void Click(Window top,Point point) { top.MouseMove(point);top.MouseDown(point,MouseButton.Left);top.MouseUp(point,MouseButton.Left);Layout(top); }
        static void Save(Window top,string file) { Layout(top);AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=top.CaptureRenderedFrame()!;frame.Save(file,new PngBitmapEncoderOptions()); }
        var state=new AppState { AutoUpdate=false,AutoUpdateProviders=false,RestoreConnection=false,CloseToTray=false,
            Providers=[new ProviderSite {Name="Fixture airport",Url="https://fixture.example/"}] };
        new Vault(UserData.DirectoryPath).Write("settings.dat",state);
        var window=new MainWindow(false);window.Show();await Until(()=>window.Controller.Ready);window.Rail.Select("Providers");Layout(window);
        try
        {
            var trigger=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Tag is "more-actions");
            var point=trigger.TranslatePoint(new(trigger.Bounds.Width/2,trigger.Bounds.Height/2),window)!.Value;
            Click(window,point);var sheet=window.ActiveMoreSheet!;
            using(var clock=new UiAnimationClock(sheet))
            {
                sheet.SetImmediately(0);sheet.Open();clock.Pulse(0);clock.Pulse(80);Layout(window);
                for(var i=0;i<12;i++)
                {
                    Click(window,point);check(sheet.Closing== (i%2==0),"Actual airport More pointer toggles its hidden source region during rapid clicks "+i);
                    clock.Pulse(81+i*11);Layout(window);
                }
                clock.Pulse(230);clock.Pulse(600);Layout(window);await Task.Delay(260);
                check(ReferenceEquals(window.ActiveMoreSheet,sheet)&&sheet.IsVisible&&!sheet.Closing,"Queued close completions cannot remove a reopened airport More surface");
                sheet.SetImmediately(0);sheet.Open();clock.Pulse(601);
                var sizes=new List<object>();
                for(var ms=0;ms<=280;ms+=4)
                {
                    clock.Pulse(601+ms);Layout(window);var visible=sheet.VisibleBounds;
                    check(sheet.Progress is >=0 and <=1 && visible.Width<=sheet.TargetBounds.Width+.01 && visible.Height<=sheet.TargetBounds.Height+.01,
                        "More expansion never overshoots at frame "+ms);
                    if(ms%20==0)sizes.Add(new {milliseconds=ms,sheet.Progress,visible.Width,visible.Height});
                }
                File.WriteAllText(Path.Combine(output,"more-no-bounce.json"),JsonSerializer.Serialize(sizes));
                Save(window,Path.Combine(output,"provider-more-no-bounce.png"));
            }
            await sheet.CloseAsync();await Until(()=>window.ActiveMoreSheet is null);
            check(trigger.Opacity==1&&trigger.IsEffectivelyEnabled&&!window.OwnedWindows.Any(),"Rapid airport More cycles leave no invisible source or native modal window");
            for(var cycle=0;cycle<6;cycle++)
            {
                Click(window,point);sheet=window.ActiveMoreSheet!;
                using(var clock=new UiAnimationClock(sheet)) {sheet.SetImmediately(0);sheet.Open();clock.Pulse(0);clock.Pulse(300);Layout(window);}
                Click(window,new(12,window.ClientSize.Height-12));await Until(()=>window.ActiveMoreSheet is null);
            }
            check(trigger.Opacity==1&&window.GetVisualDescendants().OfType<MoreActionsSheet>().Count()==0,"Repeated real opening/outside-dismissal removes every More input layer");
            var site=window.PageView.GetVisualDescendants().OfType<Button>().Single(b=>b.Content is TextBlock text&&text.Text==UiText.Get("Official website"));
            check(site.Classes.Contains("accent"),"Provider-card Open website uses the shared blue accent button");

            var deactivations=0;window.Deactivated+=(_,_)=>deactivations++;
            var editor=new EditorDialog("Log settings",[new("fixture","Name","fixture")],_=>Task.CompletedTask);
            var shown=editor.ShowDialog(window);Layout(window);var overlay=editor.Overlay!;
            using(var clock=new UiAnimationClock(overlay))
            {
                overlay.SetImmediately(0);overlay.Reveal();clock.Pulse(0);
                for(var ms=0;ms<=280;ms+=4)
                {
                    clock.Pulse(ms);Layout(window);
                    check(overlay.Progress is >=0 and <=1 && overlay.VisibleBounds.Width<=overlay.TargetBounds.Width+.01,
                        "Centered dialog expansion never overshoots at frame "+ms);
                }
                Save(window,Path.Combine(output,"client-dialog-no-bounce.png"));
            }
            check(TopLevel.GetTopLevel(editor)==window&&!window.OwnedWindows.Any()&&deactivations==0,"Opening a client-area dialog retains the main native window and activation state");
            var page=window.CurrentPage;Click(window,new(36,window.Rail.GlyphFor("Overview").TranslatePoint(new(12,12),window)!.Value.Y));
            check(window.CurrentPage==page,"A modal sheet blocks background page input instead of leaking its outside click");
            await shown;check(window.OpenDialogs.Count==0&&trigger.IsEffectivelyEnabled,"Outside dismissal removes the hosted modal and restores actual page input");
            var parent=new CenteredDialog { Title="Parent fixture",Content=Ui.Value("Parent"),Width=500,Height=300 };
            var parentTask=parent.ShowDialog(window);CenteredDialogChecks.Ready(parent);
            var child=new CenteredDialog {Title="Child fixture",Content=Ui.Value("Child"),Width=400,Height=220};
            var childTask=child.ShowDialog(parent);CenteredDialogChecks.Ready(child);
            check(window.OpenDialogs.Count==2&&parent.IsEnabled&&!parent.IsHitTestVisible&&child.IsHitTestVisible&&!window.OwnedWindows.Any(),"Nested confirmations share one native window and isolate input without changing disabled visual styles");
            child.Close();await childTask;check(parent.IsHitTestVisible&&window.OpenDialogs.Count==1,"Closing a nested confirmation restores the parent sheet input");
            parent.Close();await parentTask;

            var api=new AiSearchSettingsDialog(new(),_=>Task.CompletedTask);var apiTask=api.ShowDialog(window);CenteredDialogChecks.Ready(api);
            api.ProviderChoice.SelectedItem="DeepSeek";Layout(window);
            check(api.ProtocolChoice.ItemCount==3&&api.EndpointField.Text=="https://api.deepseek.com/chat/completions","DeepSeek exposes its three implemented formats and defaults to its own Chat endpoint");
            api.ProtocolChoice.SelectedIndex=1;check(api.EndpointField.Text=="https://api.deepseek.com/responses","Switching DeepSeek to Responses updates the preset endpoint");
            api.ProtocolChoice.SelectedIndex=2;check(api.EndpointField.Text=="https://api.deepseek.com/anthropic/v1/messages","Switching DeepSeek to Messages uses its Anthropic endpoint");
            api.EndpointField.Text="https://gateway.fixture.example/custom";api.ProtocolChoice.SelectedIndex=0;
            check(api.EndpointField.Text=="https://gateway.fixture.example/custom","Protocol changes preserve an explicitly entered custom gateway");
            api.ProviderChoice.SelectedItem="Custom";check(api.ProtocolChoice.ItemCount==4,"Custom API keeps all four wire formats available");
            api.Close();await apiTask;
            window.Rail.Select("Overview");Layout(window);var monitor=window.OverviewMonitor;monitor.Update(1_000_000,2_500_000,0,true,"31 ms");
            var display=new IpQualityDisplay("家庭宽带","注册地不同","Type fixture","Registration fixture",false);
            monitor.IpQuality.Update(display,true);Layout(window);
            check(!monitor.IpQuality.Badges[1].IsVisible && monitor.IpQuality.Badges[0].IsVisible,"A different registration country keeps only its known type and shows no negative native badge");
            monitor.IpQuality.Update(display with { IsNative=null,Native="待定" },true);
            check(!monitor.IpQuality.Badges[1].IsVisible,"An undetermined native-IP result has no permanent badge");
            monitor.IpQuality.Update(display with { IsNative=true,Native="原生 IP" },true);Layout(window);
            check(monitor.IpQuality.Badges[1].IsVisible&&Avalonia.Automation.AutomationProperties.GetHelpText(monitor.IpQuality.Badges[1])!.Contains("Registration fixture"),"A known native result shows the second badge with its own attributable registration details");
            var labels=window.GetVisualDescendants().OfType<Button>().Where(b=>b.Tag is string tag&&tag.StartsWith("chart-series-")).SelectMany(b=>b.GetVisualDescendants().OfType<TextBlock>()).Concat(monitor.IpQuality.Badges.Select(b=>(TextBlock)b.Child!));
            check(labels.All(label=>label.Foreground is ISolidColorBrush brush&&brush.Color==Color.Parse("#D6D6D6")),"Traffic and IP label text uses one opaque soft-white color rather than pure white or low-opacity gray");
            Save(window,Path.Combine(output,"overview-tags-0127.png"));
            ToolTip.SetIsOpen(monitor.IpQuality.Badges[1],true);monitor.IpQuality.Update(display,true);Layout(window);
            check(!ToolTip.GetIsOpen(monitor.IpQuality.Badges[1])&&!monitor.IpQuality.Badges[1].IsVisible
                &&string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(monitor.IpQuality.Badges[1])),
                "Replacing a native result with a negative result closes its tooltip and clears its accessible label");
            Save(window,Path.Combine(output,"overview-known-type-only.png"));
        }
        finally { window.Close();await Until(()=>!window.IsVisible); }
        CheckPlay(output,check);
    }
    private static void CheckPlay(string output,Action<bool,string> check)
    {
        var button=new ConnectionButton();var top=new Window {Width=240,Height=100,Background=Skin.ChromeBrush,Content=button};top.Show();top.UpdateLayout();Dispatcher.UIThread.RunJobs();
        if(top.Content is DialogSurface surface)using(var clock=new UiAnimationClock(surface,surface.Scale,surface.Offset)) {surface.Reveal();clock.Pulse(0);clock.Pulse(220);}
        try
        {
            foreach(var scale in new[] {1d,1.25,1.5,1.75,2,3,4})
            {
                top.SetRenderScaling(scale);top.UpdateLayout();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=top.CaptureRenderedFrame()!;
                frame.Save(Path.Combine(output,"play-uniform-"+(int)(scale*100)+".png"),new PngBitmapEncoderOptions());
                var point=button.Logo.TranslatePoint(new(42.1620592580931,13),top)!.Value;
                var width=(int)Math.Ceiling(3.6*scale)+4;var height=(int)Math.Floor(9*scale);
                var region=new PixelRect((int)Math.Floor(point.X*scale)-width/2,(int)Math.Ceiling(point.Y*scale),width,height);
                var data=new byte[width*height*4];var pin=GCHandle.Alloc(data,GCHandleType.Pinned);
                try {frame.CopyPixels(region,pin.AddrOfPinnedObject(),data.Length,width*4);}finally {pin.Free();}
                var variation=Enumerable.Range(0,width).Max(x=>Enumerable.Range(0,3).Max(channel=>
                    Enumerable.Range(0,height).Max(y=>(int)data[(y*width+x)*4+channel])-Enumerable.Range(0,height).Min(y=>(int)data[(y*width+x)*4+channel])));
                var rgb=Enumerable.Range(0,width).SelectMany(x=>Enumerable.Range(0,3).Select(channel=>(int)data[x*4+channel])).ToArray();
                check(variation<=2&&rgb.Max()>250&&rgb.Min()<200,"Play vertical stroke and its blue surroundings have uniform pixel coverage through the old midpoint seam at display scale "+scale+" (range "+variation+")");
            }
        }
        finally {top.Close();}
    }
}
