using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class InteractionRevisionChecks
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        static void Layout(Control window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        static void Click(Control window, Control control)
        {
            CenteredDialogChecks.Ready(window);
            Layout(window); var point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
        }
        static async Task Until(Func<bool> ready) { using var timeout = new CancellationTokenSource(8000); while (!ready()) await Task.Delay(10, timeout.Token); }
        static Button Button(Visual owner, string name) => owner.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock text && text.Text == UiText.Get(name));

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var committed = 0;
        var preparing = new EditorDialog("Import nodes", [new("text", "Name", "fixture")], async (_, session) => { await gate.Task; session.BeginCommit(); committed++; });
        preparing.Show(); Layout(preparing); var preparation = preparing.SubmitAsync(); var preparationToken = preparing.Token; preparing.Close();
        check(preparationToken.IsCancellationRequested && preparing.IsVisible && !preparing.Applied, "Dismissing a preparing editor cancels its token and waits for preparation to stop");
        gate.SetResult(); await preparation;
        check(committed == 0 && !preparing.Applied && !preparing.IsVisible, "A late preparation cannot cross the commit boundary after the editor was cancelled");
        gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var atomic = new EditorDialog("Edit subscription", [new("name", "Name", "fixture")], async (_, session) => { session.BeginCommit(); await gate.Task; committed++; });
        atomic.Show(); Layout(atomic); var applying = atomic.SubmitAsync(); var atomicToken = atomic.Token; atomic.Close();
        check(atomic.IsVisible && atomic.Session.IsCommitting && !atomicToken.IsCancellationRequested, "Closing during an actual atomic save is deferred instead of reporting a false cancellation");
        gate.SetResult(); await applying;
        check(committed == 1 && atomic.Applied && !atomic.IsVisible, "The atomic editor reports Applied only after its real save completes");

        var firstSubscription = new Subscription { Name = "First fixture", Url = "https://one.subscription.example/private" };
        var secondSubscription = new Subscription { Name = "Second fixture", Url = "https://two.subscription.example/private" };
        var active = NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@active.example:443#Active")[0] with { SubscriptionId = firstSubscription.Id };
        var inactive = NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@inactive.example:443#Inactive")[0] with { SubscriptionId = firstSubscription.Id };
        var other = NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@other.example:443#Other")[0] with { SubscriptionId = secondSubscription.Id };
        var rules = Enumerable.Range(0, 205).Select(i => new RoutingRule("DOMAIN", $"name-{i:00000}.example", "DIRECT")).ToList();
        var state = new AppState { AutoUpdate = false, AutoUpdateProviders = false, StartHidden = false, Nodes = [active, inactive, other], SelectedNode = active.Id, Subscriptions = [firstSubscription, secondSubscription], Rules = rules };
        new Vault(UserData.DirectoryPath).Write("settings.dat", state);
        var window = new MainWindow(false); window.Show(); await Until(() => window.Controller.Ready); Layout(window);
        window.ShowPage("Rules"); Layout(window);
        var rows = window.PageView.GetVisualDescendants().OfType<MovingRows>().Single(); Click(window, rows.For("0"));
        var remove = Button(window, "Remove"); check(remove.IsEnabled, "A visible singly selected rule enables its operation controls");
        Click(window, window.PageHeading);
        check(!remove.IsEnabled && !rows.For("0").Classes.Contains("selected-card") && window.ActiveRuleEditor is null, "Blank content click clears a rule selection without opening its editor");
        Click(window, rows.For("0"));
        window.SearchControl.Text = "name-00101.example"; Layout(window);
        check(!remove.IsEnabled && !rows.GetVisualDescendants().OfType<Button>().Any(b => b.Classes.Contains("selected-card")), "Filtering a selected rule out clears its operation highlight and disables Remove");
        remove.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        check(window.Controller.State.Rules.Count == rules.Count, "A stale removal event cannot delete an invisible filtered rule");
        window.SearchControl.Text = ""; Layout(window); Click(window, rows.For("0")); Click(window, Button(window, "Next"));
        check(!remove.IsEnabled && !Button(window, "Move up").IsEnabled && !Button(window, "Move down").IsEnabled, "Changing the rules page clears the target and disables both move actions and Remove");

        window.ShowPage("Nodes"); Layout(window); var group = window.SubscriptionView.Groups.Single(g => g.Key == firstSubscription.Id.ToString());
        Click(window, group.NodeRows.For(inactive.Id.ToString()));
        Click(window, window.PageHeading);
        check(!group.NodeRows.For(inactive.Id.ToString()).Classes.Contains("selected-card") && !Button(window, "Remove node").IsEnabled && window.Controller.State.SelectedNode == active.Id,
            "Blank content click clears a node's operation selection without switching the current node");
        Click(window, group.NodeRows.For(inactive.Id.ToString()));
        window.SearchControl.Text = "Other"; Layout(window);
        check(window.SearchControl.IsVisible && !window.GetVisualDescendants().OfType<Button>().Single(b => b.Tag is "remove-selection").IsEnabled && window.Controller.State.SelectedNode == active.Id,
            "Local node search hides an excluded operation target without switching the active node or permitting stale removal");
        window.SearchControl.Text = ""; Layout(window); group = window.SubscriptionView.Groups.Single(g => g.Key == firstSubscription.Id.ToString());
        group.NodeRows.For(inactive.Id.ToString()).Focus(); window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); Layout(window);
        var export = window.CreateExportDialog(); export.Show(); Layout(export);
        var exported = export.GetVisualDescendants().OfType<TextBox>().Single(t => t.Tag is "export-output").Text!;
        var exportedNodes = NodeImport.Parse(exported);
        check(exportedNodes.Count == 1 && exportedNodes[0].Server == inactive.Server, "Selected-node export uses the operation target instead of the currently connected node"); export.Close();
        var removalRow=group.NodeRows.For(inactive.Id.ToString()); var removalPoint=removalRow.TranslatePoint(new Point(removalRow.Bounds.Width/2,removalRow.Bounds.Height/2),window)!.Value;
        window.MouseMove(removalPoint); window.MouseDown(removalPoint,MouseButton.Right); window.MouseUp(removalPoint,MouseButton.Right); Layout(window);
        removalRow.ContextMenu!.Items.OfType<MenuItem>().Single(i=>i.Header?.ToString()==UiText.Get("Remove node")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        await Until(() => !window.Controller.Busy && window.Controller.State.Nodes.All(n=>n.Id!=inactive.Id)); Layout(window);
        check(window.Controller.State.Nodes.All(n => n.Id != inactive.Id) && window.Controller.State.SelectedNode == active.Id, "Removing an inactive selected node keeps the active node unchanged");
        await window.Controller.ExecuteAsync(() => window.Controller.ApplyAsync(window.Controller.State with { MixedPort = 17898 }));
        Click(window, Button(window, "Undo")); await Until(() => !window.Controller.Busy); Layout(window);
        check(window.Controller.State.Nodes.Any(n => n.Id == inactive.Id) && window.Controller.State.SelectedNode == active.Id && window.Controller.State.MixedPort == 17898, "Undo restores only removed contents while retaining settings changed after the removal");
        var secondGroup = window.SubscriptionView.Groups.Single(g => g.Key == secondSubscription.Id.ToString()); Click(window, secondGroup.Header);
        export = window.CreateExportDialog(); export.Show(); Layout(export); exported = export.GetVisualDescendants().OfType<TextBox>().Single(t => t.Tag is "export-output").Text!;
        check(exported.Contains(secondSubscription.Url, StringComparison.Ordinal) && !exported.Contains(firstSubscription.Url, StringComparison.Ordinal), "Selecting one subscription defaults export to that subscription only"); export.Close();

        await using (var delayed = new DelayedSubscription(NodeExport.Yaml([inactive], [])))
        {
            var before = SHA256.HashData(File.ReadAllBytes(UserData.File("settings.dat"))); var beforeState = window.Controller.State;
            var editor = window.CreateImportEditor(); var dialogTask = editor.ShowDialog(window); editor.Field("text").Text = delayed.Url; var pending = editor.SubmitAsync();
            await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(8)); editor.Close(); await pending; await dialogTask;
            delayed.Release.TrySetResult();
            check(!editor.Applied && !editor.IsVisible && ReferenceEquals(window.Controller.State, beforeState) && before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(UserData.File("settings.dat")))) && !window.Controller.Busy,
                "Cancelling the real subscription import during HTTP leaves both encrypted profile and loaded nodes/rules unchanged");
        }
        var optionsGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var optionsApplied = 0;
        typeof(MainWindow).GetMethod("OptionsDialog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window,
            ["Directory settings", new StackPanel(), new Func<Task>(async () => { await optionsGate.Task; optionsApplied++; })]);
        var optionsDialog = window.OpenDialogs.Single(); Layout(optionsDialog); Click(optionsDialog, Button(optionsDialog, "Save"));
        await Until(() => window.Controller.Busy); optionsDialog.Close(); Layout(optionsDialog);
        check(optionsDialog.IsVisible && !Button(optionsDialog, "Cancel").IsEnabled,
            "An options window stays open with Cancel disabled while its real save is committing");
        optionsGate.SetResult(); await Until(() => !optionsDialog.IsVisible);
        check(optionsApplied == 1 && !window.Controller.Busy, "An options save completes once before its window closes");
        window.ShowPage("Settings"); Layout(window);
        Click(window, window.SettingsSections!.ButtonAt(3)); Layout(window);
        var restoreOption = window.PageView.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content is TextBlock t && t.Text == UiText.Get("Enable IPv6"));
        window.Notification.Dismiss();
        var persistedRestore = window.Controller.State.Transport.Ipv6; restoreOption.IsChecked = !persistedRestore; Layout(window);
        check(window.SettingsDirty&&!window.Notification.Showing,"Editing settings keeps the draft indicator local without a top notice");
        window.ShowPage("Nodes");Layout(window);
        check(window.Notification.Showing&&window.Notification.IsWarning&&!window.Notification.IsError&&window.Notification.Message==UiText.Get("Unsaved changes"),"Leaving settings with a draft shows one yellow warning");
        check(window.Notification.Glyph is { Page: "Warning", FilledBody: null, OutlineBrush: Avalonia.Media.ISolidColorBrush brush } && brush.Color == Avalonia.Media.Color.Parse("#F4C152"),"The unsaved-settings notice uses a yellow outlined warning glyph from the sidebar icon family");
        using(var warningClock=new UiAnimationClock(window.Notification)){warningClock.Pulse(0);warningClock.Pulse(400);Layout(window);AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(output,"settings-unsaved-warning.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());}
        window.ShowPage("Settings");Layout(window);check(!window.Notification.Showing&&window.SettingsDirty,"Returning to settings dismisses the leave-page warning and retains the draft");
        window.Hide();typeof(MainWindow).GetMethod("Exit",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(window,null);
        await Until(() => window.OpenDialogs.Any(w => w.Tag is "discard-settings"));
        var discard = window.OpenDialogs.Single(w => w.Tag is "discard-settings"); Layout(discard); Click(discard, Button(discard, "Return to editing"));
        await Until(() => !discard.IsVisible);
        check(window.IsVisible && window.SettingsDirty && window.Controller.State.Transport.Ipv6 == persistedRestore,
            "Exiting with an unsaved settings draft offers a return path that retains the draft and leaves saved preferences unchanged");
        check(!typeof(Window).IsAssignableFrom(discard.GetType()) && !window.OwnedWindows.Any(),"The unsaved-settings prompt is a client-area sheet with no native caption app icon");
        window.Close(); await Until(() => window.OpenDialogs.Any(w => w.Tag is "discard-settings"));
        discard = window.OpenDialogs.Single(w => w.Tag is "discard-settings"); Layout(discard); Click(discard, Button(discard, "Discard and exit")); await Until(() => !window.IsVisible);
        check(new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.Transport.Ipv6 == persistedRestore,
            "Explicitly discarding an unsaved draft exits without applying it to the encrypted profile");
        var reopened=new MainWindow(false);reopened.Show();await Until(()=>reopened.Controller.Ready);reopened.ShowPage("Settings");Layout(reopened);
        check(reopened.IsVisible&&!reopened.SettingsDirty&&reopened.Controller.State.Transport.Ipv6==persistedRestore,"After discarding and closing, a fresh application window reopens the saved profile without a stuck draft or prompt");
        typeof(MainWindow).GetMethod("Exit",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(reopened,null);await Until(()=>!reopened.IsVisible);

        var connectionWindow = new MainWindow(true, state); connectionWindow.Show();
        typeof(MainWindow).GetField("_snapshot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(connectionWindow,
            new ConnectionSnapshot(0, 0, [new("fixture-connection", "fixture.example:443", "tcp", "MATCH", "TIZI", "fixture.exe", 1, 2)]));
        connectionWindow.ShowPage("Connections"); Layout(connectionWindow);
        var connectionRows = connectionWindow.PageView.GetVisualDescendants().OfType<MovingRows>().Single();
        Click(connectionWindow, connectionRows.For("fixture-connection"));
        check(Button(connectionWindow, "Close selected").IsEnabled, "A visible real-time connection can be selected for its close action");
        Click(connectionWindow, connectionWindow.PageHeading);
        check(!Button(connectionWindow, "Close selected").IsEnabled && !connectionRows.For("fixture-connection").Classes.Contains("selected-card"),
            "Blank content click clears real-time connection selection and its close target");
        connectionWindow.Close(); await connectionWindow.Controller.DisposeAsync();

        var nodeEditor = new NodeEditor(_ => Task.CompletedTask, inactive); nodeEditor.Show(); Layout(nodeEditor);
        ((TextBox)nodeEditor.Parameter("password")).Text = "changed-fixture"; nodeEditor.PortField.Value = 8443; nodeEditor.Advanced.Text = "{ unfinished draft";
        var originalProtocol = nodeEditor.Protocol.SelectedIndex; nodeEditor.Protocol.SelectedIndex = Array.FindIndex(ProtocolCatalog.Supported, p => p.CoreType == "ssh");
        nodeEditor.Protocol.SelectedIndex = originalProtocol; Layout(nodeEditor);
        check(((TextBox)nodeEditor.Parameter("password")).Text == "changed-fixture" && nodeEditor.PortField.Value == 8443 && nodeEditor.Advanced.Text == "{ unfinished draft" && nodeEditor.NameField.Text == inactive.Name,
            "Switching protocol A to B and back retains its parameters, port, unfinished JSON and shared name");
        await nodeEditor.SubmitAsync(); Layout(nodeEditor);
        check(!nodeEditor.Applied && nodeEditor.ErrorText == "Invalid JSON object." && nodeEditor.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Tag is "node-editor-error").IsVisible,
            "Node editor validation has a persistent inline error and keeps the draft open"); nodeEditor.Close();

        var dataDirectory = UserData.DirectoryPath; var failedDirectory = Path.Combine(output, "unreadable-profile-fixture"); Directory.CreateDirectory(failedDirectory);
        try
        {
            UserData.DirectoryPath = failedDirectory; var original = new byte[] { 6, 5, 4, 3, 2, 1 }; File.WriteAllBytes(UserData.File("settings.dat"), original);
            var failedWindow = new MainWindow(false); failedWindow.Show(); await Until(() => failedWindow.Controller.LoadFailure is not null); Layout(failedWindow);
            check(!failedWindow.Controller.Ready && failedWindow.GetVisualDescendants().OfType<Button>().Any(b => b.IsVisible && b.IsEffectivelyEnabled && b.Content is TextBlock t && t.Text == UiText.Get("Recover saved data")) && File.ReadAllBytes(UserData.File("settings.dat")).SequenceEqual(original),
                "An unreadable saved profile remains intact and displays persistent recovery instead of a fresh empty-state UI");
            await failedWindow.Controller.RecoverSavedDataAsync(state); Layout(failedWindow);
            check(failedWindow.Controller.Ready && failedWindow.Controller.State.Nodes.Count == 3 && failedWindow.Controller.State.Rules.Count == 205 && new Vault(failedDirectory).ProfileBackups().Any(path => File.ReadAllBytes(path).SequenceEqual(original)),
                "Explicit encrypted recovery restores usable contents and retains the unreadable original backup");
            failedWindow.Close(); await Until(() => !failedWindow.IsVisible);
        }
        finally { UserData.DirectoryPath = dataDirectory; }
    }
    private sealed class DelayedSubscription : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(); private readonly Task _server;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Url => "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/subscription";
        internal DelayedSubscription(string body) { _listener.Start(); _server = Serve(body); }
        private async Task Serve(string body)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token); using var stream = client.GetStream(); using var reader = new StreamReader(stream, leaveOpen: true);
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                Started.TrySetResult(); await Release.Task.WaitAsync(_stop.Token);
                var bytes = Encoding.UTF8.GetBytes(body); var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/yaml\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, _stop.Token); await stream.WriteAsync(bytes, _stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException) { }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _server; _stop.Dispose(); }
    }
}
