using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

/// <summary>Retain every record; format and lay out only the rows around the visible viewport.</summary>
internal sealed class LogView : ListBox
{
    private readonly AvaloniaList<ActivityEntry> _entries = [];
    private LogCategory? _category;
    private string? _language;
    internal bool Pending { get; private set; }
    internal int FormattedRows { get; private set; }
    internal IReadOnlyList<ActivityEntry> Entries => _entries;
    protected override Type StyleKeyOverride => typeof(ListBox);
    internal LogView()
    {
        Background = Brushes.Transparent; BorderThickness = default; Padding = new(12, 10);
        ItemsSource = _entries;
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { CacheLength = .5 });
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Auto);
        ItemTemplate = new FuncDataTemplate<ActivityEntry>((entry, _) =>
        {
            if (entry is null) return null;
            FormattedRows++;
            return new SelectableTextBlock { Text = FormatLine(entry), FontFamily = new("Consolas"), FontSize = 13,
                Foreground = Skin.LabelBrush, TextWrapping = TextWrapping.Wrap, LineHeight = 18,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top, Tag = "log-line" };
        });
        ItemContainerTheme = new ControlTheme(typeof(ListBoxItem))
        {
            Setters =
            {
                new Setter(TemplateProperty, new FuncControlTemplate<ListBoxItem>((item, _) => new ContentPresenter
                {
                    Name = "PART_ContentPresenter", HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    [!ContentPresenter.ContentProperty] = item[!ContentControl.ContentProperty],
                    [!ContentPresenter.ContentTemplateProperty] = item[!ContentControl.ContentTemplateProperty]
                })),
                new Setter(PaddingProperty, default(Thickness)), new Setter(MinHeightProperty, 0d),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)
            }
        };
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            var source = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<SelectableTextBlock>().FirstOrDefault();
            foreach (var text in this.GetVisualDescendants().OfType<SelectableTextBlock>())
                if (!ReferenceEquals(text, source)) text.SelectionStart = text.SelectionEnd = 0;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    internal static string FormatLine(ActivityEntry entry) => $"{entry.Time.ToLocalTime():MM-dd HH:mm:ss}  [{UiText.Get(entry.Category == LogCategory.Dns ? "DNS" : entry.Category.ToString())}] {entry.Message}";
    internal void Update(IEnumerable<ActivityEntry> source, LogCategory? category, bool force = false)
    {
        if (!force && this.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t => t.SelectionStart != t.SelectionEnd)) { Pending = true; return; }
        var next = source.Where(e => category is null || e.Category == category).ToArray();
        Pending = false;
        if (_category == category && _language == UiText.Language && _entries.Count == next.Length && _entries.Zip(next).All(pair => ReferenceEquals(pair.First, pair.Second))) return;
        var sameScope = _category == category && _language == UiText.Language;
        _category = category; _language = UiText.Language;
        var start = -1;
        if (sameScope && next.Length > 0) for (var i = 0; i < _entries.Count; i++) if (ReferenceEquals(_entries[i], next[0])) { start = i; break; }
        if (start >= 0 && _entries.Count - start <= next.Length && _entries.Skip(start).Zip(next).All(pair => ReferenceEquals(pair.First, pair.Second)))
        {
            if (start > 0) _entries.RemoveRange(0, start);
            _entries.AddRange(next.Skip(_entries.Count));
        }
        else { _entries.Clear(); _entries.AddRange(next); }
    }
}
