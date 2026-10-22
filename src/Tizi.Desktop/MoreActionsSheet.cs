using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

/// <summary>A single client-area surface grows out of its toolbar button; text is never scaled.</summary>
internal sealed class MoreActionsSheet : Control
{
    internal static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<MoreActionsSheet, double>(nameof(Progress));
    internal double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    internal Button Trigger { get; }
    internal IReadOnlyList<Button> Actions { get; }
    internal Rect SourceBounds { get; private set; }
    internal Rect TargetBounds { get; private set; }
    internal Rect VisibleBounds => ElasticEasing.Bounds(SourceBounds, TargetBounds, Progress);
    internal Task<int?> Completion => _completion.Task;
    internal bool Closing => _closing;
    private readonly TaskCompletionSource<int?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Border _body;
    private readonly TextBlock _returnCaption;
    private readonly ScrollViewer _scroll;
    private readonly DoubleTransition _transition;
    private readonly double _triggerOpacity;
    private int _generation;
    private bool _closing, _finished;
    static MoreActionsSheet() => AffectsArrange<MoreActionsSheet>(ProgressProperty);

    internal MoreActionsSheet(Button trigger, string context, IReadOnlyList<string> labels)
    {
        Trigger = trigger; _triggerOpacity = trigger.Opacity;
        Focusable = true;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        _transition = new DoubleTransition { Property = ProgressProperty, Duration = TimeSpan.FromMilliseconds(280), Easing = new SplineEasing(.2,0,.2,1) };
        Transitions = new() { _transition };
        var rows = new StackPanel { Spacing = 6 };
        var buttons = new List<Button>();
        for (var i = 0; i < labels.Count; i++)
        {
            var index = i; var button = Ui.Button(labels[i], () => _ = CloseAsync(index));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.MinHeight = 40; button.CornerRadius = new(12); button.BorderThickness = default;
            buttons.Add(button); rows.Children.Add(button);
        }
        Actions = buttons;
        _scroll = new ScrollViewer { Content = rows, AllowAutoHide = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var heading = Ui.Text("More actions", size: 20); heading.FontWeight = FontWeight.SemiBold;
        var caption = new StackPanel { Spacing = 6, Children = { heading, Ui.Text(context, true, 12) } };
        var content = new Grid { RowDefinitions = new("Auto,16,*"), Children = { caption, _scroll } };
        Grid.SetRow(_scroll, 2);
        _body = new Border { Padding = new(20), Child = content };
        VisualChildren.Add(_body); LogicalChildren.Add(_body);
        _returnCaption=new TextBlock { IsHitTestVisible=false };
        CopyTriggerText();
        VisualChildren.Add(_returnCaption);LogicalChildren.Add(_returnCaption);
        PropertyChanged += (_, change) => { if (change.Property == ProgressProperty) { UpdateClip(); InvalidateVisual(); } };
        DetachedFromVisualTree += (_, _) => Finish(null);
    }

    internal void Open()
    {
        if (_finished) return;
        if (!_closing && Progress >= .999) { Focus(); return; }
        ++_generation; _closing = false; _transition.Duration = TimeSpan.FromMilliseconds(280); _transition.Easing = new SplineEasing(.2,0,.2,1);
        Trigger.Opacity = 0; Motion.Set(this, ProgressProperty, 1d); Focus();
    }
    internal async Task CloseAsync(int? action = null)
    {
        if (_closing || _finished) return;
        _closing = true; var generation = ++_generation;
        _transition.Duration = TimeSpan.FromMilliseconds(220); _transition.Easing = new SplineEasing(.2,0,.2,1); Motion.Set(this, ProgressProperty, 0d); UpdateClip();
        await Task.Delay(240);
        if (generation == _generation) Finish(action);
    }
    internal void DismissImmediately() => Finish(null);
    private void Finish(int? result)
    {
        if (_finished) return;
        _finished = true; ++_generation; IsVisible = false; IsHitTestVisible = false;
        Trigger.Opacity = _triggerOpacity; _completion.TrySetResult(result);
    }
    internal void SetImmediately(double value) { var transitions = Transitions; Transitions = null; Progress = value; Transitions = transitions; }
    protected override Size MeasureOverride(Size available)
    {
        var width = Math.Min(360, Math.Max(0, available.Width - 32));
        var height = Math.Min(Math.Max(0, available.Height - 64), 108 + Actions.Count * 46);
        _body.Measure(new(width, height));
        _returnCaption.Measure(new(double.PositiveInfinity,double.PositiveInfinity));
        return available;
    }
    protected override Size ArrangeOverride(Size final)
    {
        var origin = Trigger.TranslatePoint(default, this) ?? new Point(16, 48);
        SourceBounds = new Rect(origin, Trigger.Bounds.Size);
        var width = Math.Min(360, Math.Max(0, final.Width - 32));
        var height = Math.Min(Math.Max(0, final.Height - 64), 108 + Actions.Count * 46);
        var x = Math.Clamp(origin.X, 16, Math.Max(16, final.Width - width - 16));
        var y = Math.Clamp(origin.Y, 40, Math.Max(40, final.Height - height - 16));
        TargetBounds = new(x, y, width, height);
        _body.Arrange(new Rect(VisibleBounds.Position,TargetBounds.Size));
        if(TriggerText is {} label&&label.TranslatePoint(default,this) is {} point)
        {
            // Keep one unscaled copy of the real button label. The full heading
            // fades away before the shrinking viewport can cut its glyphs.
            var p=Smooth(Progress/.65);var offset=point-SourceBounds.Position;
            var position=VisibleBounds.Position+new Vector(offset.X+(20-offset.X)*p,offset.Y+(20-offset.Y)*p);
            _returnCaption.Arrange(new Rect(position,label.Bounds.Size));
        }
        UpdateClip(); return final;
    }
    private void UpdateClip()
    {
        var progress = Math.Clamp(Progress, 0, 1); var visible = VisibleBounds;
        var radius = 18 + 2 * progress;
        _body.Clip = new RectangleGeometry(new Rect(visible.Size), radius, radius);
        var content=Smooth((progress-.25)/.4);
        _body.Opacity = content;
        _returnCaption.Opacity=(1-content)*_triggerOpacity;
        _body.IsHitTestVisible = !_closing && progress > .7;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        var p = Math.Clamp(Progress, 0, 1); var radius = 18 + 2 * p;
        var pColor=Smooth(p/.5);var source=DialogOrigin.SurfaceColor(Trigger);var target=Skin.MoreFill.Color;
        byte Mix(byte a,byte b)=>(byte)Math.Round(a+(b-a)*pColor);
        var surface=new SolidColorBrush(Color.FromRgb(Mix(source.R,target.R),Mix(source.G,target.G),Mix(source.B,target.B)));
        context.DrawRectangle(surface, null, VisibleBounds, radius, radius,
            new BoxShadows(new BoxShadow { OffsetY = 10 * p, Blur = 30 * p, Color = Color.FromArgb((byte)(90 * p), 0, 0, 0) }));
    }
    private TextBlock? TriggerText=>Trigger.Content as TextBlock??Trigger.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
    private void CopyTriggerText()
    {
        if(TriggerText is not {} label)return;
        _returnCaption.Text=label.Text;_returnCaption.FontFamily=label.FontFamily;_returnCaption.FontSize=label.FontSize;
        _returnCaption.FontWeight=label.FontWeight;_returnCaption.FontStyle=label.FontStyle;_returnCaption.FontStretch=label.FontStretch;
        _returnCaption.Foreground=label.Foreground;_returnCaption.FlowDirection=label.FlowDirection;
        _returnCaption.LineHeight=label.LineHeight;_returnCaption.LetterSpacing=label.LetterSpacing;
        _returnCaption.UseLayoutRounding=label.UseLayoutRounding;
    }
    private static double Smooth(double value){var p=Math.Clamp(value,0,1);return p*p*(3-2*p);}
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        if (SourceBounds.Contains(point))
        {
            if (_closing) Open(); else _ = CloseAsync();
            e.Handled = true;
        }
        else if (!VisibleBounds.Contains(point)) { _ = CloseAsync(); e.Handled = true; }
        base.OnPointerPressed(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _ = CloseAsync(); e.Handled = true; }
        base.OnKeyDown(e);
    }
    private static Rect Interpolate(Rect from, Rect to, double p) => new(from.X + (to.X - from.X) * p, from.Y + (to.Y - from.Y) * p,
        from.Width + (to.Width - from.Width) * p, from.Height + (to.Height - from.Height) * p);
}
