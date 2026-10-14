using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

internal sealed class NetworkMonitor : StackPanel
{
    internal static readonly IBrush TagForeground = new SolidColorBrush(Color.Parse("#D6D6D6"));
    internal static TextBlock TagLabel(string text,bool translate=false,double opticalOffset=1)
    {
        var label=translate?Ui.Text(text,size:13):Ui.Value(text,size:13);
        label.Foreground=TagForeground;label.TextAlignment=TextAlignment.Center;
        // Center the visible glyphs rather than the font's asymmetric line box.
        // This optical offset leaves the pill layout and its series dot unchanged.
        var alignment=new TranslateTransform(0,opticalOffset);label.RenderTransform=alignment;
        TopLevel? host=null;
        void Align(object? sender,EventArgs args)
        {
            // Small hinted text needs the optical nudge most at 1x; avoid magnifying it at higher densities.
            var density=Math.Max(1,host?.RenderScaling??1);
            // Fractional layout scales also shift the snapped font baseline relative to the capsule.
            var fractional=density-Math.Floor(density);
            alignment.Y=opticalOffset*(1/(density*density)-Math.Max(0,fractional-.25));
        }
        label.AttachedToVisualTree+=(_,_)=>{host=TopLevel.GetTopLevel(label);if(host is not null)host.ScalingChanged+=Align;Align(null,EventArgs.Empty);};
        label.DetachedFromVisualTree+=(_,_)=>{if(host is not null)host.ScalingChanged-=Align;host=null;};
        return label;
    }
    private const double MetricFontSize = 23;
    private readonly TextBlock _up = Ui.Value("—", size: MetricFontSize), _down = Ui.Value("—", size: MetricFontSize), _latency = Ui.Value("—", size: MetricFontSize), _duration = Ui.Value("—", size: MetricFontSize);
    private readonly TrafficChart _chart = new();
    private readonly WrapPanel _legend = new() { ItemSpacing = 14, LineSpacing = 8, IsVisible = false };
    private Button? _latencyToggle;
    private Border? _latencyDot;
    internal bool LatencyMonitoring { get; private set; }
    internal event Action<bool>? LatencyMonitoringChanged;
    internal TrafficChart Chart => _chart;
    internal string? LatencyValue => _latency.Text;
    internal IBrush? LatencyBrush => _latency.Foreground;
    internal string? DurationValue => _duration.Text;
    internal bool LegendVisible => _legend.IsVisible;
    internal IpQualityBadges IpQuality { get; } = new();
    private bool _connected;
    internal NetworkMonitor()
    {
        Spacing = 20;
        _chart.SetSeriesVisible(2, false);
        var metrics = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*,12,*,12,*") };
        var values = new[] { ("Download", _down), ("Upload", _up), ("Latency", _latency), ("Connected duration", _duration) };
        for (var i = 0; i < values.Length; i++)
        {
            var content = new StackPanel { Spacing = 12, Children = { Ui.Text(values[i].Item1, true, 13), values[i].Item2 } };
            var card = new Border { Background = Skin.ChromeBrush, CornerRadius = new(14), Padding = new(18, 18), Child = content }; Grid.SetColumn(card, i * 2); metrics.Children.Add(card);
        }
        Children.Add(metrics);
        SizeChanged += (_, _) =>
        {
            var twoColumns = Bounds.Width < 760;
            metrics.ColumnDefinitions = new ColumnDefinitions(twoColumns ? "*,12,*" : "*,12,*,12,*,12,*");
            metrics.RowDefinitions = new RowDefinitions(twoColumns ? "Auto,12,Auto" : "Auto");
            for (var i = 0; i < metrics.Children.Count; i++) { Grid.SetColumn(metrics.Children[i], (twoColumns ? i % 2 : i) * 2); Grid.SetRow(metrics.Children[i], twoColumns ? i / 2 * 2 : 0); }
        };
        var title = Ui.Text("Network monitor", true);
        var legend = _legend;
        legend.Children.Add(IpQuality);
        foreach (var series in new[] { ("Download", TrafficChart.DownloadColor, 0), ("Upload", TrafficChart.UploadColor, 1), ("Latency", TrafficChart.LatencyColor, 2) })
        {
            var dot = new Border { Background = series.Item2, Width = 10, Height = 10, CornerRadius = new(5), VerticalAlignment = VerticalAlignment.Center };
            var label = TagLabel(series.Item1,true);
            var toggle = Ui.Button(series.Item1, () => { }); toggle.Content = Ui.Horizontal(dot, label); toggle.Padding = new(8, 4); toggle.MinHeight = 30;
            toggle.Tag = "chart-series-" + series.Item3;
            if (series.Item3 == 2)
            {
                _latencyToggle = toggle; _latencyDot=dot; dot.Opacity = 0.45;
                ToolTip.SetTip(toggle, UiText.Get("Start latency curve"));
                toggle.Click += (_, _) => SetLatencyMonitoring(!LatencyMonitoring);
            }
            else toggle.Click += (_, _) => { var visible = !_chart.SeriesVisible(series.Item3); _chart.SetSeriesVisible(series.Item3, visible); dot.Opacity = visible ? 1 : 0.45; };
            legend.Children.Add(toggle);
        }
        var panel = new StackPanel { Spacing = 12, Children = { title, legend, _chart } };
        Children.Add(new Border { Background = Skin.ChromeBrush, CornerRadius = new(14), Padding = new(20), Child = panel });
    }
    internal void Update(long? up, long? down, int connections, bool connected, string? latency = null, TimeSpan? duration = null)
    {
        _connected = connected;
        _up.Foreground = connected && up.HasValue ? TrafficChart.UploadColor : Skin.SecondaryBrush; _down.Foreground = connected && down.HasValue ? TrafficChart.DownloadColor : Skin.SecondaryBrush;
        _duration.Foreground = connected ? Skin.LabelBrush : Skin.SecondaryBrush;
        _up.Text = connected && up.HasValue ? UiFormat.Speed(up.Value) : "—"; _down.Text = connected && down.HasValue ? UiFormat.Speed(down.Value) : "—";
        SetDuration(connected ? duration ?? TimeSpan.Zero : null);
        _legend.IsVisible = connected;
        if (!connected) IpQuality.Update(null, false);
        if (latency is not null && connected) { _latency.Text = latency; _latency.Foreground = LatencyTone.For(latency); }
        if (!connected) { _latency.Text = "—"; _latency.Foreground = Skin.SecondaryBrush; SetLatencyMonitoring(false); _chart.Reset(); }
        else { if (up.HasValue && down.HasValue) _chart.Add(up.Value, down.Value); }
    }
    internal void Delay(string value, string detail)
    {
        if (!_connected) { _latency.Text = "—"; _latency.Foreground = Skin.SecondaryBrush; return; }
        _latency.Text = value; _latency.Foreground = LatencyTone.For(value);
        if (_connected && LatencyMonitoring) _chart.AddLatency(int.TryParse(value.Split(' ')[0], out var ms) ? ms : null);
    }
    internal void SetDuration(TimeSpan? duration) => _duration.Text = duration is { } elapsed
        ? $"{Math.Max(0, (long)elapsed.TotalHours):00}:{Math.Max(0, elapsed.Minutes):00}:{Math.Max(0, elapsed.Seconds):00}" : "—";
    internal void SetLatencyMonitoring(bool enabled)
    {
        enabled &= _connected;
        if (LatencyMonitoring == enabled) return;
        LatencyMonitoring = enabled; _chart.ClearLatencies(); _chart.SetSeriesVisible(2, enabled);
        if (_latencyToggle is { } button)
        { if(_latencyDot is not null)_latencyDot.Opacity = enabled ? 1 : 0.45; ToolTip.SetTip(button, UiText.Get(enabled ? "Stop latency curve" : "Start latency curve")); }
        LatencyMonitoringChanged?.Invoke(enabled);
    }
}

internal static class UiFormat
{
    internal static string Usage(Tizi.Core.SubscriptionUsage? usage) => usage?.Used is { } used ? UiText.Get("Used") + " " + Bytes(used) + (usage.Total is > 0 ? " / " + Bytes(usage.Total.Value) : " · " + UiText.Get("Total unknown")) : UiText.Get("Usage not provided by source");
    internal static string Bytes(long value) => value >= 1024L * 1024 * 1024 ? $"{value / (1024d * 1024 * 1024):0.0} GB" : value >= 1024 * 1024 ? $"{value / (1024d * 1024):0.0} MB" : value >= 1024 ? $"{value / 1024d:0.0} KB" : value + " B";
    internal static string SpeedNumber(double bytes) => (Math.Max(0, bytes) / 1_000_000d).ToString(bytes < 1_000_000 ? "0.000" : "0.00", System.Globalization.CultureInfo.InvariantCulture);
    internal static string Speed(long bytes) => SpeedNumber(bytes) + " MB/S";
    internal static string Expiry(Tizi.Core.SubscriptionUsage? usage) => usage?.NoExpiry == true ? UiText.Get("No expiry reported") : usage?.ExpiresAt is { } expires ? expires.ToLocalTime().ToString("yyyy-MM-dd") + (usage.IsExpired(DateTimeOffset.Now) ? " · " + UiText.Get("Expired") : "") : UiText.Get("Unknown");
    internal static string Plan(Tizi.Core.ProviderPlan? plan) => plan?.Price is { } price ? $"{plan.Currency} {price:0.##} / {UiText.Get(plan.PriceUnit switch { "period" => plan.Period, "gb" => "GB", "ip" => "IP", _ => "Unit unknown" })}" : UiText.Get("Price unavailable");
    internal static string ComparisonPrice(Tizi.Core.ProviderPlan? plan) => plan is not null && Tizi.Core.ProviderDiscovery.MonthlyEquivalent(plan) is { } monthly
        ? plan.Period == "month" ? Plan(plan) : UiText.Format("{0} {1} / month equivalent", plan.Currency, monthly.ToString("0.##")) : Plan(plan);
}
