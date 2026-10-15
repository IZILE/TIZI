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
    internal NavigationGlyph Glyph => (NavigationGlyph)_icon.Child!;
    internal bool IsError { get; private set; }
    internal bool IsWarning { get; private set; }
    private readonly DispatcherTimer _expiry = new() { Interval = TimeSpan.FromSeconds(6) };

    internal ClientNotification()
    {
        Width = 520; MaxWidth = 520; Margin = new(24, 40, 24, 0);
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Top;
        Padding = new(16, 12); CornerRadius = new(14); BorderThickness = new(0.5);
        Background = new SolidColorBrush(Color.Parse("#F2313134")); BorderBrush = new SolidColorBrush(Color.Parse("#35FFFFFF"));
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 24, Color = Color.Parse("#50000000") });
        RenderTransform = Offset; Opacity = 0; IsVisible = false; IsHitTestVisible = false;
        var title = Ui.Text("Notice", size: 14); title.FontWeight = FontWeight.SemiBold; title.VerticalAlignment = VerticalAlignment.Center;
        _message.TextWrapping = TextWrapping.Wrap; _message.LineHeight = 21;
        var dismiss = new Button { Content = "×", Width = 24, Height = 24, MinHeight = 0, Padding = new(0), Classes = { "plain" }, VerticalAlignment = VerticalAlignment.Center };
        dismiss.Click += (_, _) => Dismiss(); AutomationProperties.SetName(dismiss, UiText.Get("Dismiss"));
        _icon = new Border { Width = 24, Height = 24, Child = new NavigationGlyph("Notice", Ui.DataAccent), VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid { ColumnDefinitions = new("24,12,*,8,24"), RowDefinitions = new("Auto,4,Auto"), Children = { _icon, title, _message, dismiss } };
        Grid.SetColumn(title, 2); Grid.SetColumn(_message, 2); Grid.SetRow(_message, 2); Grid.SetColumn(dismiss, 4); Child = row;
        Transitions = new() { new DoubleTransition { Property = ProgressProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new SplineEasing(0.18, 0.8, 0.25, 1) } };
        PropertyChanged += (_, change) => { if (change.Property == ProgressProperty || change.Property == BoundsProperty) Place(); };
        _expiry.Tick += (_, _) => Dismiss();
        PointerEntered += (_, _) => _expiry.Stop();
        PointerExited += (_, _) => { if (Showing) _expiry.Start(); };
        DetachedFromVisualTree += (_, _) => _expiry.Stop();
    }

    internal void Show(string message, TimeSpan? duration = null, bool isError = true, bool isWarning = false)
    {
        _expiry.Stop(); _expiry.Interval = duration ?? TimeSpan.FromSeconds(6);
        _message.Text = message; ToolTip.SetTip(_message, message); AutomationProperties.SetName(this, UiText.Get("Notice") + ": " + message);
        IsError = isError && !isWarning; IsWarning = isWarning;
        var tone = IsWarning ? new SolidColorBrush(Color.Parse("#F4C152")) : IsError ? new SolidColorBrush(Color.Parse("#FF8A94")) : Ui.DataAccent;
        _icon.Child = new NavigationGlyph(IsWarning ? "Warning" : IsError ? "Error" : "Notice", tone);
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
