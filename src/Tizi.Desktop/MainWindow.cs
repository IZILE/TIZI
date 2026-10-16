using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class MainWindow : Window
{
    private static string AppVersion => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
    private readonly AppController _app;
    private readonly bool _preview;
    private readonly NavigationRail _rail = new();
    private readonly SegmentedChoice _mode = new("Rule", "Global", "Direct") { Background = Skin.ToolbarFill };
    private readonly StableChoice _capture = new() { Classes = { "capture-mode" }, ItemsSource = new[] { "System proxy", "TUN", "Local proxy" }, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly ConnectionButton _connect = new();
    private readonly SearchField _search = new();
    private readonly MovingRows _rows = new();
    private readonly SubscriptionTree _subscriptionTree = new();
    private readonly ScrollViewer _subscriptionScroll;
    private readonly ScrollViewer _listScroll;
    private readonly ScrollViewer _overviewScroll;
    private readonly WrapPanel _pageActions = new() { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
    private readonly Border _pageActionsHost = new() { Margin = new Thickness(0, 0, 0, 16) };
    private readonly StackPanel _inspector = new() { Spacing = 14, Margin = new Thickness(18) };
    // Page scrollbars use the existing outer gutter beyond the content width.
    // Each scroll viewer clips its own viewport; the host must not clip that gutter.
    private readonly ContentControl _pageHost = new() { ClipToBounds=false };
    private readonly TextBlock _heading = Ui.Text("Overview", size: 24);
    private readonly TextBlock _count = Ui.Value("", true, 11);
    private EditorDialog? _editor;
    private readonly SubscriptionNodeChoice _nodeChoice = new();
    private readonly NetworkMonitor _monitor = new();
    private readonly StackPanel _overview = new() { Spacing = 24 };
    private readonly Border _inspectorBorder = new();
    private Grid _workspace = null!;
    private Border _contentSurface = null!;
    private Grid _clientSurface = null!;
    private Grid _toolbar = null!;
    private readonly TextBlock _brand;
    private StackPanel _toolbarModes = null!;
    private bool _probing, _forceExit;
    private bool _presentedOnce;
    private DateTimeOffset _lastProbe = DateTimeOffset.MinValue;
    private readonly Queue<int?> _probeHistory = new();
    private TrayIcon? _tray;
    private readonly NativeMenuItem _trayConnection = new("Connect");
    private readonly TextBlock _speed = Ui.Value("↑ —     ↓ —", true, 11);
    private readonly ClientNotification _notification = new();
    private readonly TextBlock _pagination = Ui.Value("", true, 11);
    private readonly TextBlock _footer = Ui.Value("", true, 11);
    private readonly LogView _logView = new();
    private readonly Button _previous, _next;
    private readonly StackPanel _paging;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<Guid, string> _latencies = [];
    private CancellationTokenSource? _latencyStop;
    private Button? _latencyButton;
    private Button? _removeSelectionButton;
    private Button? _cancelProviderSearch, _retryProviderSearch;
    private TextBlock? _providerSearchFeedback;
    private int _latencyDone, _latencyTotal;
    private AppState? _renderedState;
    private string? _renderedPage;
    private readonly ActivityLog _logs = new();
    private readonly SegmentedChoice _logFilter = new("All", "Proxy", "DNS", "Application");
    private DateTimeOffset _lastLogSave = DateTimeOffset.MinValue;
    private bool _opened;
    private WindowState _restoreState = WindowState.Normal;
    private string _page = "Overview";
    private string? _chosen;
    private int _pageIndex;
    private bool _syncing, _polling, _allowClose, _shutdown;
    private bool _logDirty, _wasConnected;
    private bool _settingsLoaded;
    private TextBlock? _uploadValue, _downloadValue;
    private long _lastUp, _lastDown;
    private long _lastPoll;
    private ConnectionSnapshot _snapshot = new(0, 0, []);
    internal string CurrentPage => _page;
    internal NavigationRail Rail => _rail;
    internal SegmentedChoice ModeControl => _mode;
    internal StableChoice CaptureControl => _capture;
    internal SubscriptionNodeChoice NodeControl => _nodeChoice;
    internal AppController Controller => _app;
    internal SubscriptionTree SubscriptionView => _subscriptionTree;
    internal NetworkMonitor OverviewMonitor => _monitor;
    internal TimeSpan LatencySampleInterval => TimeSpan.FromSeconds(_monitor.LatencyMonitoring ? _app.State.Latency.CurveIntervalSeconds : _app.State.Latency.IntervalSeconds);
    internal TextBlock PageHeading => _heading;
    internal Border ContentSurface => _contentSurface;
    internal Control PageView => (Control)_pageHost.Content!;
    internal ConnectionButton ConnectAction => _connect;
    internal ClientNotification Notification => _notification;
    internal SearchField SearchControl => _search;
    internal RuleEditor? ActiveRuleEditor { get; private set; }
    internal MoreActionsSheet? ActiveMoreSheet { get; private set; }

    internal MainWindow(bool preview = false, AppState? state = null)
    {
        _preview = preview; _app = new AppController(preview);
        _listScroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _subscriptionScroll = new ScrollViewer { Content = _subscriptionTree, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, AllowAutoHide = false };
        // Keep card edges aligned with the header; the bar uses the outer page margin.
        Ui.PageScrollBar(_subscriptionScroll); Ui.PageScrollBar(_listScroll);
        _subscriptionTree.Margin = new(0, 0, 16, 0); _rows.Margin = new(0, 0, 16, 0); _overview.Margin = new(0, 0, 16, 0);
        _listScroll.AllowAutoHide = false;
        // Keep the panel and its scroll presenter together across page visits.
        // Rewrapping the same panel gives it two visual parents on re-entry.
        _overviewScroll = new ScrollViewer { Content = _overview, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, AllowAutoHide = false };
        Ui.PageScrollBar(_overviewScroll);
        _count.TextWrapping = TextWrapping.Wrap; _count.TextTrimming = TextTrimming.None;
        if (state is not null) _app.SetPreview(state);
        Title = ""; Avalonia.Automation.AutomationProperties.SetName(this, "TIZI");
        WindowDecorations = WindowDecorations.Full;
        WindowDecorationsTheme = (ControlTheme)Application.Current!.FindResource("TiziWindowDecorations")!;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 32;
        Width = 1032; Height = 722; MinWidth = 880; MinHeight = 620; Background = Skin.ChromeBrush; ShowInTaskbar = true;
        TransparencyLevelHint = [WindowTransparencyLevel.None]; TransparencyBackgroundFallback = Skin.ChromeBrush;
        PropertyChanged += (_, change) => { if (change.Property == WindowStateProperty && WindowState != WindowState.Minimized) _restoreState = WindowState; };
        using var icon = typeof(MainWindow).Assembly.GetManifestResourceStream("Tizi.Desktop.AppIcon.ico"); if (icon is not null) Icon = new WindowIcon(icon);
        UiText.WireChoices(this);
        _mode.Indicator.Background = Skin.Blue;
        var root = new Grid { RowDefinitions = new RowDefinitions("32,Auto,*,32") };
        root.Children.Add(_captionGrid=BuildCaption());
        var brand = _brand = Ui.Value("TIZI", size: 36); brand.FontWeight = FontWeight.SemiBold;
        brand.UseLayoutRounding = false; brand.Opacity = 0; // The approved source mark is drawn by its host; keep TIZI as accessible text.
        // Keep the complete font line box while aligning the visible uppercase letters
        // to the toolbar center. The host does not reserve another header row.
        var brandHost = new WordmarkHost { Child = brand, Height = 44,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new(0, 0, 0, 24) };
        _wordmarkHost=brandHost;_shellRoot=root;
        Grid.SetRow(brandHost, 1); root.Children.Add(brandHost);
        var toolbar = _toolbar = new Grid { ColumnDefinitions = new("*,Auto,*"), Margin = new Thickness(24, 0, 24, 24) };
        Grid.SetColumn(_nodeChoice, 1); toolbar.Children.Add(_nodeChoice);
        var modeRow = _toolbarModes = Ui.Horizontal(_mode, _capture); modeRow.HorizontalAlignment = HorizontalAlignment.Left; modeRow.VerticalAlignment = VerticalAlignment.Center; toolbar.Children.Add(modeRow);
        modeRow.SizeChanged += (_, _) => Responsive();
        _connect.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(_connect, 2); toolbar.Children.Add(_connect); Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        _workspace = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Background = Skin.ChromeBrush }; _workspace.Children.Add(_rail);
        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(28, 24, 28, 12) };
        main.Children.Add(_profileStatus);
        Grid.SetRow(_undoHost, 5); main.Children.Add(_undoHost);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,Auto"), Margin = new Thickness(0, 0, 0, 20) };
        _heading.FontWeight = FontWeight.SemiBold;
        var title = new StackPanel { Spacing = 7, Children = { _heading, _count } }; header.Children.Add(title); header.Children.Add(_search); Grid.SetColumn(_search, 2); Grid.SetRow(header, 1); main.Children.Add(header);
        _pageActionsHost.Child = _pageActions; Grid.SetRow(_pageActionsHost, 2); main.Children.Add(_pageActionsHost);
        Grid.SetRow(_pageHost, 3); main.Children.Add(_pageHost);
        _previous = Ui.Button("Previous", () => { if (_pageIndex > 0) { _pageIndex--; RefreshRows(); } });
        _next = Ui.Button("Next", () => { _pageIndex++; RefreshRows(); });
        var paging = _paging = Ui.Horizontal(_previous, _pagination, _next); paging.HorizontalAlignment = HorizontalAlignment.Right; paging.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(paging, 4); main.Children.Add(paging);
        var surface = _contentSurface = new Border { Background = Skin.PasteboardBrush, CornerRadius = new(18), ClipToBounds = true, Margin = new(0, 0, 18, 0), Child = main }; Grid.SetColumn(surface, 1); _workspace.Children.Add(surface);
        _inspectorBorder.Background = Skin.ChromeBrush; _inspectorBorder.Width = 300; _inspectorBorder.BorderBrush = Skin.TabBackEdge; _inspectorBorder.BorderThickness = new Thickness(0.5, 0, 0, 0);
        _inspectorBorder.Child = Ui.Scroll(_inspector);
        Grid.SetColumn(_inspectorBorder, 2); _workspace.Children.Add(_inspectorBorder); Grid.SetRow(_workspace, 2); root.Children.Add(_workspace);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(24, 0), Children = { _footer, _speed } }; Grid.SetColumn(_speed, 1); Grid.SetRow(bottom, 3); root.Children.Add(bottom);
        _overview.Children.Add(_monitor);
        _monitor.LatencyMonitoringChanged += enabled =>
        {
            if (enabled) { _lastProbe = DateTimeOffset.MinValue; _probeTask = ProbeSelectedAsync(); }
        };
        Content = _clientSurface = new Grid { Background = Skin.ChromeBrush, ClipToBounds = true, Children = { root, _notification } }; SizeChanged += (_, _) => Responsive();
        _clientSurface.Children.Insert(0,_chromeMaterial);Appearance.Changed+=ApplyWindowAppearance;ApplyWindowAppearance();
        Closed+=(_,_)=>Appearance.Changed-=ApplyWindowAppearance;
        Closed += (_, _) => { foreach (var dialog in _dialogs.ToArray()) dialog.ForceClose(); };
        Deactivated += (_, _) =>
        {
            ActiveMoreSheet?.DismissImmediately();
            foreach (var choice in this.GetVisualDescendants().OfType<ComboBox>()) PopupMotion.CloseImmediately(choice);
            foreach (var menu in this.GetVisualDescendants().OfType<Control>().Select(c => c.ContextMenu).OfType<ContextMenu>().Distinct()) ItemContextMenu.CloseImmediately(menu);
        };
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_dialogs.Count > 0) return;
            if (_search.IsKeyboardFocusWithin && e.Source is Visual source && !ReferenceEquals(source, _search)
                && !source.GetVisualAncestors().Contains(_search)) FocusManager?.Focus(null);
            ClearSelectionOnBlankPress(e);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        PropertyChanged += (_, change) => { if (change.Property == WindowStateProperty) Dispatcher.UIThread.Post(Responsive, DispatcherPriority.Loaded); };
        _rail.Chosen += page => ShowPage(page);
        AttachSelectionGestures();
        _rows.ContextActions = ContextActionsFor;
        _rows.Chosen += ChooseItem;
        _rows.Activated += id =>
        {
            if (_multiSelect) return;
            if (_page == "Nodes" && Guid.TryParse(id, out var guid) && _app.State.Nodes.FirstOrDefault(n => n.Id == guid) is { } node) Run(() => _app.SelectAsync(node));
            else if (_page == "Providers") _ = OpenProviderDetailsAsync(id);
            else if (_page == "Rules" && int.TryParse(id, out var index)) EditRule(index);
            else if (_page == "Connections") OpenConnectionDetails();
        };
        _rows.SourceRequested += OpenUrl;
        _subscriptionTree.NodeChosen += id => { ChooseItem(id.ToString()); if (_multiSelect) ApplyMultipleSelectionVisuals(); else _subscriptionTree.SelectItem(_chosen); };
        _subscriptionTree.NodeActivated += id => { if (!_multiSelect && _app.State.Nodes.FirstOrDefault(n => n.Id == id) is { } node) Run(() => _app.SelectAsync(node)); };
        _subscriptionTree.NodeActionRequested += (id, action) => { ChooseItem(id.ToString()); if (action == "edit") EditNode(); else if (action == "remove") RemoveSelected(); };
        _subscriptionTree.SubscriptionActionRequested += (id, action) => { ChooseItem("subscription:" + id); if (action == "edit") EditSubscription(id); else if (action == "settings") SubscriptionSettings(); else if (action == "remove") RemoveSelected(); };
        _subscriptionTree.SubscriptionChosen += id => { ChooseItem("subscription:" + id); if (_multiSelect) ApplyMultipleSelectionVisuals(); else _subscriptionTree.SelectItem(_chosen); };
        _subscriptionTree.ManualGroupChosen += () => { ChooseItem("manual"); if (_multiSelect) ApplyMultipleSelectionVisuals(); else _subscriptionTree.SelectItem(_chosen); };
        _subscriptionTree.ManualGroupActionRequested += action => { ChooseItem("manual"); if (action == "remove") RemoveSelected(); };
        _subscriptionTree.VisibleItemsChanged += () => Dispatcher.UIThread.Post(() => { if (_page == "Nodes") ValidateSelection(); });
        _subscriptionTree.UrlRequested += ShowSubscriptionUrl;
        _subscriptionTree.UpdateRequested += id => Run(() => _app.UpdateSubscriptionAsync(id));
        _search.TextChanged += (_, _) => { _pageIndex = 0; RefreshRows(); };
        _connect.Click += (_, _) => Connect();
        _nodeChoice.NodeChosen += id => { if (!_app.Busy && _app.State.Nodes.FirstOrDefault(n => n.Id == id) is { } node) Run(() => _app.SelectAsync(node)); };
        _app.NodeAutoSwitched += message => ShowNotice(message);
        _nodeChoice.DropDownVisibilityChanged += open =>
        {
            _nodeChoice.UpdateLatencies(_latencies);
            if (open) StartHeaderLatencyTest(); else _headerLatencyStop?.Cancel();
        };
        _mode.Changed += index => { if (!_syncing) Run(() => _app.ModeAsync((RouteMode)index)); };
        _capture.SelectionChanged += (_, _) =>
        {
            if (_syncing || _capture.SelectedIndex < 0) return;
            // Entering Run refreshes the toolbar with Busy=true. Capture the new
            // choice first so that refresh cannot replace it with the old value.
            var capture = (CaptureMode)_capture.SelectedIndex;
            Run(() => _app.ApplyAsync(_app.State with { Capture = capture }));
        };
        _app.Changed += Refresh;
        _app.Error += ShowError;
        _app.Logged += message => { _logs.Settings = _app.State.Logs; _logs.Add(message); _logDirty = true; };
        _logFilter.Changed += _ => { if (_page == "Logs") RefreshLog(true); };
        _poll.Tick += async (_, _) => await PollAsync();
        PrepareStartupAnimation();
        Opened += async (_, _) => await StartRuntimeAsync();
        Closing += async (_, e) =>
        {
            ActiveMoreSheet?.DismissImmediately();
            if (_preview || _allowClose) return;
            if (GeneralSettingsSaving)
            {
                e.Cancel = true; if (_awaitingAutoSaveOnExit) return; _awaitingAutoSaveOnExit = true;
                try { while (GeneralSettingsSaving) await _settingsAutoSaveTask; Close(); }
                finally { _awaitingAutoSaveOnExit = false; }
                return;
            }
            if (!_forceExit && _app.State.CloseToTray && _tray is not null) { e.Cancel = true; Hide(); return; }
            if (_settingsSaving) { e.Cancel = true; ShowNotice("Saving settings…"); return; }
            if (SettingsDirty && !_discardSettingsOnExit)
            {
                e.Cancel = true;
                if (_askingDiscardSettings)
                {
                    if (_settingsDiscardDialog is { IsVisible: false }) _closeAgainAfterSettingsPrompt = true;
                    return;
                }
                _askingDiscardSettings = true;
                try
                {
                    if (await ConfirmDiscardSettingsAsync()) { _discardSettingsOnExit = true; Close(); }
                    else _forceExit = false;
                }
                finally
                {
                    _askingDiscardSettings = false;
                    if (_closeAgainAfterSettingsPrompt) { _closeAgainAfterSettingsPrompt = false; Dispatcher.UIThread.Post(Close); }
                }
                return;
            }
            e.Cancel = true; if (_shutdown) return;
            if (!CloseDialogsForOwner()) { ShowNotice("Saving settings…"); return; }
            _shutdown = true; _latencyStop?.Cancel(); _headerLatencyStop?.Cancel(); _probeStop?.Cancel(); _poll.Stop(); IsEnabled = false;
            try { if (_latencyTask is { } latency) await latency; if (_probeTask is { } probe) await probe; await PersistActivityLogAsync(); await _app.DisposeAsync(); DisposeTray(); _allowClose = true; Close(); }
            catch (Exception error) { IsEnabled = true; _shutdown = false; ShowError(error.Message); }
        };
        KeyDown += (_, e) =>
        {
            if(_startupAnimation is {} startup){if(e.Key==Key.Escape)startup.Finish();e.Handled=true;return;}
            if (_dialogs.Count > 0)
            {
                if(!_dialogs[^1].IsKeyboardFocusWithin) { _dialogs[^1].Focus();e.Handled=true; }
                return;
            }
            if (_providerDetails is not null && (e.Key == Key.Escape || e.Key == Key.Left && e.KeyModifiers.HasFlag(KeyModifiers.Alt))) { _ = ReturnProviderListAsync(); e.Handled = true; }
            else if (ActiveMoreSheet is null && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.O) { ImportFile(); e.Handled = true; }
        };
        ShowPage("Overview"); Refresh();
    }
    public override void Show()
    {
        if (IsVisible) return;
        if (_presentedOnce) { base.Show(); return; }
        if (Screens.Primary is { } screen && screen.Scaling > 0)
        {
            Width = Math.Min(Width, screen.WorkingArea.Width / screen.Scaling - 48); Height = Math.Min(Height, screen.WorkingArea.Height / screen.Scaling - 48);
            Position = new PixelPoint(screen.WorkingArea.X + (int)((screen.WorkingArea.Width - Width * screen.Scaling) / 2), screen.WorkingArea.Y + (int)((screen.WorkingArea.Height - Height * screen.Scaling) / 2));
        }
        _presentedOnce = true;
        NativeCaption.Attach(this); UpdateLayout(); Motion.SettlePaint(this);
        InitialPresentation = new FirstFramePresentation(this, _clientSurface);
        InitialPresentation.Prepare();
        try { base.Show(); }
        catch { InitialPresentation.Abort(); throw; }
    }
    internal FirstFramePresentation? InitialPresentation { get; private set; }
    private async Task StartRuntimeAsync()
    {
        if (_preview || _opened) return; _opened = true;
        CreateTray(); await _app.InitializeAsync(); _poll.Start();
        if (Program.PendingImportLink is { } importLink) ReceiveImportLink(importLink);
        await LoadActivityLogAsync();
        if (Environment.GetCommandLineArgs().Contains("--background") && _app.State.StartHidden && _tray is not null) Hide();
        if (_app.Ready && (Environment.GetCommandLineArgs().Contains("--connect-once") && _app.State.Capture == CaptureMode.Tun || _app.State.RestoreConnection && _app.State.LastConnected)) Connect();
        if (_app.Ready && !DesktopChecks.Active && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
            Dispatcher.UIThread.Post(() => _ = CheckStartupUpdatesAsync(), DispatcherPriority.Background);
    }
    internal void RestoreFromBackground()
    {
        if (_shutdown) return;
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized) WindowState = _restoreState;
        Show();
        if (_settingsDiscardDialog is { IsVisible: true } prompt) { prompt.Activate(); }
        else Activate();
    }
    private Menu BuildMenu()
    {
        MenuItem Item(string text, Action action) { var item = new MenuItem { [!MenuItem.HeaderProperty] = UiText.Bind(text) }; item.Click += (_, _) => action(); return item; }
        MenuItem Group(string text, params Control[] items)
        {
            var heading = new MenuItem { [!MenuItem.HeaderProperty] = UiText.Bind(text), ItemsSource = items };
            DialogOrigin.RegisterMenu(heading); return heading;
        }
        return new Menu { ItemsSource = new[]
        {
            Group("File", Item("Import nodes…", ImportText), Item("Import file…", ImportFile), Item("From clipboard", ImportClipboard), Item("Add subscription…", AddSubscription), Item("Export…", ExportNodes), new Separator(), Item("Exit", Exit)),
            Group("Connection", Item("Connect / Disconnect", Connect), Item("Test latency", TestLatency), Item("Close all connections", () => Run(() => _app.Engine.CloseConnectionsAsync()))),
            Group("View", NavigationRail.Pages.Select(page => Item(page, () => { _rail.Select(page); ShowPage(page); })).ToArray()),
            Group("Help", Item("Check for updates", ShowUpdates), Group("Language", Item("简体中文", () => SetLanguage("zh-CN")), Item("English", () => SetLanguage("en"))), Item("About TIZI", About)),
        } };
    }
    private Grid BuildCaption()
    {
        var menu = BuildMenu(); menu.HorizontalAlignment = HorizontalAlignment.Left;
        WindowDecorationProperties.SetElementRole(menu, WindowDecorationsElementRole.User);
        var drag = new Border { Background = Brushes.Transparent };
        WindowDecorationProperties.SetElementRole(drag, WindowDecorationsElementRole.TitleBar);
        Button Caption(string name, string path, WindowDecorationsElementRole role, Action action)
        {
            var button = new Button { Width = 46, Height = 32, MinHeight = 0, Padding = new(0), CornerRadius = new(0),
                Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(path), Stroke = Skin.CaptionInk, StrokeThickness = 1,
                    Width = 10, Height = 10, IsHitTestVisible = false }, Classes = { "plain" }, Tag = "caption-" + name };
            Avalonia.Automation.AutomationProperties.SetName(button, UiText.Get(name));
            ToolTip.SetTip(button, UiText.Get(name));
            WindowDecorationProperties.SetElementRole(button, role); button.Click += (_, _) => action(); return button;
        }
        var minimize = Caption("Minimize", "M0,5 L10,5", WindowDecorationsElementRole.MinimizeButton, () => WindowState = WindowState.Minimized);
        var maximize = Caption("Maximize", "M0,0 L10,0 L10,10 L0,10 Z", WindowDecorationsElementRole.MaximizeButton,
            () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        var close = Caption("Close", "M0,0 L10,10 M10,0 L0,10", WindowDecorationsElementRole.CloseButton, Close);
        PropertyChanged += (_, change) =>
        {
            if (change.Property != WindowStateProperty || maximize.Content is not Avalonia.Controls.Shapes.Path icon) return;
            icon.Data = Geometry.Parse(WindowState == WindowState.Maximized ? "M2,0 L10,0 L10,8 M0,2 L8,2 L8,10 L0,10 Z" : "M0,0 L10,0 L10,10 L0,10 Z");
            Avalonia.Automation.AutomationProperties.SetName(maximize, UiText.Get(WindowState == WindowState.Maximized ? "Restore" : "Maximize"));
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { minimize, maximize, close } };
        var caption = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Background = Skin.ChromeBrush, Children = { menu, drag, actions } };
        Grid.SetColumn(drag, 1); Grid.SetColumn(actions, 2); return caption;
    }
    private void Run(Func<Task> action) { if (_preview) return; _ = _app.RunAsync(action); }
    private void Connect()
    {
        if (_preview) return;
        if (_app.ConnectionState == ConnectionPhase.Connecting) { _app.CancelConnection(); return; }
        if (_app.ConnectionState is ConnectionPhase.Cancelling or ConnectionPhase.Disconnecting) return;
        if (!_app.Connected && _app.State.Capture == CaptureMode.Tun && !AppController.IsAdministrator)
        {
            Run(async () =>
            {
                if (NetworkEnvironment.OtherTunnels() is { Length: > 0 } tunnels) throw new InvalidOperationException(UiText.Format("Another VPN is using a default route: {0}. Turn off its TUN mode before enabling TIZI TUN.", string.Join(", ", tunnels)));
                await RestartAdministratorAsync(true);
            });
        }
        else Run(_app.ToggleAsync);
    }
    private async Task RestartAdministratorAsync(bool connectOnce = false)
    {
        if (_app.Connected) throw new InvalidOperationException(UiText.Get("Disconnect before restarting."));
        var path = Environment.ProcessPath ?? throw new IOException("Application path unavailable.");
        await _app.ApplyAsync(_app.State);
        try
        {
            using var elevated = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas", Arguments = connectOnce ? "--elevated --connect-once" : "--elevated" });
            if (elevated is null) throw new IOException(UiText.Get("Administrator restart could not be started."));
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223) { throw new IOException(UiText.Get("Administrator approval was cancelled. TIZI is still open.")); }
        Exit(); // Explicit restart must bypass the user's Close to tray preference.
    }
    internal void ShowError(string text) => (_providerSearchDialog is { IsVisible: true } search ? search.Notification : _notification).Show(UiText.Get(text));
    internal void ShowNotice(string text) => _notification.Show(UiText.Get(text), isError: false);
    internal void ShowPage(string page)
    {
        _aboutView=null;_aboutReturn=null;
        ActiveMoreSheet?.DismissImmediately();
        _providerDetails = null; _providerListActions = null;
        if (page == "Subscriptions") page = "Nodes";
        if (_page == "Settings" && page != "Settings" && SettingsDirty) _notification.Show(UiText.Get("Unsaved changes"), isError: false, isWarning: true);
        else if (page == "Settings" && _notification.IsWarning && _notification.Message == UiText.Get("Unsaved changes")) _notification.Dismiss();
        _page = page; _renderedPage = null; _chosen = page == "Nodes" ? _app.State.SelectedNode?.ToString() : null; _chosenRule = null; _visibleTargets.Clear(); _rows.Reset(); _pageIndex = 0; _search.Text = ""; _pageActions.Children.Clear(); _removeSelectionButton = _moveUpButton = _moveDownButton = _closeSelectionButton = _cancelProviderSearch = _retryProviderSearch = null; _providerSearchFeedback = null; UiText.Set(_heading, TextBlock.TextProperty, page == "Nodes" ? "Subscriptions and nodes" : page);
        _count.IsVisible = page is not ("Overview" or "Settings");
        _multiSelect = _selectionManagement = false; _selectedMany.Clear(); _selectedRules.Clear(); _removalBubble = null; if (_chosen is not null) _selectedMany.Add(_chosen);
        _paging.IsVisible = _previous.IsVisible = _next.IsVisible = _pagination.IsVisible = false;
        _search.IsVisible = page is "Nodes" or "Providers" or "Rules" or "Connections";
        _search.PlaceholderText = UiText.Get(page switch { "Nodes" => "Search saved nodes", "Providers" => "Filter saved providers", "Rules" => "Search rules", _ => "Search connections" });
        void Action(string label, Action action) => _pageActions.Children.Add(Ui.Button(label, action));
        void More(params (string Label, Action Action)[] entries)
        {
            var context = page == "Nodes" ? "Subscriptions and nodes" : "Providers";
            var button = Ui.Button("More", () => _ = ShowMoreActionsAsync(context, entries));
            button.Tag = "more-actions"; _pageActions.Children.Add(button);
        }
        switch (page)
        {
            case "Overview": break;
            case "Nodes":
                Action("Import nodes", ImportText);
                _latencyButton = Ui.Button(_latencyStop is null ? "Test latency" : "Cancel test", TestLatency); _latencyButton.MinWidth = 124; _pageActions.Children.Add(_latencyButton);
                AddRemoveAction(); More(("Clipboard", ImportClipboard), ("File / QR", ImportFile), ("Add node", AddNode), ("Export", ExportNodes), ("Update all subscriptions", () => Run(() => _app.UpdateAsync(providers: false))), ("Protocol compatibility", ShowProtocols)); break;
            case "Providers":
                Action("Search latest", SearchProviders); Action("Import directory", ImportProviderDirectory); AddRemoveAction(); More(("Add website", AddProvider), ("Remote directory", EditDirectory), ("Update now", () => Run(() => _app.UpdateAsync(subscriptions: false))), ("Directory settings", DirectorySettings), ("Web search settings", ConfigureSearch));
                _cancelProviderSearch = Ui.Button("Cancel search", _app.CancelSearch); _pageActions.Children.Add(_cancelProviderSearch);
                _retryProviderSearch = Ui.Button("Retry search · uses API credits", async () => { try { await _app.RetrySearchAsync(); } catch (OperationCanceledException) { } catch (Exception e) { if (_app.SearchError is null) ShowError(e.Message); } }); _pageActions.Children.Add(_retryProviderSearch);
                _providerSearchFeedback = Ui.Value("", true, 12); _providerSearchFeedback.TextWrapping = TextWrapping.Wrap; _providerSearchFeedback.MaxWidth = 480; _pageActions.Children.Add(_providerSearchFeedback); break;
            case "Rules": Action("Add rule", AddRule); Action("Import rules", ImportRules); _moveUpButton = Ui.Button("Move up", () => MoveRule(-1)); _moveDownButton = Ui.Button("Move down", () => MoveRule(1)); _pageActions.Children.Add(_moveUpButton); _pageActions.Children.Add(_moveDownButton); AddRemoveAction(); break;
            case "Connections": _closeSelectionButton = Ui.Button("Close selected", CloseSelectedConnection); _pageActions.Children.Add(_closeSelectionButton); Action("Close all", () => Run(() => _app.Engine.CloseConnectionsAsync())); break;
            case "Logs": _pageActions.Children.Add(_logFilter); Action("Clear current category", () => { _logs.Clear(SelectedLogCategory); _ = PersistActivityLogAsync(); RefreshLog(true); }); Action("Copy", CopyVisibleLog); Action("Log settings", LogOptions); break;
        }
        if (_removalBubble is not null)
        {
            var anchor = _pageActions.Children.OfType<Button>().FirstOrDefault(b => b.Tag is "more-actions") ?? _pageActions.Children.OfType<Button>().Last();
            var at = _pageActions.Children.IndexOf(anchor); _pageActions.Children.RemoveAt(at);
            _removalBubble.AttachAnchor(anchor); _pageActions.Children.Insert(at, _removalBubble);
        }
        _pageActionsHost.IsVisible = _pageActions.Children.Count > 0;
        if (page == "Overview") _pageHost.Content = _overviewScroll;
        else if (page == "Settings") _pageHost.Content = BuildSettings();
        else if (page == "Logs") RefreshLog();
        else _pageHost.Content = _listScroll;
        Refresh();
    }
    private void AddRemoveAction()
    {
        _removeSelectionButton = Ui.Button("Remove", RemoveSelected); _removeSelectionButton.Tag = "remove-selection";
        _removeSelectionButton.Background = new SolidColorBrush(Color.Parse("#D74756")); _removeSelectionButton.BorderThickness = default;
        _removalBubble = new RemovalBubble(_removeSelectionButton);
    }
    private void UpdateSelectionActions()
    {
        if (_multiSelect) ApplyMultipleSelectionVisuals();
        var visible = IsVisibleTarget(_chosen);
        var nodeSelected = visible && Guid.TryParse(_chosen, out var id) && _app.State.Nodes.Any(n => n.Id == id);
        var ruleIndex = SelectedRuleIndex;
        if (_moveUpButton is { } up) up.IsEnabled = !_multiSelect && visible && ruleIndex > 0;
        if (_moveDownButton is { } down) down.IsEnabled = !_multiSelect && visible && ruleIndex is { } at && at + 1 < _app.State.Rules.Count;
        if (_closeSelectionButton is { } close) close.IsEnabled = visible && _snapshot.Connections.Any(c => c.Id == _chosen);
        if (_removeSelectionButton is not { } remove) return;
        remove.IsEnabled = _page switch
        {
            "Nodes" => nodeSelected || visible && (SelectedSubscriptionId is not null || _chosen == "manual"),
            "Providers" => visible && Guid.TryParse(_chosen, out var provider) && _app.State.Providers.Any(p => p.Id == provider),
            "Rules" => visible && ruleIndex is not null,
            _ => false
        };
        remove.Content = Ui.Text(_page == "Nodes" ? _chosen == "manual" ? "Remove manual nodes" : SelectedSubscriptionId is not null ? "Remove subscription" : "Remove node" : "Remove");
        if (_multiSelect && _selectedMany.Count > 1) remove.Content = Ui.Value(UiText.Format("Remove selected ({0})", _selectedMany.Count));
        _removalBubble?.SetOpen(_selectionManagement && remove.IsEnabled);
    }
    internal async Task ShowMoreActionsAsync(string context, params (string Label, Action Action)[] entries)
    {
        if (ActiveMoreSheet is { IsVisible: true } active) { active.Open(); return; }
        var trigger = _removalBubble?.Anchor is { Tag: "more-actions" } anchor ? anchor : _pageActions.Children.OfType<Button>().SingleOrDefault(b => b.Tag is "more-actions");
        if (trigger is null) return;
        var sheet = new MoreActionsSheet(trigger, context, entries.Select(e => e.Label).ToArray());
        ActiveMoreSheet = sheet; _clientSurface.Children.Add(sheet); UpdateLayout(); sheet.Open();
        int? chosen;
        try { chosen = await sheet.Completion; }
        finally { _clientSurface.Children.Remove(sheet); if (ReferenceEquals(ActiveMoreSheet, sheet)) { ActiveMoreSheet = null; if (IsActive) trigger.Focus(); } }
        // Remove the overlay before dispatching an action that may open an editor or file picker.
        if (!_shutdown && chosen is int index && index >= 0 && index < entries.Length) entries[index].Action();
    }
    private void Refresh()
    {
        RefreshProfileStatus();
        if (_wasConnected != _app.Connected)
        { _wasConnected = _app.Connected; if (!_app.Connected) _latencyStop?.Cancel(); _lastUp = _lastDown = _lastPoll = 0; _snapshot = new ConnectionSnapshot(0, 0, []); _speed.Text = "↑ —     ↓ —"; _lastProbe = DateTimeOffset.MinValue; _probeHistory.Clear(); _monitor.Update(null, null, 0, _app.Connected, duration: _app.ConnectedDuration); }
        _syncing = true;
        // Busy still blocks conflicting input, but does not dim every row for a short save.
        Classes.Set("busy-stable", _app.Busy);
        _mode.SelectedIndex = (int)_app.State.Mode; _capture.SelectedIndex = (int)_app.State.Capture;
        _capture.IsEnabled = _app.Ready && !_app.Connected && !_app.Busy; _mode.IsEnabled = _app.Ready && !_app.Busy;
        _connect.SetState(_app.ConnectionState, _app.Busy || !_app.Ready, _app.ConnectionFailure);
        _monitor.IpQuality.Update(IpQualityPresentation.From(_app.IpQualityResult),_app.Connected);
        _footer.Text = _app.Updating ? UiText.Get("Updating subscriptions and website status…") : _app.Connected ? $"127.0.0.1:{_app.State.MixedPort}  ·  mihomo 1.19.32" : $"TIZI {AppVersion}  ·  Windows";
        _pageActions.IsEnabled = _app.Ready && !_app.Busy; _rows.IsEnabled = _app.Ready && !_app.Busy; _subscriptionTree.IsEnabled = _app.Ready && !_app.Busy;
        if (_page == "Providers")
        {
            if (_cancelProviderSearch is { } cancel) { cancel.IsVisible = _app.SearchBusy; cancel.IsEnabled = !_app.SearchCancelling; }
            if (_retryProviderSearch is { } retry) retry.IsVisible = !_app.SearchBusy && _app.SearchError is not null;
            if (_providerSearchFeedback is { } feedback)
            {
                feedback.Text = UiText.Get(_app.SearchCancelling ? "Cancelling search…" : _app.SearchBusy ? "Searching providers… Previous results remain available." : "");
                feedback.IsVisible = feedback.Text.Length > 0;
            }
        }
        UpdateSelectionActions();
        var selected = _app.State.Nodes.FirstOrDefault(n => n.Id == _app.State.SelectedNode);
        if (_probedNode != selected?.Id) { _probedNode = selected?.Id; _probeHistory.Clear(); _lastProbe = DateTimeOffset.MinValue; if (_app.Connected) _monitor.Delay("—", UiText.Get("Waiting for latency sample")); }
        _trayConnection.Header = UiText.Get(_app.ConnectionState switch { ConnectionPhase.Connecting => "Cancel connection", ConnectionPhase.Cancelling => "Cancelling connection…", ConnectionPhase.Disconnecting => "Disconnecting…", _ => _app.Connected ? "Disconnect" : "Connect" });
        _trayConnection.IsEnabled = _connect.IsEnabled;
        if (!_app.Busy) _nodeChoice.Update(_app.State);
        _nodeChoice.IsHitTestVisible = _nodeChoice.CanSwitch && _app.Ready && !_app.Busy;
        _syncing = false;
        if (!ReferenceEquals(_renderedState, _app.State) || _renderedPage != _page)
        { _renderedState = _app.State; _renderedPage = _page; RefreshRows(); RefreshInspector(); }
        if (_page == "Settings" && _app.Ready && !_settingsLoaded && _aboutView is null) { _settingsLoaded = true; _pageHost.Content = BuildSettings(); }
        Responsive();
    }
    private void RefreshRows()
    {
        if (_aboutView is not null) return;
        if (_page is "Overview" or "Settings" or "Logs") return;
        if (_page == "Providers" && _providerDetails is { } details)
        {
            if (!_app.State.Providers.Any(p => p.Id == details.ProviderId)) _ = ReturnProviderListAsync();
            return;
        }
        var query = _search.Text?.Trim() ?? "";
        if (_page == "Nodes")
        {
            _subscriptionTree.SetState(_app.State, _latencies, query); _subscriptionTree.SelectItem(_chosen); _pageHost.Content = _subscriptionScroll;
            ValidateSelection(); UpdateSelectionActions();
            _count.Text = UiText.Format("{0} subscriptions · {1} nodes", _app.State.Subscriptions.Count, _app.State.Nodes.Count);
            UpdateLatencyProgress();
            _previous.IsVisible = _next.IsVisible = _pagination.IsVisible = false; return;
        }
        if (_page == "Rules" && _chosenRule is not null)
        { var at = _app.State.Rules.FindIndex(r => ReferenceEquals(r, _chosenRule)); _chosen = at >= 0 ? at.ToString() : null; if (at < 0) _chosenRule = null; }
        IEnumerable<DisplayRow> source = _page switch
        {
            "Nodes" => _app.State.Nodes.Select(n => new DisplayRow(n.Id.ToString(), n.Name, $"{n.Type.ToUpperInvariant()}   {n.Server}:{n.Port}", _latencies.GetValueOrDefault(n.Id, "—"), n.CountryCode ?? NodeRegion.Match(n.Name), Latency: true)),
            "Subscriptions" => _app.State.Subscriptions.Select(s => new DisplayRow(s.Id.ToString(), s.Name, UiText.Get("Expires") + " · " + UiFormat.Expiry(s.Usage), UiText.Format("{0} nodes", _app.State.Nodes.Count(n => n.SubscriptionId == s.Id)))),
            "Providers" => ProviderDirectory.OrderByLowestPrice(_app.State.Providers, _app.State.Discovery.Search.Currency).Select(p => { var plan = ProviderDirectory.LowestPlan(p, _app.State.Discovery.Search.Currency); return new DisplayRow(p.Id.ToString(), p.Name, new Uri(p.Url).Host, UiFormat.ComparisonPrice(plan), Provider: p, PricePlan: plan); }),
            "Rules" => _app.State.Rules.Select((r, i) => new DisplayRow(i.ToString(), r.Type == "MATCH" ? UiText.Get("All remaining traffic") : r.Value, r.Type + (r.NoResolve ? " · no-resolve" : ""), UiText.Get(r.Action))),
            "Connections" => _snapshot.Connections.Select(c => new DisplayRow(c.Id, c.Host, c.Network.ToUpperInvariant() + " · " + c.Rule, $"↑ {Bytes(c.Upload)}  ↓ {Bytes(c.Download)}")),
            _ => [],
        };
        var rows = source.Where(row => query.Length == 0 || (row.Title + " " + row.Subtitle + " " + row.Badge).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _count.Text = _page is "Overview" or "Settings" ? "" : _page == "Logs" ? UiText.Format("{0} entries", _logs.Entries.Count) : UiText.Format("{0} items", rows.Count);
        if (_page == "Providers") _count.Text = (query.Length > 0 ? UiText.Format("{0} matching · ", rows.Count) : "") + UiText.Format("{0} cached · {1}/15 in latest search", _app.State.Providers.Count, _app.State.LastProviderSearch?.Results.Count ?? 0) + " · " + UiText.Get("Lowest monthly equivalent first") + " · " + (_app.State.ProvidersSearchedAt is { } searched ? UiText.Format("Last search {0}", searched.ToLocalTime().ToString("MM-dd HH:mm")) : UiText.Get("Saved directory"));
        if (_page == "Rules" && _app.State.RulesUpdatedAt is { } updated) _count.Text += " · " + _app.State.RuleSourceName + " · " + updated.ToLocalTime().ToString("MM-dd HH:mm");
        _pageIndex = Math.Clamp(_pageIndex, 0, Math.Max(0, (rows.Count - 1) / 100));
        var shown = rows.Skip(_pageIndex * 100).Take(100).ToArray();
        _visibleTargets.Clear(); foreach (var row in shown) _visibleTargets.Add(row.Id);
        ValidateSelection(); _rows.SetRows(shown, _chosen); UpdateSelectionActions();
        _paging.IsVisible = _previous.IsVisible = _next.IsVisible = _pagination.IsVisible = rows.Count > 100; _previous.IsEnabled = _pageIndex > 0; _next.IsEnabled = (_pageIndex + 1) * 100 < rows.Count;
        _pagination.Text = $"{_pageIndex + 1} / {Math.Max(1, (rows.Count + 99) / 100)}";
        if (_page is not ("Overview" or "Logs" or "Settings"))
        {
            if (rows.Count == 0)
            {
                var empty = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 420 };
                var label = Ui.Text(query.Length > 0 ? "No matching items" : _page switch { "Nodes" => "Import nodes to get started", "Subscriptions" => "Add your subscription address", "Providers" => "Add websites or a remote directory", "Rules" => "Unmatched traffic uses the selected node", "Connections" => "No active connections", _ => "No items" }, true);
                label.TextWrapping = TextWrapping.Wrap; label.TextAlignment = TextAlignment.Center; empty.Children.Add(label);
                _pageHost.Content = empty;
            }
            else if (_pageHost.Content != _listScroll) _pageHost.Content = _listScroll;
        }
    }
    private void RefreshInspector()
    {
        if (_page == "Providers" || (_page == "Nodes" && SelectedSubscriptionId is not null))
        { _inspector.Children.Clear(); foreach (var child in BuildSelectedDetails().Children.ToArray()) { var parent = child.Parent as Panel; parent?.Children.Remove(child); _inspector.Children.Add(child); } Responsive(); return; }
        _inspector.Children.Clear(); var title = Ui.Text("Connection", true, 11); title.FontWeight = FontWeight.SemiBold; _inspector.Children.Add(title);
        var status = Ui.Text(_app.Connected ? "Connected" : "Disconnected", size: 17); status.FontWeight = FontWeight.SemiBold; _inspector.Children.Add(status);
        void Detail(string label, string value)
        {
            var content = Ui.Value(value); content.TextWrapping = TextWrapping.Wrap; content.TextTrimming = TextTrimming.None;
            if (label == "Latency") content.Foreground = LatencyTone.For(value);
            if (label == "Uploaded") _uploadValue = content; if (label == "Downloaded") _downloadValue = content;
            _inspector.Children.Add(new StackPanel { Spacing = 5, Children = { Ui.Text(label, true, 11), content } });
        }
        Detail("Mode", UiText.Get(_app.State.Mode.ToString())); Detail("Capture", UiText.Get(_app.State.Capture switch { CaptureMode.Tun => "TUN", CaptureMode.LocalOnly => "Local proxy", _ => "System proxy" }));
        Detail("Local address", "127.0.0.1:" + _app.State.MixedPort); _inspector.Children.Add(Ui.Line());
        if (_page == "Nodes" && Guid.TryParse(_chosen, out var id) && _app.State.Nodes.FirstOrDefault(n => n.Id == id) is { } node)
        { Detail("Selected node", node.Name); Detail("Protocol", node.Type.ToUpperInvariant()); Detail("Server", node.Server + ":" + node.Port); Detail("Latency", _latencies.GetValueOrDefault(node.Id, "—")); }
        else if (_page == "Providers" && Guid.TryParse(_chosen, out var pid) && _app.State.Providers.FirstOrDefault(p => p.Id == pid) is { } site)
        { Detail("Website", site.Name); Detail("Address", site.Url); Detail("Last check", site.CheckedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—"); Detail("Source", site.Source is null ? UiText.Get("Added manually") : new Uri(site.Source).Host); }
        else if (_page == "Subscriptions" && Guid.TryParse(_chosen, out var sid) && _app.State.Subscriptions.FirstOrDefault(s => s.Id == sid) is { } sub)
        { Detail("Subscription", sub.Name); Detail("Last update", sub.UpdatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—"); }
        else if (_page == "Connections" && _snapshot.Connections.FirstOrDefault(c => c.Id == _chosen) is { } conn) { Detail("Process", conn.Process); Detail("Chain", conn.Chain); Detail("Rule", conn.Rule); }
        _inspector.Children.Add(Ui.Line()); Detail("Uploaded", Bytes(_snapshot.Upload)); Detail("Downloaded", Bytes(_snapshot.Download));
        if (_page == "Providers") Detail("Update interval", UiText.Format("Every {0} minutes", _app.State.UpdateMinutes));
    }
    private Control BuildSettings() => BuildSettingsCards();
    private void SetLanguage(string language)
    {
        UiText.Language = language; if (!_preview) UiText.SaveLanguage(language); _search.PlaceholderText = UiText.Get("Search"); ShowPage(_page);
    }
    private void ImportText() { if (!_preview) _ = CreateImportEditor().ShowDialog(this); }
    internal EditorDialog CreateImportEditor() => CreateEditor("Import nodes", [new("text", "Subscription URL, node links, JSON, YAML or .conf", Multiline: true)], (values, session) => ImportContentAsync(values["text"], session), "Import");
    private async void ImportFile()
    {
        if (_preview) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = UiText.Get("Import nodes"), AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Proxy configuration / QR") { Patterns = ["*.yaml", "*.yml", "*.json", "*.conf", "*.txt", "*.url", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"] }, FilePickerFileTypes.All] });
            if (files.Count == 0) return; await using var stream = await files[0].OpenReadAsync(); var extension = Path.GetExtension(files[0].Name).ToLowerInvariant();
            var image = extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";
            await ImportPayloadAsync(await ReadBoundedAsync(stream, image ? QrImport.MaxBytes : NodeImport.MaxBytes), image);
        }
        catch (ImportReviewCancelledException) { }
        catch (Exception e) { ShowError(e.Message); }
    }
    private void AddSubscription() => OpenSubscriptionDialog("");
    private void OpenSubscriptionDialog(string url, string name = "") { if (!_preview) _ = CreateSubscriptionEditor(url, name).ShowDialog(this); }
    internal EditorDialog CreateSubscriptionEditor(string url, string name = "") => CreateEditor("Add subscription", [new("url", "Subscription URL", url, Secret: true), new("name", "Name (optional)", name)], async (values, session) =>
    { var subscription = ImportDocument.RemoteSubscription(values["url"]) ?? throw new FormatException("Enter a valid HTTP or HTTPS address without a fragment or embedded login."); var name = string.IsNullOrWhiteSpace(values["name"]) ? subscription.Name : values["name"]; Required(name); await _app.AddSubscriptionAsync(name, subscription.Url, document => ReviewImportAsync(document, session.Token), session.Token, session.BeginCommit); }, "Add");
    private void AddProvider() => Dialog("Add website", [new("name", "Name"), new("url", "Website URL", "https://"), new("region", "Region (optional)")], async values =>
    {
        Required(values["name"]); var uri = SourceUpdates.WebUri(values["url"]); if (_app.State.Providers.Count >= 500) throw new FormatException("Maximum 500 websites.");
        if (_app.State.Providers.Any(p => SourceUpdates.Normalize(p.Url).Equals(SourceUpdates.Normalize(uri.AbsoluteUri), StringComparison.OrdinalIgnoreCase))) throw new FormatException(UiText.Get("This website is already listed."));
        await _app.ApplyAsync(_app.State with { Providers = [.. _app.State.Providers, new ProviderSite { Name = values["name"], Url = uri.AbsoluteUri, Region = values["region"] }] });
    }, "Add");
    private void EditDirectory() => Dialog("Remote directory", [new("url", "Directory JSON URL", _app.State.CatalogUrl)], async values =>
    {
        if (values["url"].Length > 0) SourceUpdates.WebUri(values["url"]);
        await _app.ApplyAsync(_app.State with { CatalogUrl = values["url"] });
        await _app.UpdateAsync();
    });
    private void OpenProvider()
    {
        if (_chosen is null || !Guid.TryParse(_chosen, out var id) || _app.State.Providers.FirstOrDefault(p => p.Id == id) is not { } site) return;
        Process.Start(new ProcessStartInfo(SourceUpdates.WebUri(site.Url).AbsoluteUri) { UseShellExecute = true });
    }
    private void AddNode() { if (!_preview) OpenNodeEditor(null); }
    private void EditNode()
    {
        if (!Guid.TryParse(_chosen, out var id) || _app.State.Nodes.FirstOrDefault(n => n.Id == id) is not { } node) return;
        if (!_preview) OpenNodeEditor(node);
    }
    private void OpenNodeEditor(ProxyNode? original)
    {
        if (OpenDialogs.OfType<NodeEditor>().FirstOrDefault() is { } open) { open.Activate(); return; }
        _ = new NodeEditor(edited => _app.ExecuteAsync(async () =>
        {
            if (original is null) { await _app.ApplyImportAsync(new([edited], [], [], [], "Manual")); return; }
            var current = _app.State.Nodes.FirstOrDefault(n => n.Id == original.Id);
            if (current is null || !JsonNode.DeepEquals(current.Options, original.Options)) throw new InvalidOperationException("This node has changed. Reopen it to edit.");
            await _app.ApplyAsync(_app.State with { Nodes = _app.State.Nodes.Select(n => n.Id == original.Id ? edited : n).ToList() }, true);
        }), original).ShowDialog(this);
    }
    private void AddRule() { if (!_preview) OpenRuleEditor(null); }
    private void EditRule(int index) { if (_app.Ready && !_app.Busy) OpenRuleEditor(index); }
    private void OpenRuleEditor(int? index)
    {
        if (ActiveRuleEditor is { IsVisible: true } open) { open.Activate(); return; }
        var editor = ActiveRuleEditor = CreateRuleEditor(index);
        editor.Closed += (_, _) => { if (ActiveRuleEditor == editor) ActiveRuleEditor = null; };
        _ = editor.ShowDialog(this);
    }
    internal RuleEditor CreateRuleEditor(int? index)
    {
        var original = index is { } position ? _app.State.Rules[position] : null;
        return new RuleEditor(original ?? new("DOMAIN-SUFFIX", "", "TIZI"), rule => _app.ExecuteAsync(async () =>
        {
            var rules = _app.State.Rules.ToList();
            if (index is { } at)
            {
                if (at >= rules.Count || rules[at] != original) throw new InvalidOperationException(UiText.Get("This rule has changed. Reopen it to edit."));
                rules[at] = rule;
            }
            else rules = RuleImport.Merge(rules, [rule]);
            await _app.ApplyAsync(_app.State with { Rules = rules }, true);
        }), adding: index is null);
    }
    private void ImportRules() => Dialog("Import rules", [new("rules", "Rule URL, Clash YAML / JSON, or one rule per line", Multiline: true)], async (values, session) =>
    {
        var text = values["rules"]; var source = ImportDocument.RemoteUrl(text);
        if (source is not null) { using var http = new HttpClient(); text = await new SourceUpdates(http).DownloadAsync(source, NodeImport.MaxBytes, session.Token); }
        var imported = await Task.Run(() => RuleImport.Parse(text), session.Token);
        if (imported.PolicyMappings.Count > 0 && !await ReviewImportAsync(new([], imported.Rules, [], [UiText.Get("Proxy groups use their default action; proxy traffic follows the selected TIZI node."), .. imported.PolicyMappings.Select(p => p.Key + " → " + UiText.Get(p.Value))], "Rules"), session.Token)) throw new ImportReviewCancelledException();
        session.BeginCommit();
        await _app.ApplyAsync(_app.State with { Rules = RuleImport.Merge(_app.State.Rules, imported.Rules), RuleSourceName = source is null ? "" : new Uri(source).Host, RulesUpdatedAt = DateTimeOffset.Now }, true);
    }, "Import");
    private void MoveRule(int step) => Run(async () =>
    {
        if (SelectedRuleIndex is not { } index) return; var rules = _app.State.Rules.ToList(); var target = index + step; if (target < 0 || target >= rules.Count) return;
        if (rules[index].Type == "MATCH" || rules[target].Type == "MATCH") throw new InvalidOperationException(UiText.Get("A final rule must be the last rule."));
        (rules[index], rules[target]) = (rules[target], rules[index]); await _app.ApplyAsync(_app.State with { Rules = rules }, true); _chosen = target.ToString(); RefreshRows();
    });
    private async void RemoveSelected()
    {
        if (_multiSelect && _selectedMany.Count > 1) { RemoveMultipleItems(); return; }
        if (_preview || !IsVisibleTarget(_chosen) || _chosen is not { } target) return;
        var page = _page; var subscriptionId = SelectedSubscriptionId;
        var manual = page == "Nodes" && target == "manual";
        var manualTargets = manual ? ManualNodes(_app.State).Select(n => n.Id).ToHashSet() : [];
        if (page == "Nodes" && subscriptionId is { } sid && _app.State.Subscriptions.FirstOrDefault(s => s.Id == sid) is { } sub)
            if (!await ConfirmRemovalAsync(sub.Name, _app.State.Nodes.Count(n => n.SubscriptionId == sid))) return;
        if (manual && manualTargets.Count > 1)
            if (!await ConfirmRemovalAsync(UiText.Get("Manual nodes"), manualTargets.Count, true)) return;
        if (_page != page || _chosen != target || !IsVisibleTarget(target)) return;
        Run(async () =>
        {
            var before = _app.State; var state = before; Func<AppState, AppState>? undo = null;
            if (page == "Nodes")
            {
                if (manual && !manualTargets.SetEquals(ManualNodes(before).Select(n => n.Id)))
                    throw new InvalidOperationException(UiText.Get("The node group changed. Reopen its menu before removing it."));
                var removed = manual ? ManualNodes(before).ToArray() : subscriptionId is { } ownerId ? before.Nodes.Where(n => n.SubscriptionId == ownerId).ToArray()
                    : Guid.TryParse(target, out var nodeId) ? before.Nodes.Where(n => n.Id == nodeId).ToArray() : [];
                var removedIds = removed.Select(n => n.Id).ToHashSet();
                if (_app.Connected && before.SelectedNode is { } active && removedIds.Contains(active))
                    throw new InvalidOperationException(UiText.Get("Disconnect or switch nodes before removing the current node."));
                var nodes = before.Nodes.Where(n => !removedIds.Contains(n.Id)).ToList();
                var subscription = before.Subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
                state = before with { Nodes = nodes, Subscriptions = subscription is null ? before.Subscriptions : before.Subscriptions.Where(s => s.Id != subscription.Id).ToList(), SelectedNode = removedIds.Contains(before.SelectedNode ?? Guid.Empty) ? null : before.SelectedNode };
                undo = current =>
                {
                    if (current.Nodes.Any(n => removedIds.Contains(n.Id)) || subscription is not null && current.Subscriptions.Any(s => s.Id == subscription.Id || SourceUpdates.Normalize(s.Url) == SourceUpdates.Normalize(subscription.Url)))
                        throw new InvalidOperationException(UiText.Get("Items changed after removal. Recover an encrypted backup instead."));
                    var restored = current.Nodes.ToList(); foreach (var node in removed) restored.Insert(Math.Min(before.Nodes.IndexOf(node), restored.Count), node);
                    var subscriptions = current.Subscriptions.ToList(); if (subscription is not null) subscriptions.Insert(Math.Min(before.Subscriptions.IndexOf(subscription), subscriptions.Count), subscription);
                    return current with { Nodes = restored, Subscriptions = subscriptions, SelectedNode = current.SelectedNode ?? before.SelectedNode };
                };
            }
            else if (page == "Providers" && Guid.TryParse(target, out var providerId) && before.Providers.FirstOrDefault(p => p.Id == providerId) is { } provider)
            {
                state = before with { Providers = before.Providers.Where(p => p.Id != providerId).ToList() };
                undo = current =>
                {
                    if (current.Providers.Any(p => p.Id == provider.Id || SourceUpdates.Normalize(p.Url) == SourceUpdates.Normalize(provider.Url))) throw new InvalidOperationException(UiText.Get("Items changed after removal. Recover an encrypted backup instead."));
                    var restored = current.Providers.ToList(); restored.Insert(Math.Min(before.Providers.IndexOf(provider), restored.Count), provider); return current with { Providers = restored };
                };
            }
            else if (page == "Rules" && SelectedRuleIndex is { } index)
            {
                var removed = before.Rules[index]; var remaining = before.Rules.Where((_, i) => i != index).ToList(); state = before with { Rules = remaining };
                undo = current =>
                {
                    if (!current.Rules.SequenceEqual(remaining)) throw new InvalidOperationException(UiText.Get("Items changed after removal. Recover an encrypted backup instead."));
                    var restored = current.Rules.ToList(); restored.Insert(index, removed); return current with { Rules = restored };
                };
            }
            if (undo is null) return;
            _chosen = null; _chosenRule = null; await _app.ApplyAsync(state, true); OfferUndo(undo);
        });
    }
    private Task? _latencyTask, _probeTask;
    private CancellationTokenSource? _probeStop;
    private void TestLatency() { if (_latencyStop is not null) _latencyStop.Cancel(); else _latencyTask = TestLatencyAsync(); }
    private CancellationTokenSource? _headerLatencyStop;
    internal Func<ProxyNode, CancellationToken, Task<int>>? HeaderLatencyFixture { get; set; }
    private async void StartHeaderLatencyTest()
    {
        if (_shutdown || _latencyStop is not null || _headerLatencyStop is not null || _preview && HeaderLatencyFixture is null) return;
        using var stop = new CancellationTokenSource(); _headerLatencyStop = stop;
        try { _latencyTask = TestLatencyAsync(HeaderLatencyFixture, _nodeChoice.Candidates.ToArray(), stop.Token); await _latencyTask; }
        finally
        {
            _headerLatencyStop = null;
            if (stop.IsCancellationRequested && _nodeChoice.IsDropDownOpen && !_shutdown) StartHeaderLatencyTest();
        }
    }
    internal bool TestingLatency => _latencyStop is not null;
    internal async Task TestLatencyAsync(Func<ProxyNode, CancellationToken, Task<int>>? fixture = null, ProxyNode[]? requestedNodes = null, CancellationToken extraCancellation = default)
    {
        if (_shutdown || _latencyStop is not null) return;
        if (DesktopChecks.Active && fixture is null) { ShowError("Desktop verification mode: node connections are disabled."); return; }
        var nodes = requestedNodes ?? _app.State.Nodes.ToArray(); if (nodes.Length == 0) { ShowError(UiText.Get("Import a subscription or add a node")); return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_app.Cancellation, extraCancellation); _latencyStop = cancellation;
        var options = _app.State.Latency; using var limit = new SemaphoreSlim(options.Concurrency); _latencyDone = 0; _latencyTotal = nodes.Length;
        var state = _app.State; var url = state.DelayUrl;
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        refresh.Tick += (_, _) => { _nodeChoice.UpdateLatencies(_latencies); if (_page == "Nodes") { _subscriptionTree.UpdateLatencies(_latencies); UpdateLatencyProgress(); } };
        refresh.Start(); UpdateLatencyProgress();
        try
        {
            await using var probe = options.Method == "HTTP" && !_app.Connected && fixture is null
                ? await NodeLatencyProbe.StartAsync(state with { Nodes = nodes.ToList() }, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "engine", "mihomo.exe")), Path.Combine(UserData.DirectoryPath, "latency"), cancellation.Token) : null;
            await Task.WhenAll(nodes.Select(async node =>
        {
            await limit.WaitAsync(cancellation.Token); string value;
            try { value = (await (fixture?.Invoke(node, cancellation.Token) ?? (options.Method == "HTTP" ? probe is null ? _app.Engine.DelayAsync(node, url, cancellation.Token, options.TimeoutMs) : probe.MeasureAsync(node, url, options.TimeoutMs, cancellation.Token) : NodeTransport.MeasureAsync(node, options, cancellation.Token)))) + " ms"; }
            catch (NotSupportedException error) { value = UiText.Get(error.Message); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception) { value = UiText.Get("Timeout"); }
            finally { limit.Release(); }
            _latencies[node.Id] = value; _latencyDone++;
            }));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ShowError(error.Message); }
        finally
        {
            refresh.Stop(); _latencyStop = null; _nodeChoice.UpdateLatencies(_latencies);
            if (!_shutdown && _page == "Nodes") { _subscriptionTree.UpdateLatencies(_latencies); UpdateLatencyProgress(); RefreshInspector(); }
        }
    }
    private void UpdateLatencyProgress()
    {
        if (_page != "Nodes") return;
        var label = TestingLatency ? "Cancel test" : "Test latency";
        if (_latencyButton is not null && !Equals(_latencyButton.Tag, label)) { _latencyButton.Tag = label; _latencyButton.Content = Ui.Text(label); }
        if (TestingLatency) _count.Text = UiText.Format("{0} subscriptions · {1} nodes", _app.State.Subscriptions.Count, _app.State.Nodes.Count) + " · " + UiText.Format("Testing {0}/{1}", _latencyDone, _latencyTotal);
        else if (_latencyTotal > 0) _count.Text = UiText.Format("{0} subscriptions · {1} nodes", _app.State.Subscriptions.Count, _app.State.Nodes.Count) + " · " + UiText.Format("Tested {0}/{1}", _latencyDone, _latencyTotal);
    }
    private void Dialog(string title, IReadOnlyList<EditorField> fields, Func<Dictionary<string, string>, Task> apply, string action = "Save")
    { if (!_preview) _ = CreateEditor(title, fields, apply, action).ShowDialog(this); }
    private void Dialog(string title, IReadOnlyList<EditorField> fields, Func<Dictionary<string, string>, EditorSession, Task> apply, string action = "Save")
    { if (!_preview) _ = CreateEditor(title, fields, apply, action).ShowDialog(this); }
    private EditorDialog CreateEditor(string title, IReadOnlyList<EditorField> fields, Func<Dictionary<string, string>, Task> apply, string action)
    {
        var dialog = new EditorDialog(title, fields, values => _app.ExecuteAsync(() => apply(values)), action); _editor = dialog;
        dialog.Closed += (_, _) => { if (_editor == dialog) _editor = null; if (title is "Import nodes" or "Add subscription" && dialog.Applied) ShowImportResult(); };
        return dialog;
    }
    private EditorDialog CreateEditor(string title, IReadOnlyList<EditorField> fields, Func<Dictionary<string, string>, EditorSession, Task> apply, string action)
    {
        var dialog = new EditorDialog(title, fields, (values, session) => _app.ExecuteAsync(() => apply(values, session)), action, _app.Cancellation); _editor = dialog;
        dialog.Closed += (_, _) => { if (_editor == dialog) _editor = null; if (title is "Import nodes" or "Add subscription" && dialog.Applied) ShowImportResult(); };
        return dialog;
    }
    private void About() => _ = OpenAboutAsync();
    private void RefreshLog(bool force = false)
    {
        _logs.Settings = _app.State.Logs; _logs.Trim(DateTimeOffset.Now);
        _count.Text = UiText.Format("{0} entries", _logs.Entries.Count(e => SelectedLogCategory is null || e.Category == SelectedLogCategory));
        _logView.Update(_logs.Entries, SelectedLogCategory, force);
        if (_pageHost.Content != _logView) _pageHost.Content = _logView;
    }
    private async Task PollAsync()
    {
        _monitor.SetDuration(_app.ConnectedDuration);
        if ((_logDirty || _logView.Pending) && _page == "Logs" && _aboutView is null) { _logDirty = false; RefreshLog(); }
        if (DateTimeOffset.Now - _lastLogSave > TimeSpan.FromMinutes(1)) { _lastLogSave = DateTimeOffset.Now; await PersistActivityLogAsync(); }
        if (!_app.Connected || _polling || _shutdown) return; _polling = true;
        try
        {
            var snapshot = await _app.Engine.ConnectionsAsync(); if (!_app.Connected || _shutdown) return;
            var now = Stopwatch.GetTimestamp(); var seconds = _lastPoll == 0 ? 1 : Math.Max(0.1, (now - _lastPoll) / (double)Stopwatch.Frequency);
            var up = _lastPoll == 0 ? 0 : Math.Max(0, snapshot.Upload - _lastUp); var down = _lastPoll == 0 ? 0 : Math.Max(0, snapshot.Download - _lastDown);
            _speed.Text = $"↑ {UiFormat.Speed((long)(up / seconds))}     ↓ {UiFormat.Speed((long)(down / seconds))}"; _lastUp = snapshot.Upload; _lastDown = snapshot.Download; _lastPoll = now; _snapshot = snapshot;
            if (_uploadValue is not null) _uploadValue.Text = Bytes(snapshot.Upload); if (_downloadValue is not null) _downloadValue.Text = Bytes(snapshot.Download);
            _monitor.Update((long)(up / seconds), (long)(down / seconds), snapshot.Connections.Count, true, duration: _app.ConnectedDuration);
            if (DateTimeOffset.Now - _lastProbe >= LatencySampleInterval) _probeTask = ProbeSelectedAsync();
            if (_page == "Connections") RefreshRows();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or ObjectDisposedException) { }
        finally { _polling = false; }
    }
    private static void Required(string text) { if (string.IsNullOrWhiteSpace(text) || text.Length > 200) throw new FormatException(UiText.Get("Enter a name (maximum 200 characters).")); }
    private static string Bytes(long value) => value >= 1024 * 1024 * 1024 ? $"{value / (1024d * 1024 * 1024):0.0} GB" : value >= 1024 * 1024 ? $"{value / (1024d * 1024):0.0} MB" : value >= 1024 ? $"{value / 1024d:0.0} KB" : value + " B";
}
