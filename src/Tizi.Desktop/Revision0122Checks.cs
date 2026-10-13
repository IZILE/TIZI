using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class Revision0122Checks
{
    internal static async Task RunAsync(string output,Action<bool,string> check)
    {
        void Layout(TopLevel window){window.UpdateLayout();Dispatcher.UIThread.RunJobs();}
        void Click(Window window,Control control,MouseButton button=MouseButton.Left)
        {Layout(window);var point=control.TranslatePoint(new Point(control.Bounds.Width/2,control.Bounds.Height/2),window)!.Value;window.MouseMove(point);window.MouseDown(point,button);window.MouseUp(point,button);Layout(window);}
        async Task Until(Func<bool> done){using var stop=new CancellationTokenSource(8000);while(!done())await Task.Delay(10,stop.Token);}
        async Task LongPress(Window window,Control control)
        {
            Layout(window);var point=control.TranslatePoint(new Point(control.Bounds.Width/2,control.Bounds.Height/2),window)!.Value;
            window.MouseMove(point);window.MouseDown(point,MouseButton.Left);await Task.Delay(500);Layout(window);window.MouseUp(point,MouseButton.Left);Layout(window);
        }
        void Capture(Window window,string file)
        {Layout(window);AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(output,file),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());}
        var sub=new Subscription{Name="Grid fixture",Url="https://subscription.example/"};var other=new Subscription{Name="Other fixture",Url="https://other.example/"};
        var template=NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@node.example:443#Fixture")[0];
        var nodes=Enumerable.Range(0,18).Select(i=>template with{Id=Guid.NewGuid(),Name=$"日本 {i:00} | IEPL · Fixture",SubscriptionId=sub.Id}).ToList();
        var outside=template with{Id=Guid.NewGuid(),Name="Other fixture",SubscriptionId=other.Id};nodes.Add(outside);
        var state=new AppState{Nodes=nodes,Subscriptions=[sub,other],SelectedNode=nodes[0].Id,AutoUpdate=false,AutoUpdateProviders=false,RestoreConnection=false,Rules=Enumerable.Range(0,6).Select(i=>new RoutingRule("DOMAIN",$"fixture-{i}.example","DIRECT")).ToList()};
        new Vault(UserData.DirectoryPath).Write("settings.dat",state);var window=new MainWindow(false);window.Show();await Until(()=>window.Controller.Ready);window.ShowPage("Nodes");Layout(window);
        check(window.Width==1032&&window.Height==722,"The normal application opens at the requested compact desktop size");
        var group=window.SubscriptionView.Groups.Single(g=>g.Key==sub.Id.ToString());var panel=group.NodeRows.GetVisualDescendants().OfType<NodeTilePanel>().Single();
        check(group.Expanded&&panel.Columns>=3,"The active node subscription opens by default and shows multiple columns at the default window size");
        var normalColumns=panel.Columns;window.Width=880;Layout(window);check(panel.Columns<normalColumns,"Narrowing the actual node page reduces its grid column count");window.Width=1640;Layout(window);check(panel.Columns>normalColumns,"Widening the actual node page adds grid columns");window.Width=1032;Layout(window);
        var switched=nodes[4];Click(window,group.NodeRows.For(switched.Id.ToString()));Click(window,group.NodeRows.For(switched.Id.ToString()));await Until(()=>!window.Controller.Busy&&window.Controller.State.SelectedNode==switched.Id);Layout(window);
        var grounds=group.NodeRows.GetVisualDescendants().OfType<Canvas>().Single().Children.OfType<Border>();
        check(grounds.Single(b=>Equals(b.Tag,switched.Id.ToString())).Background==MovingRows.CurrentNodeBrush&&grounds.Count(b=>b.Background==MovingRows.CurrentNodeBrush)==1&&!group.NodeRows.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Tag is "current-node"),"Double-clicking switches the actual current node and moves its single blue card background without a duplicate checkmark");
        Click(window,group.NodeRows.For(nodes[0].Id.ToString()));Click(window,group.NodeRows.For(nodes[0].Id.ToString()));await Until(()=>!window.Controller.Busy&&window.Controller.State.SelectedNode==nodes[0].Id);Layout(window);
        var remove=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Tag is "remove-selection");var inactive=nodes[1];
        Click(window,group.NodeRows.For(inactive.Id.ToString()));check(!remove.IsEffectivelyVisible&&window.Controller.State.SelectedNode==nodes[0].Id,"A normal node click only selects and leaves Remove hidden and the current node unchanged");
        var requests=new List<Guid>();window.HeaderLatencyFixture=(node,_)=>{requests.Add(node.Id);return Task.FromResult(35+nodes.IndexOf(node)*20);};
        window.NodeControl.IsDropDownOpen=true;Layout(window);await Until(()=>!window.TestingLatency);Layout(window);
        var popup=window.NodeControl.GetVisualDescendants().OfType<Popup>().Single();var badges=popup.Child!.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.Tag is "header-node-latency"&&t.IsVisible).ToArray();
        check(requests.Count==18&&!requests.Contains(outside.Id)&&badges.Length==18&&badges.All(b=>b.Text?.EndsWith(" ms")==true),"Opening the unchanged header dropdown measures only its subscription and displays each delay beside its original row");
        window.NodeControl.IsDropDownOpen=false;Layout(window);var started=0;window.HeaderLatencyFixture=async(_,token)=>{started++;await Task.Delay(Timeout.Infinite,token);return 0;};
        window.NodeControl.IsDropDownOpen=true;await Until(()=>started>0);window.NodeControl.IsDropDownOpen=false;await Until(()=>!window.TestingLatency);Layout(window);
        check(!window.Controller.Connected&&!window.Controller.Engine.IsRunning&&window.Controller.State.SelectedNode==nodes[0].Id,"Closing the header dropdown cancels its synthetic tests without connecting, changing the current node or leaving an engine running");window.HeaderLatencyFixture=null;
        await LongPress(window,group.NodeRows.For(inactive.Id.ToString()));
        check(window.SelectionMenu is {IsOpen:true}&&window.SelectionMenu.Items.OfType<MenuItem>().Count()==2,"Holding a node opens the animated Multi-select / Select all menu before any toolbar Remove appears");
        window.SelectionMenu!.Items.OfType<MenuItem>().Single(i=>i.Header?.ToString()==UiText.Get("Multi-select")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var bubble=window.GetVisualDescendants().OfType<RemovalBubble>().Single();await Until(()=>bubble.IsVisible);Layout(window);
        bubble.SetOpen(false);
        using(var clock=new UiAnimationClock(bubble)){clock.Pulse(0);bubble.SetOpen(true);clock.Pulse(1);clock.Pulse(101);check(bubble.Progress>0&&bubble.Progress<1,"The red removal bubble exposes an intermediate opening frame");clock.Pulse(300);Layout(window);}
        Click(window,group.NodeRows.For(nodes[2].Id.ToString()));
        check(window.SelectedItems.Count==2&&remove.IsEffectivelyVisible&&remove.Background is ISolidColorBrush brush&&brush.Color==Color.Parse("#D74756"),"Long-press selection mode allows additional cards and reveals one red bulk Remove after the last action");Capture(window,"nodes-multiple-selection.png");
        Click(window,group.NodeRows.For(inactive.Id.ToString()),MouseButton.Right);check(window.SelectedItems.Count==2,"Right-clicking within a multiple selection retains all selected targets");group.NodeRows.For(inactive.Id.ToString()).ContextMenu!.Close();await Task.Delay(200);
        check(window.SelectedItems.SetEquals(new[]{inactive.Id.ToString(),nodes[2].Id.ToString()})&&window.Controller.State.SelectedNode==nodes[0].Id,$"Bulk removal has exactly the two intended inactive targets (indices: {string.Join(",",window.SelectedItems.Select(id=>nodes.FindIndex(n=>n.Id.ToString()==id)))}, active index: {nodes.FindIndex(n=>n.Id==window.Controller.State.SelectedNode)})");
        Click(window,remove);await Until(()=>!window.Controller.Busy&&window.Controller.State.Nodes.Count==17);Layout(window);
        check(window.Controller.State.SelectedNode==nodes[0].Id&&window.Controller.State.Rules.Count==6,$"Bulk node removal affects exactly the chosen inactive nodes and preserves the active node and routing rules (active index: {nodes.FindIndex(n=>n.Id==window.Controller.State.SelectedNode)}, rules: {window.Controller.State.Rules.Count})");
        Click(window,window.GetVisualDescendants().OfType<Button>().Single(b=>b.Content is TextBlock t&&t.Text==UiText.Get("Undo")));await Until(()=>!window.Controller.Busy&&window.Controller.State.Nodes.Count==19);Layout(window);
        check(window.Controller.State.Nodes.Select(n=>n.Id).SequenceEqual(nodes.Select(n=>n.Id)),"Bulk removal can be undone in the original node order");
        var beforeSmall=window.Controller.State;await window.Controller.ApplyAsync(beforeSmall with {Nodes=nodes.Take(3).Append(outside).ToList()});Layout(window);
        group=window.SubscriptionView.Groups.Single(g=>g.Key==sub.Id.ToString());panel=group.NodeRows.GetVisualDescendants().OfType<NodeTilePanel>().Single();
        var few=group.NodeRows.ItemIds.Select(group.NodeRows.For).ToArray();
        check(panel.Columns==1&&few.Length==3&&few.All(b=>Math.Abs(b.Bounds.Width-group.NodeRows.Bounds.Width)<1)&&few.Skip(1).Zip(few).All(pair=>pair.First.Bounds.Y>=pair.Second.Bounds.Bottom+8),"Three nodes retain the original horizontal full-width list rather than sparse grid tiles");Capture(window,"nodes-small-list.png");
        window.Width=1640;Layout(window);check(panel.Columns==1,"Widening a small subscription preserves its original list layout");window.Width=1032;
        await window.Controller.ApplyAsync(beforeSmall with {Nodes=nodes.Take(4).Append(outside).ToList()});Layout(window);
        check(group.NodeRows.GetVisualDescendants().OfType<NodeTilePanel>().Single().Columns>=3,"Adding a fourth node switches the same subscription to its responsive grid");
        await window.Controller.ApplyAsync(beforeSmall);Layout(window);
        window.ShowPage("Rules");Layout(window);var rows=window.PageView.GetVisualDescendants().OfType<MovingRows>().Single();await LongPress(window,rows.For("0"));
        window.SelectionMenu!.Items.OfType<MenuItem>().Single(i=>i.Header?.ToString()==UiText.Get("Select all")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Until(()=>window.SelectedItems.Count==6);Layout(window);
        check(rows.ItemIds.All(id=>rows.For(id).Classes.Contains("selected-card")),"Select all highlights every visible rule without opening an editor");
        Click(window,window.PageHeading);check(window.SelectedItems.Count==0,"A blank content click clears multiple selection and retracts its management action");
        typeof(MainWindow).GetMethod("Exit",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,null);await Until(()=>!window.IsVisible);
        DiscoverySettings? saved=null;var original=new DiscoverySettings{ApiKey="synthetic-key-never-sent"};
        var api=new AiSearchSettingsDialog(original,value=>{saved=value;return Task.CompletedTask;});api.Show();Layout(api);
        if(api.Content is DialogSurface surface){using var clock=new UiAnimationClock(surface,surface.Scale,surface.Offset);surface.Reveal();clock.Pulse(0);clock.Pulse(220);Layout(api);}
        api.ProviderChoice.SelectedItem="DeepSeek";Layout(api);
        check(api.KeyField.Text==""&&api.EndpointField.Text?.Contains("deepseek.com")==true,"Switching AI providers uses the provider endpoint and does not reuse another provider's secret");
        api.KeyField.Text="synthetic-key-never-sent";api.ProviderChoice.SelectedItem="OpenAI";Layout(api);check(api.KeyField.Text==original.ApiKey,"Switching back restores that provider's locally stored key draft");
        api.ProviderChoice.SelectedItem="Gemini";Layout(api);api.KeyField.Text="synthetic-key-never-sent";Capture(api,"ai-api-settings.png");await api.SaveAsync();
        check(saved is {Provider:"Gemini",Protocol:"gemini"}&&saved.Profiles.ContainsKey("DeepSeek")&&saved.Profiles["OpenAI"].ApiKey==original.ApiKey&&!api.IsVisible,"Saving API settings preserves separate provider profiles and closes only after applying them");
    }
}
