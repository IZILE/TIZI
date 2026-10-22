using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Tizi.Desktop;

internal static class MenuAppearanceChecks
{
    internal static void Run(MainWindow window,string theme,string output,Action<bool,string> check)
    {
        static void Layout(Control top){top.UpdateLayout();Dispatcher.UIThread.RunJobs();top.UpdateLayout();Motion.SettlePaint(top);}
        static Popup PopupFor(MenuItem item)=>item.GetVisualDescendants().OfType<Popup>().Single(p=>p.Name=="PART_Popup"&&ReferenceEquals(p.TemplatedParent,item));
        static Border Face(Popup popup)=>popup.Child!.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="menuFace");
        static ContentPresenter Header(MenuItem item)=>item.GetVisualDescendants().OfType<ContentPresenter>().Single(p=>p.Name=="PART_HeaderPresenter"&&ReferenceEquals(p.TemplatedParent,item));
        static Rect Bounds(Control control,Visual root)=>new(control.TranslatePoint(default,root)!.Value,control.Bounds.Size);
        static void PressKey(Window root,Key key,PhysicalKey physical){root.KeyPress(key,RawInputModifiers.None,physical,null);root.KeyRelease(key,RawInputModifiers.None,physical,null);}
        static void Save(Window root,string file)
        {
            var moving=root.GetVisualDescendants().OfType<SelectionIndicator>().SelectMany(i=>new Avalonia.Animation.Animatable[]{i,i.Position})
                .Concat(root.GetVisualDescendants().OfType<NavigationGlyph>())
                .Concat(root.GetVisualDescendants().OfType<MenuSelectionPanel>()).ToArray();
            using var clock=new UiAnimationClock(moving);clock.Pulse(0);clock.Pulse(340);Layout(root);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=root.CaptureRenderedFrame()!;image.Save(file,new PngBitmapEncoderOptions());
        }
        var oldLanguage=UiText.Language;
        var menu=window.GetVisualDescendants().OfType<Menu>().Single();var headings=menu.Items.OfType<MenuItem>().ToArray();
        var help=headings[3];var commands=help.Items.OfType<MenuItem>().ToArray();var language=commands[1];var choices=language.Items.OfType<MenuItem>().ToArray();
        Color Ink(MenuItem item)=>item.Classes.Contains("moving-menu-ink")?Skin.OnAccent.Color:Skin.Label;
        static void SettleRows(Control owner)
        {
            using var clock=new UiAnimationClock(owner.GetVisualDescendants().OfType<MenuSelectionPanel>().ToArray());
            clock.Pulse(0);clock.Pulse(200);Layout(owner);
        }
        var samples=new List<object>();
        void RowInkMotion(string locale)
        {
            help.IsSelected=true;help.IsSubMenuOpen=true;Layout(window);
            var popup=PopupFor(help);((PopupRevealSurface)popup.Child!).SetImmediately(1);
            language.IsSelected=true;language.IsSubMenuOpen=true;Layout(window);
            ((PopupRevealSurface)PopupFor(language).Child!).SetImmediately(1);
            var panel=Face(popup).GetVisualDescendants().OfType<MenuSelectionPanel>().Single(p=>p.Children.Contains(language));
            using var clock=new UiAnimationClock(panel);clock.Pulse(0);
            window.MouseMove(Bounds(language,window).Center);Layout(window);clock.Pulse(1);clock.Pulse(201);Layout(window);
            check(Header(language).Foreground==Skin.OnAccent,theme+" "+locale+" settled blue language row has white text");
            var frames=new List<object>();
            void Sample(int ms)
            {
                clock.Pulse(ms);Layout(window);
                var rows=commands.Select(item=>new{item.Header,ink=((ISolidColorBrush)Header(item).Foreground!).Color,label=Bounds(Header(item),panel),selected=item.IsSelected,submenuOpen=item.IsSubMenuOpen}).ToArray();
                check(rows.All(row=>row.ink==Skin.Label||(row.ink==Skin.OnAccent.Color&&panel.HighlightAlpha>=.995&&panel.HighlightBounds.Contains(row.label))),
                      theme+" "+locale+" every menu label stays readable during moving and departing blue fill at "+ms+" ms");
                frames.Add(new{milliseconds=ms,highlight=panel.HighlightBounds,alpha=panel.HighlightAlpha,rows});
                if(ms is 218 or 250 or 386)
                {AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=window.CaptureRenderedFrame()!;image.Save(Path.Combine(output,theme.ToLowerInvariant()+"-menu-leave-"+locale+"-"+ms+".png"),new PngBitmapEncoderOptions());}
            }
            window.MouseMove(Bounds(commands[2],window).Center);Layout(window);
            for(var ms=202;ms<=386;ms+=8)Sample(ms);
            check(Header(language).Foreground==Skin.LabelBrush&&Header(commands[2]).Foreground==Skin.OnAccent,
                  theme+" "+locale+" departed language row restores normal ink while the covered destination is white");
            window.MouseMove(Bounds(language,window).Center);Layout(window);Sample(387);Sample(427);
            window.MouseMove(Bounds(commands[0],window).Center);Layout(window);
            for(var ms=428;ms<=612;ms+=8)Sample(ms);
            check(Header(commands[0]).Foreground==Skin.OnAccent&&Header(language).Foreground==Skin.LabelBrush,
                  theme+" "+locale+" interrupted reverse movement restores old-row ink and settles readable destination ink");
            menu.Close();Layout(window);
            File.WriteAllText(Path.Combine(output,theme.ToLowerInvariant()+"-menu-ink-"+locale+".json"),JsonSerializer.Serialize(frames,new JsonSerializerOptions{WriteIndented=true}));
        }
        void HeadingMotion(string locale)
        {
            menu.Close();Layout(window);
            var indicator=menu.GetVisualDescendants().OfType<MenuSelectionPanel>().Single(p=>p.Orientation==Avalonia.Layout.Orientation.Horizontal);
            Border HeadingFace(MenuItem item)=>item.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="PART_LayoutRoot"&&ReferenceEquals(b.TemplatedParent,item));
            Point Center(MenuItem item)=>Bounds(item,window).Center;
            var prefix=theme+" "+locale+" heading motion: ";
            var frames=new List<object>();
            using var clock=new UiAnimationClock(indicator);
            void Sample(int ms){clock.Pulse(ms);Layout(window);frames.Add(new{milliseconds=ms,indicator=indicator.HighlightBounds,expanded=headings.SingleOrDefault(i=>i.IsSubMenuOpen)?.Header});}
            window.MouseMove(Center(headings[0]));Layout(window);Sample(1);Sample(181);
            check(!headings.Any(i=>i.IsSubMenuOpen)&&ReferenceEquals(indicator.HighlightFill,Skin.TabFront),prefix+"closed-menu hover retains its original surface");
            window.MouseDown(Center(headings[0]),MouseButton.Left);window.MouseUp(Center(headings[0]),MouseButton.Left);Layout(window);Sample(182);Sample(362);
            check(headings[0].IsSubMenuOpen&&ReferenceEquals(indicator.HighlightFill,Face(PopupFor(headings[0])).Background),prefix+"click changes the moving surface to the dropdown fill");
            check(headings.All(i=>HeadingFace(i).Background is ISolidColorBrush b&&b.Color.A==0&&HeadingFace(i).Transitions is null),prefix+"individual headings paint no second background or brush transition");
            var from=indicator.HighlightBounds.Left;
            window.MouseMove(Center(help));Layout(window);Sample(363);
            check(help.IsSubMenuOpen&&!headings[0].IsSubMenuOpen&&ReferenceEquals(indicator.Highlighted,help),prefix+"pointer crossing transfers the expanded menu and its shared surface together");
            Sample(420);
            check(indicator.HighlightBounds.Left>from&&indicator.HighlightBounds.Left<indicator.Destination.Left&&ReferenceEquals(indicator.HighlightFill,Skin.MenuSurface),prefix+"horizontal travel keeps one dropdown-colored surface between headings");
            var displayed=indicator.HighlightBounds.Left;
            window.MouseMove(Center(headings[1]));Layout(window);
            check(Math.Abs(indicator.HighlightBounds.Left-displayed)<.5,prefix+"rapid reversal starts at the displayed position");
            Sample(421);Sample(620);
            check(headings[1].IsSubMenuOpen&&headings.Count(i=>i.IsSubMenuOpen)==1&&indicator.HighlightBounds==indicator.Destination,prefix+"rapid reversal ends at the only expanded heading");
            window.MouseMove(new(window.Bounds.Width-5,window.Bounds.Height-5));Layout(window);Sample(621);Sample(820);
            check(ReferenceEquals(indicator.Highlighted,headings[1])&&ReferenceEquals(indicator.HighlightFill,Skin.MenuSurface),prefix+"leaving the headings keeps the expanded panel's fill");
            menu.Close();Layout(window);Sample(821);Sample(1000);
            check(!headings.Any(i=>i.IsSubMenuOpen)&&ReferenceEquals(indicator.HighlightFill,Skin.TabFront),prefix+"dismissal restores the original hover style");
            File.WriteAllText(Path.Combine(output,theme.ToLowerInvariant()+"-heading-motion-"+locale+".json"),JsonSerializer.Serialize(frames,new JsonSerializerOptions{WriteIndented=true}));
        }
        try
        {
            window.Rail.Select("Overview");window.ShowPage("Overview");Layout(window);
            foreach(var locale in new[]{"zh-CN","en"})
            {
                UiText.Language=locale;HeadingMotion(locale);help.IsSelected=true;help.IsSubMenuOpen=true;help.Focus();Layout(window);
                var primary=PopupFor(help);((PopupRevealSurface)primary.Child!).SetImmediately(1);
                language.IsSelected=true;language.IsSubMenuOpen=true;language.Focus();Layout(window);
                var secondary=PopupFor(language);((PopupRevealSurface)secondary.Child!).SetImmediately(1);Layout(window);
                SettleRows(window);var primaryFace=Face(primary);var secondaryFace=Face(secondary);
                foreach(var item in headings.Concat(commands).Concat(choices))
                {
                    var presenter=Header(item);var texts=presenter.GetVisualDescendants().OfType<TextBlock>().ToArray();
                    check(presenter.Foreground is ISolidColorBrush ink&&ink.Color==Ink(item)&&texts.Length>0
                          &&texts.All(t=>t.Foreground is ISolidColorBrush foreground&&foreground.Color==Ink(item)),
                          theme+" "+locale+" menu label follows the same readable ink: "+item.Header);
                }
                check(ReferenceEquals(primaryFace.Background,Skin.MenuSurface)&&ReferenceEquals(secondaryFace.Background,Skin.MenuSurface)
                      &&primaryFace.CornerRadius==secondaryFace.CornerRadius&&primaryFace.BorderThickness==secondaryFace.BorderThickness,
                      theme+" "+locale+" primary and cascading menus share their surface, rounded corners and edge");
                check(primaryFace.BoxShadow.Count==1&&secondaryFace.BoxShadow==primaryFace.BoxShadow,
                      theme+" "+locale+" both menu levels share a restrained shadow");
                var a=Bounds(primaryFace,window);var b=Bounds(secondaryFace,window);var row=Bounds(language,window);var first=Bounds(choices[0],window);
                samples.Add(new{locale,primary=a,secondary=b,parentRow=row,firstChildRow=first});
                check(Math.Abs(b.Left-a.Right)<=2&&Math.Abs(first.Top-row.Top)<=2,
                      theme+" "+locale+" submenu joins the primary edge and aligns its first row with the parent row");
                window.MouseMove(first.Center);Layout(window);
                check(language.IsSubMenuOpen&&help.IsSubMenuOpen,theme+" "+locale+" entering the submenu keeps its parent menu open");
                choices[0].IsSelected=true;choices[0].Focus();Layout(window);SettleRows(window);
                var arrow=language.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p=>p.Name=="PART_ChevronPath");
                check(arrow.Stroke==Skin.OnAccent,theme+" "+locale+" the submenu chevron remains white on the original blue highlight");
                Save(window,Path.Combine(output,theme.ToLowerInvariant()+"-menu-"+locale+".png"));
                PressKey(window,Key.Left,PhysicalKey.ArrowLeft);Layout(window);
                check(!language.IsSubMenuOpen&&help.IsSubMenuOpen,theme+" "+locale+" Left closes only the nested menu");
                language.IsSelected=true;language.Focus();PressKey(window,Key.Right,PhysicalKey.ArrowRight);Layout(window);
                check(language.IsSubMenuOpen,theme+" "+locale+" Right reopens the focused submenu");
                ((PopupRevealSurface)PopupFor(language).Child!).SetImmediately(1);choices[0].Focus();
                PressKey(window,Key.Escape,PhysicalKey.Escape);Layout(window);
                if(help.IsSubMenuOpen){help.Focus();PressKey(window,Key.Escape,PhysicalKey.Escape);Layout(window);}
                check(!help.IsSubMenuOpen&&!language.IsSubMenuOpen,theme+" "+locale+" Escape dismisses the cascading menu without dispatching an action");
                window.MouseMove(new Point(window.Bounds.Width-5,window.Bounds.Height-5));
            }
            foreach(var locale in new[]{"zh-CN","en"}){UiText.Language=locale;RowInkMotion(locale);}
            File.WriteAllText(Path.Combine(output,theme.ToLowerInvariant()+"-menu-layout.json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{language.IsSubMenuOpen=false;help.IsSubMenuOpen=false;UiText.Language=oldLanguage;Layout(window);}
    }
}
