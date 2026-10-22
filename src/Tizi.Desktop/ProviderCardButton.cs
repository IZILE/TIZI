using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
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
    private ContextMenu? _activeContextMenu;
    private static readonly IBrush HoverTint=new SolidColorBrush(Color.Parse("#182979F5")), MultipleTint=new SolidColorBrush(Color.Parse("#262979F5"));
    private static readonly IBrush ClearNodeTint=new SolidColorBrush(Color.FromArgb(0,41,121,245));
    internal ProviderCardButton(bool nodeTile=false) { _nodeTile=nodeTile; Classes.Add("provider-card"); }
    internal void SetMultipleSelected(bool selected) { _multipleSelected=selected; UpdateFeedback(); }
    internal void SetContextMenuActive(ContextMenu menu,bool active)
    {
        if(active)_activeContextMenu=menu;
        else if(ReferenceEquals(_activeContextMenu,menu))_activeContextMenu=null;
        UpdateFeedback();
    }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);_face=e.NameScope.Find<Border>("buttonFace");
        if(!_nodeTile&&_face is not null)
        {
            // Keep the tint's RGB fixed. Interpolating from a transparent dark RGB
            // to the light selection brush produces a darker intermediate frame.
            _face.Transitions=null;Motion.SetBackground(_face,Ui.SelectionBrush);
            _face.Background=Ui.SelectionBrush;_face.Opacity=0;
            _face.Transitions=new Transitions{new DoubleTransition{Property=OpacityProperty,
                Duration=TimeSpan.FromMilliseconds(120),Easing=new SplineEasing(0,0,.58,1)}};
        }
        else if(_nodeTile&&_face is not null)
        {
            var transitions=_face.Transitions;_face.Transitions=null;
            _face.Background=ClearNodeTint;_face.BorderBrush=ClearNodeTint;_face.Transitions=transitions;
        }
        UpdateFeedback();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change);if(change.Property==IsPointerOverProperty)UpdateFeedback(); }
    private void UpdateFeedback()
    {
        if(_face is null)return;
        if(!_nodeTile)
        {
            Motion.Set(_face,OpacityProperty,IsPointerOver||_activeContextMenu is not null?1d:0d);
            return;
        }
        _face.BorderThickness=new Thickness(1.25);
        Motion.Set(_face,Border.BorderBrushProperty,IsPointerOver||_multipleSelected?MovingRows.CurrentNodeBrush:ClearNodeTint);
        Motion.Set(_face,Border.BackgroundProperty,_multipleSelected?MultipleTint:IsPointerOver?HoverTint:ClearNodeTint);
    }
}
