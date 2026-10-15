using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Tizi.Core;

namespace Tizi.Desktop;

/// <summary>The header stays within the selected node's subscription.</summary>
internal sealed class SubscriptionNodeChoice : ComboBox
{
    private sealed record Option(ProxyNode Node, string SubscriptionName) { public override string ToString() => Node.Name; }
    private bool _refreshing;
    private int _openingGeneration;
    private ProxyNode[] _candidates = [];
    private readonly Dictionary<TextBlock, Guid> _badges = [];
    private IReadOnlyDictionary<Guid, string> _latencies = new Dictionary<Guid, string>();
    internal IReadOnlyList<ProxyNode> Candidates => _candidates;
    internal bool CanSwitch => _candidates.Length > 1;
    internal event Action<Guid>? NodeChosen;
    internal event Action<bool>? DropDownVisibilityChanged;
    protected override Type StyleKeyOverride => typeof(ComboBox);

    internal SubscriptionNodeChoice()
    {
        Classes.Add("header-node");
        MinWidth = 280; MinHeight = 58; Padding = new(10, 6, 14.5, 6);
        HorizontalAlignment = HorizontalAlignment.Center;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        ItemTemplate = new FuncDataTemplate<Option>((option, _) =>
        {
            if (option is null) return Ui.Text("Select a node", true);
            var node = option.Node;
            var flag = FlagImages.Control(node.CountryCode ?? NodeRegion.Match(node.Name)); flag.Width = 36; flag.Height = 28; flag.VerticalAlignment = VerticalAlignment.Center;
            var name = Ui.Value(node.Name, size: 15); name.FontWeight = FontWeight.Medium; name.TextTrimming = TextTrimming.CharacterEllipsis; name.Tag = "header-node-name";
            var details = Ui.Value(string.Join(" · ", new[] { option.SubscriptionName, node.Type.ToUpperInvariant(), NodeTransport.Summary(node) }.Where(s => s.Length > 0)), true, 12.5);
            details.Foreground = new SolidColorBrush(Color.Parse("#BCC0C7")); details.Margin = new(0, 3, 0, 0);
            details.TextTrimming = TextTrimming.CharacterEllipsis; details.TextAlignment = TextAlignment.Left; details.Tag = "header-node-details";
            // Both captions share the same text column. The flag never shifts their left edge.
            var badge = Ui.Value("—", true, 12); badge.Tag = "header-node-latency"; badge.IsVisible = false;
            var content = new Grid { ColumnDefinitions = new("36,10,*,0,Auto"), RowDefinitions = new("Auto,Auto"), VerticalAlignment = VerticalAlignment.Center, Children = { flag, name, details, badge }, Tag = "header-node-title" };
            Grid.SetRowSpan(flag, 2); Grid.SetColumn(name, 2); Grid.SetColumn(details, 2); Grid.SetRow(details, 1);
            Grid.SetColumn(badge, 4); Grid.SetRowSpan(badge, 2);
            content.AttachedToVisualTree += (_, _) =>
            {
                badge.IsVisible = content.GetVisualAncestors().OfType<ComboBoxItem>().Any();
                content.ColumnDefinitions[3].Width = new(badge.IsVisible ? 8 : 0);
                if (badge.IsVisible) { _badges[badge] = node.Id; UpdateLatencies(_latencies); }
            };
            content.DetachedFromVisualTree += (_, _) => _badges.Remove(badge);
            ToolTip.SetTip(content, node.Name + "\n" + details.Text); return content;
        });
        SelectionChanged += (_, _) =>
        {
            if (!_refreshing && CanSwitch && SelectedItem is Option option && _candidates.Any(n => n.Id == option.Node.Id)) NodeChosen?.Invoke(option.Node.Id);
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsDropDownOpenProperty) return;
            var generation = ++_openingGeneration;
            if (IsDropDownOpen) Dispatcher.UIThread.Post(() => ScrollSelectedToTop(generation), DispatcherPriority.Loaded);
            DropDownVisibilityChanged?.Invoke(IsDropDownOpen);
        };
    }
    private void ScrollSelectedToTop(int generation)
    {
        if (!IsDropDownOpen || generation != _openingGeneration) return;
        var popup = this.GetVisualDescendants().OfType<Popup>().FirstOrDefault();
        if (popup?.Child is not { } body) return;
        body.UpdateLayout();
        var scroll = body.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll?.Content is not ItemsPresenter presenter || ContainerFromIndex(SelectedIndex) is not { } row) return;
        // A small trailing scroll area lets even the last node align with the top;
        // the collection and every node's original index remain untouched.
        presenter.Margin = new(0, 0, 0, Math.Max(0, scroll.Viewport.Height - row.Bounds.Height));
        body.UpdateLayout();
        if (row.TranslatePoint(default, presenter) is { } point) scroll.Offset = new(0, point.Y);
    }
    internal void UpdateLatencies(IReadOnlyDictionary<Guid, string> values)
    {
        _latencies = values;
        foreach (var (badge, id) in _badges)
        { badge.Text = values.GetValueOrDefault(id, "—"); badge.Foreground = LatencyTone.For(badge.Text); }
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is ComboBoxItem row)
        {
            // The popup frame owns 4 px on either side. Match the selected face's
            // 10 px content inset and the same trailing arrow space as the selected face.
            row.Padding = new(6, 6, 28.5, 6);
            row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }
    }

    internal void Update(AppState state)
    {
        PlaceholderText = UiText.Get("Select a node");
        var selected = state.Nodes.FirstOrDefault(n => n.Id == state.SelectedNode);
        var subscription = selected?.SubscriptionId is { } id ? state.Subscriptions.FirstOrDefault(s => s.Id == id) : null;
        var candidates = selected is null ? Array.Empty<ProxyNode>() : subscription is null ? [selected] : state.Nodes.Where(n => n.SubscriptionId == subscription.Id).ToArray();
        _refreshing = true;
        try
        {
            var name = subscription?.Name ?? "";
            if (!_candidates.SequenceEqual(candidates) || Items.OfType<Option>().FirstOrDefault()?.SubscriptionName != name)
            {
                _candidates = candidates; IsDropDownOpen = false;
                ItemsSource = candidates.Select(n => new Option(n, name)).ToArray();
            }
            SelectedItem = Items.OfType<Option>().FirstOrDefault(o => o.Node.Id == selected?.Id);
            Classes.Set("single-node", !CanSwitch);
            IsHitTestVisible = CanSwitch; Focusable = CanSwitch;
            AutomationProperties.SetName(this, selected?.Name ?? UiText.Get("Select a node"));
            ToolTip.SetTip(this, selected?.Name);
        }
        finally { _refreshing = false; }
    }
}
