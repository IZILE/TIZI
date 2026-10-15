using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    internal SegmentedChoice? SettingsSections { get; private set; }
    internal bool SettingsDirty { get; private set; }
    private StableChoice? _settingsLanguage;
    private Control? _settingsView;
    private Action? _syncGeneralOptions;
    private string? _settingsFingerprint;
    private NumericUpDown? _settingsPort;
    private StableChoice? _settingsOutbound;
    private Button? _settingsSave;
    private TextBlock? _settingsDraftStatus, _settingsConnectionHint, _settingsDataSummary;
    private string? _settingsStatusKey;
    private bool _settingsSaving, _settingsRuntimeSubscribed;
    private bool _askingDiscardSettings, _discardSettingsOnExit;
    private bool _closeAgainAfterSettingsPrompt;
    private Task _settingsAutoSaveTask = Task.CompletedTask;
    private bool _awaitingAutoSaveOnExit;
    internal bool GeneralSettingsSaving => !_settingsAutoSaveTask.IsCompleted;
    private CenteredDialog? _settingsDiscardDialog;
    private async Task<bool> ConfirmDiscardSettingsAsync()
    {
        RestoreFromBackground();
        var dialog = _settingsDiscardDialog = new CenteredDialog { Title = UiText.Get("Unsaved settings"), Tag = "discard-settings", Width = 480, SizeToContent = SizeToContent.Height, Background = Skin.ChromeBrush };
        var message = Ui.Text("Your settings have not been saved. Return to keep editing, or discard them and exit."); message.TextWrapping = TextWrapping.Wrap;
        var buttons = Ui.Horizontal(Ui.Button("Return to editing", () => dialog.Close(false)), Ui.Button("Discard and exit", () => dialog.Close(true))); buttons.HorizontalAlignment = HorizontalAlignment.Right;
        dialog.Content = new StackPanel { Margin = new(24), Spacing = 22, Children = { message, buttons } };
        dialog.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { dialog.Close(false); e.Handled = true; } };
        try { return await dialog.ShowDialog<bool>(this); }
        finally { if (ReferenceEquals(_settingsDiscardDialog, dialog)) _settingsDiscardDialog = null; }
    }
    private string SettingsFingerprint() => JsonSerializer.Serialize(new
    {
        _app.State.MixedPort, _app.State.OutboundInterface, _app.State.DelayUrl,
        _app.State.Tunnel, _app.State.Transport, _app.State.Dns, _app.State.Latency
    });
    private Control BuildSettingsCards()
    {
        _settingsLoaded = _app.Ready;
        if (!_app.Ready) return Ui.Text("Loading saved settings…", true);
        var fingerprint = SettingsFingerprint();
        // A draft belongs to the editor, not to the connection phase or another save.
        // Keep its controls and selected section until the user explicitly saves it.
        if (_settingsView is not null && (SettingsDirty || GeneralSettingsSaving || _settingsFingerprint == fingerprint))
        {
            if (!GeneralSettingsSaving) _syncGeneralOptions?.Invoke();
            if (_settingsLanguage is not null) _settingsLanguage.SelectedIndex = UiText.Language == "en" ? 1 : 0;
            RefreshSettingsRuntimeState();
            return _settingsView;
        }
        var selectedSection = SettingsSections?.SelectedIndex ?? 0;
        SettingsDirty = false;
        var state = _app.State;
        var sections = new[] { "General", "Tunnel", "Proxy", "TCP / UDP", "DNS", "Diagnostics" };
        var groups = sections.ToDictionary(key => key, _ => new SettingsCards { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new(0, 0, 16, 0) });
        TextBlock Heading(string title)
        {
            var heading = Ui.Text(title, size: 19); heading.FontWeight = FontWeight.SemiBold; return heading;
        }
        StackPanel Card(string section, string title)
        {
            var compact = section == "Diagnostics";
            var body = new StackPanel { Spacing = compact ? 6 : 18, Tag = section };
            var heading = Heading(title); if (compact) heading.Margin = new(0,0,0,4);
            body.Children.Add(heading);
            groups[section].Children.Add(new Border { Background = Skin.ChromeBrush, CornerRadius = new(14), Padding = new(compact ? 16 : 24), Child = body }); return body;
        }
        void Field(StackPanel body, string label, Control control)
        {
            var field = Labeled(label, control); if (body.Tag is "Diagnostics") field.Spacing = 4;
            body.Children.Add(field);
        }
        TextBox Text(string value, bool multiline = false) => new() { Text = value, AcceptsReturn = multiline, Height = multiline ? 112 : double.NaN, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center, Classes = { "text-input" } };
        StableChoice Choice(string[] values, string value) => new() { ItemsSource = values, SelectedIndex = Math.Max(0, Array.IndexOf(values, value)), ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>((item, _) => Ui.Text(item?.ToString() ?? "")) };
        var general = Card("General", "Language");
        var language = _settingsLanguage = Choice(["简体中文", "English"], UiText.Language == "en" ? "English" : "简体中文");
        language.SelectionChanged += (_, _) => { var next = language.SelectedIndex == 1 ? "en" : "zh-CN"; if (!_syncing && UiText.Language != next) SetLanguage(next); }; general.Children.Add(language);
        general.Children.Add(Heading("Version updates")); general.Children.Add(Ui.Value("TIZI " + AppVersion, true)); general.Children.Add(_versionUpdateButton = Ui.Button("Check for updates", ShowUpdates)); RefreshUpdateButton();
        var startup = Card("General", "Startup and closing");
        var login = Option("Start TIZI when I sign in", state.StartAtLogin); startup.Children.Add(login);
        var hidden = Option("Start in the tray", state.StartHidden); startup.Children.Add(hidden);
        var restore = Option("Restore the previous connection on startup", state.RestoreConnection); startup.Children.Add(restore);
        var close = Choice(["Exit application", "Keep running in the tray"], state.CloseToTray ? "Keep running in the tray" : "Exit application"); Field(startup, "When closing the window", close);
        var tunnel = Card("Tunnel", "TUN interface");
        var stack = Choice(["gvisor", "system", "mixed"], state.Tunnel.Stack); Field(tunnel, "Network stack", stack);
        var mtu = Number(state.Tunnel.Mtu, 1280, 9000); Field(tunnel, "MTU", mtu);
        var strict = Option("Strict routing", state.Tunnel.StrictRoute); tunnel.Children.Add(strict);
        var hijack = Option("Redirect DNS into the tunnel", state.Tunnel.HijackDns); tunnel.Children.Add(hijack);
        var routes = Text(string.Join('\n', state.Tunnel.ExcludedRoutes), true); Field(Card("Tunnel", "Route exclusions"), "Excluded networks (CIDR, one per line)", routes);
        var proxy = Card("Proxy", "Local proxy");
        var port = _settingsPort = Number(state.MixedPort, 1024, 65535); port.Tag = "settings-local-port"; Field(proxy, "Local port", port);
        var interfaces = new[] { "Automatic (physical gateway)" }.Concat(NetworkEnvironment.OutboundInterfaces()).ToArray();
        if (state.OutboundInterface.Length > 0 && !interfaces.Contains(state.OutboundInterface)) interfaces = [.. interfaces, state.OutboundInterface];
        var outbound = _settingsOutbound = Choice(interfaces, state.OutboundInterface); outbound.Tag = "settings-outbound-interface"; Field(proxy, "Outbound interface", outbound);
        var connectionHint = _settingsConnectionHint = Ui.Text("Disconnect before changing the local port or outbound interface.", true, 12);
        connectionHint.TextWrapping = TextWrapping.Wrap; proxy.Children.Add(connectionHint);
        var automaticSwitchText = Ui.Text("Switch timed-out nodes within the same country or region"); automaticSwitchText.TextWrapping = TextWrapping.Wrap;
        var automaticSwitch = new CheckBox { Content = automaticSwitchText, IsChecked = state.AutoSwitchOnTimeout };
        ToolTip.SetTip(automaticSwitch, UiText.Get("After two failed connection checks, use a verified available node with the same country or region label. Keep the current node if none is available."));
        automaticSwitch.IsCheckedChanged += async (_, _) =>
        {
            var enabled = automaticSwitch.IsChecked == true; if (enabled == _app.State.AutoSwitchOnTimeout) return;
            try { await _app.ExecuteAsync(() => _app.ApplyAsync(_app.State with { AutoSwitchOnTimeout = enabled })); }
            catch (Exception failure) { automaticSwitch.IsChecked = _app.State.AutoSwitchOnTimeout; ShowError(failure.Message); }
        };
        Card("Proxy", "Automatic node switching").Children.Add(automaticSwitch);
        var tcp = Card("TCP / UDP", "TCP");
        var ipv6 = Option("Enable IPv6", state.Transport.Ipv6); tcp.Children.Add(ipv6);
        var concurrent = Option("Concurrent TCP connections", state.Transport.TcpConcurrent); tcp.Children.Add(concurrent);
        var keepalive = Option("TCP keepalive", state.Transport.KeepAlive); tcp.Children.Add(keepalive);
        var idle = Number(state.Transport.KeepAliveIdle, 1, 600); Field(tcp, "Keepalive idle (seconds)", idle);
        var keepInterval = Number(state.Transport.KeepAliveInterval, 1, 600); Field(tcp, "Keepalive interval (seconds)", keepInterval);
        var udpCard = Card("TCP / UDP", "UDP"); var udp = Option("Allow UDP relay", state.Transport.Udp); udpCard.Children.Add(udp);
        var udpTimeout = Number(state.Tunnel.UdpTimeout, 10, 3600); Field(udpCard, "UDP session timeout (seconds)", udpTimeout);
        var dnsCard = Card("DNS", "DNS resolver");
        var dnsMode = Choice(["automatic", "fake-ip", "redir-host"], state.Dns.Mode); Field(dnsCard, "DNS mode", dnsMode);
        var dns6 = Option("Resolve IPv6 addresses", state.Dns.Ipv6); dnsCard.Children.Add(dns6);
        var respect = Option("DNS follows routing rules", state.Dns.RespectRules); dnsCard.Children.Add(respect);
        var servers = Text(string.Join('\n', state.Dns.Servers), true); Field(dnsCard, "DNS servers (one per line)", servers);
        var advancedDns = Card("DNS", "DNS bootstrap and hosts");
        var proxyDns = Text(string.Join('\n', state.Dns.ProxyServers), true); Field(advancedDns, "Proxy server DNS (one per line)", proxyDns);
        var bootstrap = Text(string.Join('\n', state.Dns.BootstrapServers), true); Field(advancedDns, "Bootstrap DNS (IP addresses)", bootstrap);
        var hosts = Text(JsonSerializer.Serialize(state.Dns.Hosts, new JsonSerializerOptions { WriteIndented = true }), true); Field(advancedDns, "Local DNS mappings (JSON)", hosts);
        var tests = Card("Diagnostics", "Latency testing");
        var method = Choice(["HTTP", "TCP", "ICMP"], state.Latency.Method); Field(tests, "Test method", method);
        var delay = Text(state.DelayUrl); Field(tests, "Latency test URL", delay);
        var timeout = Number(state.Latency.TimeoutMs, 500, 15000); Field(tests, "Timeout (ms)", timeout);
        var parallel = Number(state.Latency.Concurrency, 1, 16); Field(tests, "Concurrent tests", parallel);
        var probeInterval = Number(state.Latency.CurveIntervalSeconds, 1, 300); Field(tests, "Curve interval (seconds)", probeInterval);
        var tools = Card("Diagnostics", "Tools");
        var ipTools=Card("Diagnostics","IP detection");
        var automaticIp=new CheckBox { Content=Ui.Text("Inspect exit IP after connecting"),IsChecked=state.IpQuality.Enabled };
        automaticIp.IsCheckedChanged+=async (_,_)=>
        {
            var enabled=automaticIp.IsChecked==true;if(enabled==_app.State.IpQuality.Enabled)return;
            try { await _app.ExecuteAsync(()=>_app.ApplyAsync(_app.State with { IpQuality=_app.State.IpQuality with { Enabled=enabled } })); }
            catch(Exception failure) { automaticIp.IsChecked=_app.State.IpQuality.Enabled;ShowError(failure.Message); }
        };
        ipTools.Children.Add(automaticIp);ipTools.Children.Add(Ui.Button("IP detection services",ConfigureIpQuality));
        tools.Children.Add(new WrapPanel { ItemSpacing = 10, LineSpacing = 10, Children = { Ui.Button("DNS lookup", DnsLookup), Ui.Button("Open data folder", () => Process.Start(new ProcessStartInfo(EnsureDataFolder()) { UseShellExecute = true })), Ui.Button("Recover saved data", RecoverSavedData) } });
        _settingsDataSummary = Ui.Value(UiText.Format("{0} subscriptions · {1} nodes · {2} rules", state.Subscriptions.Count, state.Nodes.Count, state.Rules.Count), true, 13);
        _settingsDataSummary.TextWrapping = TextWrapping.Wrap; tools.Children.Add(_settingsDataSummary);
        var toolsCard = (Control)tools.Parent!; var ipCard = (Control)ipTools.Parent!;
        groups["Diagnostics"].Children.Remove(toolsCard); groups["Diagnostics"].Children.Remove(ipCard);
        groups["Diagnostics"].Children.Add(new StackPanel { Spacing = 18, Tag = "diagnostics-tools-column", Children = { toolsCard, ipCard } });
        static List<string> Lines(TextBox input) => (input.Text ?? "").Split(['\r', '\n', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var syncingGeneral = false; var generalGeneration = 0;
        _syncGeneralOptions = () =>
        {
            var saved = _app.State;
            syncingGeneral = true;
            try { login.IsChecked = saved.StartAtLogin; hidden.IsChecked = saved.StartHidden; restore.IsChecked = saved.RestoreConnection; close.SelectedIndex = saved.CloseToTray ? 1 : 0; }
            finally { syncingGeneral = false; }
        };
        async Task SaveGeneralAsync(Task previousSave, GeneralPreferences preferences, int generation)
        {
            await previousSave;
            try { await _app.UpdateGeneralPreferencesAsync(preferences); }
            catch (Exception failure)
            {
                if (generation == generalGeneration)
                {
                    _syncGeneralOptions();
                }
                ShowError(failure.Message);
            }
            finally
            {
                Dispatcher.UIThread.Post(RefreshSettingsRuntimeState);
            }
        }
        foreach (var input in new Control[] { login, hidden, restore, close }) input.PropertyChanged += (_, change) =>
        {
            if (_preview || syncingGeneral || (change.Property != ToggleButton.IsCheckedProperty && change.Property != SelectingItemsControl.SelectedIndexProperty)) return;
            var preferences = new GeneralPreferences(login.IsChecked == true, hidden.IsChecked == true, restore.IsChecked == true, close.SelectedIndex == 1);
            _settingsAutoSaveTask = SaveGeneralAsync(_settingsAutoSaveTask, preferences, ++generalGeneration);
        };
        Control[] inputs = [stack, mtu, strict, hijack, routes, port, outbound, ipv6, concurrent, keepalive, idle, keepInterval, udp, udpTimeout, dnsMode, dns6, respect, servers, proxyDns, bootstrap, hosts, method, delay, timeout, parallel, probeInterval];
        string Draft() => JsonSerializer.Serialize(inputs.Select<Control, object?>(input => input switch
        {
            TextBox text => text.Text ?? "", NumericUpDown number => number.Value,
            CheckBox option => option.IsChecked, StableChoice choice => choice.SelectedIndex, _ => null
        }));
        var savedDraft = Draft();
        var error = Ui.Value(""); error.Tag = "settings-error"; error.TextWrapping = TextWrapping.Wrap;
        error.Foreground = new SolidColorBrush(Color.Parse("#FF9B8F")); error.IsVisible = false;
        var draftStatus = _settingsDraftStatus = Ui.Text("Unsaved changes", true, 13);
        draftStatus.Tag = "settings-draft-status"; draftStatus.TextWrapping = TextWrapping.Wrap; draftStatus.IsVisible = false;
        _settingsStatusKey = "Unsaved changes";
        void ClearError() { error.IsVisible = false; error.Text = ""; }
        void UpdateDirty()
        {
            var dirty = Draft() != savedDraft;
            var changed = SettingsDirty != dirty;
            SettingsDirty = dirty;
            RefreshSettingsRuntimeState();
            if (changed && !dirty && _notification.Message == UiText.Get("Unsaved changes")) _notification.Dismiss();
        }
        foreach (var input in inputs) input.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty || change.Property == NumericUpDown.ValueProperty || change.Property == ToggleButton.IsCheckedProperty || change.Property == SelectingItemsControl.SelectedIndexProperty)
            { ClearError(); UpdateDirty(); }
        };
        var save = _settingsSave = Ui.Button("Save settings", () => Run(async () =>
        {
            _settingsSaving = true; RefreshSettingsRuntimeState();
            try
            {
            var submittedDraft = Draft(); SourceUpdates.WebUri(delay.Text ?? ""); var previous = _app.State;
            var next = previous with
            {
                MixedPort = (int)(port.Value ?? 7897), OutboundInterface = outbound.SelectedIndex > 0 ? interfaces[outbound.SelectedIndex] : "", DelayUrl = delay.Text ?? "",
                Tunnel = previous.Tunnel with { Stack = stack.SelectedItem?.ToString() ?? "gvisor", Mtu = (int)(mtu.Value ?? 1500), StrictRoute = strict.IsChecked == true, HijackDns = hijack.IsChecked == true, UdpTimeout = (int)(udpTimeout.Value ?? 300), ExcludedRoutes = Lines(routes) },
                Transport = previous.Transport with { Ipv6 = ipv6.IsChecked == true, TcpConcurrent = concurrent.IsChecked == true, KeepAlive = keepalive.IsChecked == true, KeepAliveIdle = (int)(idle.Value ?? 15), KeepAliveInterval = (int)(keepInterval.Value ?? 15), Udp = udp.IsChecked == true },
                Dns = previous.Dns with { Mode = dnsMode.SelectedItem?.ToString() ?? "automatic", Ipv6 = dns6.IsChecked == true, RespectRules = respect.IsChecked == true, Servers = Lines(servers), ProxyServers = Lines(proxyDns), BootstrapServers = Lines(bootstrap), Hosts = JsonSerializer.Deserialize<Dictionary<string, string>>(hosts.Text ?? "{}") ?? [] },
                Latency = previous.Latency with { Method = method.SelectedItem?.ToString() ?? "HTTP", TimeoutMs = (int)(timeout.Value ?? 5000), Concurrency = (int)(parallel.Value ?? 4), CurveIntervalSeconds = (int)(probeInterval.Value ?? 1) }
            };
            // A port/interface edit may have been drafted while disconnected, then
            // retained when the user connected. Disabled controls do not make that
            // pending edit safe to apply to a live listener.
            if (_app.Connected && (next.MixedPort != previous.MixedPort || next.OutboundInterface != previous.OutboundInterface))
                throw new InvalidOperationException(UiText.Get("Disconnect before changing the local port or outbound interface."));
            AdvancedValidation.Check(next);
            await _app.ApplyAsync(next, reload: _app.Connected);
            savedDraft = submittedDraft; ClearError(); UpdateDirty(); _settingsFingerprint = SettingsFingerprint();
            ShowNotice(SettingsDirty ? "Settings saved. Newer changes are still unsaved." : "Settings saved");
            }
            catch (Exception failure)
            {
                error.Text = UiText.Get(failure is JsonException ? "Invalid JSON object." : failure.Message); error.IsVisible = true;
                throw;
            }
            finally { _settingsSaving = false; RefreshSettingsRuntimeState(); }
        }), true); save.HorizontalAlignment = HorizontalAlignment.Right;
        save.Tag = "settings-save";
        var selected = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
        SettingsSections = new SegmentedChoice(sections); SettingsSections.Changed += index =>
        {
            var next = groups[sections[index]];
            if (selected.Content is SettingsCards previous && !ReferenceEquals(previous,next)) next.ContinueFrom(previous);
            selected.Content = next;
            RefreshSettingsRuntimeState();
        }; selected.Content = groups[sections[0]];
        SettingsSections.SelectedIndex = Math.Clamp(selectedSection, 0, sections.Length - 1);
        selected.Content = groups[sections[SettingsSections.SelectedIndex]];
        var scroll = new ScrollViewer { AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = selected };
        Ui.PageScrollBar(scroll);
        var footerContent = new Grid { ColumnDefinitions = new("*,16,Auto"), Children = { draftStatus, save } }; Grid.SetColumn(save, 2);
        var footer = new Border { Padding = new(0, 16, 0, 0), Child = new StackPanel { Spacing = 10, Children = { error, footerContent } } };
        var layout = new Grid { RowDefinitions = new("Auto,16,*,Auto"), Children = { SettingsSections, scroll, footer } }; Grid.SetRow(scroll, 2); Grid.SetRow(footer, 3);
        _settingsFingerprint = fingerprint; _settingsView = layout;
        if (!_settingsRuntimeSubscribed)
        {
            _settingsRuntimeSubscribed = true; _app.Changed += RefreshSettingsRuntimeState;
            Closed += (_, _) => { _app.Changed -= RefreshSettingsRuntimeState; _settingsRuntimeSubscribed = false; };
        }
        RefreshSettingsRuntimeState(); return layout;
    }
    private void RefreshSettingsRuntimeState()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshSettingsRuntimeState); return; }
        var canEditListener = _app.Ready && !_app.Connected && !_app.Busy;
        if (_settingsPort is { } port) port.IsEnabled = canEditListener;
        if (_settingsOutbound is { } outbound) outbound.IsEnabled = canEditListener;
        if (_settingsConnectionHint is { } hint) hint.IsVisible = _app.Connected;
        if (_settingsDataSummary is { } summary) summary.Text = UiText.Format("{0} subscriptions · {1} nodes · {2} rules", _app.State.Subscriptions.Count, _app.State.Nodes.Count, _app.State.Rules.Count);
        var optionOnly = SettingsSections?.SelectedIndex == 0;
        if (_settingsSave is { } save) { save.IsVisible = !optionOnly; save.IsEnabled = _app.Ready && !_app.Busy && !_settingsSaving && !GeneralSettingsSaving; }
        if (_settingsDraftStatus is not { } status) return;
        var key = _settingsSaving ? "Saving settings…" : SettingsDirty ? "Unsaved changes" : null;
        status.IsVisible = !optionOnly && key is not null;
        if (key is not null && key != _settingsStatusKey) { _settingsStatusKey = key; UiText.Set(status, TextBlock.TextProperty, key); }
    }
    private void DnsLookup() => Dialog("DNS lookup", [new("host", "Domain", "example.com")], async values =>
    {
        var host = values["host"].Trim(); if (Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Length > 253) throw new FormatException("Invalid domain.");
        var addresses = await System.Net.Dns.GetHostAddressesAsync(host, _app.Cancellation);
        ShowNotice(host + " · " + string.Join(" · ", addresses.Select(a => a.ToString())));
    }, "Look up");
}

/// <summary>Wide cards at desktop sizes; a readable single column in narrow windows.</summary>
internal sealed class SettingsCards : Panel
{
    internal static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<SettingsCards,double>(nameof(Progress),1);
    internal double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty,value); }
    private Rect[] _from = [];
    private double _fromHeight;
    private int _generation;
    private readonly GhostSurface _ghosts;
    internal int Columns { get; private set; } = 1;
    static SettingsCards() => AffectsMeasure<SettingsCards>(ProgressProperty);
    internal SettingsCards()
    {
        _ghosts=new GhostSurface(this) { IsHitTestVisible=false, ZIndex=-1 }; VisualChildren.Add(_ghosts);
        Transitions = new() { new DoubleTransition { Property=ProgressProperty, Duration=TimeSpan.FromMilliseconds(160), Easing=new SplineEasing(.16,1,.3,1) } };
        PropertyChanged += (_,e) => { if(e.Property==ProgressProperty) _ghosts.InvalidateVisual(); };
        AttachedToVisualTree += (_, _) =>
        {
            var generation = _generation;
            Dispatcher.UIThread.Post(() => { if(generation==_generation && VisualRoot is not null) Motion.SettlePaint(this); },DispatcherPriority.Loaded);
        };
    }
    internal void ContinueFrom(SettingsCards previous)
    {
        _from=previous.DisplayedCards(); _fromHeight=previous.Bounds.Height;
        var transitions=Transitions; Transitions=null; Progress=0; Transitions=transitions;
        var generation=++_generation;
        Dispatcher.UIThread.Post(() => { if(generation==_generation && VisualRoot is not null) { Motion.SettlePaint(this); Progress=1; } },DispatcherPriority.Loaded);
    }
    internal Rect[] DisplayedCards() => Children.Select(c=>c.Bounds).Concat(
        _from.Skip(Children.Count).Select(r=>new Rect(r.X,r.Y,r.Width,r.Height*(1-Math.Clamp(Progress,0,1))))).Where(r=>r.Height>.1).ToArray();
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsFinite(available.Width) ? available.Width : 900; Columns = width >= 760 ? 2 : 1;
        var cell = (width - (Columns - 1) * 18) / Columns; var height = 0d;
        for (var i = 0; i < Children.Count; i += Columns)
        {
            var row = 0d; for (var j = i; j < Math.Min(i + Columns, Children.Count); j++) { Children[j].Measure(new(cell, double.PositiveInfinity)); row = Math.Max(row, Children[j].DesiredSize.Height); }
            height += row + (i == 0 ? 0 : 18);
        }
        return new(width, _from.Length==0 ? height : _fromHeight+(height-_fromHeight)*Math.Clamp(Progress,0,1));
    }
    protected override Size ArrangeOverride(Size final)
    {
        if(!VisualChildren.Contains(_ghosts)) VisualChildren.Add(_ghosts);
        _ghosts.Measure(final); _ghosts.Arrange(new Rect(0,0,final.Width,Math.Max(final.Height,_fromHeight)));
        var cell = (final.Width - (Columns - 1) * 18) / Columns; var top = 0d;
        for (var i = 0; i < Children.Count; i += Columns)
        {
            var height = Children.Skip(i).Take(Columns).Max(c => c.DesiredSize.Height);
            for (var j = i; j < Math.Min(i + Columns, Children.Count); j++)
            {
                var target=new Rect((j-i)*(cell+18),top,cell,height);
                var source=j<_from.Length ? _from[j] : new Rect(target.X,target.Y,target.Width,0);
                var p=_from.Length==0 ? 1 : Math.Clamp(Progress,0,1);
                var rect=new Rect(source.X+(target.X-source.X)*p,source.Y+(target.Y-source.Y)*p,
                    source.Width+(target.Width-source.Width)*p,source.Height+(target.Height-source.Height)*p);
                Children[j].ClipToBounds=true; Children[j].Arrange(rect);
            }
            top += height + 18;
        }
        return final;
    }
    private sealed class GhostSurface(SettingsCards owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            var p=Math.Clamp(owner.Progress,0,1);
            if(p>=1) return;
            foreach(var rect in owner._from.Skip(owner.Children.Count))
                context.DrawRectangle(Skin.ChromeBrush,null,new Rect(rect.X,rect.Y,rect.Width,rect.Height*(1-p)),14,14);
        }
    }
}
