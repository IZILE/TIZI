using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Tizi.Desktop;

/// <summary>A localized compact segmented picker, as used by the Mac tool options.</summary>
internal sealed class SegmentedChoice : Border
{
    private readonly Button[] _buttons;
    private readonly SelectionIndicator _selection;
    private int _selected;
    private bool _animateSelection;
    internal event Action<int>? Changed;
    internal SegmentedChoice(params string[] choices)
    {
        ArgumentOutOfRangeException.ThrowIfZero(choices.Length);
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
        CornerRadius = new CornerRadius(18);
        Background = new SolidColorBrush(Color.FromRgb(53, 53, 53));
        BorderBrush = new SolidColorBrush(Colors.White, 0.08); BorderThickness = new Thickness(0.5);
        _selection = new SelectionIndicator { Height = 34, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(Color.FromRgb(41, 121, 245)) };
        // Let the actual templates measure their text and previews. Sharing the columns makes
        // segments equal without estimating font widths or forgetting template borders.
        var row = new Grid();
        Grid.SetIsSharedSizeScope(row, true);
        _buttons = choices.Select((name, index) =>
        {
            var button = new Button { Classes = { "plain", "selection-item" }, Padding = new Thickness(15, 0),
                MinHeight = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = Brushes.Transparent,
                Content = new TextBlock { [!TextBlock.TextProperty] = UiText.Bind(name) } };
            button.Click += (_, _) => { if (_selected == index) return; SelectedIndex = index; Changed?.Invoke(index); };
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "choice" });
            Grid.SetColumn(button, index); row.Children.Add(button); return button;
        }).ToArray();
        Child = new Grid { Children = { _selection, row } };
        row.SizeChanged += (_, _) => PlaceSelection();
        foreach (var button in _buttons) button.SizeChanged += (_, _) => PlaceSelection();
        DetachedFromVisualTree += (_, _) => _animateSelection = false;
        SelectedIndex = 0;
    }
    internal int SelectedIndex
    {
        get => _selected;
        set
        {
            var index = Math.Clamp(value, 0, _buttons.Length - 1);
            // Initial shared-column layout can take more than one pass. Place that initial
            // selection directly; animate once an already laid-out choice actually changes.
            if (_selected != index && _buttons[_selected].Bounds.Width > 0) _animateSelection = true;
            _selected = index;
            PlaceSelection();
        }
    }
    internal Control SelectionHighlight => _selection;
    internal SelectionIndicator Indicator => _selection;
    internal int Count => _buttons.Length;
    internal Button ButtonAt(int index) => _buttons[index];
    internal void Compact(bool compact)
    {
        var padding = new Thickness(compact ? 8 : 15, 0);
        foreach (var button in _buttons) if (button.Padding != padding) button.Padding = padding;
    }
    private void PlaceSelection()
    {
        if (_buttons[_selected].Bounds.Width <= 0) return;
        _selection.MoveTo(new Rect(_buttons[_selected].Bounds.X, 0, _buttons[_selected].Bounds.Width, 34), _animateSelection);
    }
}
