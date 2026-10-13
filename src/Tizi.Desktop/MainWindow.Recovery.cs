using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private readonly Border _profileStatus = new() { IsVisible = false, Padding = new(16), Margin = new(0, 0, 0, 18), CornerRadius = new(12), Background = Skin.ChromeBrush };
    private string? _profileStatusKey;
    private void RefreshProfileStatus()
    {
        var key = _app.Ready ? "ready" : _app.LoadFailure ?? "loading";
        if (_profileStatusKey == key) return; _profileStatusKey = key;
        _profileStatus.IsVisible = !_app.Ready;
        if (_app.Ready) return;
        var label = Ui.Text(_app.LoadFailure ?? "Loading saved settings…", true); label.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var content = new StackPanel { Spacing = 12, Children = { label } };
        if (_app.LoadFailure is not null)
        {
            content.Children.Add(Ui.Horizontal(Ui.Button("Recover saved data", RecoverSavedData), Ui.Button("Open data folder", () => Process.Start(new ProcessStartInfo(EnsureDataFolder()) { UseShellExecute = true }))));
        }
        _profileStatus.Child = content;
    }
    private async void RecoverSavedData()
    {
        if (_preview || _app.Busy) return;
        if (_app.Connected) { ShowError("Disconnect before recovering saved data."); return; }
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = UiText.Get("Recover saved data"), AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("TIZI encrypted backup") { Patterns = ["*.dat"] }]
            });
            if (files.Count == 0) return;
            var path = files[0].TryGetLocalPath() ?? throw new IOException(UiText.Get("Choose a local TIZI backup file."));
            var backup = await Task.Run(() => new Vault(Path.GetDirectoryName(path)!).Read<AppState>(Path.GetFileName(path))) ?? throw new IOException(UiText.Get("Saved configuration is empty."));
            var merged = _app.Ready ? ProfileRecovery.MergeContents(_app.State, backup) : backup;
            var dialog = new Window { Title = UiText.Get("Recover saved data"), Width = 540, SizeToContent = SizeToContent.Height, Background = Skin.ChromeBrush, WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon, ShowInTaskbar = false };
            var summary = Ui.Value(UiText.Format("{0} subscriptions · {1} nodes · {2} rules", merged.Subscriptions.Count, merged.Nodes.Count, merged.Rules.Count)); summary.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            var note = Ui.Text(_app.Ready ? "Missing subscriptions, nodes and rules will be merged. Current preferences and existing items will be kept." : "The backup will restore saved settings. The unreadable original will be kept in encrypted backups.", true); note.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            var error = Ui.Value(""); error.IsVisible = false; error.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            var cancel = Ui.Button("Cancel", () => dialog.Close());
            var apply = Ui.Button("Recover", async () =>
            {
                dialog.IsEnabled = false;
                try { await _app.RecoverSavedDataAsync(backup); dialog.Close(); ShowImportResult(); ShowNotice("Saved data recovered. Current preferences have been kept."); }
                catch (Exception e) { error.IsVisible = true; error.Text = UiText.Get(e.Message); ShowError(e.Message); }
                finally { dialog.IsEnabled = true; }
            }, true);
            var actions = Ui.Horizontal(cancel, apply); actions.HorizontalAlignment = HorizontalAlignment.Right;
            dialog.Content = new StackPanel { Spacing = 18, Margin = new(24), Children = { summary, note, error, actions } };
            dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape && dialog.IsEnabled) { dialog.Close(); e.Handled = true; } };
            await dialog.ShowDialog(this);
        }
        catch (Exception e) { ShowError(e.Message); }
    }
}
