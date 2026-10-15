using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Tizi.Desktop;

// Presentation only. Actual assessment results are supplied by the caller;
// constructing this control never starts a network request or fabricates a score.
internal sealed record IpQualityDisplay(string Type, string Score, string Native, string TypeDetails, string ScoreDetails, string NativeDetails, int? RiskScore = null, bool? IsNative = null);

internal sealed class IpQualityBadges : StackPanel
{
    private readonly Border[] _badges;
    internal bool HasResult { get; private set; }
    internal IReadOnlyList<Border> Badges => _badges;
    internal IpQualityBadges()
    {
        Orientation = Orientation.Horizontal; Spacing = 6; IsVisible = false;
        _badges = Enumerable.Range(0, 3).Select(index => new Border
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
        _badges[1].IsVisible = result?.RiskScore is >= 0 and <= 100;
        _badges[2].IsVisible = result?.IsNative == true;
        HasResult = result is not null && _badges.Any(badge => badge.IsVisible);
        IsVisible = connected && HasResult;
        if (result is null) return;
        // Opaque fills sampled from the supplied reference; native IP keeps its separate light green.
        var blue = Color.Parse("#2866C9"); var lowRiskGreen = Color.Parse("#357F20"); var nativeGreen = Color.Parse("#4FA83B"); var yellow = Color.Parse("#D2B635"); var orange = Color.Parse("#C8812A"); var red = Color.Parse("#CA3540");
        var risk = result.RiskScore is not (>= 0 and <= 100) ? (Color?)null : result.RiskScore <= 25 ? lowRiskGreen : result.RiskScore <= 50 ? yellow : result.RiskScore <= 75 ? orange : red;
        var items = new[] { (result.Type, result.TypeDetails+"\n\n"+(_badges[1].IsVisible ? "" : result.ScoreDetails+"\n\n")+result.NativeDetails, result.Type == UiText.Get("Unknown") ? (Color?)null : blue), (result.Score, result.ScoreDetails, risk), (result.Native, result.NativeDetails, (Color?)nativeGreen) };
        for (var i = 0; i < _badges.Length; i++)
        {
            var badge = _badges[i]; var (value, details, tone) = items[i];
            ((TextBlock)badge.Child!).Text = value;
            ((TextBlock)badge.Child!).Foreground = Skin.BadgeInk;
            badge.Background = new SolidColorBrush(tone ?? Color.Parse("#454545"));
            var explanation = Ui.Value(details, size: 13); explanation.TextWrapping = TextWrapping.Wrap; explanation.MaxWidth = 340; explanation.LineHeight = 21;
            ToolTip.SetTip(badge, explanation); AutomationProperties.SetName(badge, value); AutomationProperties.SetHelpText(badge, details);
        }
    }
}
