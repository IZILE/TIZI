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
    internal static bool Headless {get;private set;}
    internal static string? PendingImportLink { get; private set; }
    private static bool ImportLink(string value) => new[] { "clash://", "shadowrocket://", "clash-verge://", "tizi://", "sub://" }.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--activate-existing")) return SingleInstance.ActivateExisting(importLink: args.FirstOrDefault(ImportLink)) ? 0 : 1;
        if (args is ["--apply-update", var updateParent, var updateTicks, var updateFile, var updateHash, var updateTarget])
            return UpdateInstaller.Apply(int.Parse(updateParent), long.Parse(updateTicks), updateFile, updateHash, updateTarget);
        if (args is ["--desktop-qa", var desktopOutput]) { DesktopChecks.Prepare(desktopOutput); return Build(false).StartWithClassicDesktopLifetime(args); }
        if (args is ["--verify-first-frame", var frameOutput])
        {
            frameOutput = Path.GetFullPath(frameOutput); DesktopChecks.Prepare(frameOutput);
            Build(false).SetupWithoutStarting(); return NativePresentationChecks.Run(frameOutput);
        }
        if(args is ["--verify-startup-native",var startupOutput,var startupTheme])
        {
            startupOutput=Path.GetFullPath(startupOutput);DesktopChecks.Prepare(startupOutput);StartupAnimation.AllowVerification=true;
            File.WriteAllText(UserData.File("appearance.json"),System.Text.Json.JsonSerializer.Serialize(new AppearancePreferences(startupTheme)));
            Build(false).SetupWithoutStarting();return NativePresentationChecks.Run(startupOutput);
        }
        if (args is ["--verify-wireguard-sample", var qr1, var qr2, var userData, var searchVault, var importReport]) return ImportChecks.WireGuardSample(qr1, qr2, userData, searchVault, importReport);
        if (args is ["--import-ikuuu-cache", var ikuuuData, var ikuuuReport]) return ImportChecks.IKuuuCache(ikuuuData, ikuuuReport);
        if (args is ["--configure-search-stdin", var vault]) return DiscoveryChecks.Configure(vault);
        if (args is ["--verify-discovery", var searchData, var searchOutput]) return DiscoveryChecks.VerifyAsync(searchData, searchOutput).GetAwaiter().GetResult();
        if (args is ["--watchdog", var pid, var ticks, var child, var childTicks, var data, var runtime])
            return Watchdog(int.Parse(pid), long.Parse(ticks), int.Parse(child), long.Parse(childTicks), data, runtime);
        if (args is ["--quality-watchdog", var qualityPid, var qualityTicks, var qualityChild, var qualityChildTicks, var qualityData, var qualityRuntime])
            return Watchdog(int.Parse(qualityPid),long.Parse(qualityTicks),int.Parse(qualityChild),long.Parse(qualityChildTicks),qualityData,qualityRuntime,restoreProxy:false);
        if (args is ["--verify-ui", var output])
        {
            UserData.DirectoryPath = Path.Combine(Path.GetFullPath(output), "settings");
            Build(true).SetupWithoutStarting(); return UiChecks.Run(output);
        }
        if (args is ["--verify-brand-motion", var motionOutput])
        {
            UserData.DirectoryPath = Path.Combine(Path.GetFullPath(motionOutput), "settings");
            Build(true).SetupWithoutStarting(); return BrandMotionChecks.Run(motionOutput);
        }
        if(args is ["--verify-appearance",var appearanceOutput])
        {
            UserData.DirectoryPath=Path.Combine(Path.GetFullPath(appearanceOutput),"settings");Build(true).SetupWithoutStarting();return AppearanceChecks.Run(appearanceOutput);
        }
        if(args is ["--verify-node-hover",var hoverOutput])
        {
            UserData.DirectoryPath=Path.Combine(Path.GetFullPath(hoverOutput),"settings");Build(true).SetupWithoutStarting();return NodeHoverChecks.Run(hoverOutput);
        }
        if (args is ["--profile-log-navigation", var logOutput])
        {
            UserData.DirectoryPath = Path.Combine(Path.GetFullPath(logOutput), "settings");
            Build(true).SetupWithoutStarting(); return LogNavigationProbe.Run(logOutput);
        }
        if (args is ["--verify-connection", var connectionOutput])
        {
            UserData.DirectoryPath = Path.Combine(Path.GetFullPath(connectionOutput), "settings");
            Build(true).SetupWithoutStarting();
            var check = ConnectionChecks.RunAsync(connectionOutput);
            while (!check.IsCompleted) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            return check.GetAwaiter().GetResult();
        }
        if (args is ["--verify-interactions", var interactionOutput])
        {
            UserData.DirectoryPath = Path.Combine(Path.GetFullPath(interactionOutput), "settings"); Build(true).SetupWithoutStarting();
            var check = Revision0127Checks.Run(interactionOutput);
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
        if (args is ["--verify-subscription-imports", var samples, var sampleOutput])
        {
            UserData.DirectoryPath = Path.Combine(Path.GetFullPath(sampleOutput), "settings"); Build(true).SetupWithoutStarting();
            var check = SubscriptionImportChecks.RunAsync(Path.GetFullPath(samples), Path.GetFullPath(sampleOutput));
            while (!check.IsCompleted) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            return check.GetAwaiter().GetResult();
        }
        if (args is ["--preview-updates", var updateOutput])
        {
            UserData.DirectoryPath=Path.Combine(Path.GetFullPath(updateOutput),"settings");Build(true).SetupWithoutStarting();
            var check=UpdatePreviewChecks.RunAsync(updateOutput,(okay,message)=>{if(!okay)throw new InvalidOperationException(message);});
            while(!check.IsCompleted){Avalonia.Threading.Dispatcher.UIThread.RunJobs();Thread.Sleep(10);}check.GetAwaiter().GetResult();return 0;
        }
        if (args is ["--verify-profile-view", var sourceDirectory, var profileOutput])
        {
            UserData.DirectoryPath = Path.Combine(Path.GetFullPath(profileOutput), "settings"); Build(true).SetupWithoutStarting();
            var check = ProfileViewChecks.RunAsync(Path.GetFullPath(sourceDirectory), Path.GetFullPath(profileOutput));
            while (!check.IsCompleted) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            return check.GetAwaiter().GetResult();
        }
        PendingImportLink = args.FirstOrDefault(ImportLink);
        using var mutex = new Mutex(true, "Local\\TIZI-" + Environment.UserName, out var created);
        if (!created)
        {
            if (!args.Contains("--elevated"))
            {
                if (SingleInstance.ActivateExisting(importLink: PendingImportLink)) return 0;
                // A medium-integrity launcher cannot write to an elevated app's pipe.
                // Elevate only activation; do not wait for its lifetime mutex or start a second app.
                try
                {
                    var activation = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
                    activation.ArgumentList.Add("--activate-existing");
                    if (PendingImportLink is not null) activation.ArgumentList.Add(PendingImportLink);
                    using var existing = Process.Start(activation); return existing is null ? 1 : 0;
                }
                catch (System.ComponentModel.Win32Exception) { return 1; }
            }
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
        Headless=headless;
        var builder = AppBuilder.Configure<DesktopApp>();
        if (headless) builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, ShouldRenderOnUIThread = true }).UseSkia();
        else builder.UsePlatformDetect();
        return builder.With(UiFonts.Options()).LogToTrace();
    }
    private static int Watchdog(int parentId, long parentTicks, int engineId, long engineTicks, string data, string runtime,bool restoreProxy=true)
    {
        Process? parent = null, engine = null;
        try
        {
            try { parent = Process.GetProcessById(parentId); if (parent.StartTime.ToUniversalTime().Ticks != parentTicks) { parent.Dispose(); parent = null; } } catch (ArgumentException) { }
            try { engine = Process.GetProcessById(engineId); if (engine.StartTime.ToUniversalTime().Ticks != engineTicks || !string.Equals(engine.MainModule?.FileName, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "engine", "mihomo.exe")), StringComparison.OrdinalIgnoreCase)) { engine.Dispose(); engine = null; } } catch (ArgumentException) { }
            if (parent is not null && engine is not null) Task.WhenAny(parent.WaitForExitAsync(), engine.WaitForExitAsync()).GetAwaiter().GetResult();
            if(restoreProxy)new ProxyLease(new WindowsProxyRegistry(), new Vault(data)).Restore();
            if (engine is { HasExited: false }) { engine.Kill(true); engine.WaitForExit(5000); }
            // The caller passes only our session directory; never remove another path.
            var expectedRoot = Path.GetFullPath(Path.Combine(data, restoreProxy ? "run" : "quality-run")) + Path.DirectorySeparatorChar;
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
        Button.ClickEvent.AddClassHandler<Button>((button, e) => { if (Ui.OwnButtonEvent(button, e)) DialogOrigin.Remember(button); });
        MenuItem.ClickEvent.AddClassHandler<MenuItem>((item, e) => { if (ReferenceEquals(e.Source, item)) DialogOrigin.Remember(item); });
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (TrayMenuPlacement.IsTrayMenu(window)) { TrayMenuPlacement.Attach(window); return; }
            NativeCaption.Attach(window); DialogMotion.Open(window);
        });
        Resources["DefaultFontFamily"] = UiFonts.Interface;
        RequestedThemeVariant = ThemeVariant.Dark; _theme = new FluentTheme(); Styles.Add(_theme); MacControls.Apply(this); Appearance.Initialize(this);
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
