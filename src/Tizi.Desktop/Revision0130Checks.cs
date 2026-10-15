using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class Revision0130Checks
{
    internal static async Task RunAsync(string output, Action<bool,string> check)
    {
        static void Layout(Control top) { top.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        static void Click(Window top,Control control,Point local)
        {
            var point=control.TranslatePoint(local,top)!.Value;
            top.MouseMove(point);top.MouseDown(point,MouseButton.Left);top.MouseUp(point,MouseButton.Left);Layout(top);
        }
        static void Save(Window top,string path)
        {
            Layout(top);AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame=top.CaptureRenderedFrame()!;frame.Save(path,new PngBitmapEncoderOptions());
        }
        static async Task Until(Func<bool> ready)
        {using var timeout=new CancellationTokenSource(8000);while(!ready())await Task.Delay(10,timeout.Token);}
        var folder=Path.Combine(output,"revision-0130");Directory.CreateDirectory(folder);
        var moreClicks=0;var removeClicks=0;
        var more=Ui.Button("More",()=>moreClicks++);var remove=Ui.Button("Remove",()=>removeClicks++);
        remove.Background=new SolidColorBrush(Color.Parse("#D74756"));remove.BorderThickness=default;
        var bubble=new RemovalBubble(remove);bubble.AttachAnchor(more);
        var fixture=new Window {Width=640,Height=150,Background=Skin.ChromeBrush,
            Content=new StackPanel {Margin=new(24),VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center,Children={bubble}}};
        fixture.Show();Layout(fixture);
        try
        {
            if(fixture.Content is DialogSurface surface)
            {using var clock=new UiAnimationClock(surface,surface.Scale,surface.Offset);surface.Reveal();clock.Pulse(0);clock.Pulse(200);Layout(fixture);}
            foreach(var point in new[]{new Point(3,18),new Point(more.Bounds.Width-3,18),new Point(more.Bounds.Width/2,3),new Point(more.Bounds.Width/2,33)})
                Click(fixture,more,point);
            check(moreClicks==4,"All four padding edges of the real More capsule dispatch a click without touching its text");
            using var animation=new UiAnimationClock(bubble);animation.Pulse(0);bubble.SetOpen(true);animation.Pulse(1);animation.Pulse(341);Layout(fixture);
            check(bubble.Progress==1&&remove.IsHitTestVisible,"Fission finishes and accepts input within 340 milliseconds");
            var labels=new List<object>();
            foreach(var count in new[]{3,12,999})
            {
                remove.Content=Ui.Value(UiText.Format("Remove selected ({0})",count));Layout(fixture);
                var text=(TextBlock)remove.Content;
                var ink=new Rect(text.TranslatePoint(default,bubble)!.Value,text.Bounds.Size);
                check(remove.Bounds.Left>=more.Bounds.Right+7.99&&bubble.Shape().Bud.Contains(ink),
                    "Changing the selected count to "+count+" keeps all label content in the red lobe after independent child layout");
                Click(fixture,remove,new(3,18));Click(fixture,remove,new(remove.Bounds.Width-3,18));
                Save(fixture,Path.Combine(folder,"remove-count-"+count+".png"));
                labels.Add(new {count,button=remove.Bounds.ToString(),ink=ink.ToString(),bud=bubble.Shape().Bud.ToString()});
            }
            check(removeClicks==6,"Both padding edges of the resized removal capsule remain actionable");
            remove.Content=Ui.Text("Remove");Layout(fixture);
            check(bubble.Shape().Bud.Width==remove.Bounds.Width&&remove.Bounds.Left>=more.Bounds.Right+7.99,
                "Shortening the count label also remeasures its surface without leaving text at the anchor");
            bubble.SetOpen(false);animation.Pulse(342);animation.Pulse(542);Layout(fixture);
            check(bubble.Progress==0&&!remove.IsEffectivelyVisible,"Removal merges and releases its layout within 200 milliseconds");
            File.WriteAllText(Path.Combine(folder,"dynamic-count-layout.json"),JsonSerializer.Serialize(labels));
        }
        finally {fixture.Close();}

        var badges=new IpQualityBadges();
        var missing=new IpQualityResult("",[],null,DateTimeOffset.UtcNow);
        badges.Update(IpQualityPresentation.From(missing),true);
        check(!badges.IsVisible&&!badges.HasResult&&badges.Badges.All(b=>!b.IsVisible),"Missing IP observations show no unknown type, billing-cycle translation or dash score");
        var partial=new IpQualityResult("8.8.8.8",[new("Fixture","Residential",null,null,null,null,"OK")],null,DateTimeOffset.UtcNow);
        badges.Update(IpQualityPresentation.From(partial),true);
        check(badges.IsVisible&&badges.Badges[0].IsVisible&&!badges.Badges[1].IsVisible&&!badges.Badges[2].IsVisible,
            "A known type with no comparable risk score displays only its actual known type");
        var conflict=new IpQualityResult("8.8.8.8",[new("A","Residential","US",null,null,5,"OK"),new("B","Residential","US",null,null,90,"OK")],"US",DateTimeOffset.UtcNow);
        badges.Update(IpQualityPresentation.From(conflict),true);
        check(badges.Badges[0].IsVisible&&!badges.Badges[1].IsVisible&&badges.Badges[2].IsVisible,
            "Substantial risk disagreement hides the combined score without hiding independent known type/native data");
        check(Avalonia.Automation.AutomationProperties.GetHelpText(badges.Badges[0])!.Contains("90 / 100"),
            "Raw scores remain in the known type's hover details when a conflicting combined score is hidden");

        var subscription=new Subscription {Name="Fixture subscription",Url="https://subscription.example/"};
        var nodes=Enumerable.Range(1,30).Select(index=>NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@node.example:443#Fixture%20"+index).Single() with {SubscriptionId=subscription.Id}).ToList();
        var choiceState=new AppState {Nodes=nodes,Subscriptions=[subscription],SelectedNode=nodes[23].Id};
        var choice=new SubscriptionNodeChoice {VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center};var chosen=0;choice.NodeChosen+=_=>chosen++;
        var chooser=new Window {Width=640,Height=600,Content=choice,Background=Skin.ChromeBrush};chooser.Show();choice.Update(choiceState);Layout(chooser);
        try
        {
            choice.IsDropDownOpen=true;Layout(chooser);
            var popup=choice.GetVisualDescendants().OfType<Popup>().Single();
            var scroll=popup.Child!.GetVisualDescendants().OfType<ScrollViewer>().Single();
            check(choice.SelectedIndex==23&&Math.Abs(choice.ContainerFromIndex(23)!.TranslatePoint(default,scroll)!.Value.Y)<1,
                "Opening scrolls the selected node to the viewport top while retaining its original index");
            check(choice.Items.Select(item=>item!.ToString()).SequenceEqual(nodes.Select(node=>node.Name)),"Opening never changes the subscription's default node order");
            choice.Update(choiceState with {SelectedNode=nodes[12].Id});Layout(chooser);
            check(choice.Items[0]!.ToString()==nodes[0].Name,"A selection update does not reorder an already open dropdown beneath the pointer");
            PopupMotion.CloseImmediately(choice);choice.IsDropDownOpen=true;Layout(chooser);
            check(choice.SelectedIndex==12&&Math.Abs(choice.ContainerFromIndex(12)!.TranslatePoint(default,scroll)!.Value.Y)<1,
                "The next opening scrolls the newly selected node to the top instead of reordering any items");
            check(chosen==0&&choice.Candidates.Select(node=>node.Id).SequenceEqual(nodes.Select(node=>node.Id)),
                "Scrolling emits no node-change event and preserves the latency candidate set");
            PopupMotion.CloseImmediately(choice);choice.Update(choiceState with {SelectedNode=nodes[^1].Id});choice.IsDropDownOpen=true;Layout(chooser);
            check(choice.SelectedIndex==29&&Math.Abs(choice.ContainerFromIndex(29)!.TranslatePoint(default,scroll)!.Value.Y)<1,
                "Even the last selected node can align with the viewport top without changing the default order");
            Save(chooser,Path.Combine(folder,"selected-node-scrolled.png"));
        }
        finally {PopupMotion.CloseImmediately(choice);chooser.Close();}

        var state=new AppState {AutoUpdate=false,AutoUpdateProviders=false,RestoreConnection=false,CloseToTray=false,
            Providers=[new ProviderSite {Name="Fixture",Url="https://fixture.example/"}]};
        new Vault(UserData.DirectoryPath).Write("settings.dat",state);
        var window=new MainWindow(false);window.Show();await Until(()=>window.Controller.Ready);Layout(window);
        try
        {
            var menu=window.GetVisualDescendants().OfType<Menu>().Single();var file=(MenuItem)menu.Items[0]!;
            file.IsSubMenuOpen=true;Layout(window);var item=file.Items.OfType<MenuItem>().First();
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));file.IsSubMenuOpen=false;
            await Until(()=>window.OpenDialogs.Count==1);Layout(window);
            var dialog=window.OpenDialogs.Single();var overlay=dialog.Overlay!;
            var heading=new Rect(file.TranslatePoint(default,window)!.Value,file.Bounds.Size);
            check(overlay.SourceBounds==heading&&file.Opacity==1,"A File menu command grows from its persistent top heading while the heading stays visible");
            using(var clock=new UiAnimationClock(overlay))
            {
                overlay.SetImmediately(0);overlay.Reveal();clock.Pulse(0);clock.Pulse(overlay.OpeningMilliseconds+10);Layout(window);
                check(overlay.Progress==1&&overlay.VisibleBounds==overlay.TargetBounds,"A top-menu sheet reaches its final shape at the configured gentle duration");
                Save(window,Path.Combine(folder,"file-menu-import-open.png"));dialog.Close();clock.Pulse(311);clock.Pulse(541);Layout(window);
                check(overlay.Progress==0&&overlay.VisibleBounds==heading,"Retraction ends at the visible File heading rather than the vanished menu row");
            }
            await Until(()=>!dialog.IsVisible);check(file.Opacity==1,"The top menu heading is restored after the sheet closes");
            window.Rail.Select("Settings");Layout(window);var section=window.SettingsSections!.ButtonAt(5);
            var update=window.PageView.GetVisualDescendants().OfType<Button>().Single(b=>b.Content is TextBlock t&&t.Text==UiText.Get("Check for updates"));
            var updateFace=update.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="buttonFace");
            var general=update.GetVisualAncestors().OfType<SettingsCards>().Single();
            Click(window,section,new(section.Bounds.Width/2,section.Bounds.Height/2));Layout(window);
            var cards=window.PageView.GetVisualDescendants().OfType<SettingsCards>().Single(c=>c.IsEffectivelyVisible);
            using(var clock=new UiAnimationClock(cards))
            {
            cards.Progress=0;clock.Pulse(0);cards.Progress=1;clock.Pulse(260);Layout(window);
            var column=cards.Children.OfType<StackPanel>().Single(c=>c.Tag is "diagnostics-tools-column");
            foreach(var width in new[]{1032d,880d})
            {
                window.Width=width;Layout(window);var tools=column.Children[0];var ip=column.Children[1];
                check(Math.Abs(tools.Bounds.Bottom+18-ip.Bounds.Top)<.1&&ip.Bounds.Right<=column.Bounds.Width+.1,
                    "IP detection sits directly under Tools and fits the actual diagnostics column at width "+width);
                if(width==1032)check(column.Bounds.Left>cards.Children[0].Bounds.Left,"Default diagnostics uses its right column's unused space for Tools and IP detection");
            }
            window.Width=1032;Layout(window);
            var diagnosticsScroll=cards.GetVisualAncestors().OfType<ScrollViewer>().First();
            var interval=cards.GetVisualDescendants().OfType<NumericUpDown>().Last();
            var services=cards.GetVisualDescendants().OfType<Button>().Single(b=>b.Content is TextBlock t&&t.Text==UiText.Get("IP detection services"));
            check(interval.TranslatePoint(new(0,interval.Bounds.Height),diagnosticsScroll)!.Value.Y<=diagnosticsScroll.Viewport.Height+.1,
                "Default window shows the entire final latency interval control without scrolling");
            check(services.TranslatePoint(new(0,services.Bounds.Height),diagnosticsScroll)!.Value.Y<=diagnosticsScroll.Viewport.Height+.1,
                "Default window shows the complete IP detection service button beneath Tools");
            check(cards.Bounds.Height<=diagnosticsScroll.Viewport.Height+.1,"Settled default diagnostics fits every field in its viewport");
            window.SettingsSections!.Indicator.MoveTo(window.SettingsSections.Indicator.Destination,false);
            Save(window,Path.Combine(folder,"diagnostics-default.png"));
            }
            var paintFrames=new List<object>();
            foreach(var from in new[]{1,2,3,4,5})
            {
                var fromButton=window.SettingsSections!.ButtonAt(from);Click(window,fromButton,new(fromButton.Bounds.Width/2,fromButton.Bounds.Height/2));
                var previous=window.PageView.GetVisualDescendants().OfType<SettingsCards>().Single();
                using(var settle=new UiAnimationClock(previous)) {previous.Progress=0;settle.Pulse(0);previous.Progress=1;settle.Pulse(260);Layout(window);}
                using var clock=new UiAnimationClock(general,updateFace);clock.Pulse(0);
                var returnButton=window.SettingsSections.ButtonAt(0);Click(window,returnButton,new(returnButton.Bounds.Width/2,returnButton.Bounds.Height/2));clock.Pulse(1);
                for(var ms=0;ms<=256;ms+=8)
                {
                    clock.Pulse(1+ms);Layout(window);
                    check(updateFace.Background is ISolidColorBrush paint&&paint.Color==Color.Parse("#3A3A3A"),
                        "Check for updates retains its opaque normal paint when returning from settings section "+from+" at "+ms+" ms");
                    paintFrames.Add(new {section=from,milliseconds=ms,general.Progress,paint=(updateFace.Background as ISolidColorBrush)?.Color.ToString()});
                    if(from==2&&ms is 0 or 16 or 64 or 160)Save(window,Path.Combine(folder,"settings-return-"+ms+".png"));
                }
            }
            File.WriteAllText(Path.Combine(folder,"settings-return-paint.json"),JsonSerializer.Serialize(paintFrames));
            var proxySection=window.SettingsSections!.ButtonAt(2);Click(window,proxySection,new(proxySection.Bounds.Width/2,proxySection.Bounds.Height/2));
            var automaticSwitch=window.PageView.GetVisualDescendants().OfType<CheckBox>().Single(option=>option.Content is TextBlock t&&t.Text==UiText.Get("Switch timed-out nodes within the same country or region"));
            check(automaticSwitch.IsChecked==false&&!window.Controller.State.AutoSwitchOnTimeout,"Automatic same-region timeout switching is off for an existing default profile");
            automaticSwitch.IsChecked=true;await Until(()=>window.Controller.State.AutoSwitchOnTimeout&&!window.Controller.Busy);
            check(new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.AutoSwitchOnTimeout&&!window.Controller.Engine.IsRunning,
                "Enabling automatic switching saves the option without starting an engine or touching the current connection");
            Save(window,Path.Combine(folder,"automatic-switch-default.png"));
            automaticSwitch.IsChecked=false;await Until(()=>!window.Controller.State.AutoSwitchOnTimeout&&!window.Controller.Busy);
            check(!new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.AutoSwitchOnTimeout,"The same switch can be disabled and persists immediately");
            window.Rail.Select("Providers");Layout(window);
            var link=window.PageView.GetVisualDescendants().OfType<Button>().Single(b=>b.Content is TextBlock t&&t.Text==UiText.Get("Official website"));
            check(link.Classes.Contains("accent"),"The provider card's short official-website label retains the blue accent");
            var site=window.Controller.State.Providers.Single();await window.OpenProviderDetailsAsync(site.Id.ToString());Layout(window);
            check(window.GetVisualDescendants().OfType<Button>().Any(b=>b.Content is TextBlock t&&t.Text==UiText.Get("Official website")&&b.Classes.Contains("accent")),
                "The provider detail action uses the same short official-website label and blue accent");
        }
        finally {window.Close();await Until(()=>!window.IsVisible);}
    }
}
