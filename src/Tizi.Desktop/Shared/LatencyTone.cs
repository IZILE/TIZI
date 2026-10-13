using System.Globalization;
using Avalonia.Media;

namespace Tizi.Desktop;

internal static class LatencyTone
{
    internal static readonly IBrush Good = Brush.Parse("#70DCA4");
    internal static readonly IBrush Moderate = Brush.Parse("#FFB86C");
    internal static readonly IBrush Poor = Brush.Parse("#FF808C");

    internal static IBrush For(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "—") return Skin.SecondaryBrush;
        if (value.EndsWith(" ms", StringComparison.Ordinal)
            && int.TryParse(value.AsSpan(0, value.Length - 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds)
            && milliseconds >= 0)
            return milliseconds <= 150 ? Good : milliseconds <= 300 ? Moderate : Poor;
        return Poor;
    }
}
