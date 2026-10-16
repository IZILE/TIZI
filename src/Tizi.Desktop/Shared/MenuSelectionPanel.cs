using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

/// <summary>A menu keeps one highlight beneath its items, including keyboard-selected items.</summary>
internal sealed class MenuSelectionPanel : StackPanel
{
    private static readonly IBrush MenuBlue = Skin.Blue;
    private readonly Dictionary<TextBlock,IBrush?> _originalInk=[];
    private static readonly StyledProperty<double> LeftProperty = AvaloniaProperty.Register<MenuSelectionPanel, double>("HighlightLeft");
    private static readonly StyledProperty<double> TopProperty = AvaloniaProperty.Register<MenuSelectionPanel, double>("HighlightTop");
    private static readonly StyledProperty<double> HighlightWidthProperty = AvaloniaProperty.Register<MenuSelectionPanel, double>("HighlightWidth");
    private static readonly StyledProperty<double> HighlightHeightProperty = AvaloniaProperty.Register<MenuSelectionPanel, double>("HighlightHeight");
    private static readonly StyledProperty<double> AlphaProperty = AvaloniaProperty.Register<MenuSelectionPanel, double>("HighlightAlpha");
    private readonly HashSet<Control> _items = [];
    private readonly Surface _surface;
    private bool _placed, _queued, _attached, _menuExpanded;
    private int _generation;
    private Rect _destination;
    internal Rect HighlightBounds => new(GetValue(LeftProperty), GetValue(TopProperty), GetValue(HighlightWidthProperty), GetValue(HighlightHeightProperty));
    internal Rect Destination => _destination;
    internal double HighlightAlpha => GetValue(AlphaProperty);
    internal IBrush HighlightFill => Orientation == Orientation.Horizontal ? (_menuExpanded ? Skin.MenuSurface : Skin.TabFront) : MenuBlue;
    internal Control? Highlighted { get; private set; }

    public MenuSelectionPanel()
    {
        _surface = new Surface(this) { ZIndex = -1, IsHitTestVisible = false };
        VisualChildren.Add(_surface);
        PropertyChanged += (_, args) =>
        {
            if (args.Property == LeftProperty || args.Property == TopProperty || args.Property == HighlightWidthProperty
                || args.Property == HighlightHeightProperty || args.Property == AlphaProperty)
            { _surface.InvalidateVisual(); RefreshInk(); }
            if (args.Property == IsPointerOverProperty) Schedule();
        };
        // Hit-test the space between rows too. Crossing a margin or separator must not briefly
        // fade the shared highlight out and back in before the next row receives the pointer.
        Background = Brushes.Transparent;
        ClipToBounds = true;
        var easing = new SplineEasing(0, 0, 0.58, 1);
        Transitions = new Transitions();
        foreach (var property in new[] { LeftProperty, TopProperty, HighlightWidthProperty, HighlightHeightProperty, AlphaProperty })
            Transitions.Add(new DoubleTransition { Property = property,
                Duration = TimeSpan.FromMilliseconds(property == AlphaProperty ? 80 : SelectionIndicator.DurationMilliseconds), Easing = easing });
        Children.CollectionChanged += (_, _) => Reconcile();
        AttachedToVisualTree += (_, _) => { _attached = true; Reconcile(); Schedule(); };
        DetachedFromVisualTree += (_, _) =>
        {
            foreach(var (text,ink) in _originalInk)text.Foreground=ink;_originalInk.Clear();
            foreach(var item in _items)item.Classes.Remove("moving-menu-ink");
            ++_generation; _queued = false; _placed = false; _attached = false; Highlighted = null;
            var transitions = Transitions; Transitions = null; SetValue(AlphaProperty, 0); Transitions = transitions;
        };
    }

    private void Reconcile()
    {
        if (!VisualChildren.Contains(_surface)) VisualChildren.Add(_surface);
        var current = Children.Where(item => item is MenuItem or ComboBoxItem).ToHashSet();
        foreach (var item in _items.Except(current).ToArray())
        { item.PropertyChanged -= ItemChanged; item.Classes.Remove("moving-menu"); item.Classes.Remove("moving-menu-ink"); _items.Remove(item); }
        foreach (var item in current.Except(_items).ToArray())
        { item.Classes.Add("moving-menu"); item.PropertyChanged += ItemChanged; _items.Add(item); }
        Schedule();
    }
    private void ItemChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property.Name is "IsSelected" or "IsPointerOver" or "IsSubMenuOpen" or "IsEnabled" or "IsVisible") Schedule();
    }
    private void Schedule()
    {
        if (_queued || !_attached) return;
        _queued = true; var generation = _generation;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _generation) return;
            _queued = false;
            if (_attached) Refresh();
        }, DispatcherPriority.Loaded);
    }
    private void Refresh()
    {
        var enabled = Children.Where(item => item is MenuItem or ComboBoxItem && item.IsEnabled && item.IsVisible).ToArray();
        var open = Orientation == Orientation.Horizontal ? enabled.OfType<MenuItem>().FirstOrDefault(item => item.IsSubMenuOpen) : null;
        if (_menuExpanded != (open is not null))
        { _menuExpanded = open is not null; _surface.InvalidateVisual(); }
        // An expanded menu owns the horizontal surface. Pointer hover must not paint
        // another heading while its dropdown still belongs to the previous heading.
        var item = open ?? enabled.FirstOrDefault(item => item.IsPointerOver)
            ?? enabled.FirstOrDefault(item => item is MenuItem { IsSelected: true } or ComboBoxItem { IsSelected: true })
            ?? enabled.FirstOrDefault(item => item is MenuItem { IsSubMenuOpen: true })
            ?? (IsPointerOver && enabled.Contains(Highlighted) ? Highlighted : null);
        Highlighted = item;
        foreach(var row in enabled.OfType<ComboBoxItem>())
        foreach(var text in row.GetVisualDescendants().OfType<TextBlock>())
        {
            if(Orientation==Orientation.Vertical&&ReferenceEquals(row,item))
            {if(!_originalInk.ContainsKey(text))_originalInk[text]=text.Foreground;text.Foreground=Skin.OnAccent;}
            else if(_originalInk.Remove(text,out var ink))text.Foreground=ink;
        }
        if (item is null) { Motion.Set(this, AlphaProperty, 0.0); return; }
        var bounds = item.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (_placed && _destination == bounds) { Motion.Set(this, AlphaProperty, 1.0); return; }
        var transitions = Transitions;
        if (!_placed) Transitions = null;
        Motion.Set(this, LeftProperty, bounds.X); Motion.Set(this, TopProperty, bounds.Y);
        Motion.Set(this, HighlightWidthProperty, bounds.Width); Motion.Set(this, HighlightHeightProperty, bounds.Height);
        Motion.Set(this, AlphaProperty, 1.0); Transitions = transitions;
        _destination = bounds; _placed = true;
        RefreshInk();
    }
    private void RefreshInk()
    {
        foreach(var row in _items.OfType<MenuItem>())
        {
            var header=row.GetVisualDescendants().OfType<ContentPresenter>()
                .FirstOrDefault(p=>p.Name=="PART_HeaderPresenter"&&ReferenceEquals(p.TemplatedParent,row));
            var white=Orientation==Orientation.Vertical&&HighlightAlpha>=.995&&header is { Bounds.Width: >0, Bounds.Height: >0 }
                &&header.TranslatePoint(default,this) is { } origin
                &&HighlightBounds.Contains(new Rect(origin,header.Bounds.Size));
            // White ink is safe only while the moving blue surface covers the label.
            // Logical selection/open state can outlive pointer departure or lead the surface.
            row.Classes.Set("moving-menu-ink",white);
        }
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        _surface.Measure(finalSize); _surface.Arrange(new Rect(finalSize)); Refresh(); RefreshInk(); return result;
    }
    private sealed class Surface(MenuSelectionPanel owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            if (owner.HighlightAlpha <= 0 || owner.HighlightBounds.Width <= 0) return;
            using (context.PushOpacity(owner.HighlightAlpha))
                context.DrawRectangle(owner.HighlightFill, null,
                    owner.HighlightBounds, owner.Orientation == Orientation.Horizontal ? 6 : 5, owner.Orientation == Orientation.Horizontal ? 6 : 5);
        }
    }
}
