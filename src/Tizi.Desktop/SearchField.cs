using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Tizi.Desktop;

internal sealed class SearchField : Border
{
    internal TextBox Input { get; } = new() { Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(0), MinHeight = 0, VerticalContentAlignment = VerticalAlignment.Center, Classes = { "search-entry" } };
    internal Button Clear { get; }
    internal string? Text { get => Input.Text; set => Input.Text = value; }
    internal string? PlaceholderText { get => Input.PlaceholderText; set { Input.PlaceholderText = value; AutomationProperties.SetName(Input, value); } }
    internal event EventHandler? TextChanged;
    internal SearchField()
    {
        Width = 260; Height = 36; VerticalAlignment = VerticalAlignment.Center; CornerRadius = new(18);
        Background = Skin.TabBack; BorderBrush = Skin.SearchEdge; BorderThickness = new(1);
        Transitions = new() { new BrushTransition { Property = BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new SplineEasing(0, 0, .58, 1) } };
        var icon = new Path { Data = Geometry.Parse("M10.8,6.2 A4.6,4.6 0 1 1 1.6,6.2 A4.6,4.6 0 1 1 10.8,6.2 M9.6,9.6 L14,14"), Stroke = Skin.SecondaryBrush, StrokeThickness = 1.5, StrokeLineCap = PenLineCap.Round, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        Clear = new Button { Width = 22, Height = 22, MinHeight = 0, Padding = new(5), CornerRadius = new(11), Classes = { "plain" }, IsVisible = false,
            Content = new Path { Data = Geometry.Parse("M0,0 L8,8 M8,0 L0,8"), Stroke = Skin.SecondaryBrush, StrokeThickness = 1.4, Width = 8, Height = 8, IsHitTestVisible = false } };
        UiText.Set(Clear, ToolTip.TipProperty, "Clear search"); AutomationProperties.SetName(Clear, UiText.Get("Clear search"));
        Clear.Click += (_, _) => { Input.Text = ""; Input.Focus(); };
        Input.TextChanged += (_, _) => { Clear.IsVisible = !string.IsNullOrEmpty(Input.Text); TextChanged?.Invoke(this, EventArgs.Empty); };
        Input.GotFocus += (_, _) => Motion.Set(this, BorderBrushProperty, Ui.DataAccent);
        Input.LostFocus += (_, _) => Motion.Set(this, BorderBrushProperty, Skin.SearchEdge);
        Input.KeyDown += (_, e) => { if (e.Key == Key.Escape && !string.IsNullOrEmpty(Input.Text)) { Input.Text = ""; e.Handled = true; } };
        var row = new Grid { ColumnDefinitions = new("16,8,*,6,22"), Margin = new(12, 5, 6, 5), Children = { icon, Input, Clear } };
        Grid.SetColumn(Input, 2); Grid.SetColumn(Clear, 4); Child = row;
    }
}
