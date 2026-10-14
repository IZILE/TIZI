using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

// Dense rendered-pixel regression for the two-card brightness flash in the user's video.
// Uses synthetic cards and the production template, without viewing the desktop.
internal static class ProviderCardChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var directory = Path.Combine(output, "provider-selection-frames"); Directory.CreateDirectory(directory);
        var sites = new[] { new ProviderSite { Name = "Airport A", Url = "https://a.example/" }, new ProviderSite { Name = "Airport B", Url = "https://b.example/" } };
        var rows = new MovingRows { Margin = new(16) };
        rows.SetRows(sites.Select(p => new DisplayRow(p.Id.ToString(), p.Name, p.Url, Provider: p)), null);
        rows.For(sites[1].Id.ToString()).MinHeight = 220;
        var window = new Window { Width = 650, Height = 500, Background = new SolidColorBrush(Color.Parse("#1B1B1B")), Content = rows };
        window.Show(); Layout(window);
        if (window.Content is DialogSurface surface)
        { using var settle = new UiAnimationClock(surface, surface.Scale, surface.Offset); surface.Reveal(); settle.Pulse(0); settle.Pulse(200); }
        var cards = sites.Select(p => (ProviderCardButton)rows.For(p.Id.ToString())).ToArray();
        var contents = cards.Select(c => c.Content).ToArray();
        var presenters = cards.Select(c => c.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter" && ReferenceEquals(p.TemplatedParent, c))).ToArray();
        var faces=cards.Select(card=>card.GetVisualDescendants().OfType<Border>().First(border=>border.Name=="buttonFace")).ToArray();
        var pixels=new List<object>();var minimum=255;var maximum=0;var intermediateFrames=0;
        using var clock=new UiAnimationClock(faces);
        Layout(window);AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var initial=window.CaptureRenderedFrame()??throw new IOException("Missing airport baseline frame.");
        var baseline=Sample(initial,cards[0],window);
        for(var frame=0;frame<=180;frame++)
        {
            if(frame is 1 or 34 or 50 or 100)window.MouseMove(cards[0].TranslatePoint(new(300,75),window)!.Value);
            if(frame is 30 or 38 or 80)window.MouseMove(cards[1].TranslatePoint(new(300,75),window)!.Value);
            if(frame==145){rows.Select(sites[0].Id.ToString());window.MouseMove(new(2,2));}
            if(frame is >=110 and <=140)
            {
                var point=cards[0].TranslatePoint(new(300,75),window)!.Value;window.MouseMove(point);
                if(frame%2==0)window.MouseDown(point,MouseButton.Left);else window.MouseUp(point,MouseButton.Left);
            }
            clock.Pulse(frame*8);Layout(window);AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bitmap=window.CaptureRenderedFrame()??throw new IOException("Missing airport hover frame.");
            var values=cards.Select(card=>Sample(bitmap,card,window)).ToArray();
            minimum=Math.Min(minimum,values.Min());maximum=Math.Max(maximum,values.Max());
            if(values.Any(value=>value>baseline+1&&value<64))intermediateFrames++;
            if(values.Any(value=>value<baseline-1||value>66))throw new IOException($"Airport hover brightness overshoot at frame {frame}: {string.Join(',',values)}, baseline {baseline}.");
            if(presenters.Any(p=>p.Opacity!=1)||rows.Backplate.IsVisible)throw new IOException("Airport hover dimmed text or retained a click-selection backplate.");
            pixels.Add(new {frame,milliseconds=frame*8,gray=values});
            if(frame is 0 or 15 or 31 or 34 or 38 or 51 or 81 or 101 or 112 or 145 or 180)bitmap.Save(Path.Combine(directory,frame.ToString("D3")+".png"),new PngBitmapEncoderOptions());
            if(frame==180)check(values.All(value=>Math.Abs(value-baseline)<=1)&&cards[0].Classes.Contains("selected-card"),
                "Leaving an airport card removes its gray emphasis even when it remains the logical action target");
        }
        window.MouseUp(new(320,90),MouseButton.Left);
        check(minimum>=baseline-1&&maximum<=66&&maximum>=64&&intermediateFrames>20,
            "181 rendered airport hover frames fade through intermediate colors during rapid reversals without a brightness flash");
        check(cards.Select((card,i)=>ReferenceEquals(card.Content,contents[i])).All(value=>value)&&presenters.All(p=>p.Opacity==1),
            "Airport hover and repeated clicks retain original content trees and fully opaque text");
        File.WriteAllText(Path.Combine(output,"provider-selection-render-check.json"),JsonSerializer.Serialize(new {frames=pixels.Count,baseline,minimum,maximum,intermediateFrames,samples=pixels,
            scope="Production provider cards with pointer-driven emphasis; native desktop compositor timing not measured."},new JsonSerializerOptions {WriteIndented=true}));
        window.Close();
    }
    private static void Layout(Control window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static int Sample(Bitmap bitmap, Control card, Control window)
    {
        var point = card.TranslatePoint(new(card.Bounds.Width / 2, 75), window)!.Value; var scale = TopLevel.GetTopLevel(window)!.RenderScaling;
        var region = new PixelRect((int)(point.X * scale), (int)(point.Y * scale), 4, 4); var data = new byte[64]; var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { bitmap.CopyPixels(region, pin.AddrOfPinnedObject(), data.Length, 16); }
        finally { pin.Free(); }
        return (int)Math.Round(Enumerable.Range(0, 16).Average(i => (data[i * 4] + data[i * 4 + 1] + data[i * 4 + 2]) / 3d));
    }
}
