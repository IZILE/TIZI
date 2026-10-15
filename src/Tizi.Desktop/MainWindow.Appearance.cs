using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private Grid? _captionGrid,_shellRoot;
    private WordmarkHost? _wordmarkHost;
    private readonly ExperimentalAcrylicBorder _chromeMaterial=new(){IsHitTestVisible=false,IsVisible=false};
    internal bool FrostedChromeVisible=>_chromeMaterial.IsVisible;
    internal ExperimentalAcrylicBorder ChromeMaterial=>_chromeMaterial;
    private void ApplyWindowAppearance()
    {
        TransparencyBackgroundFallback=Skin.ChromeBrush;
        TransparencyLevelHint=[WindowTransparencyLevel.AcrylicBlur,WindowTransparencyLevel.Mica,WindowTransparencyLevel.None];
        Background=Brushes.Transparent;
        _clientSurface.Background=Brushes.Transparent;
        _workspace.Background=Brushes.Transparent;_rail.Background=Brushes.Transparent;
        if(_captionGrid is {} caption)caption.Background=Brushes.Transparent;
        _chromeMaterial.Material=new ExperimentalAcrylicMaterial
        {
            BackgroundSource=AcrylicBackgroundSource.Digger,TintColor=Skin.Chrome,
            TintOpacity=Skin.IsLight?.82:.86,MaterialOpacity=.92,FallbackColor=Skin.Chrome
        };
        _chromeMaterial.IsVisible=true;
        var syncing=_syncing;_syncing=true;
        try
        {
            if(_settingsAppearance is {} choice)choice.SelectedIndex=Appearance.Current.Theme switch {"Light"=>1,"System"=>2,_=>0};
        }
        finally{_syncing=syncing;}
        // Every functional page retains its own opaque pasteboard and card surfaces.
        foreach(var control in this.GetVisualDescendants().OfType<Control>())control.InvalidateVisual();
    }
}
