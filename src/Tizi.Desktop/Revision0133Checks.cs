using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class Revision0133Checks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var folder = Path.Combine(output, "revision-0133"); Directory.CreateDirectory(folder);
        var node = NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@jp.example:443#Japan%20%7C%20Simulated").Single();
        var preview = new MainWindow(true, new AppState { Nodes = [node], SelectedNode = node.Id, AutoUpdate = false, AutoUpdateProviders = false, RestoreConnection = false, CloseToTray = false });
        preview.Show(); preview.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        preview.OverviewMonitor.Update(2000, 5000, 0, true, "220 ms", TimeSpan.FromMinutes(4));
        preview.OverviewMonitor.SetLatencyMonitoring(true);
        var chart = preview.OverviewMonitor.Chart; chart.Reset(); chart.PreviewElapsed = 1;
        var start = DateTimeOffset.Parse("2026-10-15T00:00:00Z");
        for (var i = 0; i <= 60; i++) chart.Add(2000, 5000, start.AddSeconds(i));
        chart.AddLatency(null, start.AddSeconds(1)); chart.AddLatency(null, start.AddSeconds(6));
        check(!chart.LatencySegments().Any(), "Failures before the first real result do not invent a zero or a held latency");
        chart.ClearLatencies();
        chart.AddLatency(400, start.AddSeconds(5)); chart.AddLatency(2015, start.AddSeconds(11));
        chart.AddLatency(null, start.AddSeconds(16)); chart.AddLatency(null, start.AddSeconds(21));
        chart.AddLatency(180, start.AddSeconds(27)); chart.AddLatency(220, start.AddSeconds(28));
        var segments = chart.LatencySegments().ToArray();
        check(segments.Length == 6, "Slow and failed samples retain one connected path through the live chart edge");
        check(segments[0].From == 400 && segments[0].To == 2015 && !segments[0].Estimated, "A result taking more than three seconds still connects to the preceding real result with a solid curve");
        check(segments[1].Estimated && segments[2].Estimated && segments[3].Estimated,
            "Failed measurements and their recovery use distinct missing-data connectors");
        check(segments[1].From == 2015 && segments[1].To == 2015 && segments[2].To == 2015,
            "Consecutive failures hold the last known height visually instead of dropping to zero");
        check(!segments[4].Estimated && segments[4].To == 220, "Ordinary samples resume a solid measured curve after recovery");
        check(segments.Zip(segments.Skip(1), (a, b) => a.ToTime == b.FromTime && a.To == b.From).All(x => x),
            "Every slow, failed and recovered segment shares exactly the same joint with the next segment");
        check(segments[^1].Estimated && segments[^1].ToTime == chart.DisplayTime && segments[^1].To == 220,
            "Pending samples remain connected to Now without fabricating a fresh measured value");
        chart.PreviewElapsed = 2;
        check(chart.LatencySegments().Last().ToTime == start.AddSeconds(61), "The pending connector advances continuously with the shared time axis");
        preview.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        chart.HoverAt(chart.Plot.Left + chart.Plot.Width * 15 / 60);
        check(chart.HoverAt(chart.Plot.Left + chart.Plot.Width * 15 / 60).Contains(UiText.Get("Timeout")),
            "Hovering a failed sample still reports Timeout rather than the connector's held height");
        chart.HoverAt(chart.Plot.Left + chart.Plot.Width * 26 / 60);
        check(chart.HoverAt(chart.Plot.Left + chart.Plot.Width * 26 / 60).Contains("180 ms"), "Recovered hover values use the actual measured latency");
        preview.MouseMove(chart.TranslatePoint(chart.Plot.Center, preview)!.Value); preview.MouseMove(default);
        preview.ConnectAction.Logo.PreviewTime = 1.1;
        using (var clock = new UiAnimationClock(preview.ConnectAction.Logo))
        { clock.Pulse(0); preview.ConnectAction.SetState(ConnectionPhase.Connected, false); clock.Pulse(1); clock.Pulse(1100); }
        preview.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using (var frame = preview.CaptureRenderedFrame()!) frame.Save(Path.Combine(folder, "poor-network-overview.png"), new PngBitmapEncoderOptions());
        File.WriteAllText(Path.Combine(folder, "segments.json"), JsonSerializer.Serialize(segments, new JsonSerializerOptions { WriteIndented = true }));
        chart.Reset(); check(!chart.LatencySegments().Any() && !chart.HasData, "Disconnect removes both measured and pending curve segments"); preview.Close();
        var monitor = new NetworkMonitor(); monitor.Update(0, 0, 0, true); monitor.SetLatencyMonitoring(true);
        monitor.Delay("2015 ms", "fixture"); monitor.Delay(UiText.Get("Timeout"), "fixture");
        check(monitor.LatencyValue == UiText.Get("Timeout") && monitor.Chart.LatencyCount == 2,
            "The production monitor keeps failures as null samples and preserves the visible timeout result");
        monitor.Update(null, null, 0, false);
        check(!monitor.Chart.HasData && !monitor.LatencyMonitoring, "The production monitor clears missing-data connectors on disconnect");
    }
}
