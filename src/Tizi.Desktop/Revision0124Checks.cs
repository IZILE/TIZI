using System.Net;
using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

// Network waits and animation reversals use synthetic data and an isolated profile.
internal static class Revision0124Checks
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        void Layout(Control top) { top.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        async Task Until(Func<bool> done) { using var stop = new CancellationTokenSource(8000); while (!done()) await Task.Delay(10, stop.Token); }
        void Capture(Control top, string file) { Layout(top); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var frame = top.CaptureRenderedFrame(); frame?.Save(Path.Combine(output, file), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
        void SwitchBack(Window target) { typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null); typeof(WindowBase).GetMethod("HandleActivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null); }
        void Click(Control top, Control control) { Layout(top); var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value; top.MouseMove(point); top.MouseDown(point, MouseButton.Left); top.MouseUp(point, MouseButton.Left); Layout(top); }
        var subscription = new Subscription { Name = "Animation fixture", Url = "https://feed.example/" };
        const string feed = "ss://YWVzLTEyOC1nY206Zml4dHVyZQ@node.example:443#Fixture";
        var first = NodeImport.Parse(feed).Single() with { SubscriptionId = subscription.Id };
        var nodes = Enumerable.Range(0, 9).Select(i => first with { Id = i == 0 ? first.Id : Guid.NewGuid(), Name = $"Fixture {i}" }).ToList();
        var state = new AppState { Nodes = nodes, Subscriptions = [subscription], SelectedNode = first.Id, AutoUpdate = false, AutoUpdateProviders = false, RestoreConnection = false };
        new Vault(UserData.DirectoryPath).Write("settings.dat", state);
        var window = new MainWindow(false); window.Show(); await Until(() => window.Controller.Ready); window.ShowPage("Nodes"); Layout(window);
        try
        {
            var rows = window.SubscriptionView.Groups.Single().NodeRows;
            var grounds = rows.GetVisualDescendants().OfType<Canvas>().Single().Children.OfType<Border>().ToArray();
            var oldGround = grounds.Single(g => Equals(g.Tag, nodes[0].Id.ToString())); var newGround = grounds.Single(g => Equals(g.Tag, nodes[4].Id.ToString()));
            var contents = rows.ItemIds.Select(id => rows.For(id).Content).ToArray();
            using (var clock = new UiAnimationClock(grounds.Cast<Animatable>().ToArray()))
            {
                clock.Pulse(0); clock.Pulse(200);
                await window.Controller.ExecuteAsync(() => window.Controller.SelectAsync(nodes[4])); Layout(window);
                clock.Pulse(201); clock.Pulse(265);
                var blue = ((ISolidColorBrush)MovingRows.CurrentNodeBrush).Color; var gray = ((ISolidColorBrush)Skin.TabBack).Color;
                bool Intermediate(Border ground) => ground.Background is ISolidColorBrush brush && brush.Color != blue && brush.Color != gray;
                check(Intermediate(oldGround) && Intermediate(newGround), "Changing a grid node fades the outgoing blue background and incoming blue background through intermediate colors");
                Color Composite(Color color)
                {
                    var baseColor = ((ISolidColorBrush)Skin.ChromeBrush).Color; var alpha = color.A / 255d;
                    return Color.FromRgb((byte)Math.Round(color.R * alpha + baseColor.R * (1 - alpha)), (byte)Math.Round(color.G * alpha + baseColor.G * (1 - alpha)), (byte)Math.Round(color.B * alpha + baseColor.B * (1 - alpha)));
                }
                var neutral = Composite(gray);
                bool Bounded(Border ground)
                {
                    if (ground.Background is not ISolidColorBrush brush) return false;
                    var displayed = Composite(brush.Color);
                    return displayed.R >= Math.Min(blue.R, neutral.R) - 1 && displayed.R <= Math.Max(blue.R, neutral.R) + 1
                        && displayed.G >= Math.Min(blue.G, neutral.G) - 1 && displayed.G <= Math.Max(blue.G, neutral.G) + 1
                        && displayed.B >= Math.Min(blue.B, neutral.B) - 1 && displayed.B <= Math.Max(blue.B, neutral.B) + 1;
                }
                check(Bounded(oldGround) && Bounded(newGround), "Node selection fade stays between the original gray and blue without a bright intermediate tint");
                check(rows.ItemIds.Select(id => rows.For(id).Content).SequenceEqual(contents), "Changing the active grid node retains the original card content trees without fading or rebuilding their text");
                Capture(window, "nodes-switch-middle.png");
                var beforeOld = ((ISolidColorBrush)oldGround.Background!).Color; var beforeNew = ((ISolidColorBrush)newGround.Background!).Color;
                await window.Controller.ExecuteAsync(() => window.Controller.SelectAsync(nodes[0])); Layout(window);
                check(((ISolidColorBrush)oldGround.Background!).Color == beforeOld && ((ISolidColorBrush)newGround.Background!).Color == beforeNew, "Reversing grid selection mid-fade retains the displayed colors without a flash");
                clock.Pulse(266); clock.Pulse(466); Layout(window);
                check(grounds.Count(g => g.Background is ISolidColorBrush brush && brush.Color == blue) == 1 && ((ISolidColorBrush)oldGround.Background!).Color == blue, "Reversed grid selection settles with exactly one solid blue current node");
            }
            window.ShowPage("Settings"); Layout(window); Capture(window, "settings-title-unified.png"); window.ShowPage("Nodes"); Layout(window);

            var observed = new CountingHandler(); window.Controller.SourceHttpClientFactory = () => new(observed);
            window.Controller.SearchHttpClientFactory = () => throw new InvalidOperationException("An automatic update must never call an AI search API.");
            var autoMethod = typeof(AppController).GetMethod("AutoUpdateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await window.Controller.ApplyAsync(window.Controller.State with { Providers = [new() { Name = "Website fixture", Url = "https://website.example/" }], AutoUpdate = false, AutoUpdateProviders = true, AutomaticWebsiteChecksOptIn = false });
            await (Task)autoMethod.Invoke(window.Controller, null)!;
            check(observed.Requests == 0 && !window.Controller.SearchBusy, "An older saved auto-website flag triggers no website or AI requests without a new explicit opt-in");
            await window.Controller.ApplyAsync(window.Controller.State with { AutomaticWebsiteChecksOptIn = true });
            await (Task)autoMethod.Invoke(window.Controller, null)!;
            check(observed.Requests == 1 && observed.OnlyHead && !window.Controller.SearchBusy, "Explicitly enabling automatic website status performs only a website HEAD check and never starts AI discovery");

            window.HeaderLatencyFixture = (_, _) => Task.FromResult(35); window.NodeControl.IsDropDownOpen = true; Layout(window);
            var popup = window.NodeControl.GetVisualDescendants().OfType<Popup>().Single(); SwitchBack(window); Layout(window);
            check(!window.NodeControl.IsDropDownOpen && !popup.IsOpen, "Switching windows immediately disposes the node dropdown popup and releases its input host");
            window.HeaderLatencyFixture = null;
            var context = rows.For(nodes[1].Id.ToString()).ContextMenu!; context.Open(rows.For(nodes[1].Id.ToString())); Layout(window);
            SwitchBack(window); Layout(window);
            check(!context.IsOpen && window.Controller.State.Nodes.Count == nodes.Count, "Switching windows dismisses the item context menu without dispatching an item operation");

            var gate = new DelayedFeed(feed); window.Controller.SourceHttpClientFactory = () => new(gate);
            await window.Controller.ApplyAsync(window.Controller.State with { AutoUpdate = true, AutoUpdateProviders = false });
            var automatic = (Task)autoMethod.Invoke(window.Controller, null)!;
            await Until(() => gate.Requested); Layout(window);
            var add = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock t && t.Text == UiText.Get("Import nodes"));
            check(window.Controller.Updating && !window.Controller.Busy && add.IsEffectivelyEnabled && rows.IsEffectivelyEnabled, "A delayed automatic subscription request leaves content cards and toolbar buttons available instead of only the sidebar");
            SwitchBack(window); Layout(window);
            Click(window, add); var editor = window.OpenDialogs.OfType<EditorDialog>().Single(); editor.Close(); Layout(window);
            check(window.IsEnabled && !window.Controller.Busy, "After switching away and back during a background request, an actual content button still opens and closes its editor");
            var manual = first with { Id = Guid.NewGuid(), SubscriptionId = null, Name = "Concurrent manual fixture" };
            var rule = new RoutingRule("DOMAIN", "concurrent.example", "DIRECT");
            await window.Controller.ExecuteAsync(() => window.Controller.ApplyAsync(window.Controller.State with { Nodes = [.. window.Controller.State.Nodes, manual], Rules = [rule], MixedPort = 7899 }));
            gate.Release.TrySetResult(); await automatic; Layout(window);
            check(window.Controller.State.Nodes.Any(n => n.Id == manual.Id) && window.Controller.State.Rules.SequenceEqual([rule]) && window.Controller.State.MixedPort == 7899 && !window.Controller.Busy,
                "Background refresh merges into the latest saved state and retains concurrent node imports, rules and settings");

            var stale = new DelayedFeed(feed); window.Controller.SourceHttpClientFactory = () => new(stale);
            var pending = window.Controller.UpdateAsync(automatic: true, providers: false); await Until(() => stale.Requested);
            await window.Controller.ExecuteAsync(() => window.Controller.ApplyAsync(window.Controller.State with { Subscriptions = [], Nodes = [manual], SelectedNode = manual.Id }));
            stale.Release.TrySetResult(); await pending; Layout(window);
            check(window.Controller.State.Subscriptions.Count == 0 && window.Controller.State.Nodes.Single().Id == manual.Id && window.Controller.State.SelectedNode == manual.Id,
                "A subscription deleted while its automatic refresh is pending cannot be resurrected by the late response");

            var action = false; var more = window.ShowMoreActionsAsync("Nodes", ("Add node", () => action = true)); Layout(window);
            SwitchBack(window); await more; Layout(window);
            check(window.ActiveMoreSheet is null && !action && !window.GetVisualDescendants().OfType<MoreActionsSheet>().Any(), "Switching windows dismisses More and removes its input overlay without executing an action");
        }
        finally
        {
            window.Controller.SourceHttpClientFactory = null;
            window.Controller.SearchHttpClientFactory = null;
            typeof(MainWindow).GetMethod("Exit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            await Until(() => !window.IsVisible);
        }
    }
    private sealed class CountingHandler : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        internal bool OnlyHead { get; private set; } = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; OnlyHead &= request.Method == HttpMethod.Head; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }
    }
    private sealed class DelayedFeed(string body) : HttpMessageHandler
    {
        internal bool Requested { get; private set; }
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requested = true; await Release.Task.WaitAsync(cancellationToken); return new(HttpStatusCode.OK) { Content = new StringContent(body) }; }
    }
}
