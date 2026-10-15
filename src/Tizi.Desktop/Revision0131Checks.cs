using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class Revision0131Checks
{
    internal static async Task RunAsync(string output,Action<bool,string> check)
    {
        static void Layout(Control top){top.UpdateLayout();Dispatcher.UIThread.RunJobs();top.UpdateLayout();}
        static async Task Until(Func<bool> condition){using var timeout=new CancellationTokenSource(8000);while(!condition())await Task.Delay(10,timeout.Token);}
        static void Save(Window window,string path){Layout(window);AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=window.CaptureRenderedFrame()!;image.Save(path,new PngBitmapEncoderOptions());}
        static void Settle(SettingsCards cards){var transitions=cards.Transitions;cards.Transitions=null;cards.Progress=1;cards.Transitions=transitions;}
        var folder=Path.Combine(output,"revision-0131");Directory.CreateDirectory(folder);
        var node=NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@node.example:443#Japan%20Fixture").Single();
        var providers=Enumerable.Range(1,12).Select(i=>new ProviderSite{Name="Fixture provider "+i,Url=$"https://provider{i}.example/",Region="Fixture data"}).ToList();
        var state=new AppState{Nodes=[node],SelectedNode=node.Id,Providers=providers,Mode=RouteMode.Global,
            AutoUpdate=false,AutoUpdateProviders=false,RestoreConnection=false,StartHidden=false,CloseToTray=false};
        new Vault(UserData.DirectoryPath).Write("settings.dat",state);
        var window=new MainWindow(false);var opened=0;var loadedAtOpening=false;
        window.Opened+=(_,_)=>{opened++;loadedAtOpening=window.Controller.Ready&&window.Controller.State.SelectedNode==node.Id;};
        window.Show();window.Show();
        check(window.IsVisible,"Startup displays immediately and retains the original post-opening configuration load order");
        await Until(()=>window.Controller.Ready);Layout(window);
        try
        {
            check(opened==1&&!loadedAtOpening,"One visible opening starts the asynchronous saved-profile load, without a loading gate");
            check(window.ModeControl.SelectedIndex==(int)RouteMode.Global&&window.CaptureControl.IsEnabled,
                "The saved mode and controls update after the original asynchronous initialization finishes");
            Save(window,Path.Combine(folder,"first-ready-frame.png"));
            window.Hide();window.Show();Layout(window);check(window.IsVisible&&window.Controller.Ready,"Restoring an initialized window needs no second loading presentation");
            var menu=window.GetVisualDescendants().OfType<Menu>().Single();var file=(MenuItem)menu.Items[0]!;
            file.IsSubMenuOpen=true;Layout(window);var command=file.Items.OfType<MenuItem>().First();
            command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));file.IsSubMenuOpen=false;await Until(()=>window.OpenDialogs.Count==1);Layout(window);
            var dialog=window.OpenDialogs.Single();var overlay=dialog.Overlay!;
            check(file.Opacity==1&&!overlay.HasSourcePreview,"Opening from File keeps the real heading visible without a duplicate moving heading snapshot");
            var frames=new List<object>();
            using(var clock=new UiAnimationClock(overlay))
            {
                overlay.SetImmediately(0);overlay.Reveal();clock.Pulse(0);
                foreach(var ms in new[]{0,16,32,64,120,240,320})
                {
                    clock.Pulse(ms);Layout(window);AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var image=window.CaptureRenderedFrame()!;
                    var p=file.TranslatePoint(default,window)!.Value;var bounds=new PixelRect((int)p.X,(int)p.Y,(int)file.Bounds.Width,(int)file.Bounds.Height);
                    var data=new byte[bounds.Width*bounds.Height*4];var pin=GCHandle.Alloc(data,GCHandleType.Pinned);
                    try{image.CopyPixels(bounds,pin.AddrOfPinnedObject(),data.Length,bounds.Width*4);}finally{pin.Free();}
                    var ink=Enumerable.Range(0,data.Length/4).Count(i=>data[i*4]>150&&data[i*4+1]>150&&data[i*4+2]>150);
                    check(ink>20&&file.Opacity==1,"File heading retains visible text pixels during sheet expansion at "+ms+" ms");
                    frames.Add(new{ms,ink,overlay.Progress});
                    if(ms is 0 or 32 or 240)image.Save(Path.Combine(folder,"file-heading-"+ms+".png"),new PngBitmapEncoderOptions());
                }
                dialog.Close();clock.Pulse(321);clock.Pulse(561);Layout(window);
                check(file.Opacity==1&&overlay.VisibleBounds==overlay.SourceBounds,"Closing still returns to the visible heading");
            }
            await Until(()=>!dialog.IsVisible);File.WriteAllText(Path.Combine(folder,"heading-frames.json"),JsonSerializer.Serialize(frames));
            window.ShowPage("Settings");Layout(window);var dns=window.SettingsSections!.ButtonAt(4);
            dns.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(window);
            var cards=window.PageView.GetVisualDescendants().OfType<SettingsCards>().Single();Settle(cards);Layout(window);
            window.SettingsSections.Indicator.MoveTo(window.SettingsSections.Indicator.Destination,false);
            var scroll=cards.GetVisualAncestors().OfType<ScrollViewer>().First();
            var widths=new List<double>();
            foreach(var height in new[]{722d,1200d,722d})
            {
                window.Height=height;Layout(window);widths.Add(cards.Bounds.Width);
                check(Math.Abs(scroll.Viewport.Width-scroll.Bounds.Width)<.1,"Settings scrollbar overlays the existing margin at window height "+height);
            }
            check(widths.Max()-widths.Min()<.1,"DNS cards keep exactly the same width when a vertical scrollbar appears or disappears");
            check(cards.Margin.Right>=16,"Settings preserves space between controls and the overlaid scrollbar");
            Save(window,Path.Combine(folder,"settings-dns-default.png"));
            window.ShowPage("Providers");Layout(window);
            var list=(ScrollViewer)window.PageView;var rows=list.GetVisualDescendants().OfType<MovingRows>().Single();
            check(Math.Abs(list.Viewport.Width-list.Bounds.Width)<.1&&Math.Abs(rows.Bounds.Width+rows.Margin.Right-list.Bounds.Width)<.1,
                "Airport cards extend to the subscription-page edge while the bar uses their existing right margin");
            Save(window,Path.Combine(folder,"providers-default.png"));
            await window.OpenProviderDetailsAsync(providers[0].Id.ToString());Layout(window);
            var details=window.GetVisualDescendants().OfType<ProviderDetailsPage>().Single();
            var detailsScroll=(ScrollViewer)details.Child!;
            using(var clock=new UiAnimationClock(details,details.Offset)) {details.Opacity=0;details.Offset.X=18;clock.Pulse(0);details.Reveal();clock.Pulse(170);Layout(window);}
            var detailBody=((Border)detailsScroll.Content!).Child!;
            var detailLeft=detailBody.TranslatePoint(default,window.ContentSurface)!.Value.X;
            var detailRight=window.ContentSurface.Bounds.Width-detailLeft-detailBody.Bounds.Width;
            check(Math.Abs(detailLeft-detailRight)<.1,
                "Airport detail content has equal outer margins while the bar uses the right blank strip");
            check(Math.Abs(detailsScroll.Viewport.Width-detailsScroll.Bounds.Width)<.1,"Airport detail scrollbar does not reserve another content column");
            Save(window,Path.Combine(folder,"provider-detail-default.png"));
        }
        finally{window.Close();await Until(()=>!window.IsVisible);}
    }
}
