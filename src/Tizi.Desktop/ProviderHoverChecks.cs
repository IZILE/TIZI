using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class ProviderHoverChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);var checks=new List<string>();
        try
        {
            Verify(output,(okay,label)=>{if(!okay)throw new InvalidDataException(label);checks.Add(label);});
            File.WriteAllText(Path.Combine(output,"provider-hover-checks.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,binary=VerificationStamp.Current(),syntheticData=true,personalConfigurationAccessed=false},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception failure){File.WriteAllText(Path.Combine(output,"provider-hover-failure.txt"),string.Join('\n',checks)+"\n"+failure);return 1;}
    }
    internal static void Verify(string output,Action<bool,string> check)
    {
        Directory.CreateDirectory(output);var old=Appearance.Current;
        try{foreach(var theme in new[]{"Light","Dark"}){VerifyTheme(theme,output,check);VerifyListRows(theme,output,check);}}
        finally{Appearance.Set(old.Theme,false);}
    }
    private static void VerifyListRows(string theme,string output,Action<bool,string> check)
    {
        Appearance.Set(theme,false);var folder=Path.Combine(output,"list-hover-"+theme.ToLowerInvariant());Directory.CreateDirectory(folder);
        var rows=new MovingRows{Margin=new(16)};
        rows.SetRows([new("rule","45.43.32.234/32","IP-CIDR · no-resolve"),new("connection","chatgpt.com:443","TCP · DomainSuffix chatgpt.com")],null);
        var window=new Window{Width=650,Height=400,Background=Skin.PasteboardBrush,Content=rows};window.Show();
        void Layout(){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
        Layout();if(window.Content is DialogSurface surface){using var settle=new UiAnimationClock(surface,surface.Scale,surface.Offset);surface.Reveal();settle.Pulse(0);settle.Pulse(220);Layout();}
        try
        {
            var row=rows.For("rule");var feedback=row.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="feedback"&&ReferenceEquals(b.TemplatedParent,row));
            using var clock=new UiAnimationClock(feedback);
            Color Frame(int ms,string name)
            {
                clock.Pulse(ms);Layout();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=window.CaptureRenderedFrame()!;
                image.Save(Path.Combine(folder,name+".png"),new PngBitmapEncoderOptions());
                var point=row.TranslatePoint(new(row.Bounds.Width-30,30),window)!.Value;var data=VerificationPixels.Bgra(image);var index=((int)point.Y*image.PixelSize.Width+(int)point.X)*4;
                return Color.FromRgb(data[index+2],data[index+1],data[index]);
            }
            window.MouseMove(new(2,2));var idle=Frame(0,"idle");window.MouseMove(row.TranslatePoint(new(200,30),window)!.Value);Frame(1,"enter");var hover=Frame(101,"hover");
            check(row.IsPointerOver&&(theme=="Light"?idle.R-hover.R>=12&&idle.G-hover.G>=12&&idle.B-hover.B>=12:hover==idle),
                theme+" ordinary rule/connection rows expose a visible light-mode hover tint while preserving the dark fill");
            var shades=new List<int>();window.MouseMove(new(2,2));Frame(102,"leave-start");for(var ms=110;ms<=198;ms+=8)shades.Add(Frame(ms,"leave-"+ms).R);
            check(shades.Zip(shades.Skip(1),(a,b)=>theme=="Light"?b>=a:b==a).All(v=>v)&&shades[^1]==idle.R,
                theme+" ordinary row hover fades back monotonically to the unchanged page fill");
            rows.Select("rule");Motion.SettlePaint(rows);var before=Frame(300,"selected");window.MouseMove(row.TranslatePoint(new(200,30),window)!.Value);Frame(301,"selected-enter");var after=Frame(401,"selected-hover");
            check(before==after&&rows.Indicator.IsVisible,theme+" selected rows keep the existing moving selection color without another hover layer");
        }
        finally{window.Close();}
    }
    private static void VerifyTheme(string theme,string output,Action<bool,string> check)
    {
        static void Layout(Control top){top.UpdateLayout();Dispatcher.UIThread.RunJobs();top.UpdateLayout();}
        Appearance.Set(theme,false);
        var directory=Path.Combine(output,"provider-hover-"+theme.ToLowerInvariant());Directory.CreateDirectory(directory);
        var sites=new[]{new ProviderSite{Name="Airport A",Url="https://a.example/"},new ProviderSite{Name="Airport B",Url="https://b.example/"}};
        var rows=new MovingRows{Margin=new(16)};var entries=sites.Select(p=>new DisplayRow(p.Id.ToString(),p.Name,p.Url,Provider:p)).ToArray();rows.SetRows(entries,null);
        var window=new Window{Width=650,Height=500,Background=Skin.PasteboardBrush,Content=rows};window.Show();Layout(window);
        if(window.Content is DialogSurface dialog){using var settle=new UiAnimationClock(dialog,dialog.Scale,dialog.Offset);dialog.Reveal();settle.Pulse(0);settle.Pulse(220);}
        try
        {
            var cards=sites.Select(p=>(ProviderCardButton)rows.For(p.Id.ToString())).ToArray();
            var faces=cards.Select(c=>c.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="buttonFace"&&ReferenceEquals(b.TemplatedParent,c))).ToArray();
            var content=cards.Select(c=>c.Content).ToArray();
            var presenters=cards.Select(c=>c.GetVisualDescendants().OfType<ContentPresenter>().Single(p=>p.Name=="PART_ContentPresenter"&&ReferenceEquals(p.TemplatedParent,c))).ToArray();
            using var clock=new UiAnimationClock(faces);var samples=new List<object>();
            Color[] Frame(int ms,string phase,bool save=false)
            {
                clock.Pulse(ms);Layout(window);AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap=window.CaptureRenderedFrame()??throw new IOException("Missing hover frame.");
                var data=VerificationPixels.Bgra(bitmap);
                Color Pixel(ProviderCardButton card)
                {
                    var p=card.TranslatePoint(new(card.Bounds.Width/2,75),window)!.Value;var index=((int)(p.Y*window.RenderScaling)*bitmap.PixelSize.Width+(int)(p.X*window.RenderScaling))*4;
                    return Color.FromRgb(data[index+2],data[index+1],data[index]);
                }
                var pixels=cards.Select(Pixel).ToArray();samples.Add(new{milliseconds=ms,phase,pixels=pixels.Select(c=>c.ToString()).ToArray()});
                if(save)bitmap.Save(Path.Combine(directory,phase+"-"+ms+".png"),new PngBitmapEncoderOptions());
                return pixels;
            }
            void Move(int index)=>window.MouseMove(index<0?new(2,2):cards[index].TranslatePoint(new(220,75),window)!.Value);
            static int Difference(Color a,Color b)=>Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)));
            static bool Between(Color value,Color a,Color b)=>value.R>=Math.Min(a.R,b.R)-1&&value.R<=Math.Max(a.R,b.R)+1
                &&value.G>=Math.Min(a.G,b.G)-1&&value.G<=Math.Max(a.G,b.G)+1&&value.B>=Math.Min(a.B,b.B)-1&&value.B<=Math.Max(a.B,b.B)+1;
            Move(-1);var idle=Frame(0,"idle",true)[0];Move(0);Frame(1,"enter");var hover=Frame(201,"hover",true)[0];
            Move(-1);Frame(202,"leave");Frame(402,"idle");Move(0);Frame(403,"enter");
            var worst=0;var monotonic=true;var previous=idle;
            for(var ms=411;ms<=603;ms+=8)
            {
                var color=Frame(ms,"enter",ms is 427 or 451 or 483 or 603)[0];
                if(!Between(color,idle,hover))worst=Math.Max(worst,Math.Min(Difference(color,idle),Difference(color,hover)));
                if(Difference(color,hover)>Difference(previous,hover)+1)monotonic=false;previous=color;
            }
            File.WriteAllText(Path.Combine(directory,"samples.json"),JsonSerializer.Serialize(new{theme,idle=idle.ToString(),hover=hover.ToString(),worst,monotonic,samples},new JsonSerializerOptions{WriteIndented=true}));
            check(worst==0&&monotonic,theme+" provider hover enters monotonically within its endpoint colors (overshoot "+worst+")");
            Move(-1);Frame(604,"leave");previous=hover;
            for(var ms=612;ms<=804;ms+=8)
            {
                var color=Frame(ms,"leave",ms is 628 or 660 or 804)[0];
                if(!Between(color,idle,hover)||Difference(color,idle)>Difference(previous,idle)+1)throw new InvalidDataException(theme+" provider hover flashes while leaving at "+ms+" ms: "+color);
                previous=color;
            }
            check(Difference(previous,idle)<=1,theme+" provider hover leaves monotonically and returns to the unchanged idle fill");
            var continuous=true;var bounded=true;var trees=true;
            for(var frame=0;frame<=180;frame++)
            {
                var ms=805+frame*8;var before=Frame(ms,"rapid");
                if(frame%7==0)
                {
                    Move(frame%21==0?-1:frame%2);Layout(window);var after=Frame(ms,"retarget");
                    continuous&=before.Zip(after).All(p=>Difference(p.First,p.Second)<=1);
                }
                if(frame is 35 or 70 or 105)rows.SetRows(entries,sites[0].Id.ToString());
                if(frame is 45 or 46){var p=cards[0].TranslatePoint(new(220,75),window)!.Value;if(frame==45)window.MouseDown(p,MouseButton.Left);else window.MouseUp(p,MouseButton.Left);}
                var pixels=Frame(ms+1,"rapid");bounded&=pixels.All(c=>Between(c,idle,hover));
                trees&=cards.Select((c,i)=>ReferenceEquals(c.Content,content[i])).All(x=>x)&&presenters.All(p=>p.Opacity==1)&&!rows.Backplate.IsVisible;
            }
            check(continuous&&bounded,theme+" rapid pointer reversals keep displayed colors continuous without a light or dark flash");
            check(trees,theme+" provider hover, refresh and clicks keep one background and fully opaque original content");
            Move(-1);Frame(2300,"idle");var link=cards[0].GetVisualDescendants().OfType<Button>().Single();
            window.MouseMove(link.TranslatePoint(new(link.Bounds.Width/2,link.Bounds.Height/2),window)!.Value);Frame(2301,"website");var nested=Frame(2501,"website",true)[0];
            check(cards[0].IsPointerOver&&Difference(nested,hover)<=1,theme+" entering the nested website button retains the parent card hover");
            rows.Select(sites[0].Id.ToString());Move(-1);Frame(2502,"final");var final=Frame(2702,"final",true);
            check(final.All(c=>Difference(c,idle)<=1)&&cards[0].Classes.Contains("selected-card"),theme+" pointer departure clears hover even when the card remains selected for actions");
            File.WriteAllText(Path.Combine(directory,"samples.json"),JsonSerializer.Serialize(new{theme,idle=idle.ToString(),hover=hover.ToString(),worst,monotonic,samples},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{window.Close();}
    }
}
