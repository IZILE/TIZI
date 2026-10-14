using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Tizi.Core;

namespace Tizi.Desktop;

// Isolated CPU/layout probe, never reads the user's log file or starts an engine.
internal static class LogNavigationProbe
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        var measurements = new List<object>();
        static void Frame(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var frame = window.CaptureRenderedFrame(); }
        try
        {
            UiText.Language = "zh-CN";
            foreach (var count in new[] { 652, 5000 })
            {
                var state = new AppState { AutoUpdate = false, AutoUpdateProviders = false, RestoreConnection = false, Logs = new() { MaxEntries = 20000, Proxy = true, Dns = true } };
                var window = new MainWindow(true, state); window.Show(); Frame(window);
                var logs = (ActivityLog)typeof(MainWindow).GetField("_logs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                logs.Settings = state.Logs;
                for (var i = 0; i < count; i++) logs.Add((i % 3) switch
                {
                    0 => $"[TCP] 127.0.0.1:{18000 + i % 40000} --> api.fixture-{i}.example:443 match DomainSuffix(fixture.example) using TIZI[Example node] · " + new string('x', i % 20 == 0 ? 700 : 80),
                    1 => $"[DNS] dns query fixture-{i}.example A from 127.0.0.1 · synthetic response 192.0.2.1 · cached=False",
                    _ => $"[Application] Fixture record {i} · automatic status refresh completed; profile stays unchanged"
                });
                var timer = Stopwatch.StartNew(); window.ShowPage("Logs"); Frame(window); var enter = timer.Elapsed.TotalMilliseconds;
                var filter = (SegmentedChoice)typeof(MainWindow).GetField("_logFilter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var times = new List<double>();
                foreach (var index in new[] { 1, 2, 3, 0, 1, 2, 3, 0 })
                {
                    var button = filter.ButtonAt(index); var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    timer.Restart(); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Frame(window); times.Add(timer.Elapsed.TotalMilliseconds);
                }
                var eventTimes = new List<double>(); var layoutTimes = new List<double>(); var renderTimes = new List<double>();
                foreach (var index in new[] { 1, 2, 3, 0, 1, 2, 3, 0 })
                {
                    var button = filter.ButtonAt(index); timer.Restart(); button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent, button)); eventTimes.Add(timer.Elapsed.TotalMilliseconds);
                    timer.Restart(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); layoutTimes.Add(timer.Elapsed.TotalMilliseconds);
                    timer.Restart(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var rendered = window.CaptureRenderedFrame(); renderTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
                timer.Restart(); window.ShowPage("Overview"); Frame(window); window.ShowPage("Logs"); Frame(window); var revisit = timer.Elapsed.TotalMilliseconds;
                measurements.Add(new { records = count, view = window.PageView.GetType().Name, enterMs = enter, filterMs = times, filterHandlerMs = eventTimes, filterLayoutMs = layoutTimes, filterHeadlessRenderMs = renderTimes, revisitMs = revisit });
                if (count == 652) { window.Rail.Select("Logs"); Frame(window); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(output, "logs-synthetic.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
                window.Close();
            }
            File.WriteAllText(Path.Combine(output, "log-navigation.json"), JsonSerializer.Serialize(new { measurements, scope = "Synthetic data and headless CPU/layout/rendering; not native desktop frame timing." }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "log-navigation-failure.txt"), error.ToString()); return 1; }
    }
}
