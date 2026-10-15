using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class Revision0132Checks
{
    internal static void Run(string output,Action<bool,string> check)
    {
        static void Layout(Control top){top.UpdateLayout();Dispatcher.UIThread.RunJobs();top.UpdateLayout();}
        var folder=Path.Combine(output,"revision-0132");Directory.CreateDirectory(folder);
        var window=new MainWindow(true,new AppState{AutoUpdate=false,AutoUpdateProviders=false,RestoreConnection=false,CloseToTray=false});window.Show();Layout(window);
        try
        {
            check(window.TransparencyLevelHint[0]==WindowTransparencyLevel.AcrylicBlur&&window.FrostedChromeVisible,
                "The main window requests the restrained frosted chrome material");
            check(window.TransparencyBackgroundFallback is ISolidColorBrush fallback&&fallback.Color==Skin.Chrome
                &&window.ContentSurface.Background is ISolidColorBrush background&&background.Color==Skin.Pasteboard&&background.Color.A==255,
                "The native fallback and functional page remain opaque while the outer chrome is frosted");
            check(window.Opacity==1,"No application-wide transparent fade is applied to the main window");
            var menu=window.GetVisualDescendants().OfType<Menu>().Single();var file=(MenuItem)menu.Items[0]!;
            file.IsSubMenuOpen=true;Layout(window);var popup=file.GetVisualDescendants().OfType<Popup>().Single();
            var reveal=(PopupRevealSurface)popup.Child!;
            check(reveal.GentleMenuMotion,"The actual File dropdown uses the dedicated gentle menu motion");
            var frames=new List<object>();var previous=0d;
            using(var clock=new UiAnimationClock(reveal))
            {
                reveal.SetImmediately(0);clock.Pulse(0);reveal.Progress=1;
                for(var ms=0;ms<=264;ms+=8)
                {
                    clock.Pulse(ms);Layout(window);var p=reveal.Progress;
                    check(p>=previous-.0001&&p is >=0 and <=1,"File dropdown opens monotonically without rebound at "+ms+" ms");
                    if(ms==16)check(p<.1,"File dropdown begins gently instead of opening most of its height in the first frame");
                    frames.Add(new{milliseconds=ms,progress=p});previous=p;
                    if(ms is 0 or 32 or 80 or 264){AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=window.CaptureRenderedFrame()!;frame.Save(Path.Combine(folder,"file-dropdown-"+ms+".png"),new PngBitmapEncoderOptions());}
                }
                check(reveal.Progress==1,"The File dropdown finishes its measured 260 millisecond expansion");
            }
            file.IsSubMenuOpen=false;
            DialogOrigin.Remember(file);
            var dialog=new CenteredDialog{Title="Fixture",Width=620,SizeToContent=SizeToContent.Height,Content=new Border{Height=200}};
            dialog.Show(window);Layout(window);var surface=dialog.Overlay!;
            var nodeSurface=new DialogOverlay(new Border{Height=200},"Fixture",default,620,null,double.PositiveInfinity,null,()=>{});
            check(surface.OpeningMilliseconds==300&&surface.ClosingMilliseconds==220&&nodeSurface.OpeningMilliseconds==280&&nodeSurface.ClosingMilliseconds==220,
                "Top-menu and editor sheets use continuous, gentle opening and closing motion");
            using(var clock=new UiAnimationClock(surface))
            {
                surface.SetImmediately(0);clock.Pulse(0);surface.Reveal();clock.Pulse(0);clock.Pulse(16);
                check(surface.Progress<.1,"Top-menu sheet geometry also starts gradually instead of jumping away from the heading");
                clock.Pulse(300);check(surface.Progress==1,"The top-menu sheet completes its 300 millisecond expansion");
                surface.Dismiss();clock.Pulse(301);clock.Pulse(421);
                check(surface.Progress>0&&surface.Retracting,"The 220 millisecond reverse motion is still present after 120 milliseconds");
                clock.Pulse(531);check(surface.Progress==0,"The top-menu reverse motion ends without overshoot");
            }
            dialog.ForceClose();
            File.WriteAllText(Path.Combine(folder,"dropdown-frames.json"),JsonSerializer.Serialize(frames));
        }
        finally{window.Close();}
    }
}
