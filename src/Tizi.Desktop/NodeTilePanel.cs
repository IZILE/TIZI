using Avalonia;
using Avalonia.Controls;

namespace Tizi.Desktop;

// Shares MovingRows' selection surface and item identity; only the arrangement changes.
internal sealed class NodeTilePanel : Panel
{
    private const double MinimumWidth = 240, Gap = 10;
    internal int Columns { get; private set; } = 1;
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsFinite(available.Width) ? available.Width : MinimumWidth;
        Columns = Math.Max(1, (int)Math.Floor((width + Gap) / (MinimumWidth + Gap)));
        var cell = Math.Max(0, (width - Gap * (Columns - 1)) / Columns);
        var height = 0d;
        for (var i = 0; i < Children.Count; i += Columns)
        {
            var rowHeight = 0d;
            for (var j = i; j < Math.Min(i + Columns, Children.Count); j++)
            { Children[j].Measure(new(cell, double.PositiveInfinity)); rowHeight = Math.Max(rowHeight, Children[j].DesiredSize.Height); }
            height += rowHeight + (i == 0 ? 0 : Gap);
        }
        return new(width, height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        var cell = Math.Max(0, (final.Width - Gap * (Columns - 1)) / Columns);
        var top = 0d;
        for (var i = 0; i < Children.Count; i += Columns)
        {
            var height = Children.Skip(i).Take(Columns).Max(c => c.DesiredSize.Height);
            for (var j = i; j < Math.Min(i + Columns, Children.Count); j++) Children[j].Arrange(new Rect((j-i)*(cell+Gap), top, cell, height));
            top += height + Gap;
        }
        return final;
    }
}
