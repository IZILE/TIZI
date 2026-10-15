using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class PageScrollChecks
{
    internal static async Task RunAsync(string output,Action<bool,string> check)
    {
        var subscription=new Subscription{Name="Scroll fixture",Url="https://fixture.example/subscription"};
        var node=NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@node.example:443#Scroll%20fixture").Single();
        var state=new AppState{AutoUpdate=false,AutoUpdateProviders=false,RestoreConnection=false,
            Subscriptions=[subscription],Nodes=Enumerable.Range(0,80).Select(i=>node with{Id=Guid.NewGuid(),Name="Fixture "+i,SubscriptionId=subscription.Id}).ToList(),
            Providers=Enumerable.Range(0,24).Select(i=>new ProviderSite{Name="Fixture "+i,Url="https://provider"+i+".example/"}).ToList(),
            Rules=Enumerable.Range(0,80).Select(i=>new RoutingRule("DOMAIN","fixture"+i+".example","DIRECT")).ToList()};
        state=state with{SelectedNode=state.Nodes[0].Id};
        var window=new MainWindow(true,state);window.Show();
        static void Layout(Control control){control.UpdateLayout();Dispatcher.UIThread.RunJobs();control.UpdateLayout();}
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var logs=(ActivityLog)typeof(MainWindow).GetField("_logs",flags)!.GetValue(window)!;
        for(var i=0;i<400;i++)logs.Add("Scroll fixture "+i+" · "+new string('x',140));
        typeof(MainWindow).GetField("_snapshot",flags)!.SetValue(window,new ConnectionSnapshot(0,0,
            Enumerable.Range(0,80).Select(i=>new ConnectionInfo(i.ToString(),"fixture"+i+".example:443","tcp","MATCH","Fixture","fixture.exe",0,0)).ToList()));
        try
        {
            foreach(var size in new[]{new Size(920,620),new Size(1032,722),new Size(1500,900)})
            {
                window.Width=size.Width;window.Height=size.Height;Layout(window);
                foreach(var page in new[]{"Nodes","Providers","Rules","Connections","Settings","Logs"})
                {
                    window.Height=page=="Settings"?620:size.Height;
                    window.ShowPage(page);Layout(window);
                    if(page=="Settings")
                    {
                        window.SettingsSections!.ButtonAt(4).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Layout(window);
                        // Exercise the settled scrollbar, independent of tab-layout animation timing.
                        foreach(var cards in window.PageView.GetVisualDescendants().OfType<SettingsCards>())
                        {cards.Transitions=null;cards.Progress=1;}
                        Layout(window);
                    }
                    var view=window.PageView;
                    var scroll=view as ScrollViewer??view.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s=>s.Extent.Height>s.Viewport.Height+1)
                        ??throw new InvalidDataException(page+" at "+size+": no overflowing scroll viewer");
                    Layout(window);Motion.SettlePaint(window);
                    var bar=scroll.GetVisualDescendants().OfType<ScrollBar>().SingleOrDefault(b=>b.Orientation==Orientation.Vertical&&b.IsVisible&&b.Bounds.Height>0)
                        ??throw new InvalidDataException(page+" at "+size+": no scrollbar for "+scroll.Extent+" / "+scroll.Viewport);
                    var knob=bar.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="scrollKnob");
                    var edge=bar.TranslatePoint(new(bar.Bounds.Width,0),window.ContentSurface)!.Value.X;
                    check(Math.Abs(window.ContentSurface.Bounds.Width-edge-12)<.6,page+" at "+size+": scrollbar uses the shared outer gutter");
                    var unclipped=knob.Bounds.Width>0&&knob.Bounds.Height>0&&knob.GetVisualAncestors().OfType<Control>().Where(a=>a.ClipToBounds).All(a=>
                    {var p=knob.TranslatePoint(default,a)!.Value;return p.X>=-.6&&p.Y>=-.6&&p.X+knob.Bounds.Width<=a.Bounds.Width+.6&&p.Y+knob.Bounds.Height<=a.Bounds.Height+.6;});
                    check(unclipped,page+" at "+size+": the thumb is inside every active clipping boundary");
                    var before=scroll.Offset.Y;
                    var from=knob.TranslatePoint(new(knob.Bounds.Width/2,knob.Bounds.Height/2),window)!.Value;
                    var delta=before>1?-40:40;
                    window.MouseMove(from);window.MouseDown(from,MouseButton.Left);window.MouseMove(from+new Vector(0,delta));window.MouseUp(from+new Vector(0,delta),MouseButton.Left);Layout(window);
                    check(Math.Abs(scroll.Offset.Y-before)>1,page+" at "+size+": dragging the visible thumb scrolls its actual content");
                    if(page=="Logs")
                    {
                        var text=view.GetVisualDescendants().OfType<SelectableTextBlock>().First();var p=text.TranslatePoint(default,window.ContentSurface)!.Value;
                        check(Math.Abs(p.X-28)<.6&&Math.Abs(window.ContentSurface.Bounds.Width-p.X-text.Bounds.Width-28)<.6,
                            "Logs at "+size+": text shares the same left and right margins as page cards");
                    }
                }
            }
        }
        finally{window.Close();await window.Controller.DisposeAsync();}
    }
}
