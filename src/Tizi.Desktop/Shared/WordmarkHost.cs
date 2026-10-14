using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Tizi.Desktop;

/// <summary>The approved source artwork occupies the original toolbar's optical center.</summary>
internal sealed class WordmarkHost : Decorator
{
    internal WordmarkHost() => RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    private static readonly BrandRaster Mark = new("BrandMark",28);
    internal Rect InkBounds => Mark.Bounds(new(Bounds.Width/2,Bounds.Height/2),TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using (context.PushOpacity(Skin.Label.A / 255d)) Mark.Draw(context,new(Bounds.Width/2,Bounds.Height/2),this);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(72, 44);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is TextBlock text)
        {
            text.Arrange(new Rect(0, (finalSize.Height - text.DesiredSize.Height) / 2, finalSize.Width, text.DesiredSize.Height));
        }
        return finalSize;
    }
}
