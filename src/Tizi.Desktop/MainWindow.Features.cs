using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private Guid? _probedNode;
    internal void ReceiveImportLink(string link)
    {
        RestoreFromBackground();
        if (_editor is not null) { ShowError("Close the current editor before importing a link."); return; }
        try
        {
            var url = ImportDocument.RemoteUrl(link) ?? throw new FormatException("This command is not a subscription import.");
            // The browser can fill an import dialog, but cannot connect or silently replace a profile.
            OpenSubscriptionDialog(url);
        }
        catch (Exception e) when (e is FormatException or UriFormatException) { ShowError(e.Message); }
    }
    private void Responsive()
    {
        if (_clientSurface is null) return;
        _clientSurface.Margin = OffScreenMargin;
        var width = (ClientSize.Width > 0 ? ClientSize.Width : Width) - OffScreenMargin.Left - OffScreenMargin.Right;
        _rail.Compact(width < 1040);
        _mode.Compact(width < 1040);
        // Use the content panel's two edges as the common reference, including its inspector.
        _inspectorBorder.IsVisible = width >= 1440 && _chosen is not null && _page == "Connections";
        var inset = Math.Max(28, 24 + _brand.DesiredSize.Width + 28 - _rail.Width);
        var left = _rail.Width + inset;
        var right = 18 + inset + (_inspectorBorder.IsVisible ? _inspectorBorder.Width : 0);
        _toolbar.Margin = new Thickness(left, 0, right, 24);
        var available = width - left - right;
        var horizontalWidth = _mode.DesiredSize.Width + _capture.DesiredSize.Width + 8;
        var stack = available - 2 * Math.Max(horizontalWidth, _connect.Width) - 32 < 280;
        _toolbarModes.Orientation = stack ? Orientation.Vertical : Orientation.Horizontal;
        _toolbarModes.Spacing = 8;
        var modesWidth = stack ? Math.Max(_mode.DesiredSize.Width, _capture.DesiredSize.Width) : horizontalWidth;
        _nodeChoice.MaxWidth = Math.Clamp(available - 2 * Math.Max(modesWidth, _connect.Width) - 32, 180, 340);
        _nodeChoice.MinWidth = Math.Min(280, _nodeChoice.MaxWidth);
        _notification.MaxWidth = Math.Min(520, width - 48);
    }
    private void Exit() { _forceExit = true; Close(); }
    private void CreateTray()
    {
        if (_preview || _tray is not null) return;
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Tizi.Desktop.AppIcon.ico");
        var open = new NativeMenuItem(UiText.Get("Open TIZI")); open.Click += (_, _) => RestoreFromBackground();
        _trayConnection.Header = UiText.Get(_app.Connected ? "Disconnect" : "Connect"); _trayConnection.Click += (_, _) => Connect();
        var exit = new NativeMenuItem(UiText.Get("Exit")); exit.Click += (_, _) => Exit();
        _tray = new TrayIcon { Icon = stream is null ? Icon : new WindowIcon(stream), ToolTipText = "TIZI", Menu = new NativeMenu { Items = { open, _trayConnection, new NativeMenuItemSeparator(), exit } } };
        _tray.Clicked += (_, _) => RestoreFromBackground(); _tray.IsVisible = true;
        if (Application.Current is { } application) TrayIcon.SetIcons(application, new TrayIcons { _tray });
    }
    private void DisposeTray()
    {
        if (Application.Current is { } application) TrayIcon.SetIcons(application, null);
        _tray?.Dispose(); _tray = null;
    }
    private Guid? SelectedSubscriptionId => _chosen?.StartsWith("subscription:", StringComparison.Ordinal) == true && Guid.TryParse(_chosen[13..], out var id) ? id : null;
    private static string UsageText(SubscriptionUsage? usage) => UiFormat.Usage(usage);
    private async Task ProbeSelectedAsync(bool explicitRequest = false)
    {
        if (_preview || _shutdown || _probing) return;
        var options = _app.State.Latency;
        _monitor.Chart.LatencyGapSeconds = Math.Max(3, options.CurveIntervalSeconds * 2.5);
        if (!_app.Connected && !explicitRequest) return;
        if (DesktopChecks.Active) { if (explicitRequest) ShowError("Desktop verification mode: node connections are disabled."); return; }
        var node = _app.State.Nodes.FirstOrDefault(n => n.Id == _app.State.SelectedNode); if (node is null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_app.Cancellation); _probeStop = cancellation;
        var state = _app.State; var connected = _app.Connected;
        _probing = true; _lastProbe = DateTimeOffset.Now;
        if (_probedNode != node.Id) { _probedNode = node.Id; _probeHistory.Clear(); }
        int? delay = null;
        try
        {
            await using var probe = options.Method == "HTTP" && !connected
                ? await NodeLatencyProbe.StartAsync(state with { Nodes = [node], SelectedNode = node.Id }, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "engine", "mihomo.exe")), Path.Combine(UserData.DirectoryPath, "latency"), cancellation.Token) : null;
            delay = options.Method == "HTTP" ? probe is null ? await _app.Engine.DelayAsync(node, state.DelayUrl, cancellation.Token, options.TimeoutMs) : await probe.MeasureAsync(node, state.DelayUrl, options.TimeoutMs, cancellation.Token) : await NodeTransport.MeasureAsync(node, options, cancellation.Token);
        }
        catch (NotSupportedException error) { if (explicitRequest) ShowError(UiText.Get(error.Message)); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or InvalidOperationException or ObjectDisposedException or System.Net.Sockets.SocketException or TimeoutException) { }
        catch (Exception error) { if (explicitRequest && !_shutdown) ShowError(error.Message); }
        finally { _probeStop = null; _probing = false; }
        if (connected && !_app.Connected || _app.State.SelectedNode != node.Id || _shutdown || cancellation.IsCancellationRequested) return;
        _probeHistory.Enqueue(delay); while (_probeHistory.Count > 8) _probeHistory.Dequeue();
        var valid = _probeHistory.Where(d => d.HasValue).Select(d => d!.Value).ToArray(); var jitter = valid.Length < 2 ? "—" : $"{valid.Zip(valid.Skip(1), (a, b) => Math.Abs(a - b)).Average():0} ms";
        var value = delay.HasValue ? delay + " ms" : UiText.Get("Timeout"); _latencies[node.Id] = value;
        _monitor.Delay(value, options.Method + " · " + UiText.Format("jitter {0} · failed {1}/{2} · {3}", jitter, _probeHistory.Count(d => !d.HasValue), _probeHistory.Count, DateTime.Now.ToString("HH:mm:ss")));
        if (_page == "Nodes") _subscriptionTree.UpdateLatencies(_latencies);
    }
    private static string EnsureDataFolder() { Directory.CreateDirectory(UserData.DirectoryPath); return UserData.DirectoryPath; }
    private async Task ImportContentAsync(string text, EditorSession? session = null)
    {
        var token = session?.Token ?? _app.Cancellation;
        Action? commit = session is null ? null : session.BeginCommit;
        if (ImportDocument.RemoteUrl(text) is { } url) { await _app.AddSubscriptionAsync(new Uri(url).Host, url, document => ReviewImportAsync(document, token), token, commit); if (_editor is null) ShowImportResult(); return; }
        var document = await _app.ParseImportAsync(text, token);
        if (document.Warnings.Count > 0 && !await ReviewImportAsync(document, token)) throw new ImportReviewCancelledException();
        await _app.ApplyImportAsync(document, cancellation: token, beforeCommit: commit);
        if (_editor is null) ShowImportResult();
    }
    private async Task ImportContentsAsync(IReadOnlyList<string> texts)
    {
        // Decode everything before committing, so a QR containing a command cannot partially alter settings.
        var documents = new List<ImportDocument>();
        foreach (var text in texts)
        {
            if (ImportDocument.RemoteUrl(text) is { } url) documents.Add(new([], [], [new Subscription { Name = new Uri(url).Host, Url = url }], [], "Subscription URL"));
            else documents.Add(await _app.ParseImportAsync(text));
        }
        var all = new ImportDocument(documents.SelectMany(d => d.Nodes).ToList(), documents.SelectMany(d => d.Rules).ToList(), documents.SelectMany(d => d.Subscriptions).ToList(), documents.SelectMany(d => d.Warnings).ToList(), "QR / clipboard");
        if (all.Warnings.Count > 0 && !await ReviewImportAsync(all)) throw new ImportReviewCancelledException();
        await _app.ApplyImportAsync(all, document => document.Warnings.Any(w => !all.Warnings.Contains(w)) ? ReviewImportAsync(document) : Task.FromResult(true));
        if (_editor is null) ShowImportResult();
    }
    private void ShowImportResult()
    {
        _rail.Select("Nodes"); ShowPage("Nodes"); ShowNotice(UiText.Format("Import complete · {0} subscriptions · {1} nodes", _app.State.Subscriptions.Count, _app.State.Nodes.Count));
    }
    internal async Task ImportPayloadAsync(byte[] bytes, bool image)
    {
        if (bytes.Length > (image ? QrImport.MaxBytes : NodeImport.MaxBytes)) throw new FormatException("Import is too large.");
        if (image) { var links = await Task.Run(() => QrImport.Decode(bytes)); await _app.ExecuteAsync(() => ImportContentsAsync(links)); }
        else
        {
            using var reader = new StreamReader(new MemoryStream(bytes), new System.Text.UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync(); await _app.ExecuteAsync(() => ImportContentAsync(text));
        }
    }
    private Task<bool> ReviewImportAsync(ImportDocument document) => ReviewImportAsync(document, _app.Cancellation);
    private async Task<bool> ReviewImportAsync(ImportDocument document, CancellationToken cancellation)
    {
        var dialog = new Window { Title = UiText.Get("Import review"), Width = 700, Height = 560, MinWidth = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Skin.ChromeBrush, ShowInTaskbar = false, Icon = Icon };
        var supported = Ui.Value(UiText.Format("{0} nodes · {1} rules · {2} subscriptions", document.Nodes.Count, document.Rules.Count, document.Subscriptions.Count)); supported.TextWrapping = TextWrapping.Wrap;
        var warnings = new TextBox { Text = string.Join('\n', document.Warnings.Select(UiText.Get)), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Classes = { "text-input" } };
        var accept = Ui.Button("Import supported items", () => dialog.Close(true), true); var cancel = Ui.Button("Cancel", () => dialog.Close(false)); var actions = Ui.Horizontal(cancel, accept); actions.HorizontalAlignment = HorizontalAlignment.Right;
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,12,Auto,12,*,16,Auto"), Margin = new(24), Children = { supported } };
        var label = Ui.Text("These settings need manual conversion", true); Grid.SetRow(label, 2); content.Children.Add(label); Grid.SetRow(warnings, 4); content.Children.Add(warnings); Grid.SetRow(actions, 6); content.Children.Add(actions);
        warnings.VerticalContentAlignment = VerticalAlignment.Top;
        dialog.Content = content;
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(false); e.Handled = true; } };
        cancellation.ThrowIfCancellationRequested();
        using var registration = cancellation.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (dialog.IsVisible) dialog.Close(false); }));
        var accepted = await dialog.ShowDialog<bool>(_editor ?? (Window)this);
        cancellation.ThrowIfCancellationRequested(); return accepted;
    }
    private async void ImportClipboard()
    {
        if (_preview || Clipboard is not { } clipboard) return;
        try
        {
            using var data = await clipboard.TryGetDataAsync(); if (data is null) throw new FormatException(UiText.Get("Clipboard is empty."));
            if (data.Contains(DataFormat.Bitmap))
            {
                using var bitmap = await data.TryGetBitmapAsync() ?? throw new FormatException("Clipboard image unavailable.");
                if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > QrImport.MaxPixels) throw new FormatException("Clipboard image is too large.");
                using var bytes = new MemoryStream(); bitmap.Save(bytes, new PngBitmapEncoderOptions()); await ImportPayloadAsync(bytes.ToArray(), true);
            }
            else { var text = await clipboard.TryGetTextAsync() ?? throw new FormatException(UiText.Get("Clipboard contains no text or QR image.")); await _app.ExecuteAsync(() => ImportContentAsync(text)); }
        }
        catch (ImportReviewCancelledException) { }
        catch (Exception e) { ShowError(e.Message); }
    }
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum)
    {
        using var bytes = new MemoryStream(); var buffer = new byte[16384]; int count;
        while ((count = await stream.ReadAsync(buffer)) > 0) { if (bytes.Length + count > maximum) throw new FormatException("Import is too large."); await bytes.WriteAsync(buffer.AsMemory(0, count)); }
        return bytes.ToArray();
    }
    private ProviderSearchDialog? _providerSearchDialog;
    internal ProviderSearchDialog? ProviderSearchWindow => _providerSearchDialog;
    private void SearchProviders()
    {
        if (_providerSearchDialog is { } opened) { opened.Activate(); return; }
        if (string.IsNullOrWhiteSpace(_app.State.Discovery.ApiKey)) { ConfigureSearch(); return; }
        ProviderSearchDialog? dialog = null;
        dialog = new ProviderSearchDialog(_app.State.Discovery.Search,
            options => _app.SaveSearchCriteriaAsync(options, dialog!.Cancellation),
            _app.DiscoverAsync, _app.CancelSearch) { Icon = Icon };
        _providerSearchDialog = dialog;
        void RefreshSearch()
        {
            if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { Avalonia.Threading.Dispatcher.UIThread.Post(RefreshSearch); return; }
            if (ReferenceEquals(_providerSearchDialog, dialog)) dialog.SetSearchState(_app.SearchBusy, _app.SearchError, _app.SearchCancelling, _app.SearchCancelled);
        }
        _app.Changed += RefreshSearch;
        dialog.Closed += (_, _) => { _app.Changed -= RefreshSearch; if (ReferenceEquals(_providerSearchDialog, dialog)) _providerSearchDialog = null; };
        RefreshSearch();
        // Keep connection and navigation available while the independent network request is pending.
        dialog.Show(this);
    }
    private void ImportProviderDirectory() { if (!_preview) _ = CreateProviderDirectoryEditor().ShowDialog(this); }
    internal EditorDialog CreateProviderDirectoryEditor() => CreateEditor("Import directory", [new("directory", "Directory URL or JSON", Multiline: true)], async (values, session) =>
    {
        var text = values["directory"]; var source = ImportDocument.RemoteUrl(text);
        if (source is not null) { using var http = new HttpClient(); text = await new SourceUpdates(http).DownloadAsync(source, 1024 * 1024, session.Token); }
        var imported = await Task.Run(() => ProviderDirectory.Import(text, source ?? "Imported directory", _app.State.Providers), session.Token);
        session.BeginCommit();
        await _app.ApplyAsync(_app.State with { Providers = ProviderDirectory.Merge(_app.State.Providers, imported) });
    }, "Import");
    private void ConfigureSearch()
    {
        if (_preview) return;
        var dialog = new AiSearchSettingsDialog(_app.State.Discovery, settings => _app.ApplyAsync(_app.State with { Discovery = settings }));
        dialog.Icon = Icon; _ = dialog.ShowDialog(this);
    }
    private void ShowProtocols()
    {
        if (_preview) return;
        var content = new StackPanel { Spacing = 14, Children = { Ui.Text("Import and engine configuration available", size: 18) } };
        foreach (var protocol in ProtocolCatalog.Supported) content.Children.Add(Ui.Value(protocol.Name));
        content.Children.Add(Ui.Line()); content.Children.Add(Ui.Text("Additional backend required", true, 18));
        foreach (var name in ProtocolCatalog.Pending) content.Children.Add(Ui.Value(name, true));
        var dialog = new Window { Title = UiText.Get("Protocol compatibility"), Width = 560, Height = 700, Background = Skin.ChromeBrush, WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon, ShowInTaskbar = false };
        dialog.Content = Ui.Scroll(content, new(26)); _ = dialog.ShowDialog(this);
    }
    private Window? _providerDetails;
    internal Window? ProviderDetailsWindow => _providerDetails;
    internal async Task OpenProviderDetailsAsync(string id)
    {
        if (_providerDetails is not null || _page != "Providers") return;
        if (!Guid.TryParse(id, out var providerId) || _app.State.Providers.FirstOrDefault(p => p.Id == providerId) is not { } site) return;
        _chosen = id; _rows.Select(id); UpdateSelectionActions();
        var dialog = new ProviderDetailsDialog(site, _app.State.Discovery.Search.Currency, OpenUrl) { Icon = Icon };
        _providerDetails = dialog;
        try { await dialog.ShowDialog(this); } finally { _providerDetails = null; }
    }
    private StackPanel BuildSelectedDetails()
    {
        var panel = new StackPanel { Spacing = 18 };
        void Detail(string label, string value) { var text = Ui.Value(value); text.TextWrapping = TextWrapping.Wrap; text.TextTrimming = TextTrimming.None; panel.Children.Add(new StackPanel { Spacing = 7, Children = { Ui.Text(label, true, 13), text } }); }
        if (_page == "Nodes" && SelectedSubscriptionId is { } sid && _app.State.Subscriptions.FirstOrDefault(s => s.Id == sid) is { } sub)
        {
            Detail("Subscription", sub.Name); Detail("Expires", UiFormat.Expiry(sub.Usage)); Detail("Traffic quota", UsageText(sub.Usage)); Detail("Last update", sub.UpdatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? UiText.Get("Unknown"));
            if (sub.Website is { } url) panel.Children.Add(Ui.Button("Open website", () => OpenUrl(url)));
        }
        if (_page == "Providers" && Guid.TryParse(_chosen, out var pid) && _app.State.Providers.FirstOrDefault(p => p.Id == pid) is { } site)
        {
            Detail("Website", site.Name); Detail("Address", site.Url);
            panel.Children.Add(Ui.Button("Open website", () => OpenUrl(site.Url), true));
            if (site.Profile is { } profile)
            {
                Detail("About", profile.Description); Detail("Regions", string.Join(" · ", profile.Regions));
                foreach (var plan in profile.Plans)
                {
                    Detail(plan.Name, UiFormat.Plan(plan) + (plan.TrafficGb is { } quota ? $" · {quota:0.##} GB" : ""));
                    if (plan.PriceQuote.Length > 0) Detail("Advertised price", plan.PriceQuote);
                    if (plan.Conditions.Length > 0) { var conditions = Ui.Value(plan.Conditions, true, 13); conditions.TextWrapping = TextWrapping.Wrap; panel.Children.Add(conditions); }
                }
                if (profile.ReviewSummary.Length > 0) Detail("User reviews and rankings", profile.ReviewSummary);
                foreach (var review in profile.Reviews)
                {
                    Detail(review.Title, (review.Date ?? UiText.Get("Date unknown")) + (review.Rank is { } rank ? " · " + UiText.Format("Rank {0} in this list", rank) : "") + "\n" + review.Summary);
                    panel.Children.Add(Ui.Button("Read review", () => OpenUrl(review.Url)));
                }
                Detail("Researched", profile.ResearchedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                foreach (var source in profile.Sources) panel.Children.Add(Ui.Button(new Uri(source).Host, () => OpenUrl(source)));
            }
            Detail("Website response", site.StatusCode is { } code ? $"HTTP {code} · {site.LatencyMs} ms" : site.CheckedAt is not null ? "—" : UiText.Get("Not checked"));
        }
        return panel;
    }
    private void OpenUrl(string url) { if (_preview) return; try { Process.Start(new ProcessStartInfo(SourceUpdates.WebUri(url).AbsoluteUri) { UseShellExecute = true }); } catch (Exception error) { ShowError(error.Message); } }
}
