using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Tizi.Desktop;

internal static class VisualChecks
{
    // These render the isolated application; they do not inspect or automate the user's desktop.
    internal static void Run(MainWindow window, string output)
    {
        var timings = new List<double>(); var frames = 0;
        for (var cycle = 0; cycle < 5; cycle++)
        foreach (var page in NavigationRail.Pages.Reverse().Concat(NavigationRail.Pages))
        {
            var time = Stopwatch.StartNew(); window.ShowPage(page); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            timings.Add(time.Elapsed.TotalMilliseconds);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame() ?? throw new IOException("Navigation produced no rendered frame.");
            var position = window.PageHeading.TranslatePoint(default, window)!.Value;
            var scale = window.RenderScaling;
            var region = new PixelRect((int)(position.X * scale), (int)(position.Y * scale), Math.Max(1, (int)(window.PageHeading.Bounds.Width * scale)), Math.Max(1, (int)(window.PageHeading.Bounds.Height * scale)));
            var stride = region.Width * 4; var pixels = new byte[stride * region.Height]; var buffer = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try { frame.CopyPixels(region, buffer.AddrOfPinnedObject(), pixels.Length, stride); }
            finally { buffer.Free(); }
            var light = 0; var opaque = 0;
            for (var i = 0; i < pixels.Length; i += 4) { if (pixels[i] > 150 && pixels[i + 1] > 150 && pixels[i + 2] > 150) light++; if (pixels[i + 3] == 255) opaque++; }
            if (light < 12 || opaque < region.Width * region.Height * 0.98) throw new IOException("Blank or translucent page header during navigation: " + page);
            frames++;
        }
        var sorted = timings.Order().ToArray();
        File.WriteAllText(Path.Combine(output, "navigation-render-check.json"), JsonSerializer.Serialize(new
        {
            renderedFrames = frames, blankFrames = 0, ruleCount = window.Controller.State.Rules.Count,
            navigationCpuMs = new { median = sorted[sorted.Length / 2], p95 = sorted[(int)(sorted.Length * 0.95)], maximum = sorted[^1] },
            scope = "Headless CPU navigation and rendered content only; native compositor, taskbar and desktop frame timing remain unverified."
        }, new JsonSerializerOptions { WriteIndented = true }));
        LogoPreview(output);
        NotificationPreview(output);
        SidebarPreview(output);
    }

    private static void SidebarPreview(string output)
    {
        var folder = Path.Combine(output, "sidebar-frames"); Directory.CreateDirectory(folder);
        var rail = new NavigationRail();
        var window = new Window { Width = 260, Height = 420, Background = Skin.ChromeBrush, Content = rail }; window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        if (window.Content is DialogSurface surface)
        { using var settle = new UiAnimationClock(surface, surface.Scale, surface.Offset); surface.Reveal(); settle.Pulse(0); settle.Pulse(200); }
        var glyphs = NavigationRail.Pages.Select(rail.GlyphFor).ToArray();
        using var clock = new UiAnimationClock(glyphs.Cast<Avalonia.Animation.Animatable>().Concat([rail.Indicator, rail.Indicator.Position]).ToArray());
        for (var frame = 0; frame < 180; frame++)
        {
            if (frame % 24 == 12) rail.Select(NavigationRail.Pages[(frame / 24 + 1) % NavigationRail.Pages.Length]);
            clock.Pulse(frame * 20); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bitmap = window.CaptureRenderedFrame() ?? throw new IOException("Missing sidebar fill frame.");
            bitmap.Save(Path.Combine(folder, frame.ToString("D3") + ".png"), new PngBitmapEncoderOptions());
        }
        // A side-by-side geometry sheet uses the same production glyphs, never redrawn substitutes.
        var sheet = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 24, Margin = new(24) };
        foreach (var page in NavigationRail.Pages)
        {
            var outline = new NavigationGlyph(page); var solid = new NavigationGlyph(page); solid.Select(true, false);
            sheet.Children.Add(new StackPanel { Spacing = 24, Children = { outline, solid } });
        }
        var sheetWindow = new Window { Width = 390, Height = 120, Background = Skin.ChromeBrush, Content = sheet }; sheetWindow.Show(); sheetWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        if (sheetWindow.Content is DialogSurface sheetSurface)
        { using var settle = new UiAnimationClock(sheetSurface, sheetSurface.Scale, sheetSurface.Offset); sheetSurface.Reveal(); settle.Pulse(0); settle.Pulse(200); }
        using (var bitmap = new RenderTargetBitmap(new PixelSize(390, 120), new Vector(96, 96)))
        { bitmap.Render(sheetWindow); bitmap.Save(Path.Combine(output, "sidebar-glyphs.png"), new PngBitmapEncoderOptions()); }
        sheetWindow.Close(); window.Close();
    }

    private static void LogoPreview(string output)
    {
        var frames = Path.Combine(output, "logo-frames"); Directory.CreateDirectory(frames);
        var button = new ConnectionButton { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var window = new Window { Width = 240, Height = 100, Background = Skin.ChromeBrush, Content = button }; window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        if (window.Content is DialogSurface surface)
        { using var settle = new UiAnimationClock(surface, surface.Scale, surface.Offset); surface.Reveal(); settle.Pulse(0); settle.Pulse(200); }
        using var clock = new UiAnimationClock(button.Logo, button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "buttonFace"));
        for (var frame = 0; frame < 240; frame++)
        {
            var time = frame * 33;
            button.Logo.PreviewTime = time / 1000d;
            if (frame == 18) button.SetState(ConnectionPhase.Connecting, true);
            if (frame == 58) button.SetState(ConnectionPhase.Connected, false);
            if (frame == 96) button.SetState(ConnectionPhase.Failed, false, "Synthetic connection failure");
            if (frame == 140) button.SetState(ConnectionPhase.Connecting, true);
            if (frame == 171) button.SetState(ConnectionPhase.Connected, false);
            if (frame == 192) button.SetState(ConnectionPhase.Disconnected, false);
            clock.Pulse(time); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bitmap = window.CaptureRenderedFrame() ?? throw new IOException("Missing connection logo preview.");
            bitmap.Save(Path.Combine(frames, frame.ToString("D3") + ".png"), new PngBitmapEncoderOptions());
        }
        button.SetState(ConnectionPhase.Disconnected, false); window.Close();
    }

    private static void NotificationPreview(string output)
    {
        var folder = Path.Combine(output, "notification-frames"); Directory.CreateDirectory(folder);
        var notice = new ClientNotification();
        var window = new Window { Width = 640, Height = 220, Background = Skin.ChromeBrush,
            Content = new Grid { ClipToBounds = true, Children = { new Border { Background = Skin.PasteboardBrush, CornerRadius = new(18), Margin = new(20, 120, 20, 16) }, notice } } };
        window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        if (window.Content is DialogSurface surface) { using var settle = new UiAnimationClock(surface, surface.Scale, surface.Offset); surface.Reveal(); settle.Pulse(0); settle.Pulse(200); }
        using var clock = new UiAnimationClock(notice);
        for (var frame = 0; frame < 70; frame++)
        {
            if (frame == 5) notice.Show(UiText.Get("Proxy engine stopped unexpectedly."));
            if (frame == 45) notice.Dismiss();
            clock.Pulse(frame * 33); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bitmap = window.CaptureRenderedFrame() ?? throw new IOException("Missing notification preview.");
            bitmap.Save(Path.Combine(folder, frame.ToString("D3") + ".png"), new PngBitmapEncoderOptions());
        }
        notice.Dismiss(); window.Close();
    }
}
