using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Tizi.Desktop;

// Card content stays fully opaque above its background and hover feedback.
internal sealed class ProviderCardButton : Button
{
    private readonly bool _nodeTile;
    private Border? _face;
    private bool _multipleSelected;
    private static readonly IBrush HoverTint=new SolidColorBrush(Color.Parse("#182979F5")), MultipleTint=new SolidColorBrush(Color.Parse("#262979F5"));
    internal ProviderCardButton(bool nodeTile=false) { _nodeTile=nodeTile; Classes.Add("provider-card"); }
    internal void SetMultipleSelected(bool selected) { _multipleSelected=selected; UpdateFeedback(); }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    { base.OnApplyTemplate(e);_face=e.NameScope.Find<Border>("buttonFace");UpdateFeedback(); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change);if(change.Property==IsPointerOverProperty)UpdateFeedback(); }
    private void UpdateFeedback()
    {
        if(!_nodeTile||_face is null)return;
        _face.BorderThickness=new Thickness(1.25);
        Motion.Set(_face,Border.BorderBrushProperty,IsPointerOver||_multipleSelected?MovingRows.CurrentNodeBrush:Brushes.Transparent);
        Motion.Set(_face,Border.BackgroundProperty,_multipleSelected?MultipleTint:IsPointerOver?HoverTint:Brushes.Transparent);
    }
}
