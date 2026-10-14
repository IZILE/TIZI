using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

/// <summary>A client-area dialog lifetime. It never creates or activates another OS window.</summary>
internal class CenteredDialog : ContentControl
{
    private MainWindow? _host;
    private Control? _source;
    private double _sourceOpacity;
    private bool _prepared, _closing, _closed;
    private readonly TaskCompletionSource<object?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal DialogOverlay? Overlay { get; private set; }
    internal string Title { get; set; } = "";
    internal SizeToContent SizeToContent { get; set; }
    internal Size ClientSize => Bounds.Size;
    internal Control? Owner { get; private set; }
    internal bool Modal { get; private set; }
    internal IEnumerable<CenteredDialog> OwnedDialogs => _host?.OpenDialogs.Where(sheet=>ReferenceEquals(sheet.Owner,this)) ?? [];
    internal IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)!.StorageProvider;
    internal event EventHandler? Opened;
    internal event EventHandler? Closed;
    internal event EventHandler<CancelEventArgs>? Closing;
    internal CenteredDialog() { IsVisible=false;Focusable=true;KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Cycle); }
    internal void Show(Control owner) => Present(owner,false);
    internal Task ShowDialog(Control owner) { Present(owner,true);return _completion.Task; }
    internal async Task<T> ShowDialog<T>(Control owner)
    { Present(owner,true);var result=await _completion.Task;return result is T value ? value : default!; }
    private void Present(Control owner,bool modal)
    {
        if(_prepared) { Activate();return; }
        if(Content is not Control body)throw new InvalidOperationException("A dialog needs content.");
        _host=TopLevel.GetTopLevel(owner) as MainWindow ?? throw new InvalidOperationException("A dialog needs the main window as its host.");
        _prepared=true;Owner=owner;Modal=modal;
        var width=double.IsFinite(Width)?Width:620;
        var height=SizeToContent.HasFlag(SizeToContent.Height)?(double?)null:double.IsFinite(Height)?Height:null;
        var origin=DialogOrigin.Take(owner);_source=origin?.Control;
        var snapshot=DialogOrigin.Capture(_source);
        if(_source is not null) { _sourceOpacity=_source.Opacity;_source.Opacity=0; }
        var padding=Padding;var maxHeight=MaxHeight;Content=null;Padding=default;
        Width=Height=double.NaN;MinWidth=MinHeight=0;MaxWidth=MaxHeight=double.PositiveInfinity;
        HorizontalAlignment=HorizontalAlignment.Stretch;VerticalAlignment=VerticalAlignment.Stretch;
        Background=Brushes.Transparent;
        Overlay=new DialogOverlay(body,Title,padding,width,height,maxHeight,origin,snapshot,Close);Content=Overlay;
        AddHandler(KeyDownEvent,(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _host.AttachDialog(this);IsVisible=true;_host.UpdateLayout();Opened?.Invoke(this,EventArgs.Empty);
        Dispatcher.UIThread.Post(()=>{if(IsVisible&&!_closing){Overlay.Reveal();if(!IsKeyboardFocusWithin)Focus();}},DispatcherPriority.Loaded);
    }
    internal void Activate() { if(!_closed){_host?.ActivateDialog(this);Focus();} }
    internal void Close() => Close(null);
    internal void Close(object? result)
    {
        if(_closed||_closing)return;
        var args=new CancelEventArgs();Closing?.Invoke(this,args);if(args.Cancel)return;
        _closing=true;Overlay?.Dismiss();_ = FinishAfterMotionAsync(result);
    }
    internal Task CloseSheetAsync(object? result=null)
    { Close(result);return _closing||_closed?_completion.Task:Task.CompletedTask; }
    private async Task FinishAfterMotionAsync(object? result) { await Task.Delay(240);Finish(result); }
    internal bool CloseForOwner()
    {
        if(_closed)return true;
        var args=new CancelEventArgs();Closing?.Invoke(this,args);if(args.Cancel)return false;
        Finish(null);return true;
    }
    internal void ForceClose() => Finish(null);
    private void Finish(object? result)
    {
        if(_closed)return;_closed=true;
        foreach(var child in OwnedDialogs.ToArray())child.ForceClose();
        IsVisible=false;_host?.DetachDialog(this);
        if(_source is not null) { _source.Opacity=_sourceOpacity;Dispatcher.UIThread.Post(()=>{if(_source.IsEffectivelyVisible&&_source.IsEffectivelyEnabled)_source.Focus();}); }
        try { Closed?.Invoke(this,EventArgs.Empty); }
        finally { _completion.TrySetResult(result); }
    }
}

internal sealed record DialogOrigin(Control? Control, Rect ScreenBounds, Color Color, double Radius, string Label)
{
    private static DialogOrigin? _last;
    private static long _remembered;
    internal static void Remember(Control control)
    {
        if (control.GetVisualAncestors().OfType<MoreActionsSheet>().FirstOrDefault() is { } actions) control = actions.Trigger;
        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0 || !control.IsVisible) return;
        var scale = TopLevel.GetTopLevel(control)?.RenderScaling ?? 1;
        var position = control.PointToScreen(default);
        var text = control is MenuItem menu ? menu.Header : (control as ContentControl)?.Content;
        var label = text is TextBlock block ? block.Text ?? "" : text as string ?? control.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
        var radius = control is Button button ? button.CornerRadius.TopLeft : 12;
        _last = new(control, new(position.X, position.Y, control.Bounds.Width * scale, control.Bounds.Height * scale), SurfaceColor(control), radius, label);
        _remembered = System.Diagnostics.Stopwatch.GetTimestamp();
    }
    internal static Color SurfaceColor(Control control)
    {
        var result=Skin.Chrome;
        foreach(var current in control.GetVisualAncestors().OfType<Control>().Reverse().Append(control))
        {
            IBrush? brush=current switch {Border border=>border.Background,Panel panel=>panel.Background,TemplatedControl templated=>templated.Background,_=>null};
            if(brush is ISolidColorBrush solid)result=Composite(result,solid.Color);
            if(current is MovingRows rows&&rows.RowSurfaceColor(control) is { } row)result=Composite(result,row);
        }
        if(control is Button && control.GetVisualDescendants().OfType<Border>().FirstOrDefault(border=>border.Name=="buttonFace")?.Background is ISolidColorBrush face)
            result=Composite(result,face.Color);
        return result;
    }
    private static Color Composite(Color underneath,Color paint)
    {
        // Transparent white carries no visible ink and must never become a white morph frame.
        var alpha=paint.A/255d;
        byte Mix(byte a,byte b)=>(byte)Math.Round(a+(b-a)*alpha);
        return Color.FromRgb(Mix(underneath.R,paint.R),Mix(underneath.G,paint.G),Mix(underneath.B,paint.B));
    }
    internal static Bitmap? Capture(Control? control)
    {
        if(control is null||control.Bounds.Width<=0||control.Bounds.Height<=0)return null;
        RenderTargetBitmap? snapshot=null;
        var textMode=TextOptions.GetTextRenderingMode(control);
        try
        {
            var scale=Math.Min(TopLevel.GetTopLevel(control)?.RenderScaling??1,Math.Min(4096/control.Bounds.Width,4096/control.Bounds.Height));
            snapshot=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(control.Bounds.Width*scale),(int)Math.Ceiling(control.Bounds.Height*scale)),new Vector(96*scale,96*scale));
            // The preview is composited over a changing surface, so LCD subpixel ink is inappropriate.
            TextOptions.SetTextRenderingMode(control,TextRenderingMode.Antialias);
            snapshot.Render(control);return snapshot;
        }
        catch(Exception error) {snapshot?.Dispose();System.Diagnostics.Debug.WriteLine(error.Message);return null;}
        finally {TextOptions.SetTextRenderingMode(control,textMode);}
    }
    internal static DialogOrigin? Take(Control? owner)
    {
        if (owner is null || TopLevel.GetTopLevel(owner) is not { } top) return null;
        var screen = top.PointToScreen(default); var scale = top.RenderScaling;
        var ownerBounds = new Rect(screen.X, screen.Y, top.ClientSize.Width * scale, top.ClientSize.Height * scale);
        if (_last is { } last && System.Diagnostics.Stopwatch.GetElapsedTime(_remembered).TotalMilliseconds < 1800
            && ownerBounds.Intersects(last.ScreenBounds))
        { _last = null; return last; }
        if (top.FocusManager?.GetFocusedElement() is Control focused && focused is Button)
        { Remember(focused); var result = _last; _last = null; return result; }
        return null;
    }
}

/// <summary>The surface changes its bounds; its final-sized text is clipped and faded, never stretched.</summary>
internal sealed class DialogOverlay : Control
{
    internal static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<DialogOverlay, double>(nameof(Progress));
    internal static readonly StyledProperty<double> VisualProgressProperty = AvaloniaProperty.Register<DialogOverlay, double>(nameof(VisualProgress));
    internal double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    internal double VisualProgress { get => GetValue(VisualProgressProperty); set => SetValue(VisualProgressProperty, value); }
    internal Rect SourceBounds { get; private set; }
    internal Rect TargetBounds { get; private set; }
    internal Rect VisibleBounds => ElasticEasing.Bounds(SourceBounds, TargetBounds, Progress);
    internal bool Retracting { get; private set; }
    internal double PreferredMaxHeight { get; }
    private readonly double _width;
    private readonly double? _height;
    private readonly DialogOrigin? _origin;
    private readonly Action _dismiss;
    private readonly Grid _body;
    private readonly TextBlock _label;
    private readonly Image? _preview;
    private Bitmap? _snapshot;
    private readonly DoubleTransition _transition, _visualTransition;
    internal bool HasSourcePreview => _snapshot is not null;
    internal Color SourceColor => _origin?.Color ?? Skin.Chrome;
    internal double SourceContentOpacity => _preview?.Opacity ?? _label.Opacity;
    internal Color SurfaceColor
    {
        get
        {
            // Finish the color hand-off during the first part of the expansion, before a large blue sheet can form.
            var p=Smooth(Progress/.55);var source=SourceColor;var target=Skin.Chrome;
            byte Mix(byte a,byte b)=>(byte)Math.Round(a+(b-a)*p);
            return Color.FromRgb(Mix(source.R,target.R),Mix(source.G,target.G),Mix(source.B,target.B));
        }
    }
    static DialogOverlay() => AffectsArrange<DialogOverlay>(ProgressProperty);

    internal DialogOverlay(Control content, string title, Thickness padding, double width, double? height, double maxHeight, DialogOrigin? origin, Bitmap? snapshot, Action dismiss)
    {
        _width = width; _height = height; PreferredMaxHeight = maxHeight; _origin = origin; _dismiss = dismiss;
        ClipToBounds = true;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var caption = Ui.Value(title, size: 20); caption.FontWeight = FontWeight.SemiBold; caption.Margin = new(24, 20, 24, 0);
        var body = new Border { Padding = padding, Child = content };
        _body = new Grid { RowDefinitions = new("Auto,16,*"), Children = { caption, body } };
        Grid.SetRow(body, 2);
        _label = Ui.Value(origin?.Label ?? "", size: 14); _label.TextAlignment = TextAlignment.Center; _label.VerticalAlignment = VerticalAlignment.Center;
        VisualChildren.Add(_body); LogicalChildren.Add(_body); VisualChildren.Add(_label); LogicalChildren.Add(_label);
        _snapshot=snapshot;
        if(snapshot is not null)
        {
            _preview=new Image {Source=snapshot,Stretch=Stretch.None,IsHitTestVisible=false};
            RenderOptions.SetBitmapInterpolationMode(_preview,BitmapInterpolationMode.HighQuality);
            VisualChildren.Add(_preview);LogicalChildren.Add(_preview);
        }
        _transition = new DoubleTransition { Property = ProgressProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new SplineEasing(.16,1,.3,1) };
        _visualTransition=new DoubleTransition {Property=VisualProgressProperty,Duration=TimeSpan.FromMilliseconds(260),Easing=new LinearEasing()};
        Transitions = new() { _transition,_visualTransition };
        PropertyChanged += (_, e) => { if (e.Property == ProgressProperty || e.Property==VisualProgressProperty) { UpdateClip(); InvalidateVisual(); } };
        DetachedFromVisualTree+=(_,_)=> {if(_preview is not null)_preview.Source=null;_snapshot?.Dispose();_snapshot=null;};
    }
    internal void Reveal() { Retracting = false; _transition.Duration = _visualTransition.Duration = TimeSpan.FromMilliseconds(260); Motion.Set(this, ProgressProperty, 1d);Motion.Set(this,VisualProgressProperty,1d); UpdateClip(); }
    internal void Dismiss() { Retracting = true; _transition.Duration = _visualTransition.Duration = TimeSpan.FromMilliseconds(220); Motion.Set(this, ProgressProperty, 0d);Motion.Set(this,VisualProgressProperty,0d); UpdateClip(); }
    internal void SetImmediately(double value) { var transitions = Transitions; Transitions = null; Progress = VisualProgress = value; Transitions = transitions; }
    protected override Size MeasureOverride(Size available)
    {
        var size = new Size(Math.Max(0, Math.Min(_width, available.Width - 48)), Math.Max(0, Math.Min(PreferredMaxHeight, available.Height - 48)));
        _body.Measure(new(size.Width, _height is { } height ? Math.Min(height, size.Height) : size.Height)); _label.Measure(available);_preview?.Measure(available);
        return available;
    }
    protected override Size ArrangeOverride(Size final)
    {
        var width = Math.Max(0, Math.Min(_width, final.Width - 48));
        var height = Math.Max(0, Math.Min(_height ?? _body.DesiredSize.Height, Math.Min(PreferredMaxHeight, final.Height - 48)));
        TargetBounds = new((final.Width - width) / 2, (final.Height - height) / 2, width, height);
        if (_origin is { } origin && TopLevel.GetTopLevel(this) is { } top)
        {
            var offset = this.PointToScreen(default); var scale = top.RenderScaling;
            var screen = origin.ScreenBounds;
            if (origin.Control is { } source && TopLevel.GetTopLevel(source) is Window { IsVisible: true } sourceWindow)
            { var point = source.PointToScreen(default); screen = new(point.X, point.Y, source.Bounds.Width * sourceWindow.RenderScaling, source.Bounds.Height * sourceWindow.RenderScaling); }
            SourceBounds = new((screen.X - offset.X) / scale, (screen.Y - offset.Y) / scale, screen.Width / scale, screen.Height / scale);
        }
        else SourceBounds = new(final.Width / 2 - 48, final.Height / 2 - 18, 96, 36);
        var previewBounds=new Rect(VisibleBounds.Center-new Vector(SourceBounds.Width/2,SourceBounds.Height/2),SourceBounds.Size);
        _body.Arrange(TargetBounds); _label.Arrange(previewBounds);_preview?.Arrange(previewBounds); UpdateClip(); return final;
    }
    private void UpdateClip()
    {
        var p = Math.Clamp(Progress, 0, 1);var t=Math.Clamp(VisualProgress,0,1); var bounds = VisibleBounds; var radius = Radius(p);
        _body.Clip = new RectangleGeometry(new(bounds.X - TargetBounds.X, bounds.Y - TargetBounds.Y, bounds.Width, bounds.Height), radius, radius);
        _body.Opacity = Smooth((t - .2) / .65); _body.IsHitTestVisible = !Retracting && p > .7 && t>.45;
        _label.Opacity = _preview is null?1-Smooth(t/.45):0;
        if(_preview is not null)
        {
            _preview.Opacity=1-Smooth(t/.45);
            _preview.Clip=new RectangleGeometry(new Rect(bounds.Position-_preview.Bounds.Position,bounds.Size),radius,radius);
        }
    }
    private double Radius(double p) => (_origin?.Radius ?? 18) + (18 - (_origin?.Radius ?? 18)) * p;
    public override void Render(DrawingContext context)
    {
        base.Render(context); var p = Math.Clamp(Progress, 0, 1);var t=Math.Clamp(VisualProgress,0,1);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(88 * t), 0, 0, 0)), null, new Rect(Bounds.Size));
        context.DrawRectangle(new SolidColorBrush(SurfaceColor),
            new Pen(new SolidColorBrush(Color.FromArgb((byte)(34 * p), 255, 255, 255)), .5), VisibleBounds, Radius(p), Radius(p),
            new BoxShadows(new BoxShadow { OffsetY = 12 * p, Blur = 36 * p, Color = Color.FromArgb((byte)(100 * p), 0, 0, 0) }));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!VisibleBounds.Contains(e.GetPosition(this))) { if (!Retracting) _dismiss(); e.Handled = true; }
        else if (Retracting || Progress < .7 || VisualProgress<=.45) e.Handled = true;
        base.OnPointerPressed(e);
    }
    private static double Smooth(double value) {var t=Math.Clamp(value,0,1);return t*t*(3-2*t);}
}
