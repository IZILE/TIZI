using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Tizi.Core;

namespace Tizi.Desktop;

// Exercises the normal Windows renderer with an isolated vault, without a node connection.
internal static class NativePresentationChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        new Vault(UserData.DirectoryPath).Write("settings.dat", new AppState { AutoUpdate = false, AutoUpdateProviders = false, RestoreConnection = false, CloseToTray = false });
        Exception? failure = null;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Dispatcher.UIThread.Post(async () =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow();
                var opened = 0; var loadedAtOpening = false; var cloakedAtOpening = 0;
                window.Opened += (_, _) =>
                {
                    if (opened++ > 0) return;
                    loadedAtOpening = window.Controller.Ready;
                    DwmGetWindowAttribute(window.TryGetPlatformHandle()!.Handle, 14, out cloakedAtOpening, sizeof(int));
                };
                var watch = Stopwatch.StartNew(); window.Show();
                var frame = window.InitialPresentation!;
                DwmGetWindowAttribute(window.TryGetPlatformHandle()!.Handle, 14, out var initialCloak, sizeof(int));
                Check(frame.WasCloaked && initialCloak == 1 && cloakedAtOpening == 1,
                    "First native Show keeps the unfinished surface app-cloaked, including during Opened");
                await frame.Completed.Task.WaitAsync(TimeSpan.FromSeconds(8));
                var firstShowMs = watch.Elapsed.TotalMilliseconds;
                DwmGetWindowAttribute(window.TryGetPlatformHandle()!.Handle, 14, out var finalCloak, sizeof(int));
                DwmGetWindowAttribute(window.TryGetPlatformHandle()!.Handle, 3, out var disabledTransitions, sizeof(int));
                Check(frame.FrameRendered && !frame.TimedOut, "The actual native render target drew before reveal without a fail-open timeout");
                Check(!frame.IsCloaked && finalCloak == 0 && disabledTransitions == 0,
                    "Both native visibility and ordinary DWM transitions are restored after first presentation");
                Check(opened == 1 && !loadedAtOpening, "The original asynchronous post-opening profile load order is preserved");
                window.Hide(); window.Show();
                Check(ReferenceEquals(frame, window.InitialPresentation), "Tray restore reuses the first presentation instead of re-cloaking");
                DwmGetWindowAttribute(window.TryGetPlatformHandle()!.Handle, 14, out var restoredCloak, sizeof(int));
                Check(restoredCloak == 0, "The restored native window stays revealed");
                while (!window.Controller.Ready) await Task.Delay(10);
                Check(!window.Controller.Connected && !window.Controller.Engine.IsRunning, "Native checks never connect a node or start the proxy engine");
                window.Close(); while (window.IsVisible) await Task.Delay(10);
                File.WriteAllText(Path.Combine(output, "native-first-frame.json"), JsonSerializer.Serialize(new
                {
                    passed = checks.Count, checks, backend = "normal Windows platform", firstShowMs,
                    initialCloak, cloakedAtOpening, loadedAtOpening, finalCloak, disabledTransitions,
                    targetActuallyDrew = frame.FrameRendered, timeout = frame.TimedOut, networkConnection = false,
                    limitation = "Native draw and DWM visibility ordering verified; no high-speed desktop recording.", binary = VerificationStamp.Current()
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error) { failure = error; File.WriteAllText(Path.Combine(output, "native-failure.txt"), error.ToString()); }
            finally
            {
                if (window is { IsVisible: true }) window.Close();
                Dispatcher.UIThread.BeginInvokeShutdown(DispatcherPriority.Send); lifetime.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(lifetime.Token);
        return failure is null && checks.Count == 7 ? 0 : 1;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}
