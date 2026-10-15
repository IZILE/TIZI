using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Tizi.Desktop;

/// <summary>Source-derived, supersampled pixel coverage at each common monitor scale.</summary>
internal sealed class BrandRaster(string name, int logicalHeight)
{
    private readonly (StreamGeometry Geometry,double Stroke)? _vector=Vector(name,logicalHeight);
    internal bool IsVector => _vector is not null;
    private readonly Dictionary<int, Bitmap> _variants = [];
    private readonly int[] _heights = new[] { 1d, 1.25, 1.5, 1.75, 2, 2.5, 3, 4 }.Select(scale => (int)Math.Round(logicalHeight*scale)).ToArray();
    private Bitmap? _master;
    private static Bitmap Load(string resource)
    {
        using var stream = typeof(BrandRaster).Assembly.GetManifestResourceStream(resource)!;
        return new Bitmap(stream);
    }
    internal Bitmap AtScale(double scale)
    {
        var height = logicalHeight*Math.Clamp(double.IsFinite(scale) ? scale : 1, 1, 8);
        var selected = _heights.FirstOrDefault(value => value >= height-.01);
        if (selected == 0) return _master ??= Load("Tizi.Desktop."+name+".png");
        if (!_variants.TryGetValue(selected, out var image))
            _variants[selected] = image = Load($"Tizi.Desktop.Brand.{name}-{selected}.png");
        return image;
    }
    internal Rect Bounds(Point center, double scale=1)
    {
        var image = AtScale(scale);
        var width = logicalHeight*(double)image.PixelSize.Width/image.PixelSize.Height;
        return new(center.X-width/2, center.Y-logicalHeight/2d, width, logicalHeight);
    }
    internal void Draw(DrawingContext context, Point center, Visual owner)
    {
        if(_vector is { } vector)
        {
            using(context.PushTransform(Matrix.CreateTranslation(center.X,center.Y)))
                context.DrawGeometry(null,new Pen(name=="BrandMark"?Skin.BrandInk:Brushes.White,vector.Stroke,lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round),vector.Geometry);
            return;
        }
        var scale = TopLevel.GetTopLevel(owner)?.RenderScaling ?? 1;
        context.DrawImage(AtScale(scale), Bounds(center, scale));
    }
    private static (StreamGeometry,double)? Vector(string name,int height)
    {
        if(name is not ("BrandMark" or "ConnectionLadder"))return null;
        var source=BrandGeometry.Lines(name,height);return(BrandGeometry.Path(source.Lines),source.Stroke);
    }
}
