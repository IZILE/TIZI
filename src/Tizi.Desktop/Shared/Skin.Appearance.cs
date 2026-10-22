using Avalonia;
using Avalonia.Media;

namespace Tizi.Desktop;

internal static partial class Skin
{
    private static readonly Dictionary<string,SolidColorBrush> Palette=[];
    private static SolidColorBrush Tone(string key,string color)
    {
        var brush=new SolidColorBrush(Color.Parse(color));Palette.Add("Tizi"+key,brush);return brush;
    }
    internal static readonly SolidColorBrush WindowBackdrop=Tone("WindowBackdrop","#21201F");
    internal static readonly SolidColorBrush WindowBackdropTint=Tone("WindowBackdropTint","#FA21201F");
    internal static readonly SolidColorBrush ControlFill=Tone("ControlFill","#3A3A3A");
    internal static readonly SolidColorBrush FieldFill=Tone("FieldFill","#353535");
    internal static readonly SolidColorBrush ToolbarFill=Tone("ToolbarFill","#353535");
    internal static readonly SolidColorBrush ToolbarDisabledFill=Tone("ToolbarDisabledFill","#66353535");
    internal static readonly SolidColorBrush ToolbarDisabledEdge=Tone("ToolbarDisabledEdge","#0F666666");
    internal static readonly SolidColorBrush ContentCardFill=Tone("ContentCardFill","#09FFFFFF");
    internal static readonly SolidColorBrush InputFill=Tone("InputFill","#1A1A1A");
    internal static readonly SolidColorBrush ControlEdge=Tone("ControlEdge","#52666666");
    internal static readonly SolidColorBrush FieldEdge=Tone("FieldEdge","#25666666");
    internal static readonly SolidColorBrush InputEdge=Tone("InputEdge","#18FFFFFF");
    internal static readonly SolidColorBrush SearchEdge=Tone("SearchEdge","#20FFFFFF");
    internal static readonly SolidColorBrush PopupFill=Tone("PopupFill","#353535");
    internal static readonly SolidColorBrush MoreFill=Tone("MoreFill","#303030");
    internal static readonly SolidColorBrush SelectionFill=Tone("SelectionFill","#414141");
    internal static readonly SolidColorBrush MenuSurface=Tone("MenuSurface","#303030");
    internal static readonly SolidColorBrush MenuEdge=Tone("MenuEdge","#26FFFFFF");
    internal static readonly SolidColorBrush ChoiceSelected=Tone("ChoiceSelected","#184B88");
    internal static readonly SolidColorBrush HoverInk=Tone("HoverInk","#FFFFFF");
    internal static readonly SolidColorBrush HoverSubtle=Tone("HoverSubtle","#0AFFFFFF");
    internal static readonly SolidColorBrush RowHover=Tone("RowHover","#0BFFFFFF");
    internal static readonly SolidColorBrush MenuHover=Tone("MenuHover","#1CFFFFFF");
    internal static readonly SolidColorBrush MenuSelected=Tone("MenuSelected","#2BFFFFFF");
    internal static readonly SolidColorBrush ChevronInk=Tone("ChevronInk","#DFFFFFFF");
    internal static readonly SolidColorBrush PlaceholderInk=Tone("PlaceholderInk","#A9A9AE");
    internal static readonly SolidColorBrush ScrollThumb=Tone("ScrollThumb","#80A5A5A5");
    internal static readonly SolidColorBrush ScrollThumbHover=Tone("ScrollThumbHover","#C0BBBBBB");
    internal static readonly SolidColorBrush ScrollThumbPressed=Tone("ScrollThumbPressed","#DDD0D0D0");
    internal static readonly SolidColorBrush CheckFill=Tone("CheckFill","#292929");
    internal static readonly SolidColorBrush CheckEdge=Tone("CheckEdge","#686868");
    internal static readonly SolidColorBrush TrackFill=Tone("TrackFill","#484848");
    internal static readonly SolidColorBrush TooltipFill=Tone("TooltipFill","#EF292929");
    internal static readonly SolidColorBrush NotificationFill=Tone("NotificationFill","#F2313134");
    internal static readonly SolidColorBrush NotificationEdge=Tone("NotificationEdge","#35FFFFFF");
    internal static readonly SolidColorBrush DataBlue=Tone("DataBlue","#82B4FF");
    internal static readonly SolidColorBrush Download=Tone("Download","#58D6B2");
    internal static readonly SolidColorBrush Latency=Tone("Latency","#F6C76C");
    internal static readonly SolidColorBrush Good=Tone("Good","#70DCA4");
    internal static readonly SolidColorBrush Moderate=Tone("Moderate","#FFB86C");
    internal static readonly SolidColorBrush Poor=Tone("Poor","#FF808C");
    internal static readonly SolidColorBrush Error=Tone("Error","#FFB5A5");
    internal static readonly SolidColorBrush ErrorStrong=Tone("ErrorStrong","#FF9B8F");
    internal static readonly SolidColorBrush Warning=Tone("Warning","#F4C152");
    internal static readonly SolidColorBrush NotificationError=Tone("NotificationError","#FF8A94");
    internal static readonly SolidColorBrush TagInk=Tone("TagInk","#D6D6D6");
    internal static readonly SolidColorBrush BadgeInk=Tone("BadgeInk","#D6D6D6");
    internal static readonly SolidColorBrush OnAccent=Tone("OnAccent","#D9FFFFFF");
    internal static readonly SolidColorBrush BrandInk=Tone("BrandInk","#FFFFFF");
    internal static readonly SolidColorBrush CaptionInk=Tone("CaptionInk","#DCDCDC");
    internal static readonly SolidColorBrush DetailInk=Tone("DetailInk","#BCC0C7");
    internal static readonly SolidColorBrush Blue=Tone("Blue","#2979F5");

    internal static void RegisterPalette(Application app)
    {
        foreach(var (key,brush) in Palette)app.Resources[key]=brush;
        app.Resources["TiziLabel"]=LabelBrush;app.Resources["TiziSecondary"]=SecondaryBrush;app.Resources["TiziBlue"]=Blue;
    }
}
