using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Tizi.Desktop;

/// <summary>One time axis, separate speed and latency scales, with bounded real samples.</summary>
internal sealed class TrafficChart : Control
{
    internal static readonly IBrush DownloadColor = Skin.Download, UploadColor = Ui.DataAccent, LatencyColor = Skin.Latency;
    private readonly List<(DateTimeOffset Time, long Up, long Down)> _traffic = [];
    private readonly List<(DateTimeOffset Time, int? Value)> _delays = [];
    private readonly bool[] _visible = [true, true, true];
    private readonly DispatcherTimer _motion = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTimeOffset _now;
    private long _started;
    private double _interval = 1, _fromMaximum = 100000;
    internal double? PreviewElapsed { get; set; }
    private double SecondsSinceSample => PreviewElapsed ?? (_started == 0 ? 0 : Stopwatch.GetElapsedTime(_started).TotalSeconds);
    internal DateTimeOffset DisplayTime => _now == default ? _now : _now.AddSeconds(-_interval + SecondsSinceSample);
    internal bool Animating => _motion.IsEnabled;
    private double? _hoverX;
    private string _hoverText = "";
    internal bool HasData => _traffic.Count > 0 || _delays.Count > 0;
    internal event Action<string>? HoverChanged;
    internal int SampleCount => _traffic.Count;
    internal int LatencyCount => _delays.Count;
    internal Rect Plot => new(78, 22, Math.Max(0, Bounds.Width - 144), Math.Max(0, Bounds.Height - 50));
    internal double SpeedMaximum => NiceMaximum(_traffic.Select(s => Math.Max(s.Up, s.Down)).DefaultIfEmpty(0).Max(), 100000);
    internal double LatencyMaximum => NiceMaximum(_delays.Select(s => s.Value ?? 0).DefaultIfEmpty(0).Max(), 100);
    internal readonly record struct DelaySegment(DateTimeOffset FromTime, double From, DateTimeOffset ToTime, double To, bool Estimated);
    // Missing measurements keep their null value. Only their visual connector holds the last
    // measured height; the dashed stroke and hover text distinguish it from a new measurement.
    internal IEnumerable<DelaySegment> LatencySegments()
    {
        (DateTimeOffset Time, double Value, bool Missing)? previous = null;
        foreach (var sample in _delays)
        {
            if (previous is null && sample.Value is null) continue;
            var value = sample.Value ?? previous!.Value.Value;
            if (previous is { } from && sample.Time > from.Time)
                yield return new(from.Time, from.Value, sample.Time, value,
                    from.Missing || sample.Value is null);
            previous = (sample.Time, value, sample.Value is null);
        }
        if (previous is { } last && DisplayTime > last.Time)
            yield return new(last.Time, last.Value, DisplayTime, last.Value, true);
    }
    internal TrafficChart()
    {
        Height = 220; ClipToBounds = true;
        _motion.Tick += (_, _) => { if(_hoverX is { } x)HoverAt(x);else InvalidateVisual(); };
        AttachedToVisualTree += (_, _) => { if (HasData) _motion.Start(); };
        DetachedFromVisualTree += (_, _) => _motion.Stop();
        PointerMoved += (_, e) => { var point = e.GetPosition(this); if (Plot.Contains(point)) HoverAt(point.X); else ClearHover(); };
        PointerExited += (_, _) => ClearHover();
    }
    private double RenderMaximum => _fromMaximum+(SpeedMaximum-_fromMaximum)*Math.Clamp(SecondsSinceSample/Math.Max(.1,_interval),0,1);
    internal bool SeriesVisible(int series) => _visible[series];
    internal void SetSeriesVisible(int series, bool visible) { _visible[series] = visible; InvalidateVisual(); }
    internal void Add(long up, long down, DateTimeOffset? time = null)
    {
        var stamp = time ?? DateTimeOffset.Now; _fromMaximum = RenderMaximum;
        if (_now != default && (stamp-_now).TotalSeconds is >= .1 and <= 3) _interval = (stamp-_now).TotalSeconds;
        _now = stamp; _traffic.Add((stamp, Math.Max(0, up), Math.Max(0, down))); Advance(stamp);
        _started = Stopwatch.GetTimestamp(); if (VisualRoot is not null) _motion.Start(); InvalidateVisual();
    }
    internal void AddLatency(int? delay, DateTimeOffset? time = null)
    {
        var stamp = time ?? DateTimeOffset.Now; _delays.Add((stamp, delay is >= 0 ? delay : null));
        if (_traffic.Count == 0) { _now=stamp; _started=Stopwatch.GetTimestamp(); if(VisualRoot is not null)_motion.Start(); }
        Advance(stamp); InvalidateVisual();
    }
    private void Advance(DateTimeOffset stamp)
    {
        _traffic.RemoveAll(s => s.Time < _now.AddSeconds(-60)); _delays.RemoveAll(s => s.Time < _now.AddSeconds(-60));
        if (_traffic.Count > 120) _traffic.RemoveRange(0, _traffic.Count - 120); if (_delays.Count > 120) _delays.RemoveRange(0, _delays.Count - 120);
        if (_hoverX is { } x) HoverAt(x);
    }
    internal string HoverAt(double x)
    {
        if (!HasData) { ClearHover(); return ""; }
        _hoverX = Math.Clamp(x, Plot.Left, Plot.Right);
        var stamp = DisplayTime.AddSeconds(-60 + 60 * ((_hoverX.Value - Plot.Left) / Math.Max(1, Plot.Width)));
        var sample = _traffic.OrderBy(s => Math.Abs((s.Time - stamp).TotalMilliseconds)).FirstOrDefault();
        var latency = _delays.OrderBy(s => Math.Abs((s.Time - stamp).TotalMilliseconds)).FirstOrDefault();
        var trafficClose = sample.Time != default && Math.Abs((sample.Time - stamp).TotalSeconds) <= 2;
        var delayClose = latency.Time != default && Math.Abs((latency.Time - stamp).TotalSeconds) <= 2;
        var text = stamp.ToLocalTime().ToString("HH:mm:ss") + "  ·  " + UiText.Get("Download") + " " + (trafficClose ? UiFormat.Speed(sample.Down) : "—") + "  ·  " + UiText.Get("Upload") + " " + (trafficClose ? UiFormat.Speed(sample.Up) : "—");
        if (_visible[2]) text += "  ·  " + UiText.Get("Latency") + " " + (delayClose ? latency.Value is { } ms ? ms + " ms" : UiText.Get("Timeout") : "—");
        _hoverText = text;
        HoverChanged?.Invoke(text); InvalidateVisual(); return text;
    }
    private void ClearHover() { _hoverX = null; _hoverText = ""; HoverChanged?.Invoke(""); InvalidateVisual(); }
    internal void Reset() { _traffic.Clear(); _delays.Clear(); _now = default; _started=0; _interval=1; _fromMaximum=100000; _motion.Stop(); ClearHover(); }
    internal void ClearLatencies() { _delays.Clear(); InvalidateVisual(); }
    private static double NiceMaximum(double value, double minimum)
    {
        var raw = Math.Max(minimum, value * 1.12); var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw))); return Math.Ceiling(raw / magnitude) * magnitude;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); var plot = Plot; if (!HasData || plot.Width < 1 || plot.Height < 1) return;
        void Label(string value, double x, double y, bool right = false)
        {
            var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, Skin.SecondaryBrush);
            context.DrawText(text, new(x - (right ? text.Width : 0), y));
        }
        var speedMax = RenderMaximum; var delayMax = LatencyMaximum;
        Label(UiText.Get("Speed") + " (MB/S)", plot.Left, 0); if (_visible[2]) Label(UiText.Get("Latency") + " (ms)", Bounds.Width - 2, 0, true);
        for (var i = 0; i < 4; i++)
        {
            var fraction = i / 3d; var y = plot.Bottom - plot.Height * fraction;
            context.DrawLine(new Pen(Skin.TabBackEdge, 1), new(plot.Left, y), new(plot.Right, y));
            Label(UiFormat.SpeedNumber(speedMax * fraction), plot.Left - 10, y - 6, true); if (_visible[2]) Label($"{delayMax * fraction:0}", plot.Right + 10, y - 6);
        }
        Label("−60 s", plot.Left, plot.Bottom + 10); Label("−30 s", plot.Center.X, plot.Bottom + 10, true); Label(UiText.Get("Now"), plot.Right, plot.Bottom + 10, true);
        var cursor=DisplayTime;
        double X(DateTimeOffset time) => plot.Left + (1 - (cursor - time).TotalSeconds / 60) * plot.Width;
        void Segment(DateTimeOffset fromTime, double fromValue, DateTimeOffset toTime, double toValue, double maximum, IBrush brush, bool estimated = false)
        {
            var from = new Point(X(fromTime), plot.Bottom - Math.Clamp(fromValue / maximum, 0, 1) * plot.Height);
            var to = new Point(X(toTime), plot.Bottom - Math.Clamp(toValue / maximum, 0, 1) * plot.Height);
            // Shared horizontal tangents join the measured curve and missing-data connectors
            // without overshooting peaks, even when a slow probe takes several seconds.
            var path = new StreamGeometry();
            using (var line = path.Open())
            { line.BeginFigure(from, false); var third = (to.X - from.X) / 3; line.CubicBezierTo(new(from.X + third, from.Y), new(to.X - third, to.Y), to); line.EndFigure(false); }
            using (context.PushClip(plot)) context.DrawGeometry(null, new Pen(brush, 2, estimated ? DashStyle.Dash : null, lineCap: PenLineCap.Round), path);
        }
        void Line(IEnumerable<(DateTimeOffset Time, double? Value)> source, double maximum, IBrush brush, double maximumGap)
        {
            Point? previous = null; DateTimeOffset previousTime = default;
            foreach (var sample in source)
            {
                if (sample.Value is not { } value) { previous = null; continue; }
                var point = new Point(X(sample.Time), plot.Bottom - Math.Clamp(value / maximum, 0, 1) * plot.Height);
                if (previous is { } from && sample.Time > previousTime && (sample.Time - previousTime).TotalSeconds <= maximumGap)
                {
                    Segment(previousTime, (plot.Bottom - from.Y) / plot.Height * maximum, sample.Time, value, maximum, brush);
                }
                previous = point; previousTime = sample.Time;
            }
        }
        IEnumerable<(DateTimeOffset, double?)> Speeds(bool upload)
        {
            for (var i = 0; i < _traffic.Count; i++)
            {
                double value = upload ? _traffic[i].Up : _traffic[i].Down;
                yield return (_traffic[i].Time, value);
            }
        }
        if (_visible[0]) Line(Speeds(false), speedMax, DownloadColor, 3);
        if (_visible[1]) Line(Speeds(true), speedMax, UploadColor, 3);
        if (_visible[2]) foreach (var segment in LatencySegments())
            Segment(segment.FromTime, segment.From, segment.ToTime, segment.To, delayMax, LatencyColor, segment.Estimated);
        if (_hoverX is { } hover)
        {
            context.DrawLine(new Pen(Skin.SecondaryBrush, 1, DashStyle.Dash), new(hover, plot.Top), new(hover, plot.Bottom));
            var text = new FormattedText(_hoverText.Replace("  ·  ", "\n"), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 12, Skin.LabelBrush);
            var boxWidth = text.Width + 24; var boxHeight = text.Height + 20;
            var left = Math.Clamp(hover + 12, plot.Left, Math.Max(plot.Left, plot.Right - boxWidth));
            var box = new Rect(left, plot.Top + 12, boxWidth, boxHeight);
            context.DrawRectangle(Skin.PasteboardBrush, new Pen(Skin.TabBackEdge, 1), box, 8, 8);
            context.DrawText(text, new(box.Left + 12, box.Top + 10));
        }
    }
}
