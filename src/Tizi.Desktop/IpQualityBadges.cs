using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Tizi.Desktop;

// Presentation only. Actual assessment results are supplied by the caller;
// constructing this control never starts a network request or fabricates a score.
internal sealed record IpQualityDisplay(string Type, string Native, string TypeDetails, string NativeDetails, bool? IsNative = null);

internal sealed class IpQualityBadges : StackPanel
{
    private readonly Border[] _badges;
    internal bool HasResult { get; private set; }
    internal IReadOnlyList<Border> Badges => _badges;
    internal IpQualityBadges()
    {
        Orientation = Orientation.Horizontal; Spacing = 6; IsVisible = false;
        _badges = Enumerable.Range(0, 2).Select(index => new Border
        {
            CornerRadius = new(15), MinHeight = 30, Padding = new(11, 4), VerticalAlignment = VerticalAlignment.Center,
            Child = NetworkMonitor.TagLabel("—"), Focusable = true
        }).ToArray();
        foreach (var badge in _badges)
        {
            ToolTip.SetShowDelay(badge, 300);
            badge.GotFocus += (_, _) => ToolTip.SetIsOpen(badge, true);
            badge.LostFocus += (_, _) => ToolTip.SetIsOpen(badge, false);
            Children.Add(badge);
        }
    }
    internal void Update(IpQualityDisplay? result, bool connected)
    {
        _badges[0].IsVisible = !string.IsNullOrWhiteSpace(result?.Type) && result.Type != UiText.Get("Unknown");
        _badges[1].IsVisible = result?.IsNative == true && !string.IsNullOrWhiteSpace(result.Native);
        foreach(var badge in _badges.Where(badge=>!badge.IsVisible))ToolTip.SetIsOpen(badge,false);
        HasResult = result is not null && _badges.Any(badge => badge.IsVisible);
        IsVisible = connected && HasResult;
        if (result is null) return;
        var blue = Color.Parse("#2866C9"); var nativeGreen = Color.Parse("#4FA83B");
        var items = new[] { (result.Type, result.TypeDetails, blue), (result.IsNative == true ? result.Native : "", result.IsNative == true ? result.NativeDetails : "", nativeGreen) };
        for (var i = 0; i < _badges.Length; i++)
        {
            var badge = _badges[i]; var (value, details, tone) = items[i];
            ((TextBlock)badge.Child!).Text = value;
            ((TextBlock)badge.Child!).Foreground = Skin.BadgeInk;
            badge.Background = new SolidColorBrush(tone);
            var explanation = Ui.Value(details, size: 13); explanation.TextWrapping = TextWrapping.Wrap; explanation.MaxWidth = 340; explanation.LineHeight = 21;
            ToolTip.SetTip(badge, explanation); AutomationProperties.SetName(badge, value); AutomationProperties.SetHelpText(badge, details);
        }
    }
}
