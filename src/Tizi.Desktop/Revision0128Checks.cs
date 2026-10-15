using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class Revision0128Checks
{
    internal static async Task RunAsync(string output,Action<bool,string> check)
    {
        static void Layout(Control control){control.UpdateLayout();Dispatcher.UIThread.RunJobs();}
        static async Task Until(Func<bool> done){using var stop=new CancellationTokenSource(8000);while(!done())await Task.Delay(10,stop.Token);}
        static void Click(Window top,Point point){top.MouseMove(point);top.MouseDown(point,MouseButton.Left);top.MouseUp(point,MouseButton.Left);Layout(top);}
        static byte[] Pixel(Bitmap image,Point p,double scale)
        {
            var data=new byte[4];var pin=GCHandle.Alloc(data,GCHandleType.Pinned);
            try{image.CopyPixels(new PixelRect((int)Math.Round(p.X*scale),(int)Math.Round(p.Y*scale),1,1),pin.AddrOfPinnedObject(),4,4);}finally{pin.Free();}
            return data.Take(3).ToArray();
        }
        static Bitmap Frame(Window top){Layout(top);AvaloniaHeadlessPlatform.ForceRenderTimerTick();return top.CaptureRenderedFrame()!;}
        var node=NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@node.example:443#Blue%20node%20fixture")[0];
        var state=new AppState {Nodes=[node],SelectedNode=node.Id,Rules=[new("DOMAIN","fixture.example","DIRECT")],
            AutoUpdate=false,AutoUpdateProviders=false,RestoreConnection=false,CloseToTray=false};
        new Vault(UserData.DirectoryPath).Write("settings.dat",state);
        var window=new MainWindow(false);window.Show();await Until(()=>window.Controller.Ready);
        try
        {
            foreach(var kind in new[]{"node","rule"})
            {
                window.Rail.Select(kind=="node"?"Nodes":"Rules");Layout(window);
                var rows=kind=="node"?window.SubscriptionView.Groups.Single().NodeRows:window.PageView.GetVisualDescendants().OfType<MovingRows>().Single();
                var id=kind=="node"?node.Id.ToString():"0";var source=rows.For(id);
                rows.Select(id);Layout(window);
                var grounds=rows.GetVisualDescendants().OfType<NodeCardGround>().ToArray();
                if(grounds.Length>0)using(var settle=new UiAnimationClock(grounds)){settle.Pulse(0);settle.Pulse(200);Layout(window);}
                using var before=Frame(window);
                before.Save(Path.Combine(output,kind+"-before-editor.png"),new PngBitmapEncoderOptions());
                var caption=window.GetVisualDescendants().OfType<Button>().Single(button=>Avalonia.Automation.AutomationProperties.GetName(button)==UiText.Get("Minimize"));
                var connection=window.GetVisualDescendants().OfType<ConnectionButton>().Single();
                var backgroundPoint=connection.TranslatePoint(new(14,22),window)!.Value;
                var oldBackground=Pixel(before,backgroundPoint,window.RenderScaling);
                // Exercise the same context-menu action used by the actual node/rule edit command.
                var menu=source.ContextMenu!;var item=menu.Items.OfType<MenuItem>().First(item=>UiText.Get(item.Header?.ToString()??"")==UiText.Get(kind=="node"?"Edit node":"Edit rule"));
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Until(()=>window.OpenDialogs.Count==1);Layout(window);
                var dialog=window.OpenDialogs.Single();var overlay=dialog.Overlay!;
                check(overlay.SourceBounds.Size==source.Bounds.Size,"Context-menu "+kind+" editing grows from its actual card instead of a vanished menu item");
                check(kind=="node"?overlay.SourceColor==Color.Parse("#2979F5"):overlay.SourceColor==Color.Parse("#414141"),
                    kind+" editor starts with its visible blue/gray card color rather than transparent-white RGB");
                check(!overlay.HasSourcePreview&&overlay.GetVisualChildren().Single() is Grid{Opacity:1},"The "+kind+" editor uses one live page without a card snapshot");
                check(source.IsEffectivelyEnabled&&caption.IsEffectivelyEnabled&&window.Rail.For("Overview").IsEffectivelyEnabled,
                    "Opening "+kind+" editing keeps main-window controls visually enabled while the overlay intercepts input");
                var folder=Path.Combine(output,kind+"-dialog-frames");Directory.CreateDirectory(folder);var samples=new List<object>();
                using(var clock=new UiAnimationClock(overlay))
                {
                    overlay.SetImmediately(0);overlay.Reveal();clock.Pulse(0);
                    for(var ms=0;ms<=280;ms+=8)
                    {
                        clock.Pulse(ms);Layout(window);using var frame=Frame(window);
                        var point=new Point(overlay.VisibleBounds.Left+6,overlay.VisibleBounds.Center.Y);
                        var pixel=Pixel(frame,point,window.RenderScaling);
                        check(kind=="node"?pixel.Min()<150:pixel.Max()<100,kind+" dialog has no white surface frame at "+ms+" ms");
                        if(ms==0)check(Pixel(frame,backgroundPoint,window.RenderScaling).SequenceEqual(oldBackground),
                            "The main connection-button appearance does not jump into a disabled tint when "+kind+" editing opens");
                        if(ms==0&&kind=="node")
                        {
                            check(overlay.SurfaceColor==Skin.Chrome&&overlay.SourceContentOpacity==0,
                                "The node editor begins with an opaque page surface and no ghosted source text");
                        }
                        if(ms==16)check(overlay.SourceContentOpacity==0&&overlay.SurfaceColor==Skin.Chrome&&overlay.GetVisualChildren().Single() is Grid{Opacity:1},
                            kind+" editor retains one opaque content layer while its rectangle moves");
                        if(ms==64)check(overlay.SurfaceColor==Skin.Chrome&&overlay.Progress<1,
                            kind+" editor is already gray while its shape is still expanding, without first forming a full blue sheet");
                        samples.Add(new {milliseconds=ms,geometry=overlay.Progress,visual=overlay.VisualProgress,color=overlay.SurfaceColor.ToString(),overlay.SourceContentOpacity,pixel});
                        frame.Save(Path.Combine(folder,ms.ToString("D3")+".png"),new PngBitmapEncoderOptions());
                    }
                    check(overlay.SurfaceColor==Skin.Chrome&&overlay.SourceContentOpacity==0,"The "+kind+" editor settles to gray with no stale source text");
                }
                File.WriteAllText(Path.Combine(output,kind+"-dialog-samples.json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions {WriteIndented=true}));
                for(var i=0;i<6;i++)
                {
                    window.KeyPress(Key.Tab);window.KeyRelease(Key.Tab);Layout(window);
                    var focus=window.FocusManager?.GetFocusedElement() as Control;
                    check(focus==dialog||focus?.GetVisualAncestors().Contains(dialog)==true,"Keyboard Tab remains in the active "+kind+" editor "+i);
                }
                var oldPage=window.CurrentPage;var railPoint=window.Rail.For("Overview").TranslatePoint(new(24,20),window)!.Value;
                Click(window,railPoint);await Until(()=>!dialog.IsVisible);Layout(window);
                check(window.CurrentPage==oldPage&&source.IsEffectivelyEnabled,"The outside click retracts "+kind+" editing without activating a background action");
                Click(window,railPoint);check(window.CurrentPage=="Overview","After "+kind+" editing retracts, the same real sidebar click works immediately");
            }

            // Deliberately use transparent white to catch the alpha/RGB interpolation failure.
            var trigger=Ui.Button("Transparent fixture",()=>{});trigger.Background=new SolidColorBrush(Color.FromArgb(0,255,255,255));
            var holder=new Border {Background=new SolidColorBrush(Color.Parse("#414141")),Width=240,Height=60,Child=trigger};
            var probe=new Window {Width=300,Height=160,Background=Skin.ChromeBrush,Content=holder};probe.Show();Layout(probe);
            check(DialogOrigin.SurfaceColor(trigger).R<100&&DialogOrigin.SurfaceColor(trigger).A==255,
                "A fully transparent white brush resolves to the visible surface before any morph interpolation");
            probe.Close();
        }
        finally {window.Close();await Until(()=>!window.IsVisible);}
    }
}
