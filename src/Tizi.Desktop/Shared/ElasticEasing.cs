using Avalonia;
using Avalonia.Animation.Easings;

namespace Tizi.Desktop;

/// <summary>A small, single overshoot followed by a settled endpoint.</summary>
internal sealed class ElasticEasing(double strength = 1) : Easing
{
    public override double Ease(double progress)
    {
        if(progress<=0)return 0;if(progress>=1)return 1;
        var t=progress-1;return 1+(1+strength)*t*t*t+strength*t*t;
    }
    internal static Rect Bounds(Rect from,Rect to,double progress)
    {
        var position=Math.Clamp(progress,0,1);var size=Math.Clamp(progress,0,1.04);
        var center=from.Center+(to.Center-from.Center)*position;
        var width=from.Width+(to.Width-from.Width)*size;var height=from.Height+(to.Height-from.Height)*size;
        return new(center.X-width/2,center.Y-height/2,Math.Max(0,width),Math.Max(0,height));
    }
}
