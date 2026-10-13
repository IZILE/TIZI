using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private Window? _updateWindow;
    private void ShowUpdates()
    {
        if (_updateWindow is { } opened) { opened.Activate(); return; }
        var dialog = _updateWindow = new Window { Title = UiText.Get("Version updates"), Width = 580, MinWidth = 460, MaxHeight = 720, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Skin.ChromeBrush, ShowInTaskbar = false, Icon = Icon };
        var status = Ui.Text("Check GitHub for a new version", true); status.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var notes = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 280, IsVisible = false, Classes = { "text-input" } };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6, IsVisible = false };
        var notice = new ClientNotification(); var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var check = Ui.Button("Check for updates", () => { }, true); var install = Ui.Button("Download and install", () => { }, true); install.IsVisible = false;
        var cancel = Ui.Button("Cancel download", () => { }); cancel.IsVisible = false;
        actions.Children.Add(Ui.Button("GitHub releases", () => OpenUrl("https://github.com/IZILE/TIZI/releases"))); actions.Children.Add(cancel); actions.Children.Add(check); actions.Children.Add(install);
        dialog.Content = new Grid { Children = { new StackPanel { Margin = new(24), Spacing = 18, Children = { Ui.Value("TIZI " + AppVersion, size: 21), status, notes, progress, actions } }, notice } };
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) }; var updates = new ReleaseUpdates(http); AppRelease? release = null; CancellationTokenSource? stop = null; var closed = false;
        check.Click += async (_, _) =>
        {
            if (stop is not null) return; using var attempt = new CancellationTokenSource(TimeSpan.FromSeconds(30)); stop = attempt; check.IsEnabled = false; UiText.Set(status, TextBlock.TextProperty, "Checking for updates…");
            try
            {
                release = await updates.LatestAsync(attempt.Token); install.IsVisible = release.Version > Version.Parse(AppVersion); notes.Text = release.Notes; notes.IsVisible = release.Notes.Length > 0;
                status.Text = install.IsVisible ? UiText.Format("Version {0} is available", release.Version.ToString()) : UiText.Get("You are using the latest version");
            }
            catch (OperationCanceledException) { UiText.Set(status, TextBlock.TextProperty, "Update check cancelled"); }
            catch (Exception) { UiText.Set(status, TextBlock.TextProperty, "Could not check for updates. Try again or open GitHub."); notice.Show(UiText.Get("Could not check for updates. Try again or open GitHub.")); }
            finally { stop = null; check.IsEnabled = true; if (closed) http.Dispose(); }
        };
        cancel.Click += (_, _) => stop?.Cancel();
        install.Click += async (_, _) =>
        {
            if (release is null || stop is not null || release.Version <= Version.Parse(AppVersion)) return;
            using var attempt = new CancellationTokenSource(TimeSpan.FromMinutes(10)); stop = attempt; install.IsEnabled = check.IsEnabled = false; cancel.IsVisible = progress.IsVisible = true;
            UiText.Set(status, TextBlock.TextProperty, "Downloading update…");
            try
            {
                var directory = Path.Combine(UserData.DirectoryPath, "updates", "session-" + Guid.NewGuid().ToString("N"));
                var file = await updates.DownloadAsync(release, directory, new Progress<double>(value => progress.Value = Math.Clamp(value, 0, 100)), attempt.Token);
                if (_settingsSaving) { notice.Show(UiText.Get("Saving settings…")); return; }
                if (SettingsDirty && !_discardSettingsOnExit) { if (!await ConfirmDiscardSettingsAsync()) return; _discardSettingsOnExit = true; }
                StartUpdateInstaller(file); dialog.Close(); Exit();
            }
            catch (OperationCanceledException) { UiText.Set(status, TextBlock.TextProperty, "Download cancelled"); }
            catch (Exception) { UiText.Set(status, TextBlock.TextProperty, "Update download or verification failed. Try again or open GitHub."); notice.Show(UiText.Get("Update download or verification failed. Try again or open GitHub.")); }
            finally { stop = null; install.IsEnabled = check.IsEnabled = true; cancel.IsVisible = progress.IsVisible = false; if (closed) http.Dispose(); }
        };
        dialog.Closing += (_, _) => stop?.Cancel(); dialog.Closed += (_, _) => { closed = true; _updateWindow = null; if (stop is null) http.Dispose(); };
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        _ = dialog.ShowDialog(this);
    }
    private static void StartUpdateInstaller(string installer)
    {
        using var parent = Process.GetCurrentProcess(); var app = Environment.ProcessPath ?? throw new IOException("Application path unavailable.");
        var helper = Path.Combine(Path.GetDirectoryName(installer)!, "TIZI-update-helper.exe"); File.Copy(app, helper, false);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--apply-update", parent.Id.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString(), installer, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installer))), Path.GetDirectoryName(app)! }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new IOException("Could not start the updater.");
    }
}
