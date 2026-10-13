using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Tizi.Desktop;

internal static class FlagImages
{
    private static readonly Dictionary<string, Bitmap?> Cache = [];
    internal static Control Control(string? code)
    {
        if (code is { Length: 2 })
        {
            if (!Cache.TryGetValue(code, out var bitmap))
            { using var stream = typeof(FlagImages).Assembly.GetManifestResourceStream("Tizi.Desktop.Flags." + code.ToUpperInvariant() + ".png"); bitmap = stream is null ? null : new Bitmap(stream); Cache[code] = bitmap; }
            if (bitmap is not null) return new Image { Source = bitmap, Width = 38, Height = 30, Stretch = Stretch.Uniform };
        }
        return new Border { Width = 38, Height = 30, CornerRadius = new(7), Background = Skin.TabFront, Child = Ui.Value(code ?? "—", true, 13), Padding = new(7, 4) };
    }
}
