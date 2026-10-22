using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private Grid? _captionGrid,_shellRoot;
    private WordmarkHost? _wordmarkHost;
    private readonly Border _chromeMaterial=new(){IsHitTestVisible=false,IsVisible=false};
    internal bool FrostedChromeVisible=>_chromeMaterial.IsVisible;
    internal Border ChromeMaterial=>_chromeMaterial;
    private void ApplyWindowAppearance()
    {
        TransparencyBackgroundFallback=Skin.WindowBackdrop;
        TransparencyLevelHint=[WindowTransparencyLevel.AcrylicBlur,WindowTransparencyLevel.Mica,WindowTransparencyLevel.None];
        Background=Brushes.Transparent;
        _clientSurface.Background=Brushes.Transparent;
        _workspace.Background=Brushes.Transparent;_rail.Background=Brushes.Transparent;
        if(_captionGrid is {} caption)caption.Background=Brushes.Transparent;
        // Windows supplies the backdrop blur. A separate tint keeps the requested
        // near-black color without acrylic's extra luminosity layer.
        _chromeMaterial.Background=Skin.WindowBackdropTint;
        _chromeMaterial.IsVisible=true;
        // Every functional page retains its own opaque pasteboard and card surfaces.
        foreach(var control in this.GetVisualDescendants().OfType<Control>())control.InvalidateVisual();
    }
}
