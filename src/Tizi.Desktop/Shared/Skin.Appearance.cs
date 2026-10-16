using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Tizi.Desktop;

internal static partial class Skin
{
    internal static bool IsLight { get; private set; }
    private static readonly Dictionary<string,(SolidColorBrush Brush,Color Dark,Color Light)> Palette=[];
    private static SolidColorBrush Tone(string key,string dark,string light)
    {
        var a=Color.Parse(dark);var b=Color.Parse(light);var brush=new SolidColorBrush(IsLight?b:a);
        Palette.Add("Tizi"+key,(brush,a,b));return brush;
    }
    internal static readonly SolidColorBrush WindowBackdrop=Tone("WindowBackdrop","#21201F","#F5F2F2");
    internal static readonly SolidColorBrush WindowBackdropTint=Tone("WindowBackdropTint","#FA21201F","#FAF5F2F2");
    internal static readonly SolidColorBrush ControlFill=Tone("ControlFill","#3A3A3A","#EEEEEE");
    internal static readonly SolidColorBrush FieldFill=Tone("FieldFill","#353535","#F0F0F0");
    internal static readonly SolidColorBrush ToolbarFill=Tone("ToolbarFill","#353535","#EAEAEA");
    internal static readonly SolidColorBrush ToolbarDisabledFill=Tone("ToolbarDisabledFill","#66353535","#EAEAEA");
    internal static readonly SolidColorBrush ToolbarDisabledEdge=Tone("ToolbarDisabledEdge","#0F666666","#14000000");
    internal static readonly SolidColorBrush ContentCardFill=Tone("ContentCardFill","#09FFFFFF","#ECECEC");
    internal static readonly SolidColorBrush InputFill=Tone("InputFill","#1A1A1A","#FFFFFF");
    internal static readonly SolidColorBrush ControlEdge=Tone("ControlEdge","#52666666","#24000000");
    internal static readonly SolidColorBrush FieldEdge=Tone("FieldEdge","#25666666","#20000000");
    internal static readonly SolidColorBrush InputEdge=Tone("InputEdge","#18FFFFFF","#26000000");
    internal static readonly SolidColorBrush SearchEdge=Tone("SearchEdge","#20FFFFFF","#27000000");
    internal static readonly SolidColorBrush PopupFill=Tone("PopupFill","#353535","#F8F8F8");
    internal static readonly SolidColorBrush MoreFill=Tone("MoreFill","#303030","#F5F5F5");
    internal static readonly SolidColorBrush SelectionFill=Tone("SelectionFill","#414141","#E7E7E7");
    internal static readonly SolidColorBrush MenuSurface=Tone("MenuSurface","#303030","#FBFBFB");
    internal static readonly SolidColorBrush MenuEdge=Tone("MenuEdge","#26FFFFFF","#1A000000");
    internal static readonly SolidColorBrush ChoiceSelected=Tone("ChoiceSelected","#184B88","#D6E6FF");
    internal static readonly SolidColorBrush HoverInk=Tone("HoverInk","#FFFFFF","#000000");
    internal static readonly SolidColorBrush HoverSubtle=Tone("HoverSubtle","#0AFFFFFF","#0A000000");
    internal static readonly SolidColorBrush RowHover=Tone("RowHover","#0BFFFFFF","#0B000000");
    internal static readonly SolidColorBrush MenuHover=Tone("MenuHover","#1CFFFFFF","#10000000");
    internal static readonly SolidColorBrush MenuSelected=Tone("MenuSelected","#2BFFFFFF","#19000000");
    internal static readonly SolidColorBrush ChevronInk=Tone("ChevronInk","#DFFFFFFF","#DE24282F");
    internal static readonly SolidColorBrush PlaceholderInk=Tone("PlaceholderInk","#A9A9AE","#707681");
    internal static readonly SolidColorBrush ScrollThumb=Tone("ScrollThumb","#80A5A5A5","#80666C76");
    internal static readonly SolidColorBrush ScrollThumbHover=Tone("ScrollThumbHover","#C0BBBBBB","#AA545A64");
    internal static readonly SolidColorBrush ScrollThumbPressed=Tone("ScrollThumbPressed","#DDD0D0D0","#CC454C57");
    internal static readonly SolidColorBrush CheckFill=Tone("CheckFill","#292929","#FFFFFF");
    internal static readonly SolidColorBrush CheckEdge=Tone("CheckEdge","#686868","#8A909A");
    internal static readonly SolidColorBrush TrackFill=Tone("TrackFill","#484848","#D0D0D0");
    internal static readonly SolidColorBrush TooltipFill=Tone("TooltipFill","#EF292929","#FFFBFBFB");
    internal static readonly SolidColorBrush NotificationFill=Tone("NotificationFill","#F2313134","#FFFBFBFB");
    internal static readonly SolidColorBrush NotificationEdge=Tone("NotificationEdge","#35FFFFFF","#28000000");
    internal static readonly SolidColorBrush DataBlue=Tone("DataBlue","#82B4FF","#82B4FF");
    internal static readonly SolidColorBrush Download=Tone("Download","#58D6B2","#58D6B2");
    internal static readonly SolidColorBrush Latency=Tone("Latency","#F6C76C","#F6C76C");
    internal static readonly SolidColorBrush Good=Tone("Good","#70DCA4","#70DCA4");
    internal static readonly SolidColorBrush Moderate=Tone("Moderate","#FFB86C","#FFB86C");
    internal static readonly SolidColorBrush Poor=Tone("Poor","#FF808C","#FF808C");
    internal static readonly SolidColorBrush Error=Tone("Error","#FFB5A5","#FFB5A5");
    internal static readonly SolidColorBrush ErrorStrong=Tone("ErrorStrong","#FF9B8F","#FF9B8F");
    internal static readonly SolidColorBrush Warning=Tone("Warning","#F4C152","#F4C152");
    internal static readonly SolidColorBrush NotificationError=Tone("NotificationError","#FF8A94","#FF8A94");
    internal static readonly SolidColorBrush TagInk=Tone("TagInk","#D6D6D6","#343B45");
    internal static readonly SolidColorBrush BadgeInk=Tone("BadgeInk","#D6D6D6","#FFFFFF");
    internal static readonly SolidColorBrush OnAccent=Tone("OnAccent","#D9FFFFFF","#FFFFFF");
    internal static readonly SolidColorBrush BrandInk=Tone("BrandInk","#FFFFFF","#24282F");
    internal static readonly SolidColorBrush CaptionInk=Tone("CaptionInk","#DCDCDC","#505863");
    internal static readonly SolidColorBrush DetailInk=Tone("DetailInk","#BCC0C7","#616872");
    internal static readonly SolidColorBrush Blue=Tone("Blue","#2979F5","#2979F5");

    internal static void ApplyAppearance(Application app,ThemeVariant variant)
    {
        IsLight=variant==ThemeVariant.Light;
        static void Paint(IBrush brush,Color color)=>((SolidColorBrush)brush).Color=color;
        Paint(ChromeBrush,Chrome);Paint(PasteboardBrush,Pasteboard);Paint(LabelBrush,Label);Paint(SecondaryBrush,Secondary);
        var ink=IsLight?Colors.Black:Colors.White;
        Paint(TabFront,Color.FromArgb(0x1F,ink.R,ink.G,ink.B));Paint(TabBack,Color.FromArgb(0x09,ink.R,ink.G,ink.B));
        Paint(TabFrontEdge,Color.FromArgb(0x38,ink.R,ink.G,ink.B));Paint(TabBackEdge,Color.FromArgb(0x14,ink.R,ink.G,ink.B));
        Paint(MenuRule,Color.FromArgb(0x26,ink.R,ink.G,ink.B));
        foreach(var (key,tone) in Palette){tone.Brush.Color=IsLight?tone.Light:tone.Dark;app.Resources[key]=tone.Brush;}
        app.Resources["TiziLabel"]=LabelBrush;app.Resources["TiziSecondary"]=SecondaryBrush;app.Resources["TiziBlue"]=Blue;
    }
}
