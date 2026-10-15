using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class SubscriptionRevision0126Checks
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        static void Layout(Control window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        static void Click(Control window, Control control, MouseButton button = MouseButton.Left)
        { var point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value; window.MouseMove(point); window.MouseDown(point, button); window.MouseUp(point, button); Layout(window); }
        static async Task Until(Func<bool> done) { using var timeout = new CancellationTokenSource(8000); while (!done()) await Task.Delay(10, timeout.Token); }
        static void Remove(SubscriptionTree.GroupCard group) { var item = group.ContextMenu!.Items.OfType<MenuItem>().Single(); item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item)); }
        var subscription = new Subscription { Name = "Subscription fixture", Url = "https://fixture.example/subscription" };
        var node = NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@node.example:443#Fixture").Single();
        var subscribed = node with { Id = Guid.NewGuid(), SubscriptionId = subscription.Id, Name = "Subscription node" };
        var manual = node with { Id = Guid.NewGuid(), Name = "Manual fixture" };
        var state = new AppState { Subscriptions = [subscription], Nodes = [subscribed, manual], SelectedNode = subscribed.Id, Rules = [new("DOMAIN", "fixture.example", "DIRECT")], AutoUpdate = false, AutoUpdateProviders = false, RestoreConnection = false, CloseToTray = false };
        new Vault(UserData.DirectoryPath).Write("settings.dat", state);
        var window = new MainWindow(false); window.Show(); await Until(() => window.Controller.Ready); window.Rail.Select("Nodes"); Layout(window);
        try
        {
            var group = window.SubscriptionView.Groups.Single(g => g.Key == subscription.Id.ToString());
            foreach(var card in window.SubscriptionView.Groups)
            {
                var groupSelectionEvents=0;card.Selected+=()=>groupSelectionEvents++;
                foreach(var corner in new[]{new Point(2,2),new Point(card.Disclosure.Bounds.Width-2,2),new Point(2,card.Disclosure.Bounds.Height-2),new Point(card.Disclosure.Bounds.Width-2,card.Disclosure.Bounds.Height-2)})
                {
                    foreach(var shownCard in window.SubscriptionView.Groups)
                    {var transitions=shownCard.Reveal.Transitions;shownCard.Reveal.Transitions=null;shownCard.Reveal.Progress=shownCard.NodesShown?1:0;shownCard.Reveal.Transitions=transitions;}
                    Layout(window);card.Header.BringIntoView();Layout(window);
                    var expanded=card.Expanded;var disclosurePoint=card.Disclosure.TranslatePoint(corner,window)!.Value;
                    window.MouseMove(disclosurePoint);window.MouseDown(disclosurePoint,MouseButton.Left);window.MouseUp(disclosurePoint,MouseButton.Left);Layout(window);
                    check(card.Expanded!=expanded&&groupSelectionEvents==0&&window.Controller.State.SelectedNode==subscribed.Id,
                        "The expanded disclosure hit target toggles "+card.Key+" from padded corner "+corner+" without selecting the group or switching a node");
                }
            }
            Click(window, group.Header);
            using (var clock = new UiAnimationClock(group, group.Reveal))
            {
                clock.Pulse(0); clock.Pulse(220); Layout(window);
                check(group.IsSelected && group.NodesShown && group.Background is ISolidColorBrush open && open.Color == Skin.Chrome,
                    "An expanded subscription remains an operation target without filling its node area with gray selection");
                Click(window, group.Disclosure); clock.Pulse(220); clock.Pulse(480); Layout(window); clock.Pulse(481); clock.Pulse(700); Layout(window);
                check(!group.NodesShown && group.IsSelected && group.Background is ISolidColorBrush closed && closed.Color == ((ISolidColorBrush)Ui.SelectionBrush).Color,
                    "Collapsing the selected subscription restores its whole-card gray highlight after the node viewport retracts (" + group.IsSelected + ", " + group.NodesShown + ", " + group.Reveal.Progress + ", " + group.Background + ")");
            }
            var hand = window.SubscriptionView.Groups.Single(g => g.Key == "manual"); Click(window, hand.Header);
            using (var clock = new UiAnimationClock(hand)) { clock.Pulse(0); clock.Pulse(220); Layout(window); }
            check(!hand.NodesShown && hand.IsSelected && hand.Background is ISolidColorBrush selected && selected.Color == ((ISolidColorBrush)Ui.SelectionBrush).Color && window.SelectedItems.Contains("manual"),
                "A collapsed manual-node group can be selected and uses the same whole-card gray highlight as a subscription");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "manual-group-selected.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Click(window, hand.Header, MouseButton.Right);
            check(hand.ContextMenu is { IsOpen: true } && hand.ContextMenu.Items.OfType<MenuItem>().Single().Header?.ToString() == UiText.Get("Remove manual nodes") && !hand.NodesShown,
                "Right-clicking a collapsed manual-node group opens its removal menu without expanding it or switching a node");
            Remove(hand); await Until(() => window.Controller.State.Nodes.All(n => n.Id != manual.Id));
            check(window.Controller.State.Nodes.Single().Id == subscribed.Id && window.Controller.State.Subscriptions.Single().Id == subscription.Id && window.Controller.State.Rules.SequenceEqual(state.Rules),
                "Collapsed single-node removal removes only that manual node and preserves subscriptions and routing rules");
            await Until(() => !window.Controller.Busy); Layout(window);
            var undo = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock text && text.Text == UiText.Get("Undo")); Click(window, undo);
            await Until(() => window.Controller.State.Nodes.Any(n => n.Id == manual.Id)); Layout(window);
            check(window.Controller.State.Nodes.Select(n => n.Id).SequenceEqual(state.Nodes.Select(n => n.Id)), "Undo restores a removed manual node at its original position");
            await window.Controller.ApplyAsync(window.Controller.State with { Nodes = [.. window.Controller.State.Nodes, manual with { Id = Guid.NewGuid(), Name = "Second manual fixture" }] }); Layout(window);
            hand = window.SubscriptionView.Groups.Single(g => g.Key == "manual"); Click(window, hand.Header, MouseButton.Right); Remove(hand);
            await Until(() => window.OpenDialogs.Any());
            var confirmation = window.OpenDialogs.Single(); CenteredDialogChecks.Ready(confirmation);
            check(confirmation.Title == UiText.Get("Remove manual nodes") && confirmation.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("2", StringComparison.Ordinal) == true),
                "Removing a collapsed manual group with multiple nodes previews its actual node count in the centered confirmation");
            await confirmation.CloseSheetAsync(false); check(window.Controller.State.Nodes.Count == 3, "Cancelling manual-group removal leaves all nodes intact");
            Click(window, hand.Header, MouseButton.Right); Remove(hand); await Until(() => window.OpenDialogs.Any());
            confirmation = window.OpenDialogs.Single();
            await window.Controller.ApplyAsync(window.Controller.State with { Nodes = [.. window.Controller.State.Nodes, manual with { Id = Guid.NewGuid(), Name = "Late import fixture" }] });
            await confirmation.CloseSheetAsync(true); await Until(() => window.Notification.Message == UiText.Get("The node group changed. Reopen its menu before removing it."));
            check(window.Controller.State.Nodes.Count == 4, "A node imported during removal confirmation cannot be silently included in the deletion");
            window.Notification.Dismiss(); Layout(window);
            var point = hand.Header.TranslatePoint(new(hand.Header.Bounds.Width / 2, hand.Header.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); await Task.Delay(510); window.MouseUp(point, MouseButton.Left); Layout(window);
            check(window.SelectedItems.SetEquals(new[] { "manual" }), "Long-pressing the collapsed manual group enters the existing multiple-selection mode directly");
            Click(window, group.Header); check(window.SelectedItems.SetEquals(new[] { "manual", "subscription:" + subscription.Id }), "Manual groups and subscriptions can be selected together without selecting hidden child rows");
        }
        finally { window.Close(); await Until(() => !window.IsVisible); }
    }
}
