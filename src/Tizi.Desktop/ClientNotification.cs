using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Tizi.Desktop;

/// <summary>A client-local notification overlay: it never pushes the workspace down.</summary>
internal sealed class ClientNotification : Border
{
    internal static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<ClientNotification, double>(nameof(Progress));
    internal double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    internal TranslateTransform Offset { get; } = new();
    internal bool Showing { get; private set; }
    internal string Message => _message.Text ?? "";
    private readonly TextBlock _message = Ui.Value("", size: 13);
    private readonly Border _icon;
    private readonly TextBlock _glyph;
    internal bool IsError { get; private set; }
    private readonly DispatcherTimer _expiry = new() { Interval = TimeSpan.FromSeconds(6) };

    internal ClientNotification()
    {
        Width = 520; MaxWidth = 520; Margin = new(24, 40, 24, 0);
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Top;
        Padding = new(16, 12); CornerRadius = new(14); BorderThickness = new(0.5);
        Background = new SolidColorBrush(Color.Parse("#F2313134")); BorderBrush = new SolidColorBrush(Color.Parse("#35FFFFFF"));
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 24, Color = Color.Parse("#50000000") });
        RenderTransform = Offset; Opacity = 0; IsVisible = false; IsHitTestVisible = false;
        var title = Ui.Text("Notice", size: 14); title.FontWeight = FontWeight.SemiBold;
        _message.TextWrapping = TextWrapping.Wrap; _message.LineHeight = 21;
        var dismiss = new Button { Content = "×", Width = 24, Height = 24, MinHeight = 0, Padding = new(0), Classes = { "plain" }, VerticalAlignment = VerticalAlignment.Top };
        dismiss.Click += (_, _) => Dismiss(); AutomationProperties.SetName(dismiss, UiText.Get("Dismiss"));
        var copy = new StackPanel { Spacing = 4, Children = { title, _message } };
        _glyph = Ui.Value("!", size: 16); _glyph.FontWeight = FontWeight.Bold; _glyph.HorizontalAlignment = HorizontalAlignment.Center; _glyph.VerticalAlignment = VerticalAlignment.Center;
        _icon = new Border { Width = 24, Height = 24, CornerRadius = new(12), Background = new SolidColorBrush(Color.Parse("#D94250")), Child = _glyph, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 2, 0, 0) };
        var row = new Grid { ColumnDefinitions = new("24,12,*,8,24"), Children = { _icon, copy, dismiss } }; Grid.SetColumn(copy, 2); Grid.SetColumn(dismiss, 4); Child = row;
        Transitions = new() { new DoubleTransition { Property = ProgressProperty, Duration = TimeSpan.FromMilliseconds(340), Easing = new SplineEasing(0.18, 0.8, 0.25, 1) } };
        PropertyChanged += (_, change) => { if (change.Property == ProgressProperty || change.Property == BoundsProperty) Place(); };
        _expiry.Tick += (_, _) => Dismiss();
        PointerEntered += (_, _) => _expiry.Stop();
        PointerExited += (_, _) => { if (Showing) _expiry.Start(); };
        DetachedFromVisualTree += (_, _) => _expiry.Stop();
    }

    internal void Show(string message, TimeSpan? duration = null, bool isError = true)
    {
        _expiry.Stop(); _expiry.Interval = duration ?? TimeSpan.FromSeconds(6);
        _message.Text = message; ToolTip.SetTip(_message, message); AutomationProperties.SetName(this, UiText.Get("Notice") + ": " + message);
        IsError = isError; _glyph.Text = isError ? "!" : "i"; _icon.Background = isError ? new SolidColorBrush(Color.Parse("#D94250")) : Ui.DataAccent;
        Showing = true; IsVisible = true; IsHitTestVisible = true;
        Motion.Set(this, ProgressProperty, 1); _expiry.Start();
    }
    internal void Dismiss()
    {
        _expiry.Stop(); Showing = false; IsHitTestVisible = false;
        Motion.Set(this, ProgressProperty, 0); Place();
    }
    private void Place()
    {
        var progress = Math.Clamp(Progress, 0, 1);
        Offset.Y = -(Math.Max(72, Bounds.Height) + Margin.Top + 12) * (1 - progress); Opacity = progress;
        if (!Showing && progress <= 0) IsVisible = false;
    }
}
