using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Tizi.Core;

namespace Tizi.Desktop;

/// <summary>The header stays within the selected node's subscription.</summary>
internal sealed class SubscriptionNodeChoice : ComboBox
{
    private sealed record Option(ProxyNode Node, string SubscriptionName) { public override string ToString() => Node.Name; }
    private bool _refreshing;
    private ProxyNode[] _candidates = [];
    internal IReadOnlyList<ProxyNode> Candidates => _candidates;
    internal bool CanSwitch => _candidates.Length > 1;
    internal event Action<Guid>? NodeChosen;
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
            var content = new Grid { ColumnDefinitions = new("36,10,*"), RowDefinitions = new("Auto,Auto"), VerticalAlignment = VerticalAlignment.Center, Children = { flag, name, details }, Tag = "header-node-title" };
            Grid.SetRowSpan(flag, 2); Grid.SetColumn(name, 2); Grid.SetColumn(details, 2); Grid.SetRow(details, 1);
            ToolTip.SetTip(content, node.Name + "\n" + details.Text); return content;
        });
        SelectionChanged += (_, _) =>
        {
            if (!_refreshing && CanSwitch && SelectedItem is Option option && _candidates.Any(n => n.Id == option.Node.Id)) NodeChosen?.Invoke(option.Node.Id);
        };
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
