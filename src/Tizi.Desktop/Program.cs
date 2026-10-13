using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class Program
{
    internal static string? PendingImportLink { get; private set; }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--desktop-qa", var desktopOutput]) { DesktopChecks.Prepare(desktopOutput); return Build(false).StartWithClassicDesktopLifetime(args); }
        if (args is ["--verify-wireguard-sample", var qr1, var qr2, var userData, var searchVault, var importReport]) return ImportChecks.WireGuardSample(qr1, qr2, userData, searchVault, importReport);
        if (args is ["--import-ikuuu-cache", var ikuuuData, var ikuuuReport]) return ImportChecks.IKuuuCache(ikuuuData, ikuuuReport);
        if (args is ["--configure-search-stdin", var vault]) return DiscoveryChecks.Configure(vault);
        if (args is ["--verify-discovery", var searchData, var searchOutput]) return DiscoveryChecks.VerifyAsync(searchData, searchOutput).GetAwaiter().GetResult();
        if (args is ["--watchdog", var pid, var ticks, var child, var childTicks, var data, var runtime])
            return Watchdog(int.Parse(pid), long.Parse(ticks), int.Parse(child), long.Parse(childTicks), data, runtime);
        if (args is ["--verify-ui", var output])
        {
            UserData.DirectoryPath = Path.Combine(Path.GetFullPath(output), "settings");
            Build(true).SetupWithoutStarting(); return UiChecks.Run(output);
        }
        if (args is ["--verify-connection", var connectionOutput])
        {
            UserData.DirectoryPath = Path.Combine(Path.GetFullPath(connectionOutput), "settings");
            Build(true).SetupWithoutStarting();
            var check = ConnectionChecks.RunAsync(connectionOutput);
            while (!check.IsCompleted) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            return check.GetAwaiter().GetResult();
        }
        if (args is ["--verify-workflows", var workflowOutput])
        {
            UserData.DirectoryPath = Path.Combine(Path.GetFullPath(workflowOutput), "settings"); Build(true).SetupWithoutStarting();
            var check = WorkflowChecks.RunAsync(workflowOutput);
            while (!check.IsCompleted) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            return check.GetAwaiter().GetResult();
        }
        if (args is ["--verify-profile-view", var sourceDirectory, var profileOutput])
        {
            UserData.DirectoryPath = Path.Combine(Path.GetFullPath(profileOutput), "settings"); Build(true).SetupWithoutStarting();
            var check = ProfileViewChecks.RunAsync(Path.GetFullPath(sourceDirectory), Path.GetFullPath(profileOutput));
            while (!check.IsCompleted) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            return check.GetAwaiter().GetResult();
        }
        PendingImportLink = args.FirstOrDefault(a => new[] { "clash://", "shadowrocket://", "clash-verge://", "tizi://" }.Any(p => a.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
        using var mutex = new Mutex(true, "Local\\TIZI-" + Environment.UserName, out var created);
        if (!created)
        {
            if (!args.Contains("--elevated")) return SingleInstance.ActivateExisting(importLink: PendingImportLink) ? 0 : 1;
            try { if (!mutex.WaitOne(TimeSpan.FromSeconds(15))) return 1; }
            catch (AbandonedMutexException) { }
        }
        try
        {
            if (!AppController.IsAdministrator && !args.Contains("--elevated"))
            {
                try
                {
                    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
                    start.ArgumentList.Add("--elevated"); if (args.Contains("--background")) start.ArgumentList.Add("--background");
                    if (PendingImportLink is not null) start.ArgumentList.Add(PendingImportLink);
                    using var elevated = Process.Start(start); if (elevated is not null) return 0;
                }
                catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223) { }
            }
            return Build(false).StartWithClassicDesktopLifetime(args);
        }
        finally { mutex.ReleaseMutex(); }
    }
    private static AppBuilder Build(bool headless)
    {
        var builder = AppBuilder.Configure<DesktopApp>();
        if (headless) builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, ShouldRenderOnUIThread = true }).UseSkia();
        else builder.UsePlatformDetect();
        return builder.With(UiFonts.Options()).LogToTrace();
    }
    private static int Watchdog(int parentId, long parentTicks, int engineId, long engineTicks, string data, string runtime)
    {
        Process? parent = null, engine = null;
        try
        {
            try { parent = Process.GetProcessById(parentId); if (parent.StartTime.ToUniversalTime().Ticks != parentTicks) { parent.Dispose(); parent = null; } } catch (ArgumentException) { }
            try { engine = Process.GetProcessById(engineId); if (engine.StartTime.ToUniversalTime().Ticks != engineTicks || !string.Equals(engine.MainModule?.FileName, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "engine", "mihomo.exe")), StringComparison.OrdinalIgnoreCase)) { engine.Dispose(); engine = null; } } catch (ArgumentException) { }
            if (parent is not null && engine is not null) Task.WhenAny(parent.WaitForExitAsync(), engine.WaitForExitAsync()).GetAwaiter().GetResult();
            new ProxyLease(new WindowsProxyRegistry(), new Vault(data)).Restore();
            if (engine is { HasExited: false }) { engine.Kill(true); engine.WaitForExit(5000); }
            // The caller passes only our session directory; never remove another path.
            var expectedRoot = Path.GetFullPath(Path.Combine(data, "run")) + Path.DirectorySeparatorChar;
            var absolute = Path.GetFullPath(runtime);
            if (absolute.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(absolute).StartsWith("session-", StringComparison.Ordinal) && Directory.Exists(absolute)) Directory.Delete(absolute, true);
            return 0;
        }
        catch (Exception) { return 1; } // Journal remains for recovery on next startup.
        finally { parent?.Dispose(); engine?.Dispose(); }
    }
}
public sealed class DesktopApp : Application
{
    private FluentTheme? _theme;
    private SingleInstance? _instance;
    public override void Initialize()
    {
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (TrayMenuPlacement.IsTrayMenu(window)) { TrayMenuPlacement.Attach(window); return; }
            NativeCaption.Attach(window); DialogMotion.Open(window);
        });
        Resources["DefaultFontFamily"] = UiFonts.Interface;
        RequestedThemeVariant = ThemeVariant.Dark; _theme = new FluentTheme(); Styles.Add(_theme); MacControls.Apply(this);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (_theme is not null) Skin.Apply(_theme);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var window = new MainWindow(); desktop.MainWindow = window;
            _instance = new SingleInstance(window.RestoreFromBackground, DesktopChecks.PipeName, window.ReceiveImportLink);
            desktop.Exit += (_, _) => _instance.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
