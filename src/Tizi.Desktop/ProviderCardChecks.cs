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
        var pixels = new List<object>(); var minimum = 255; var maximum = 0; var movingFrames = 0; var resizingFrames = 0;
        var secondTop = cards[1].Bounds.Y; var firstHeight = cards[0].Bounds.Height; var secondHeight = cards[1].Bounds.Height;
        using var clock = new UiAnimationClock(rows.Indicator, rows.Indicator.Position);
        Layout(window); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var initial = window.CaptureRenderedFrame() ?? throw new IOException("Missing airport baseline frame.");
        var baseline = Sample(initial, cards[0], window);
        for (var frame = 0; frame <= 180; frame++)
        {
            var before = rows.Indicator.Position.Y;
            if (frame is 1 or 34 or 50 or 100) rows.Select(sites[0].Id.ToString());
            if (frame is 30 or 38 or 80) rows.Select(sites[1].Id.ToString());
            if (frame is 30 or 34 or 38 or 50 or 80 or 100 && Math.Abs(rows.Indicator.Position.Y - before) > .01)
                throw new IOException("Retargeting airport selection jumped away from its displayed position.");
            if (frame == 145) rows.Select(null);
            if (frame is >= 110 and <= 140)
            {
                var point = cards[0].TranslatePoint(new(300, 75), window)!.Value; window.MouseMove(point);
                if (frame % 2 == 0) window.MouseDown(point, MouseButton.Left); else window.MouseUp(point, MouseButton.Left);
            }
            clock.Pulse(frame * 8); Layout(window); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bitmap = window.CaptureRenderedFrame() ?? throw new IOException("Missing airport selection frame.");
            var values = cards.Select(c => Sample(bitmap, c, window)).ToArray();
            minimum = Math.Min(minimum, values.Min()); maximum = Math.Max(maximum, values.Max());
            var y = rows.Indicator.Position.Y; var height = rows.Indicator.Height;
            if (y > .1 && y < secondTop - .1) movingFrames++;
            if (height > firstHeight + .1 && height < secondHeight - .1) resizingFrames++;
            if (values.Any(value => value < baseline - 1 || value > 66)) throw new IOException($"Airport selection brightness overshoot at frame {frame}: {string.Join(',', values)}, baseline {baseline}.");
            if (presenters.Any(p => p.Opacity != 1)) throw new IOException("Airport selection changed text opacity.");
            pixels.Add(new { frame, milliseconds = frame * 8, gray = values, y, height = double.IsFinite(height) ? height : (double?)null });
            if (frame is 0 or 15 or 31 or 34 or 38 or 51 or 81 or 101 or 112 or 145 or 180) bitmap.Save(Path.Combine(directory, frame.ToString("D3") + ".png"), new PngBitmapEncoderOptions());
        }
        window.MouseUp(new(320, 90), MouseButton.Left);
        check(minimum >= baseline - 1 && maximum <= 66 && maximum >= 64 && movingFrames > 20 && resizingFrames > 20,
            "181 rendered airport frames retain moving and resizing intermediate selection frames during rapid reversals, with no background brightness overshoot");
        check(cards.Select((c, i) => ReferenceEquals(c.Content, contents[i])).All(value => value) && presenters.All(p => p.Opacity == 1) && rows.Backplate.Background is ISolidColorBrush brush && brush.Color.R == 65,
            "Airport selection retains both content trees and fully opaque text above one fixed-color moving background");
        File.WriteAllText(Path.Combine(output, "provider-selection-render-check.json"), JsonSerializer.Serialize(new { frames = pixels.Count, baseline, minimum, maximum, movingFrames, resizingFrames, samples = pixels, scope = "Isolated Skia-rendered production cards using the same SelectionIndicator as other content pages; native desktop compositor not inspected." }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
    }
    private static void Layout(TopLevel window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static int Sample(Bitmap bitmap, Control card, TopLevel window)
    {
        var point = card.TranslatePoint(new(card.Bounds.Width / 2, 75), window)!.Value; var scale = window.RenderScaling;
        var region = new PixelRect((int)(point.X * scale), (int)(point.Y * scale), 4, 4); var data = new byte[64]; var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { bitmap.CopyPixels(region, pin.AddrOfPinnedObject(), data.Length, 16); }
        finally { pin.Free(); }
        return (int)Math.Round(Enumerable.Range(0, 16).Average(i => (data[i * 4] + data[i * 4 + 1] + data[i * 4 + 2]) / 3d));
    }
}
