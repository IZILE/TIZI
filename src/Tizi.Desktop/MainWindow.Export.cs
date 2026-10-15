using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private async void ExportNodes()
    {
        if (_preview) return;
        await CreateExportDialog().ShowDialog(this);
    }
    internal CenteredDialog CreateExportDialog()
    {
        var dialog = new CenteredDialog { Title = UiText.Get("Export"), Width = 720, Height = 620, MinWidth = 520, Background = Skin.ChromeBrush };
        // Freeze the operation target and contents when opening; the connected node
        // and later background updates must not silently change an export's scope.
        var snapshot = _app.State;
        var selectedNode = Guid.TryParse(_chosen, out var nodeId) ? snapshot.Nodes.FirstOrDefault(n => n.Id == nodeId) : null;
        var selectedSubscription = snapshot.Subscriptions.FirstOrDefault(s => s.Id == SelectedSubscriptionId);
        var scopes = new List<(string Key, string Label)>();
        if (selectedSubscription is not null) scopes.Add(("subscription", "Selected subscription"));
        if (selectedNode is not null) scopes.Add(("node", "Selected node"));
        scopes.Add(("nodes", "All nodes")); scopes.Add(("subscriptions", "All subscriptions"));
        var scope = new StableChoice { ItemsSource = scopes.Select(s => UiText.Get(s.Label)).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var format = new StableChoice { ItemsSource = new[] { "JSON", "YAML", UiText.Get("Share link"), UiText.Get("QR code") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Tag = "export-output", Classes = { "text-input" } };
        var image = new Image { Stretch = Stretch.Uniform, MaxWidth = 360, MaxHeight = 360, HorizontalAlignment = HorizontalAlignment.Center };
        var error = Ui.Value(""); error.TextWrapping = TextWrapping.Wrap; error.Foreground = Skin.Error;
        var target = Ui.Value(""); target.TextWrapping = TextWrapping.Wrap; target.TextTrimming = TextTrimming.None; target.Tag = "export-target";
        var feedback = Ui.Value("", true, 13); feedback.IsVisible = false; feedback.TextWrapping = TextWrapping.Wrap; feedback.Tag = "export-feedback";
        Button? copy = null, save = null;
        var host = new ContentControl { Content = text }; byte[]? qr = null; string? output = null; string extension = ".json"; Bitmap? bitmap = null;
        void RefreshPreview()
        {
            try
            {
                error.Text = ""; feedback.IsVisible = false; qr = null; output = null; bitmap?.Dispose(); bitmap = null;
                var key = scopes[scope.SelectedIndex].Key;
                target.Text = key == "node" ? selectedNode!.Name : key == "subscription" ? selectedSubscription!.Name : UiText.Format("{0} items", key == "nodes" ? snapshot.Nodes.Count : snapshot.Subscriptions.Count);
                if (key is "subscription" or "subscriptions")
                {
                    var subscriptions = key == "subscription" ? new[] { selectedSubscription! } : snapshot.Subscriptions.ToArray();
                    if (subscriptions.Length == 0) throw new InvalidOperationException(UiText.Get("Select an item first."));
                    format.SelectedIndex = 0; format.IsEnabled = false; output = NodeExport.Subscriptions(subscriptions); extension = ".json";
                }
                else
                {
                    format.IsEnabled = true;
                    var nodes = key == "node" ? new[] { selectedNode! } : snapshot.Nodes.ToArray();
                    if (nodes.Length == 0) throw new InvalidOperationException(UiText.Get("Select a node"));
                    var rules = key == "node" ? Array.Empty<RoutingRule>() : snapshot.Rules.ToArray();
                    output = format.SelectedIndex switch { 0 => NodeExport.Json(nodes, rules), 1 => NodeExport.Yaml(nodes, rules), _ => string.Join('\n', nodes.Select(NodeExport.ShareLink)) };
                    extension = format.SelectedIndex switch { 0 => ".json", 1 => ".yaml", 2 => ".txt", _ => ".png" };
                    if (format.SelectedIndex == 3)
                    { if (nodes.Length != 1) throw new FormatException(UiText.Get("Choose one node for a QR code.")); qr = NodeExport.QrPng(output); using var stream = new MemoryStream(qr); bitmap = new Bitmap(stream); image.Source = bitmap; }
                }
                text.Text = output; host.Content = qr is null ? text : image;
            }
            catch (Exception e) { error.Text = UiText.Get(e.Message); output = null; qr = null; text.Text = ""; host.Content = text; }
            if (copy is not null) { copy.IsEnabled = output is not null; copy.Content = Ui.Text(qr is null ? "Copy" : "Copy link"); }
            if (save is not null) save.IsEnabled = output is not null;
        }
        copy = Ui.Button("Copy", async () =>
        {
            if (output is null || Clipboard is not { } clipboard) return;
            try { await clipboard.SetTextAsync(output); feedback.Text = UiText.Get("Copied."); feedback.IsVisible = true; }
            catch (Exception e) { error.Text = UiText.Get(e.Message); }
        });
        save = Ui.Button("Save file", async () =>
        {
            if (output is null) return;
            try
            {
                var file = await dialog.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = UiText.Get("Export"), SuggestedFileName = "TIZI" + extension, DefaultExtension = extension.TrimStart('.'), FileTypeChoices = [new FilePickerFileType(extension.TrimStart('.').ToUpperInvariant()) { Patterns = ["*" + extension] }] });
                if (file is null) return; await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await stream.WriteAsync(qr ?? new UTF8Encoding(false).GetBytes(output));
                feedback.Text = UiText.Format("Saved {0}", file.Name); feedback.IsVisible = true;
            }
            catch (Exception e) { error.Text = e.Message; }
        }, true);
        var close = Ui.Button("Close", () => dialog.Close()); var actions = Ui.Horizontal(close, copy, save); actions.HorizontalAlignment = HorizontalAlignment.Right;
        var choices = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), Children = { scope, format } }; Grid.SetColumn(format, 2);
        var note = Ui.Text("Exported files and QR codes contain node credentials.", true, 13); note.TextWrapping = TextWrapping.Wrap;
        var intro = new StackPanel { Spacing = 8, Children = { target, note } };
        var messages = new StackPanel { Spacing = 6, Children = { error, feedback } };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,12,Auto,16,*,12,Auto,12,Auto"), Margin = new(24), Children = { choices, intro, host, messages, actions } };
        Grid.SetRow(intro, 2); Grid.SetRow(host, 4); Grid.SetRow(messages, 6); Grid.SetRow(actions, 8); dialog.Content = content;
        var refreshing = false; void Changed() { if (refreshing) return; refreshing = true; try { RefreshPreview(); } finally { refreshing = false; } }
        scope.SelectionChanged += (_, _) => Changed(); format.SelectionChanged += (_, _) => Changed(); Changed();
        dialog.Closed += (_, _) => { image.Source = null; bitmap?.Dispose(); };
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        return dialog;
    }
}
