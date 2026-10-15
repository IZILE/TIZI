using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Tizi.Desktop;

internal class SecondPage : Border
{
    internal TranslateTransform Offset { get; } = new(18,0);
    internal SecondPage()
    {
        RenderTransform=Offset;Opacity=0;ClipToBounds=true;
        Transitions=new() { new DoubleTransition { Property=OpacityProperty,Duration=TimeSpan.FromMilliseconds(160),Easing=new SplineEasing(.16,1,.3,1) } };
        Offset.Transitions=new() { new DoubleTransition { Property=TranslateTransform.XProperty,Duration=TimeSpan.FromMilliseconds(160),Easing=new ElasticEasing() } };
    }
    internal void Reveal() { Motion.Set(this,OpacityProperty,1d);Motion.Set(Offset,TranslateTransform.XProperty,0d); }
    internal void Dismiss() { IsHitTestVisible=false;Motion.Set(this,OpacityProperty,0d);Motion.Set(Offset,TranslateTransform.XProperty,18d); }
}

internal sealed class AboutPage : SecondPage
{
    internal AboutPage(string version)
    {
        Tag="about-page";Margin=new(0,0,16,0);
        using var stream=typeof(MainWindow).Assembly.GetManifestResourceStream("Tizi.Desktop.BrandMark.png")!;
        var mark=new Image { Source=new Bitmap(stream),Width=102,Height=68,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,Opacity=Skin.Label.A/255d };
        RenderOptions.SetBitmapInterpolationMode(mark,BitmapInterpolationMode.HighQuality);
        var body=new StackPanel { Spacing=22,Children={mark,Ui.Value("TIZI "+version,size:24),Ui.Text("Independent project; no servers included.",true)} };
        var credits=new StackPanel { Spacing=14,Children={Ui.Text("Open-source acknowledgements",size:18),Ui.Value("mihomo 1.19.32 · MetaCubeX/mihomo · GPL-3.0",true)} };
        body.Children.Add(new Border { Background=Skin.ChromeBrush,CornerRadius=new(14),Padding=new(24),Child=credits });
        Child=Ui.Scroll(body);
    }
}
