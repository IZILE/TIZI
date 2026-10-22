using Avalonia;
using Avalonia.Styling;

namespace Tizi.Desktop;

/// <summary>The interface always uses the established dark palette, independently of Windows and legacy preferences.</summary>
internal static class Appearance
{
    internal static void Initialize(Application app)
    {
        app.RequestedThemeVariant=ThemeVariant.Dark;
        Skin.RegisterPalette(app);
    }
}
