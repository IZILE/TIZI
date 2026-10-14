using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Tizi.Core;

namespace Tizi.Desktop;

/// <summary>Subscriptions own their node rows; quota appears once at the group header.</summary>
internal sealed class SubscriptionTree : StackPanel
{
    private readonly Dictionary<string, GroupCard> _groups = [];
    private string? _chosen;
    private bool _hasSelection;
    internal event Action<Guid>? NodeChosen;
    internal event Action<Guid>? NodeActivated;
    internal event Action<Guid, string>? NodeActionRequested;
    internal event Action<Guid, string>? SubscriptionActionRequested;
    internal event Action<Guid>? SubscriptionChosen;
    internal event Action? ManualGroupChosen;
    internal event Action<string>? ManualGroupActionRequested;
    internal event Action<Guid>? UrlRequested;
    internal event Action<Guid>? UpdateRequested;
    internal event Action? VisibleItemsChanged;
    internal IReadOnlyCollection<GroupCard> Groups => _groups.Values;

    internal SubscriptionTree() { Spacing = 14; }
    internal bool ContainsVisible(string? id) => id is not null && Children.OfType<GroupCard>().Any(card =>
        id == (card.Key == "manual" ? "manual" : "subscription:" + card.Key) || card.NodesShown && card.NodeRows.Contains(id));
    internal void SelectSubscription(Guid? id) => SelectItem(id is { } subscription ? "subscription:" + subscription : null);
    internal void SelectItem(string? id)
    {
        _chosen = id; _hasSelection = true;
        foreach (var card in _groups.Values)
        {
            var selected = id == (card.Key == "manual" ? "manual" : "subscription:" + card.Key);
            card.SelectSubscription(selected);
            AutomationProperties.SetHelpText(card.Header, selected ? UiText.Get("Selected") : "");
            card.SelectNode(Guid.TryParse(id, out var node) ? node : null);
        }
    }
    internal void UpdateLatencies(IReadOnlyDictionary<Guid, string> values) { foreach (var card in _groups.Values) card.NodeRows.UpdateBadges(values); }
    internal void SelectItems(IReadOnlySet<string> ids)
    {
        foreach(var card in _groups.Values)
        { var selected=ids.Contains(card.Key == "manual" ? "manual" : "subscription:"+card.Key);card.SelectSubscription(selected);card.NodeRows.SelectMany(selected ? new HashSet<string>() : ids); }
    }
    internal void SetState(AppState state, IReadOnlyDictionary<Guid, string> latencies, string query)
    {
        bool Match(ProxyNode n) => query.Length == 0 || (n.Name + " " + n.Type + " " + n.Server).Contains(query, StringComparison.OrdinalIgnoreCase);
        var entries = state.Subscriptions.Select(s => (Key: s.Id.ToString(), Sub: (Subscription?)s, Nodes: state.Nodes.Where(n => n.SubscriptionId == s.Id).ToList())).ToList();
        var known = state.Subscriptions.Select(s => s.Id).ToHashSet();
        var manual = state.Nodes.Where(n => n.SubscriptionId is null || !known.Contains(n.SubscriptionId.Value)).ToList();
        if (manual.Count > 0) entries.Add(("manual", null, manual));
        var visible = new List<Control>();
        foreach (var entry in entries)
        {
            var titleMatch = query.Length == 0 || entry.Sub?.Name.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
            var nodes = entry.Nodes.Where(n => titleMatch || Match(n)).ToList();
            if (!titleMatch && nodes.Count == 0) continue;
            if (!_groups.TryGetValue(entry.Key, out var card))
            {
                card = new GroupCard(entry.Key, entry.Sub, entry.Nodes.Any(n => n.Id == state.SelectedNode)); _groups.Add(entry.Key, card);
                card.NodeChosen += id => NodeChosen?.Invoke(id);
                card.NodeActivated += id => NodeActivated?.Invoke(id);
                card.NodeActionRequested += (id, action) => NodeActionRequested?.Invoke(id, action);
                card.SubscriptionActionRequested += action => { if (entry.Sub is { } s) SubscriptionActionRequested?.Invoke(s.Id, action); else ManualGroupActionRequested?.Invoke(action); };
                card.Selected += () => { if (entry.Sub is { } s) SubscriptionChosen?.Invoke(s.Id); else ManualGroupChosen?.Invoke(); };
                card.Details += () => { if (entry.Sub is { } s) UrlRequested?.Invoke(s.Id); };
                card.Update += () => { if (entry.Sub is { } s) UpdateRequested?.Invoke(s.Id); };
                card.VisibleItemsChanged += () => VisibleItemsChanged?.Invoke();
            }
            card.Set(entry.Sub, nodes, state.SelectedNode, latencies, query.Length > 0);
            visible.Add(card);
        }
        if (!Children.SequenceEqual(visible)) { Children.Clear(); foreach (var card in visible) Children.Add(card); }
        var active = entries.Select(e => e.Key).ToHashSet(); foreach (var key in _groups.Keys.Where(k => !active.Contains(k)).ToArray()) _groups.Remove(key);
        SelectItem(_hasSelection ? _chosen : state.SelectedNode?.ToString());
        if (visible.Count == 0) Children.Add(Ui.Text(query.Length > 0 ? "No matching items" : "Import a subscription or add a node", true));
    }
    internal sealed class GroupCard : Border
    {
        private readonly TextBlock _title = Ui.Value("", size: 17), _count = Ui.Value("", true, 12), _expiry = Ui.Value("", true, 13), _usage = Ui.Value("", true, 13), _pageLabel = Ui.Value("", true, 12);
        private readonly ProgressBar _quota = new() { Height = 6, Minimum = 0, Maximum = 100, CornerRadius = new(3), Foreground = Skin.AccentBrush, IsHitTestVisible = false };
        private readonly RotateTransform _rotation = new();
        private readonly AnimatedReveal _reveal;
        private readonly MovingRows _nodes = new(nodeGrid: true);
        private readonly StackPanel _body = new() { Spacing = 10, Margin = new(0, 14, 0, 0) };
        private readonly Button _previous, _next;
        private List<ProxyNode> _source = [];
        private IReadOnlyDictionary<Guid, string> _latencies = new Dictionary<Guid, string>();
        private Guid? _activeNode, _operationNode;
        private int _page;
        private bool _searching;
        private bool _expanded;
        private readonly WrapPanel _details;
        internal string Key { get; }
        internal bool Expanded => _expanded;
        internal bool NodesShown => _expanded || _searching;
        internal Button Header { get; }
        internal Button Disclosure { get; }
        internal bool IsSelected { get; private set; }
        internal MovingRows NodeRows => _nodes;
        internal AnimatedReveal Reveal => _reveal;
        internal event Action<Guid>? NodeChosen;
        internal event Action<Guid>? NodeActivated;
        internal event Action<Guid, string>? NodeActionRequested;
        internal event Action<string>? SubscriptionActionRequested;
        internal event Action? Selected, Details, Update;
        internal event Action? VisibleItemsChanged;
        internal GroupCard(string key, Subscription? subscription, bool expanded)
        {
            Key = key; _expanded = expanded; CornerRadius = new(14); Background = Skin.ChromeBrush; Padding = new(16);
            _nodes.ContextActions = id => Guid.TryParse(id, out var node)
                ? [("Switch node", () => NodeActivated?.Invoke(node)), ("Edit node", () => NodeActionRequested?.Invoke(node, "edit")), ("Remove node", () => NodeActionRequested?.Invoke(node, "remove"))] : [];
            if (subscription is not null) ContextMenu = ItemContextMenu.Create(this, () => Selected?.Invoke(),
                [("Edit subscription", () => SubscriptionActionRequested?.Invoke("edit")),
                 ("Subscription URL", () => Details?.Invoke()), ("Update", () => Update?.Invoke()),
                 ("All subscription update settings", () => SubscriptionActionRequested?.Invoke("settings")),
                 ("Remove subscription", () => SubscriptionActionRequested?.Invoke("remove"))]);
            else ContextMenu = ItemContextMenu.Create(this, () => Selected?.Invoke(),
                [("Remove manual nodes", () => SubscriptionActionRequested?.Invoke("remove"))]);
            Transitions = new() { new BrushTransition { Property = BackgroundProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new SplineEasing(0, 0, .58, 1) } };
            _rotation.Transitions = new() { new DoubleTransition { Property = RotateTransform.AngleProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new SplineEasing(0.22, 0.7, 0.2, 1) } };
            var arrow = new Canvas { Width = 12, Height = 12, RenderTransform = _rotation, RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), VerticalAlignment = VerticalAlignment.Center,
                Children = { new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M3,1 L8,6 L3,11"), Stroke = Skin.SecondaryBrush, StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round } } };
            Disclosure = new Button { Width = 16, Height = 28, MinHeight = 0, Padding = default, Background = Brushes.Transparent, BorderThickness = default, Classes = { "plain", "selection-item" }, Content = arrow };
            Disclosure.Click += (_, _) => { _expanded = !_expanded; Animate(); };
            var label = new Grid { ColumnDefinitions = new("16,10,*,8,Auto"), Children = { Disclosure, _title, _count } }; Grid.SetColumn(_title, 2); Grid.SetColumn(_count, 4); _title.FontWeight = FontWeight.Medium;
            Header = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 44, Padding = new(4, 6), Classes = { "plain", "disclosure" }, CornerRadius = new(8) };
            AutomationProperties.SetAutomationId(Header, "subscription-" + key);
            Header.Click += (_, e) => { if (ReferenceEquals(e.Source, Header)) Selected?.Invoke(); };
            Ui.OnActivate(Header, () => { Selected?.Invoke(); _expanded = !_expanded; Animate(); });
            var controls = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ItemSpacing = 6, Children = { Ui.Button("Subscription URL", () => Details?.Invoke()), Ui.Button("Update", () => Update?.Invoke()) }, IsVisible = subscription is not null };
            var heading = new Grid { ColumnDefinitions = new("*,12,Auto"), Children = { Header, controls } }; Grid.SetColumn(controls, 2);
            _details = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 22, LineSpacing = 8, Margin = new(30, 3, 8, 12), Children = { _expiry, _usage } };
            _expiry.TextTrimming = _usage.TextTrimming = TextTrimming.None; _expiry.TextWrapping = _usage.TextWrapping = TextWrapping.Wrap;
            _previous = Ui.Button("Previous", () => { _page--; Rows(); }); _next = Ui.Button("Next", () => { _page++; Rows(); });
            var paging = Ui.Horizontal(_previous, _pageLabel, _next); paging.HorizontalAlignment = HorizontalAlignment.Right;
            _body.Children.Add(_nodes); _body.Children.Add(paging);
            _reveal = new AnimatedReveal { Child = _body };
            _reveal.PropertyChanged += (_, change) =>
            {
                if (change.Property == AnimatedReveal.ProgressProperty)
                { if (_reveal.Progress <= 0 && !_expanded && !_searching) _nodes.IsEnabled = false; RefreshSelectionBackground(); }
            };
            _nodes.Chosen += id => { if (Guid.TryParse(id, out var guid)) NodeChosen?.Invoke(guid); };
            _nodes.Activated += id => { if (Guid.TryParse(id, out var guid)) NodeActivated?.Invoke(guid); };
            Child = new StackPanel { Spacing = 2, Children = { heading, _details, _quota, _reveal } };
        }
        internal void Set(Subscription? subscription, List<ProxyNode> nodes, Guid? selected, IReadOnlyDictionary<Guid, string> latencies, bool searching)
        {
            _title.Text = subscription?.Name ?? UiText.Get("Manual nodes"); _count.Text = UiText.Format("{0} nodes", nodes.Count);
            _details.IsVisible = subscription is not null; _quota.IsVisible = subscription?.Usage is { Used: not null, Total: > 0 };
            _expiry.Text = UiText.Get("Expires") + " · " + UiFormat.Expiry(subscription?.Usage);
            _usage.Text = UiFormat.Usage(subscription?.Usage);
            _usage.Foreground = subscription?.Usage?.Used is not null ? Ui.DataAccent : Skin.SecondaryBrush;
            _expiry.Foreground = subscription?.Usage?.IsExpired(DateTimeOffset.Now) == true ? new SolidColorBrush(Color.Parse("#FFB5A5")) : Skin.SecondaryBrush;
            if (subscription?.Usage is { Used: { } used, Total: > 0 } usage) _quota.Value = Math.Clamp(used / (double)usage.Total.Value * 100, 0, 100);
            if (!_source.Select(n => n.Id).SequenceEqual(nodes.Select(n => n.Id))) _page = 0;
            if (_activeNode != selected && nodes.Any(n => n.Id == selected)) _expanded = true;
            _source = nodes; _activeNode = selected; _latencies = latencies; _searching = searching; Rows();
        }
        internal void SelectNode(Guid? node) { _operationNode = node; _nodes.Select(node?.ToString()); }
        internal void SelectSubscription(bool selected)
        {
            IsSelected = selected; Header.Classes.Set("selected-card", selected);
            RefreshSelectionBackground();
        }
        private void RefreshSelectionBackground() => Motion.Set(this, BackgroundProperty,
            IsSelected && !NodesShown && _reveal.Progress <= 0 ? Ui.SelectionBrush : Skin.ChromeBrush);
        private void Rows()
        {
            _page = Math.Clamp(_page, 0, Math.Max(0, (_source.Count - 1) / 100));
            _nodes.SetRows(_source.Skip(_page * 100).Take(100).Select(n => new DisplayRow(n.Id.ToString(), n.Name, n.Type.ToUpperInvariant() + " · " + UiText.Get(NodeTransport.Summary(n)), _latencies.GetValueOrDefault(n.Id, "—"), n.CountryCode ?? NodeRegion.Match(n.Name), Latency: true, Current: n.Id == _activeNode)), _operationNode?.ToString(), _source.Count);
            _previous.IsVisible = _next.IsVisible = _pageLabel.IsVisible = _source.Count > 100;
            _previous.IsEnabled = _page > 0; _next.IsEnabled = (_page + 1) * 100 < _source.Count;
            _pageLabel.Text = $"{_page + 1} / {Math.Max(1, (_source.Count + 99) / 100)}"; Animate();
        }
        private void Animate()
        {
            var shown = _expanded || _searching;
            RefreshSelectionBackground();
            UiText.Set(Disclosure, ToolTip.TipProperty, shown ? "Collapse subscription" : "Expand subscription");
            AutomationProperties.SetName(Disclosure, UiText.Get(shown ? "Collapse subscription" : "Expand subscription"));
            // Dimming visible rows before they have slid away produces a click flash.
            // Disable them only after collapse; keep their content and selection intact.
            if (shown) _nodes.IsEnabled = true;
            _reveal.IsHitTestVisible = shown;
            Motion.Set(_reveal, AnimatedReveal.ProgressProperty, shown ? 1d : 0d);
            Motion.Set(_rotation, RotateTransform.AngleProperty, shown ? 90d : 0d);
            VisibleItemsChanged?.Invoke();
        }
    }
}

/// <summary>Measure the content once at its full height; animate only the visible viewport.</summary>
internal sealed class AnimatedReveal : Decorator
{
    internal static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<AnimatedReveal, double>(nameof(Progress));
    internal double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    static AnimatedReveal() => AffectsMeasure<AnimatedReveal>(ProgressProperty);
    internal AnimatedReveal()
    {
        ClipToBounds = true;
        Transitions = new() { new DoubleTransition { Property = ProgressProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new ElasticEasing() } };
    }
    protected override Size MeasureOverride(Size available)
    {
        Child?.Measure(new Size(available.Width, double.PositiveInfinity));
        var size = Child?.DesiredSize ?? default;
        var height=size.Height*Math.Clamp(Progress,0,1)+Math.Min(12,size.Height*Math.Clamp(Progress-1,0,.04));
        return new(size.Width,height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        if (Child is { } content) content.Arrange(new Rect(0, 0, final.Width, content.DesiredSize.Height));
        return final;
    }
}
