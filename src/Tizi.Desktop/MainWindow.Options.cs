using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Media;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private void ShowSubscriptionUrl(Guid id)
    {
        if (_preview || _app.State.Subscriptions.FirstOrDefault(s => s.Id == id) is not { } subscription) return;
        var dialog = new Window { Title = UiText.Get("Subscription URL"), Width = 620, MinWidth = 460, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Skin.ChromeBrush, ShowInTaskbar = false, Icon = Icon };
        var address = new TextBox { Text = subscription.Url, IsReadOnly = true, PasswordChar = '●', Classes = { "text-input" } };
        var reveal = Option("Show URL", false); reveal.IsCheckedChanged += (_, _) => address.PasswordChar = reveal.IsChecked == true ? '\0' : '●';
        var copied = Ui.Value(""); copied.Foreground = Ui.DataAccent;
        var copy = Ui.Button("Copy URL", () => { }); copy.Click += async (_, _) => { if (Clipboard is { } clipboard) { await clipboard.SetTextAsync(subscription.Url); copied.Text = UiText.Get("Copied"); } };
        var actions = Ui.Horizontal(copied, copy, Ui.Button("Close", () => dialog.Close())); actions.HorizontalAlignment = HorizontalAlignment.Right;
        dialog.Content = new StackPanel { Margin = new(24), Spacing = 16, Children = { Ui.Value(subscription.Name, size: 18), address, reveal, actions } };
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        _ = dialog.ShowDialog(this);
    }
    private LogCategory? SelectedLogCategory => _logFilter.SelectedIndex switch { 1 => LogCategory.Proxy, 2 => LogCategory.Dns, 3 => LogCategory.Application, _ => null };
    private string VisibleLogText() => string.Join('\n', _logs.Entries.Where(e => SelectedLogCategory is null || e.Category == SelectedLogCategory).Select(e => $"{e.Time.ToLocalTime():MM-dd HH:mm:ss}  [{UiText.Get(e.Category == LogCategory.Dns ? "DNS" : e.Category.ToString())}] {e.Message}"));
    private async Task LoadActivityLogAsync()
    {
        _logs.Settings = _app.State.Logs;
        if (!_app.State.Logs.Persist) return;
        try { if (await Task.Run(() => new Vault(UserData.DirectoryPath).Read<ActivityEntry[]>("activity-log.dat")) is { } entries) _logs.Load(entries); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException) { ShowError(UiText.Get("Saved logs could not be opened.")); }
    }
    private async Task PersistActivityLogAsync()
    {
        _logs.Settings = _app.State.Logs; _logs.Trim(DateTimeOffset.Now);
        if (!_app.Ready || !_app.State.Logs.Persist) return;
        var entries = _logs.Entries.ToArray();
        try { await Task.Run(() => new Vault(UserData.DirectoryPath).Write("activity-log.dat", entries)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ShowError(UiText.Get("Logs could not be saved.")); }
    }
    private static CheckBox Option(string text, bool value) => new() { Content = new TextBlock { [!TextBlock.TextProperty] = UiText.Bind(text), TextWrapping = TextWrapping.Wrap }, IsChecked = value };
    private static NumericUpDown Number(int value, int min, int max) => new() { Value = value, Minimum = min, Maximum = max, FormatString = "0" };
    private static StackPanel Labeled(string label, Control input)
    {
        var caption = Ui.Text(label, true);
        Avalonia.Automation.AutomationProperties.SetLabeledBy(input, caption);
        return new() { Spacing = 8, Children = { caption, input } };
    }
    private async void CopyVisibleLog()
    {
        try { if (Clipboard is { } clipboard) { await clipboard.SetTextAsync(VisibleLogText()); ShowNotice("Copied."); } }
        catch (Exception error) { ShowError(error.Message); }
    }
    private void OptionsDialog(string title, StackPanel content, Func<Task> save)
    {
        if (_preview) return;
        var dialog = new Window { Title = UiText.Get(title), Width = 540, MinWidth = 440, MaxHeight = 760, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Skin.ChromeBrush, ShowInTaskbar = false, Icon = Icon };
        var error = Ui.Value(""); error.Foreground = new SolidColorBrush(Color.Parse("#FFB5A5")); error.TextWrapping = TextWrapping.Wrap; error.IsVisible = false;
        var accept = Ui.Button("Save", () => { }, true);
        var saving = false; var allowClose = false;
        var cancel = Ui.Button("Cancel", () => dialog.Close());
        accept.Click += async (_, _) =>
        {
            if (saving) return; saving = true; accept.IsEnabled = cancel.IsEnabled = false;
            UiText.Set((TextBlock)accept.Content!, TextBlock.TextProperty, "Applying changes…");
            try { await _app.ExecuteAsync(save); allowClose = true; dialog.Close(); }
            catch (Exception failure) { error.Text = UiText.Get(failure.Message); error.IsVisible = true; ShowError(failure.Message); }
            finally { saving = false; accept.IsEnabled = cancel.IsEnabled = true; UiText.Set((TextBlock)accept.Content!, TextBlock.TextProperty, "Save"); }
        };
        dialog.Closing += (_, e) => { if (saving && !allowClose) e.Cancel = true; };
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        var actions = Ui.Horizontal(cancel, accept); actions.HorizontalAlignment = HorizontalAlignment.Right;
        content.Children.Add(error); content.Children.Add(actions); content.Margin = new(24); content.Spacing = 18;
        dialog.Content = Ui.Scroll(content); _ = dialog.ShowDialog(this);
    }
    private void SubscriptionSettings()
    {
        var enabled = Option("Automatically update subscriptions", _app.State.AutoUpdate); var minutes = Number(_app.State.UpdateMinutes, 5, 1440);
        OptionsDialog("All subscription update settings", new StackPanel { Children = { enabled, Labeled("Update interval (min)", minutes) } }, () => _app.ApplyAsync(_app.State with { AutoUpdate = enabled.IsChecked == true, UpdateMinutes = (int)(minutes.Value ?? 30) }));
    }
    private void EditSubscription(Guid id)
    {
        if (_app.State.Subscriptions.FirstOrDefault(s => s.Id == id) is not { } original) return;
        Dialog("Edit subscription", [new("name", "Name", original.Name), new("url", "Subscription URL", original.Url)], async values =>
        {
            Required(values["name"]);
            var url = ImportDocument.RemoteUrl(values["url"]) ?? throw new FormatException("Enter a valid HTTP or HTTPS address without a fragment or embedded login.");
            var current = _app.State.Subscriptions.FirstOrDefault(s => s.Id == id);
            if (current is null || current.Name != original.Name || current.Url != original.Url) throw new InvalidOperationException(UiText.Get("This subscription has changed. Reopen it to edit."));
            await _app.ApplyAsync(_app.State with { Subscriptions = _app.State.Subscriptions.Select(s => s.Id == id ? s with { Name = values["name"], Url = url } : s).ToList() });
        });
    }
    private void DirectorySettings()
    {
        var enabled = Option("Automatically check website status", _app.State.AutoUpdateProviders); var minutes = Number(_app.State.ProviderUpdateMinutes, 5, 1440);
        OptionsDialog("Directory settings", new StackPanel { Children = { enabled, Labeled("Update interval (min)", minutes) } }, () => _app.ApplyAsync(_app.State with { AutoUpdateProviders = enabled.IsChecked == true, ProviderUpdateMinutes = (int)(minutes.Value ?? 30) }));
    }
    private void LogOptions()
    {
        var settings = _app.State.Logs;
        var proxy = Option("Record proxy connections", settings.Proxy); var dns = Option("Record DNS queries", settings.Dns); var persist = Option("Save logs between sessions", settings.Persist); var delete = Option("Automatically delete old logs", settings.AutoDelete);
        var level = new StableChoice { ItemsSource = new[] { "silent", "error", "warning", "info", "debug" }, SelectedItem = settings.Level };
        var days = Number(settings.RetentionDays, 1, 365); var maximum = Number(settings.MaxEntries, 100, 20000);
        OptionsDialog("Log settings", new StackPanel { Children = { proxy, dns, persist, delete, Labeled("Retention (days)", days), Labeled("Maximum entries", maximum), Labeled("Engine log level", level) } }, async () =>
        {
            var next = settings with { Proxy = proxy.IsChecked == true, Dns = dns.IsChecked == true, Persist = persist.IsChecked == true, AutoDelete = delete.IsChecked == true, RetentionDays = (int)(days.Value ?? 7), MaxEntries = (int)(maximum.Value ?? 5000), Level = level.SelectedItem?.ToString() ?? "info" };
            await _app.ApplyAsync(_app.State with { Logs = next }, reload: _app.Connected); _logs.Settings = next; _logs.Trim(DateTimeOffset.Now); RefreshLog(true); await PersistActivityLogAsync();
        });
    }
}
