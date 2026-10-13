using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

internal static class Ui
{
    internal static readonly IBrush DataAccent = new SolidColorBrush(Color.Parse("#82B4FF"));
    internal static readonly IBrush SelectionBrush = new SolidColorBrush(Color.Parse("#414141"));
    internal static bool OwnButtonEvent(Button button, RoutedEventArgs e) => e.Source is Visual source
        && (source as Button ?? source.GetVisualAncestors().OfType<Button>().FirstOrDefault()) == button;
    internal static void OnActivate(Button button, Action action)
    {
        button.AddHandler(InputElement.DoubleTappedEvent, (_, e) =>
        { if (OwnButtonEvent(button, e)) { action(); e.Handled = true; } }, handledEventsToo: true);
        button.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && OwnButtonEvent(button, e)) { action(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }
    internal static TextBlock Text(string text, bool secondary = false, double size = 14) => new()
    { [!TextBlock.TextProperty] = UiText.Bind(text), FontSize = size, Foreground = secondary ? Skin.SecondaryBrush : Skin.LabelBrush, VerticalAlignment = VerticalAlignment.Center };
    internal static TextBlock Value(string value, bool secondary = false, double size = 14) => new()
    { Text = value, FontSize = size, Foreground = secondary ? Skin.SecondaryBrush : Skin.LabelBrush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    internal static Button Button(string label, Action action, bool accent = false)
    {
        var button = new Button { Content = Text(label), MinHeight = 36, Padding = new Thickness(16, 6), CornerRadius = new CornerRadius(18) }; if (accent) button.Classes.Add("accent"); button.Click += (_, _) => action(); return button;
    }
    internal static StackPanel Horizontal(params Control[] controls)
    { var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center }; foreach (var control in controls) row.Children.Add(control); return row; }
    internal static Control Line() => new Border { Height = 0.5, Background = Skin.MenuRule, Margin = new Thickness(0, 8) };
    internal static ScrollViewer Scroll(Control content, Thickness margin = default) => new()
    {
        AllowAutoHide = false, Margin = margin,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        Content = new Border { Padding = new(0, 0, 16, 0), Child = content }
    };
    internal static NavigationGlyph Icon(string page) => new(page);
}
internal sealed class NavigationRail : Border
{
    internal static readonly string[] Pages = ["Overview", "Nodes", "Providers", "Rules", "Connections", "Logs", "Settings"];
    private readonly StackPanel _buttons = new() { Spacing = 4 };
    private readonly Grid _surface = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
    private readonly Dictionary<string, Button> _byPage = [];
    private readonly Dictionary<string, NavigationGlyph> _glyphs = [];
    internal SelectionIndicator Indicator { get; } = new() { Background = Skin.TabFront, BorderBrush = Skin.TabFrontEdge, BorderThickness = new Thickness(0.5), CornerRadius = new CornerRadius(8) };
    internal event Action<string>? Chosen;
    internal string Selected { get; private set; } = "Overview";
    internal NavigationRail()
    {
        Width = 196; Background = Skin.ChromeBrush; Padding = new Thickness(12, 18);
        foreach (var page in Pages)
        {
            var caption = Ui.Text(page == "Nodes" ? "Servers" : page, size: 15); caption.Classes.Add("nav-label");
            var glyph = Ui.Icon(page); glyph.Select(page == Selected, animate: false); _glyphs.Add(page, glyph);
            var icon = new Grid { Width = 24, Height = 24, Children = { glyph } };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,12,*"), Children = { icon, caption } }; Grid.SetColumn(caption, 2);
            var button = new Button { Height = 44, Padding = new Thickness(12, 9), HorizontalContentAlignment = HorizontalAlignment.Stretch, Classes = { "plain", "selection-item" }, CornerRadius = new CornerRadius(10), Content = row, Tag = page };
            UiText.Set(button, ToolTip.TipProperty, page); button.Click += (_, _) => Select(page); _byPage[page] = button;
            _buttons.Children.Add(button);
        }
        Grid.SetRowSpan(Indicator, 3); _surface.Children.Insert(0, Indicator); _surface.Children.Add(_buttons); Child = _surface;
        _surface.SizeChanged += (_, _) => Place(); _buttons.SizeChanged += (_, _) => Place(); foreach (var b in _byPage.Values) b.SizeChanged += (_, _) => Place();
    }
    internal Button For(string page) => _byPage[page];
    internal NavigationGlyph GlyphFor(string page) => _glyphs[page];
    internal Rect TargetBounds(string page) => For(page).TranslatePoint(new Point(0, 0), _surface) is { } p ? new Rect(p, For(page).Bounds.Size) : default;
    internal void Compact(bool compact)
    {
        Width = compact ? 72 : 196;
        foreach (var button in _byPage.Values)
            if (button.Content is Grid row) { row.Children[1].IsVisible = !compact; row.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 12); }
    }
    internal void Select(string page)
    {
        if (page == Selected) return;
        Selected = page;
        foreach (var (key, glyph) in _glyphs) glyph.Select(key == page);
        foreach (var (key, button) in _byPage)
            Avalonia.Automation.AutomationProperties.SetHelpText(button, key == page ? UiText.Get("Selected") : "");
        Place(); Chosen?.Invoke(page);
    }
    private void Place() { var button = For(Selected); if (button.Bounds.Width > 0) Indicator.MoveTo(TargetBounds(Selected)); }
}
internal sealed record DisplayRow(string Id, string Title, string Subtitle, string Badge = "", string? Country = null, Tizi.Core.ProviderSite? Provider = null, bool Latency = false, Tizi.Core.ProviderPlan? PricePlan = null, bool Current = false);
/// <summary>One continuous highlight for a bounded page of rows, no competing selected/hover fills.</summary>
internal sealed class MovingRows : Grid
{
    private readonly StackPanel _rows = new() { Spacing = 6 };
    private readonly Canvas _cardGrounds = new() { IsHitTestVisible = false };
    private readonly Dictionary<string, Border> _grounds = [];
    private readonly SelectionIndicator _indicator = new() { CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly Border _backplate;
    private readonly Dictionary<string, Button> _buttons = [];
    private readonly Dictionary<string, DisplayRow> _display = [];
    private string? _selected;
    internal event Action<string>? Chosen;
    internal event Action<string>? Activated;
    internal event Action<string>? SourceRequested;
    internal Func<string, IEnumerable<(string Label, Action Action)>>? ContextActions { get; set; }
    internal SelectionIndicator Indicator => _indicator;
    internal Border Backplate => _backplate;
    internal Button For(string id) => _buttons[id];
    internal bool Contains(string? id) => id is not null && _buttons.ContainsKey(id);
    internal void Reset() { _rows.Children.Clear(); _cardGrounds.Children.Clear(); _grounds.Clear(); _buttons.Clear(); _display.Clear(); Select(null); }
    internal MovingRows()
    {
        _backplate = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new(12), IsHitTestVisible = false, RenderTransform = _indicator.Position,
            Background = Ui.SelectionBrush
        };
        _backplate.Bind(WidthProperty, _indicator.GetObservable(WidthProperty));
        _backplate.Bind(HeightProperty, _indicator.GetObservable(HeightProperty));
        _backplate.Bind(IsVisibleProperty, _indicator.GetObservable(IsVisibleProperty));
        ClipToBounds = true; Children.Add(_cardGrounds); Children.Add(_backplate); Children.Add(_rows); Children.Add(_indicator);
        _rows.SizeChanged += (_, _) => Place();
    }
    internal void SetRows(IEnumerable<DisplayRow> source, string? selected)
    {
        var entries = source.ToList(); var ids = entries.Select(e => e.Id).ToArray();
        if (!_rows.Children.OfType<Button>().Select(b => (string)b.Tag!).SequenceEqual(ids))
        {
            _rows.Children.Clear(); _cardGrounds.Children.Clear(); _grounds.Clear(); _buttons.Clear(); _display.Clear();
            foreach (var entry in entries)
            {
                Button button = entry.Provider is null ? new Button() : new ProviderCardButton();
                button.MinHeight = entry.Provider is null ? 76 : 164; button.Padding = new Thickness(16, 14);
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.Classes.Add("plain"); button.Classes.Add("selection-item"); button.CornerRadius = new(12);
                button.Background = Brushes.Transparent; button.Tag = entry.Id;
                if (entry.Provider is not null)
                {
                    var ground = new Border { Background = Skin.TabBack, CornerRadius = new(12) };
                    _grounds.Add(entry.Id, ground); _cardGrounds.Children.Add(ground);
                }
                button.Classes.Set("provider-card", entry.Provider is not null);
                button.Click += (_, e) => { if (!ReferenceEquals(e.Source, button)) return; Select(entry.Id); Chosen?.Invoke(entry.Id); };
                Ui.OnActivate(button, () => { Select(entry.Id); Chosen?.Invoke(entry.Id); Activated?.Invoke(entry.Id); });
                Avalonia.Automation.AutomationProperties.SetHelpText(button, UiText.Get("Double-click or press Enter to open. Right-click for item actions."));
                button.SizeChanged += (_, _) => Place(); _buttons.Add(entry.Id, button); _rows.Children.Add(button);
            }
        }
        foreach (var entry in entries)
        {
            if (ContextActions is { } actions && _buttons[entry.Id].ContextMenu?.IsOpen != true)
                _buttons[entry.Id].ContextMenu = ItemContextMenu.Create(_buttons[entry.Id], () => { Select(entry.Id); Chosen?.Invoke(entry.Id); }, actions(entry.Id));
            if (_display.TryGetValue(entry.Id, out var old) && old == entry) continue;
            if (old is not null && old with { Badge = entry.Badge } == entry && _buttons[entry.Id].Content is Grid existing && existing.Children.OfType<TextBlock>().FirstOrDefault() is { } value)
            { value.Text = entry.Badge; value.Foreground = BadgeBrush(entry); _display[entry.Id] = entry; continue; }
            _display[entry.Id] = entry;
            var title = Ui.Value(entry.Country is null ? entry.Title : Tizi.Core.NodeRegion.StripFlag(entry.Title), size: 15); title.FontWeight = FontWeight.Medium;
            var subtitle = Ui.Value(entry.Subtitle, true, 13); ToolTip.SetTip(title, entry.Title); ToolTip.SetTip(subtitle, entry.Subtitle);
            var titleLine = new Grid { ColumnDefinitions = new("*,8,Auto"), Children = { title } };
            if (entry.Current)
            {
                var current = Ui.Value("✓", size: 13); current.Tag = "current-node";
                ToolTip.SetTip(current, UiText.Get("Current node")); Avalonia.Automation.AutomationProperties.SetName(current, UiText.Get("Current node"));
                Grid.SetColumn(current, 2); titleLine.Children.Add(current);
            }
            var caption = new StackPanel { Spacing = 7, Children = { titleLine, subtitle } };
            var badge = Ui.Value(entry.Badge, true, 13); badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.Foreground = BadgeBrush(entry);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,12,Auto"), Children = { caption, badge } }; Grid.SetColumn(caption, 1); Grid.SetColumn(badge, 3);
            if (entry.Country is not null) { var flag = FlagImages.Control(entry.Country); flag.Margin = new Thickness(0, 0, 14, 0); row.Children.Add(flag); }
            if (entry.Provider is { } provider)
            {
                row.RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto");
                var description = Ui.Value(provider.Profile?.Description ?? provider.Region, true, 13); description.TextWrapping = TextWrapping.Wrap; description.MaxLines = 2; description.Margin = new Thickness(0, 12, 0, 12);
                Grid.SetRow(description, 1); Grid.SetColumnSpan(description, 4); row.Children.Add(description);
                var profile = provider.Profile;
                var specs = Ui.Value(string.Join(" · ", new[] { provider.Region, entry.PricePlan?.TrafficGb is { } quota ? $"{quota:0.##} GB" : "", profile is null ? "" : string.Join(" / ", profile.SupportedProtocols) }.Where(s => s.Length > 0)), true, 13);
                specs.TextWrapping = TextWrapping.Wrap; specs.TextTrimming = TextTrimming.None; specs.Margin = new(0, 0, 0, 10);
                Grid.SetRow(specs, 2); Grid.SetColumnSpan(specs, 4); row.Children.Add(specs);
                var evidence = Ui.Value(profile?.ReviewSummary is { Length: > 0 } review ? review : UiText.Get("No saved reviews"), true, 12); evidence.TextWrapping = TextWrapping.Wrap; evidence.MaxLines = 2;
                var footer = new Grid { ColumnDefinitions = new("*,12,Auto"), Children = { evidence } };
                var link = Ui.Button("Open website", () => SourceRequested?.Invoke(provider.Url)); link.MinHeight = 30; link.Padding = new(12, 4); link.CornerRadius = new(15); Grid.SetColumn(link, 2); footer.Children.Add(link);
                Grid.SetRow(footer, 3); Grid.SetColumnSpan(footer, 4); row.Children.Add(footer);
            }
            _buttons[entry.Id].Content = row;
        }
        Select(selected);
    }
    private static IBrush BadgeBrush(DisplayRow entry) => entry.Latency ? LatencyTone.For(entry.Badge)
        : entry.PricePlan is { Price: not null } ? Ui.DataAccent : Skin.SecondaryBrush;
    internal void UpdateBadges(IReadOnlyDictionary<Guid, string> values)
    {
        foreach (var (id, button) in _buttons)
            if (Guid.TryParse(id, out var key) && values.TryGetValue(key, out var text) && _display.TryGetValue(id, out var entry) && button.Content is Grid row && row.Children.OfType<TextBlock>().FirstOrDefault() is { } badge)
            { badge.Text = text; badge.Foreground = BadgeBrush(entry with { Badge = text }); _display[id] = entry with { Badge = text }; }
    }
    internal void Select(string? id)
    {
        _selected = id; _indicator.IsVisible = id is not null && _buttons.ContainsKey(id);
        foreach (var (key, button) in _buttons)
        {
            button.Classes.Set("selected-card", key == id);
            Avalonia.Automation.AutomationProperties.SetHelpText(button, key == id ? UiText.Get("Selected") : "");
        }
        Place();
    }
    private void Place()
    {
        foreach (var (id, ground) in _grounds)
        {
            var bounds = _buttons[id].Bounds; Canvas.SetLeft(ground, bounds.X); Canvas.SetTop(ground, bounds.Y);
            ground.Width = bounds.Width; ground.Height = bounds.Height;
        }
        if (_selected is { } selected && _buttons.TryGetValue(selected, out var button) && button.Bounds.Width > 0) _indicator.MoveTo(button.Bounds);
    }
}
