using Avalonia.Media;

namespace Tizi.Desktop;

internal static class UiFonts
{
    internal static readonly FontFamily Interface = new("Microsoft YaHei UI");
    internal static FontManagerOptions Options() => new()
    {
        DefaultFamilyName = Interface.Name,
        FontFallbacks =
        [
            new FontFallback { FontFamily = new FontFamily("Microsoft YaHei UI") },
            new FontFallback { FontFamily = new FontFamily("Microsoft YaHei") },
            new FontFallback { FontFamily = new FontFamily("Segoe UI Symbol") },
        ],
    };
}
