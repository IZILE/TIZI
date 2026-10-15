using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

internal static class ChoiceScroll
{
    internal static void SelectedAtTop(ComboBox choice)
    {
        if(!choice.IsDropDownOpen||choice.SelectedIndex<0)return;
        var body=choice.GetVisualDescendants().OfType<Popup>().FirstOrDefault()?.Child;
        if(body is null)return;
        var scroll=body.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if(scroll?.Content is not ItemsPresenter presenter)return;
        var viewport=scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
        scroll.ClearValue(Control.HeightProperty);presenter.Margin=default;
        viewport?.InvalidateArrange();body.UpdateLayout();
        if(choice.ContainerFromIndex(choice.SelectedIndex) is not {} row||scroll.Viewport.Height<=0)return;
        // Keep this opening's height stable while giving the final option enough scroll room.
        scroll.Height=scroll.Viewport.Height;
        presenter.Margin=new(0,0,0,Math.Max(0,scroll.Viewport.Height-row.Bounds.Height));
        // Its constrained desired size can stay unchanged when the content grows.
        // Re-arrange explicitly so Extent includes the spacer before Offset is clamped.
        viewport?.InvalidateArrange();body.UpdateLayout();
        if(row.TranslatePoint(default,presenter) is {} point)scroll.Offset=new(0,point.Y);
    }
}
