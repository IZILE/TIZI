using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private CenteredDialog? _updateWindow;
    private AppRelease? _cachedRelease;
    private Task<AppRelease>? _releaseCheckTask;
    private bool _startupUpdateChecked;
    private Button? _versionUpdateButton;
    internal Button? VersionUpdateButton => _versionUpdateButton;
    internal Func<HttpClient>? UpdateHttpClientFactory { private get; set; }
    private bool HasNewVersion => _cachedRelease is { } release && release.Version > Version.Parse(AppVersion);
    private void RefreshUpdateButton()
    {
        if (_versionUpdateButton is not { Content: TextBlock text } button) return;
        UiText.Set(text, TextBlock.TextProperty, HasNewVersion ? "New version found" : "Check for updates");
        if (HasNewVersion)
        { if(!button.Classes.Contains("accent"))button.Classes.Add("accent"); button.Background = new SolidColorBrush(Color.Parse("#2979F5")); button.ClearValue(Button.BorderBrushProperty); text.Foreground = Skin.LabelBrush; }
        else
        { button.Classes.Remove("accent"); button.ClearValue(Button.BackgroundProperty); button.ClearValue(Button.BorderBrushProperty); text.Foreground = Skin.LabelBrush; }
    }
    internal async Task CheckStartupUpdatesAsync()
    {
        if (_startupUpdateChecked || _shutdown) return; _startupUpdateChecked = true;
        try { await LatestReleaseAsync(_app.Cancellation); } catch (Exception) { /* A startup check is silent; manual retry remains available. */ }
    }
    private async Task<AppRelease> LatestReleaseAsync(CancellationToken cancellation)
    {
        async Task<AppRelease> Fetch()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_app.Cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var http = UpdateHttpClientFactory?.Invoke() ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var release = await new ReleaseUpdates(http).LatestAsync(timeout.Token);
            if (!_shutdown) { _cachedRelease = release; RefreshUpdateButton(); }
            return release;
        }
        var task = _releaseCheckTask ??= Fetch();
        try { return await task.WaitAsync(cancellation); }
        finally { if (task.IsCompleted && ReferenceEquals(_releaseCheckTask, task)) _releaseCheckTask = null; }
    }
    internal void ShowUpdates()
    {
        if (_updateWindow is { } opened) { opened.Activate(); return; }
        var dialog = _updateWindow = new CenteredDialog { Title = UiText.Get("Version updates"), Width = 580, MinWidth = 460, MaxHeight = 720, SizeToContent = SizeToContent.Height, Background = Skin.ChromeBrush };
        var status = Ui.Text("Check GitHub for a new version", true); status.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var notes = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 280, IsVisible = false, Classes = { "text-input" } };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6, IsVisible = false };
        var notice = new ClientNotification(); var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var check = Ui.Button(HasNewVersion ? "Download new version" : "Check for updates", () => { }, true);
        var cancel = Ui.Button("Cancel download", () => { }); cancel.IsVisible = false;
        actions.Children.Add(cancel); actions.Children.Add(check);
        dialog.Content = new Grid { Children = { new StackPanel { Margin = new(24), Spacing = 18, Children = { Ui.Value("TIZI " + AppVersion, size: 21), status, notes, progress, actions } }, notice } };
        var http = UpdateHttpClientFactory?.Invoke() ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) }; var updates = new ReleaseUpdates(http); AppRelease? release = _cachedRelease; CancellationTokenSource? stop = null; var closed = false;
        void DisplayRelease()
        {
            if (release is null) return;
            var available = release.Version > Version.Parse(AppVersion); notes.Text = release.Notes; notes.IsVisible = release.Notes.Length > 0;
            status.Text = available ? UiText.Format("Version {0} is available", release.Version.ToString()) : UiText.Get("You are using the latest version");
            UiText.Set((TextBlock)check.Content!, TextBlock.TextProperty, available ? "Download new version" : "Check for updates");
        }
        DisplayRelease();
        check.Click += async (_, _) =>
        {
            if (release is not null && release.Version > Version.Parse(AppVersion)) { await Download(); return; }
            if (stop is not null) return; using var attempt = new CancellationTokenSource(TimeSpan.FromSeconds(30)); stop = attempt; check.IsEnabled = false; UiText.Set(status, TextBlock.TextProperty, "Checking for updates…");
            try
            {
                release = await LatestReleaseAsync(attempt.Token); DisplayRelease();
            }
            catch (OperationCanceledException) { UiText.Set(status, TextBlock.TextProperty, "Update check cancelled"); }
            catch (Exception) { UiText.Set(status, TextBlock.TextProperty, "Could not check for updates. Try again."); notice.Show(UiText.Get("Could not check for updates. Try again.")); }
            finally { stop = null; check.IsEnabled = true; if (closed) http.Dispose(); }
        };
        cancel.Click += (_, _) => stop?.Cancel();
        async Task Download()
        {
            if (release is null || stop is not null || release.Version <= Version.Parse(AppVersion)) return;
            using var attempt = new CancellationTokenSource(TimeSpan.FromMinutes(10)); stop = attempt; check.IsEnabled = false; cancel.IsVisible = progress.IsVisible = true;
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
            catch (Exception) { UiText.Set(status, TextBlock.TextProperty, "Update download or verification failed. Try again."); notice.Show(UiText.Get("Update download or verification failed. Try again.")); }
            finally { stop = null; check.IsEnabled = true; cancel.IsVisible = progress.IsVisible = false; if (closed) http.Dispose(); }
        }
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
