using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

// Application workflows against synthetic credentials and an isolated encrypted profile.
internal static class WorkflowChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        Directory.CreateDirectory(output); File.Delete(Path.Combine(output, "workflow-failure.txt")); File.Delete(Path.Combine(output, "workflow-checks.json")); var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
        void Layout(Control window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Click(Control window, Control control)
        {
            CenteredDialogChecks.Ready(window);
            Layout(window); var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
        }
        void RightClick(Control window, Control control)
        {
            Layout(window); var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Layout(window);
        }
        void SettleMenu(ContextMenu menu)
        {
            var surface = menu.GetVisualDescendants().OfType<PopupRevealSurface>().Single();
            using var clock = new UiAnimationClock(surface); surface.SetImmediately(0); surface.Progress = 1; clock.Pulse(0); clock.Pulse(70);
            Check(surface.Progress > 0 && surface.Progress < 1 && surface.VisibleBounds.Height > 0 && surface.VisibleBounds.Height < surface.Bounds.Height, "The actual context menu exposes an intermediate opening frame without scaling its text");
            clock.Pulse(200);
            Layout(TopLevel.GetTopLevel(menu)!);
        }
        async Task Until(Func<bool> condition) { using var timeout = new CancellationTokenSource(8000); while (!condition()) await Task.Delay(10, timeout.Token); }
        var proxyBefore = new WindowsProxyRegistry().Read();
        try
        {
            UiText.Language = "zh-CN";
            await CenteredDialogChecks.RunAsync(output, Check);
            await LogViewChecks.RunAsync(output, Check);
            await PageScrollChecks.RunAsync(output, Check);
            await SubscriptionRevision0126Checks.RunAsync(output, Check);
            await Revision0126PlusChecks.RunAsync(output, Check);
            await Revision0124Checks.RunAsync(output, Check);
            await Revision0126Checks.RunAsync(output, Check);
            var rules = Enumerable.Range(0, 9233).Select(i => new RoutingRule("DOMAIN", $"fixture{i}.example", "DIRECT")).ToList();
            var node = NodeImport.Parse("ss://YWVzLTEyOC1nY206ZGVtby1wYXNzd29yZA@jp.example.net:443#Japan%20fixture")[0];
            var state = new AppState { Nodes = [node], SelectedNode = node.Id, Rules = rules, AutoUpdate = false, AutoUpdateProviders = false, StartHidden = false };
            var window = new MainWindow(true, state); window.Show(); Layout(window);
            await window.Controller.ApplyAsync(state);
            window.ShowPage("Nodes"); Layout(window);
            var actionCalls = 0; var ownerReadyForAction = false;
            void SettleMore(MoreActionsSheet sheet) { using var clock = new UiAnimationClock(sheet); sheet.SetImmediately(0); sheet.Open(); clock.Pulse(0); clock.Pulse(350); Layout(window); }
            var actionTask = window.ShowMoreActionsAsync("Subscriptions and nodes", ("Add node", () => { actionCalls++; ownerReadyForAction = window.ActiveMoreSheet is null && window.IsEnabled; }), ("Edit node", () => actionCalls += 100));
            var actionSheet = window.ActiveMoreSheet!; SettleMore(actionSheet);
            Check(actionCalls == 0 && actionSheet.IsVisible && !window.OwnedWindows.Any(), "Opening More creates no OS dialog and executes no action");
            Click(window, actionSheet.Actions[0]); await actionTask;
            Check(actionCalls == 1 && ownerReadyForAction && !actionSheet.IsVisible, "Choosing More dispatches exactly the selected action after its overlay is removed");
            actionTask = window.ShowMoreActionsAsync("Airport navigation", ("Add website", () => actionCalls++));
            actionSheet = window.ActiveMoreSheet!; SettleMore(actionSheet);
            Check(actionSheet.GetVisualDescendants().OfType<Button>().Count() == actionSheet.Actions.Count, "More contains only its actions and has no redundant footer close button");
            window.MouseDown(new Point(12, window.ClientSize.Height - 12), MouseButton.Left);
            window.MouseUp(new Point(12, window.ClientSize.Height - 12), MouseButton.Left); await actionTask;
            Check(actionCalls == 1 && window.ActiveMoreSheet is null && window.IsEnabled, "Dismissing More outside its surface changes no data and restores the page");
            actionTask = window.ShowMoreActionsAsync("Subscriptions and nodes", ("Export", () => actionCalls++));
            actionSheet = window.ActiveMoreSheet!; SettleMore(actionSheet);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); await actionTask;
            Check(actionCalls == 1 && window.ActiveMoreSheet is null && !actionSheet.IsVisible, "Escape dismisses More without executing any action");
            actionTask = window.ShowMoreActionsAsync("Subscriptions and nodes", ("Export", () => actionCalls++));
            actionSheet = window.ActiveMoreSheet!; SettleMore(actionSheet);
            var outside = new Point(12, window.ClientSize.Height - 12);
            window.MouseDown(outside, MouseButton.Left); window.MouseUp(outside, MouseButton.Left); await actionTask;
            Check(actionCalls == 1 && window.ActiveMoreSheet is null, "An outside click closes More without passing through to a page action");
            actionTask = window.ShowMoreActionsAsync("Subscriptions and nodes", ("Export", () => actionCalls++));
            actionSheet = window.ActiveMoreSheet!;
            using (var moreClock = new UiAnimationClock(actionSheet))
            {
                actionSheet.SetImmediately(0); actionSheet.Open(); moreClock.Pulse(0); moreClock.Pulse(70); Layout(window);
                var frame = actionSheet.VisibleBounds; var closing = actionSheet.CloseAsync();
                Check(actionSheet.VisibleBounds == frame, "Interrupted More closing starts from the displayed shape");
                moreClock.Pulse(71); moreClock.Pulse(100); Layout(window); frame = actionSheet.VisibleBounds;
                actionSheet.Open(); Check(actionSheet.VisibleBounds == frame, "Reopening a closing More surface retains the current frame");
                moreClock.Pulse(101); moreClock.Pulse(450); Layout(window); await closing;
                Check(window.ActiveMoreSheet == actionSheet && actionSheet.IsVisible && actionSheet.VisibleBounds == actionSheet.TargetBounds, "An abandoned close cannot remove a reopened More surface");
                await actionSheet.CloseAsync(); await actionTask;
            }
            var previousSize = new Size(window.Width, window.Height); window.Width = 880; window.Height = 620; Layout(window);
            var selectedMoreAction = -1;
            actionTask = window.ShowMoreActionsAsync("Subscriptions and nodes", Enumerable.Range(0, 24).Select(i => ("Action " + i, (Action)(() => selectedMoreAction = i))).ToArray());
            actionSheet = window.ActiveMoreSheet!; SettleMore(actionSheet);
            var moreScroll = actionSheet.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Check(actionSheet.TargetBounds.Right <= window.ClientSize.Width - 15 && actionSheet.TargetBounds.Bottom <= window.ClientSize.Height - 15 && moreScroll.Extent.Height > moreScroll.Viewport.Height, "A long More page fits a minimum-size window and scrolls inside its rounded surface");
            moreScroll.Offset = new Vector(0, moreScroll.Extent.Height); Layout(window); Click(window, actionSheet.Actions[23]); await actionTask;
            Check(selectedMoreAction == 23 && window.ActiveMoreSheet is null, "The final action remains reachable and dispatches correctly in a long More page");
            window.Width = previousSize.Width; window.Height = previousSize.Height; Layout(window);
            async Task Import(string text, string label)
            {
                var before = window.Controller.State.Nodes.Count;
                var editor = window.CreateImportEditor(); var shown = editor.ShowDialog(window); Layout(editor);
                var field = editor.Field("text");
                Check(field.VerticalContentAlignment == VerticalAlignment.Top && field.CaretIndex == 0, label + ": empty import starts at the top with the first caret");
                field.Text = text; await editor.SubmitAsync();
                Check(editor.Applied && editor.ErrorText is null, label + ": real modal submit succeeds"); await shown;
                Check(window.Controller.State.Nodes.Count == before + 1 && window.CurrentPage == "Nodes" && !window.Controller.Busy && window.IsEnabled, label + ": saved node appears and parent becomes usable");
                Check(window.Controller.State.Rules.Count == rules.Count, label + ": the existing large ruleset is preserved");
            }
            await Import(NodeExport.ShareLink(node), "Share URL");
            await Import("{\"type\":\"WireGuard\",\"host\":\"wg.example.net\",\"port\":\"51820\",\"privateKey\":\"" + Convert.ToBase64String(new byte[32]) + "\",\"publicKey\":\"" + Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()) + "\",\"ip\":\"10.0.0.2\",\"flag\":\"GB\",\"mtu\":\"1419\",\"dns\":\"1.1.1.1\"}", "Shadowrocket JSON");
            foreach (var (payload, label) in new[] { (NodeExport.Json([node], []), "JSON file"), (NodeExport.Yaml([node], []), "YAML file"), (Convert.ToBase64String(Encoding.UTF8.GetBytes(NodeExport.ShareLink(node))), "Base64 subscription text") })
            {
                var before = window.Controller.State.Nodes.Count; await window.ImportPayloadAsync(Encoding.UTF8.GetBytes(payload), false);
                Check(window.Controller.State.Nodes.Count == before + 1, label + ": shared file payload path imports a node");
            }
            var count = window.Controller.State.Nodes.Count;
            await window.ImportPayloadAsync([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(NodeExport.Json([node], []))], false);
            Check(window.Controller.State.Nodes.Count == count + 1, "UTF-16 BOM file is decoded correctly");
            count = window.Controller.State.Nodes.Count; await window.ImportPayloadAsync(NodeExport.QrPng(NodeExport.ShareLink(node)), true);
            Check(window.Controller.State.Nodes.Count == count + 1, "QR image is decoded and committed through the application import workflow");
            const string batchLinks = "ss://YWVzLTEyOC1nY206ZGVtby1wYXNzd29yZA@jp.batch.example:443#Batch%20Japan\ntrojan://synthetic-two@sg.batch.example:8443?sni=sg.batch.example#Batch%20Singapore\nvless://29c295b8-5724-4c41-8cf1-ceae43986a58@us.batch.example:443?security=tls&sni=us.batch.example#Batch%20USA";
            var batch = NodeImport.Parse(batchLinks);
            async Task Batch(string label, Func<Task> import, IReadOnlyList<ProxyNode> expected)
            {
                var before = window.Controller.State.Nodes.Count;
                await import();
                var added = window.Controller.State.Nodes.Skip(before).ToList();
                Check(added.Count == expected.Count && expected.Count == 3, label + ": all three nodes are committed together");
                Check(added.Zip(expected).All(p => p.First.Name == p.Second.Name && p.First.Type == p.Second.Type && p.First.Server == p.Second.Server && p.First.Port == p.Second.Port && JsonNode.DeepEquals(p.First.Options, p.Second.Options)), label + ": every node retains its name, protocol, address, port and credentials");
                var saved = new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!;
                Check(saved.Nodes.Count == before + 3 && added.All(n => saved.Nodes.Any(s => s.Id == n.Id && JsonNode.DeepEquals(s.Options, n.Options))) && saved.Rules.Count == 9233 && !window.Controller.Engine.IsRunning, label + ": encrypted persistence and rules are preserved without connecting");
            }
            async Task TextBatch(string payload)
            {
                var editor = window.CreateImportEditor(); var shown = editor.ShowDialog(window); editor.Field("text").Text = payload;
                await editor.SubmitAsync(); Check(editor.Applied && editor.ErrorText is null, "Batch editor submits successfully"); await shown;
            }
            await Batch("Multiline mixed-protocol URLs", () => TextBatch(batchLinks), batch);
            await Batch("Base64 batch", () => TextBatch(Convert.ToBase64String(Encoding.UTF8.GetBytes(batchLinks))), batch);
            const string shadowBatch = "[{\"type\":\"Shadowsocks\",\"host\":\"jp.batch.example\",\"port\":443,\"remarks\":\"Batch Japan\",\"method\":\"aes-128-gcm\",\"password\":\"synthetic-one\",\"udp\":true},{\"type\":\"Trojan\",\"host\":\"sg.batch.example\",\"port\":8443,\"remarks\":\"Batch Singapore\",\"password\":\"synthetic-two\",\"tls\":true},{\"type\":\"VLESS\",\"host\":\"us.batch.example\",\"port\":443,\"remarks\":\"Batch USA\",\"password\":\"29c295b8-5724-4c41-8cf1-ceae43986a58\",\"tls\":true}]";
            await Batch("Shadowrocket JSON array", () => TextBatch(shadowBatch), NodeImport.Parse(shadowBatch));
            await Batch("JSON batch file", () => window.ImportPayloadAsync(Encoding.UTF8.GetBytes(NodeExport.Json(batch, [])), false), batch);
            await Batch("YAML batch file", () => window.ImportPayloadAsync(Encoding.UTF8.GetBytes(NodeExport.Yaml(batch, [])), false), batch);
            await Batch("UTF-16 batch file", () => window.ImportPayloadAsync([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(NodeExport.Json(batch, []))], false), batch);
            await Batch("QR batch image", () => window.ImportPayloadAsync(NodeExport.QrPng(batchLinks), true), batch);
            var atomicState = window.Controller.State;
            var vaultFile = Path.Combine(UserData.DirectoryPath, "settings.dat"); var atomicBytes = File.ReadAllBytes(vaultFile);
            var badBatch = window.CreateImportEditor(); var badShown = badBatch.ShowDialog(window);
            badBatch.Field("text").Text = batchLinks.Split('\n')[0] + "\ntrojan://synthetic@invalid.batch.example:0#Broken";
            await badBatch.SubmitAsync();
            Check(!badBatch.Applied && badBatch.ErrorText is not null && badBatch.IsVisible && ReferenceEquals(atomicState, window.Controller.State), "Mixed valid and invalid batch stays open and commits no partial nodes");
            Check(atomicBytes.SequenceEqual(File.ReadAllBytes(vaultFile)) && !window.Controller.Engine.IsRunning, "Rejected batch leaves the encrypted profile byte-for-byte unchanged and disconnected"); badBatch.Close(); await badShown;
            await using (var fixture = new SubscriptionFixture("STATUS=剩余流量：183 GB\r\nREMARKS=Example VPN\r\n" + batchLinks))
            {
                var before = window.Controller.State.Subscriptions.Count;
                var editor = window.CreateImportEditor(); var shown = editor.ShowDialog(window); editor.Field("text").Text = fixture.Url; await editor.SubmitAsync();
                Check(editor.Applied && window.Controller.State.Subscriptions.Count == before + 1, "Subscription URL downloads a localhost fixture and creates one subscription"); await shown;
                var importedSubscription = window.Controller.State.Subscriptions.Last();
                Check(importedSubscription.Name == "Example VPN", "A bare subscription URL uses the feed REMARKS name instead of the download host");
                Check(window.Controller.State.Nodes.Count(n => n.SubscriptionId == importedSubscription.Id) == 3, "Subscription URL imports every node in the batch under one subscription");
                Check(window.Controller.State.Subscriptions.Last().Usage?.Total == 1000000, "Subscription usage headers survive import");
                var current = window.Controller.State;
                var duplicate = window.CreateImportEditor(); var duplicateShown = duplicate.ShowDialog(window); duplicate.Field("text").Text = fixture.Url; await duplicate.SubmitAsync();
                Check(duplicate.Applied && duplicate.ErrorText is null && window.Controller.State.Subscriptions.Count == current.Subscriptions.Count && window.Controller.State.Nodes.Select(n => n.Id).ToHashSet().SetEquals(current.Nodes.Select(n => n.Id)), "Repeated subscription URL refreshes the existing subscription and preserves node identities"); await duplicateShown;
                await window.ImportPayloadAsync(NodeExport.QrPng(fixture.Url), true);
                Check(window.Controller.State.Subscriptions.Count == current.Subscriptions.Count && window.Controller.State.Nodes.Count == current.Nodes.Count && window.Controller.State.SelectedNode == current.SelectedNode, "Repeated QR subscription refreshes atomically without duplicating nodes or changing selection");
                var subscriptionJson = new JsonObject { ["type"] = "Subscribe", ["host"] = fixture.Url, ["url"] = "", ["remarks"] = "", ["title"] = "Example VPN", ["port"] = "0" }.ToJsonString();
                var subLink = "sub://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(fixture.Url)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "#Example%20VPN";
                foreach (var payload in new[] { subscriptionJson, "[" + subscriptionJson + "]", subLink, "shadowrocket://add/" + subLink, "clash://install-config?url=" + Uri.EscapeDataString(fixture.Url) })
                {
                    await TextBatch(payload);
                    Check(window.Controller.State.Subscriptions.Count == current.Subscriptions.Count && window.Controller.State.Nodes.Select(n => n.Id).ToHashSet().SetEquals(current.Nodes.Select(n => n.Id)), "Subscription JSON or browser command downloads a metadata-prefixed feed through the actual editor without duplicates");
                    Check(window.Controller.State.Nodes.Where(n => n.SubscriptionId == importedSubscription.Id).Zip(batch).All(p => JsonNode.DeepEquals(p.First.Options, p.Second.Options)), "Every mixed-protocol credential survives the subscription adapter");
                }
                await window.ImportPayloadAsync(Encoding.UTF8.GetBytes(subLink), false);
                Check(window.Controller.State.Subscriptions.Count == current.Subscriptions.Count && window.Controller.State.Nodes.Select(n => n.Id).ToHashSet().SetEquals(current.Nodes.Select(n => n.Id)), "A .url file containing sub:// refreshes through the file payload path");
                await window.ImportPayloadAsync(NodeExport.QrPng("shadowrocket://add/" + subLink), true);
                Check(window.Controller.State.Subscriptions.Count == current.Subscriptions.Count && window.Controller.State.Nodes.Select(n => n.Id).ToHashSet().SetEquals(current.Nodes.Select(n => n.Id)) && window.Controller.State.SelectedNode == current.SelectedNode, "A nested Shadowrocket subscription QR retains all nodes and the current selection");
                var siteCommand = "shadowrocket://add/sub://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(fixture.Url)) + "?remarks=Website%20VPN";
                var oldDomain = window.Controller.State with { Subscriptions = window.Controller.State.Subscriptions.Select(s => s.Id == importedSubscription.Id ? s with { Name = new Uri(s.Url).Host } : s).ToList() };
                await window.Controller.ApplyAsync(oldDomain);
                var beforeBrowserImport = window.Controller.State;
                window.ReceiveImportLink(siteCommand); Layout(window);
                var browserEditor = window.OpenDialogs.OfType<EditorDialog>().Single();
                Check(browserEditor.Field("url").Text == fixture.Url && browserEditor.Field("name").Text == "Website VPN" && ReferenceEquals(beforeBrowserImport, window.Controller.State), "The website Shadowrocket command opens a populated editor without committing or connecting");
                await browserEditor.SubmitAsync();
                Check(browserEditor.Applied && browserEditor.ErrorText is null && window.Controller.State.Subscriptions.Single(s => s.Id == importedSubscription.Id).Name == "Website VPN", "Submitting the website command repairs a legacy domain name with the link's title");
                Check(window.Controller.State.Nodes.Select(n => n.Id).ToHashSet().SetEquals(current.Nodes.Select(n => n.Id)) && window.Controller.State.SelectedNode == current.SelectedNode, "Browser import keeps matching node identities and selection");
                await window.Controller.ApplyAsync(window.Controller.State with { Subscriptions = window.Controller.State.Subscriptions.Select(s => s.Id == importedSubscription.Id ? s with { Name = new Uri(s.Url).Host } : s).ToList() });
                var jsonName = subscriptionJson.Replace("Example VPN", "JSON title"); await TextBatch(jsonName);
                Check(window.Controller.State.Subscriptions.Single(s => s.Id == importedSubscription.Id).Name == "JSON title", "JSON title upgrades an existing auto-generated domain name");
                await window.Controller.ApplyAsync(window.Controller.State with { Subscriptions = window.Controller.State.Subscriptions.Select(s => s.Id == importedSubscription.Id ? s with { Name = "My manual title" } : s).ToList() });
                window.ReceiveImportLink(siteCommand); Layout(window); browserEditor = window.OpenDialogs.OfType<EditorDialog>().Single();
                await browserEditor.SubmitAsync();
                Check(browserEditor.Applied && window.Controller.State.Subscriptions.Single(s => s.Id == importedSubscription.Id).Name == "My manual title", "A repeated browser command preserves a manually customized subscription name");
                Check(UiText.Get("Invalid encoded subscription URL.") == "订阅链接编码无效，请重新复制完整的订阅链接。", "Invalid encoded subscription errors have an actionable Chinese translation");
            }
            var original = window.Controller.State;
            var rejected = window.CreateImportEditor(); var rejection = rejected.ShowDialog(window); rejected.Field("text").Text = "{invalid json"; await rejected.SubmitAsync();
            Check(!rejected.Applied && rejected.ErrorText is not null && rejected.IsVisible && !window.Controller.Busy, "Invalid import reports a local error without trapping the modal or app");
            Check(rejected.Notification.Showing && rejected.Notification.IsError && rejected.Notification.Message == UiText.Get(rejected.ErrorText!), "Import failure appears in the visible modal notification bar");
            Check(ReferenceEquals(original, window.Controller.State), "Rejected import changes neither nodes nor rules"); rejected.Close(); await rejection;
            var reviewEditor = window.CreateImportEditor(); var reviewShown = reviewEditor.ShowDialog(window);
            reviewEditor.Field("text").Text = "{\"proxies\":[" + node.Options.ToJsonString() + "],\"proxy-providers\":{}}";
            var reviewSubmit = reviewEditor.SubmitAsync(); await Until(() => reviewEditor.OwnedDialogs.Any());
            reviewEditor.OwnedDialogs.Single().Close(false); await reviewSubmit;
            Check(!reviewEditor.Applied && reviewEditor.ErrorText is null && reviewEditor.IsVisible && ReferenceEquals(original, window.Controller.State), "Cancelling import review returns to the editor without reporting success or altering data"); reviewEditor.Close(); await reviewShown;
            var directory = window.CreateProviderDirectoryEditor(); var directoryShown = directory.ShowDialog(window); Layout(directory);
            var directoryField = directory.Field("directory"); Check(directoryField.VerticalContentAlignment == VerticalAlignment.Top && directoryField.CaretIndex == 0, "Airport directory editor starts at the first line");
            directoryField.Text = "{\"version\":1,\"providers\":[{\"name\":\"Provider fixture\",\"url\":\"https://provider.example/\"}]}"; await directory.SubmitAsync();
            Check(directory.Applied && window.Controller.State.Providers.Count == 1, "Airport directory modal imports a public website"); await directoryShown;
            var loaded = new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!;
            Check(loaded.Nodes.Count == window.Controller.State.Nodes.Count && loaded.Rules.Count == 9233 && loaded.Subscriptions.Count == 1, "Import saves all profile sections to the encrypted vault");
            window.ShowPage("Rules"); window.SearchControl.Text = "fixture42.example"; Layout(window);
            var filteredRule = window.PageView.GetVisualDescendants().OfType<Button>().Single(b => b.Tag is "42");
            var rulePoint = filteredRule.TranslatePoint(new Point(filteredRule.Bounds.Width / 2, filteredRule.Bounds.Height / 2), window)!.Value;
            window.MouseMove(rulePoint); window.MouseDown(rulePoint, MouseButton.Left); window.MouseUp(rulePoint, MouseButton.Left); Layout(window);
            Check(window.ActiveRuleEditor is null && filteredRule.Classes.Contains("selected-card"), "Single rule click selects the removal target without opening an editor");
            Click(window, filteredRule);
            var ruleEditor = window.ActiveRuleEditor!;
            Check(ruleEditor is { IsVisible: true } && ruleEditor.ValueEditor.Text == "fixture42.example" && ruleEditor.ActionChoice.SelectedItem?.ToString() == "DIRECT", "Double-clicking a filtered rule opens that exact source rule for editing");
            Check(ruleEditor.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == UiText.Get("Action")) && UiText.Get("Action") == "规则动作", "The rule action label is translated in the Chinese editor");
            var beforeRuleEdit = window.Controller.State;
            ruleEditor.ValueEditor.Text = "bad,value"; await ruleEditor.SubmitAsync();
            Check(!ruleEditor.Applied && ruleEditor.ErrorText is not null && ruleEditor.IsVisible && ReferenceEquals(beforeRuleEdit, window.Controller.State), "Invalid rule editing stays local and changes neither profile nor rule order");
            ruleEditor.ValueEditor.Text = "edited.example"; ruleEditor.ActionChoice.SelectedItem = "REJECT"; await ruleEditor.SubmitAsync(); Layout(window);
            var editedRules = window.Controller.State.Rules;
            Check(ruleEditor.Applied && !ruleEditor.IsVisible && editedRules.Count == 9233 && editedRules[42] == new RoutingRule("DOMAIN", "edited.example", "REJECT") && editedRules.Where((_, i) => i != 42).SequenceEqual(beforeRuleEdit.Rules.Where((_, i) => i != 42)), "Saving a rule replaces it in place and preserves every other ordered rule");
            Check(new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.Rules[42] == editedRules[42] && !window.Controller.Engine.IsRunning, "Rule changes persist to the encrypted profile without connecting");
            var ipEditor = window.CreateRuleEditor(42); var ipShown = ipEditor.ShowDialog(window);
            ipEditor.TypeChoice.SelectedItem = "IP-CIDR"; ipEditor.ValueEditor.Text = "192.0.2.0/24"; ipEditor.NoResolve.IsChecked = true; await ipEditor.SubmitAsync(); await ipShown;
            var reopenedRule = window.CreateRuleEditor(42); var reopenedShown = reopenedRule.ShowDialog(window);
            Check(reopenedRule.TypeChoice.SelectedItem?.ToString() == "IP-CIDR" && reopenedRule.NoResolve.IsEnabled && reopenedRule.NoResolve.IsChecked == true && reopenedRule.ValueEditor.Text == "192.0.2.0/24", "Reopening an IP rule retains its no-resolve option and network");
            reopenedRule.TypeChoice.SelectedItem = "MATCH"; await reopenedRule.SubmitAsync();
            Check(!reopenedRule.Applied && reopenedRule.ErrorText is not null && window.Controller.State.Rules[42].Type == "IP-CIDR", "Editing an interior rule into MATCH cannot silently reorder or truncate the ruleset");
            reopenedRule.Close(); await reopenedShown;
            var staleEditor = window.CreateRuleEditor(42); var staleShown = staleEditor.ShowDialog(window);
            var concurrentRules = window.Controller.State.Rules.ToList(); concurrentRules[42] = new("DOMAIN", "concurrent.example", "DIRECT"); await window.Controller.ApplyAsync(window.Controller.State with { Rules = concurrentRules });
            staleEditor.ValueEditor.Text = "stale.example"; await staleEditor.SubmitAsync();
            Check(!staleEditor.Applied && staleEditor.ErrorText is not null && window.Controller.State.Rules[42].Value == "concurrent.example", "An old editor cannot overwrite a rule changed by another operation");
            staleEditor.Close(); await staleShown;
            window.ShowPage("Nodes"); Layout(window); var group = window.SubscriptionView.Groups.First(g => g.Key == "manual");
            var row = group.NodeRows.GetVisualDescendants().OfType<Button>().First(b => b.Tag is string);
            var rowContent = row.Content;
            var pending = new TaskCompletionSource<int>(); var testing = window.TestLatencyAsync((_, token) => pending.Task.WaitAsync(token));
            Check(window.TestingLatency && !window.Controller.Busy && window.ConnectAction.IsEnabled, "Latency testing keeps the controller and connection action available");
            window.ShowPage("Rules"); Layout(window); var rulesView = window.PageView; pending.SetResult(35); await testing;
            Check(window.CurrentPage == "Rules" && ReferenceEquals(rulesView, window.PageView), "Completing latency tests on another page does not replace that page");
            window.ShowPage("Nodes"); Layout(window);
            Check(ReferenceEquals(rowContent, row.Content), "Latency badges update in place without rebuilding node content");
            var latencyNodes = window.Controller.State.Nodes.ToArray();
            var expectedLatency = latencyNodes.Select((n, i) => (n.Id, Value: new[] { 35, 220, 450, -1 }[i % 4])).ToDictionary(p => p.Id, p => p.Value);
            await window.TestLatencyAsync((n, _) => expectedLatency[n.Id] < 0 ? Task.FromException<int>(new TimeoutException()) : Task.FromResult(expectedLatency[n.Id]));
            foreach (var nodeRow in window.SubscriptionView.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is string tag && Guid.TryParse(tag, out _)))
            {
                var id = Guid.Parse((string)nodeRow.Tag!); var value = expectedLatency[id];
                var badge = ((Grid)nodeRow.Content!).Children.OfType<TextBlock>().Single(t=>t.Tag is "row-badge");
                var expectedBrush = value == 35 ? LatencyTone.Good : value == 220 ? LatencyTone.Moderate : LatencyTone.Poor;
                Check(badge.Text == (value < 0 ? UiText.Get("Timeout") : value + " ms") && badge.Foreground == expectedBrush,
                    "Completed offline latency workflow updates each result and its green, orange or red emphasis in place");
            }
            Check(!window.Controller.Engine.IsRunning && window.ConnectAction.IsEnabled && ReferenceEquals(rowContent, row.Content),
                "Latency result colors preserve row identity and leave the disconnected connection action available");
            window.Hide(); window.RestoreFromBackground(); Layout(window);
            Check(window.IsVisible && window.ShowInTaskbar && window.Controller.State.Rules.Count == 9233 && window.Controller.State.Subscriptions.Count == 1, "Hide and restore preserve loaded rules, subscriptions and taskbar intent");
            window.WindowState = WindowState.Maximized; window.WindowState = WindowState.Minimized; window.RestoreFromBackground(); Layout(window);
            Check(window.WindowState == WindowState.Maximized && window.ShowInTaskbar, "Restore from minimization returns to the previous maximized state");
            window.ShowPage("Providers"); Layout(window);
            var provider = window.Controller.State.Providers[0];
            var providerRows = window.PageView.GetVisualDescendants().OfType<MovingRows>().Single();
            var providerCard = providerRows.For(provider.Id.ToString());
            var providerLink = providerCard.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock t && t.Text == UiText.Get("Official website"));
            var sourceRequests = new List<string>(); var providerSelections = 0;
            providerRows.SourceRequested += sourceRequests.Add; providerRows.Chosen += _ => providerSelections++;
            Click(window, providerLink);
            Check(sourceRequests.SequenceEqual(new[] { provider.Url }) && providerSelections == 0 && window.ProviderDetailsView is null && !providerCard.Classes.Contains("selected-card"), "Airport URL mouse click opens only its website without selecting the parent card or showing details");
            foreach (var key in new[] { Key.Enter, Key.Space })
            {
                var physical = key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Space;
                providerLink.Focus(); window.KeyPress(key, RawInputModifiers.None, physical, null); window.KeyRelease(key, RawInputModifiers.None, physical, null); Layout(window);
                Check(sourceRequests.Count == (key == Key.Enter ? 2 : 3) && sourceRequests.All(url => url == provider.Url) && providerSelections == 0 && window.ProviderDetailsView is null, "Airport URL " + key + " activation never bubbles into parent selection or details");
            }
            Click(window, providerCard);
            Check(providerCard.Classes.Contains("selected-card") && providerSelections == 1 && window.ProviderDetailsView is null, "Single airport-card click selects it for Remove without opening a details page");
            var providerContent = providerCard.Content;
            Click(window, window.PageHeading);
            Check(!providerCard.Classes.Contains("selected-card") && !window.GetVisualDescendants().OfType<Button>().Single(b => b.Tag is "remove-selection").IsEffectivelyEnabled && window.ProviderDetailsView is null, "Blank content-heading click clears airport selection and disables Remove without opening details");
            Click(window, providerCard);
            Check(ReferenceEquals(providerContent, providerCard.Content) && providerCard is ProviderCardButton && providerCard.Background == Brushes.Transparent && !providerRows.Backplate.IsVisible, "Airport reselection retains its content and action target while gray emphasis stays pointer-driven");
            Click(window, window.PageHeading); Click(window, providerCard); Click(window, providerCard);
            await Until(() => window.ProviderDetailsView is not null);
            var details = window.ProviderDetailsView!; Layout(window);
            Check(details.IsVisible && details.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == provider.Name) && !window.OwnedWindows.Any() && ReferenceEquals(window.PageView, details), "Double-clicking an airport card opens its own details inside the main content area");
            await window.ReturnProviderListAsync(); await Until(() => window.ProviderDetailsView is null);
            Check(window.PageView.GetVisualDescendants().OfType<Button>().Any(b => b.Classes.Contains("selected-card")), "Airport card retains its logical action target after details close");
            Check(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Content is TextBlock t && t.Text == UiText.Get("Details")), "Airport page has no redundant Details button");
            VisualChecks.Run(window, output);
            Check(window.Controller.State.Rules.Count == 9233 && window.Controller.State.Subscriptions.Count == 1, "Repeated rendering and navigation preserve the imported profile");
            window.ShowPage("Settings"); Layout(window); var settingsView = window.PageView; window.SettingsSections!.SelectedIndex = 4; Layout(window);
            window.ShowPage("Overview"); window.ShowPage("Settings"); Layout(window);
            Check(ReferenceEquals(settingsView, window.PageView) && window.SettingsSections.SelectedIndex == 4, "Returning to unchanged settings preserves the form and selected section without rebuilding controls");
            await window.Controller.ApplyAsync(window.Controller.State with { MixedPort = 7898 }); window.ShowPage("Overview"); window.ShowPage("Settings"); Layout(window);
            Check(!ReferenceEquals(settingsView, window.PageView), "Changed settings invalidate the cached form before it can overwrite new values");
            Check(!window.Controller.Engine.IsRunning, "Import and navigation workflows never start an engine or connect a node");
            await window.Controller.DisposeAsync(); window.Close();
            var activated = false; var pipe = "TIZI-workflow-" + Guid.NewGuid().ToString("N");
            using (var instance = new SingleInstance(() => activated = true, pipe)) { Check(await Task.Run(() => SingleInstance.ActivateExisting(pipe)), "Second-launch activation message reaches an isolated named pipe"); await Until(() => activated); }
            string? received = null; pipe = "TIZI-import-workflow-" + Guid.NewGuid().ToString("N");
            const string incoming = "clash://install-config?url=https%3A%2F%2Ffixture.example%2Ffeed%3Fclash%3D3%26extend%3D1";
            using (var instance = new SingleInstance(() => { }, pipe, link => received = link))
            { Check(await Task.Run(() => SingleInstance.ActivateExisting(pipe, incoming)), "Import command reaches the existing current-user process"); await Until(() => received is not null); Check(received == incoming, "Single-instance import preserves escaped parameters without starting another client"); }
            Check(TrayMenuPlacement.PositionFor(new PixelPoint(1900, 1040), new PixelSize(180, 120), new PixelRect(0, 0, 1920, 1040)) == new PixelPoint(1740, 920), "Tray menu near taskbar is clamped above the cursor and inside the work area");
            Check(TrayMenuPlacement.PositionFor(new PixelPoint(-20, 10), new PixelSize(220, 200), new PixelRect(-1920, 0, 1920, 1040)) == new PixelPoint(-220, 10), "Tray menu supports a negative-coordinate monitor without jumping to the primary screen");
            var graph = new TrafficChart(); var chartWindow = new Window { Width = 850, Height = 360, Background = Skin.ChromeBrush, Content = graph }; chartWindow.Show(); Layout(chartWindow);
            var start = DateTimeOffset.Now.AddSeconds(-70);
            for (var i = 0; i <= 70; i++) { graph.Add(2048 + i, 4096 + i * 20, start.AddSeconds(i)); if (i % 5 == 0) graph.AddLatency(i == 65 ? null : 50 + i, start.AddSeconds(i)); }
            Check(graph.SampleCount <= 61 && graph.LatencyCount <= 13 && graph.SpeedMaximum >= 5496 && graph.LatencyMaximum >= 120, "Combined graph retains only the last minute and separate units/scales");
            var hover = graph.HoverAt(graph.Plot.Right); Check(hover.Contains("MB/S") && hover.Contains("ms") && hover.Contains(UiText.Get("Download")) && hover.Contains(UiText.Get("Upload")), "Hover inspection labels all three series with MB/S and ms");
            graph.SetSeriesVisible(1, false); Check(!graph.SeriesVisible(1) && graph.SeriesVisible(0) && graph.SeriesVisible(2), "Each chart series toggles independently"); graph.SetSeriesVisible(1, true);
            if (chartWindow.Content is DialogSurface chartSurface) { using var clock = new UiAnimationClock(chartSurface, chartSurface.Scale, chartSurface.Offset); chartSurface.Reveal(); clock.Pulse(0); clock.Pulse(200); Layout(chartWindow); }
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout(chartWindow); using (var image = chartWindow.CaptureRenderedFrame()) image?.Save(Path.Combine(output, "combined-chart.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); graph.Reset(); Check(graph.SampleCount == 0 && graph.LatencyCount == 0 && !graph.HasData && graph.HoverAt(graph.Plot.Center.X) == "", "Disconnect clears samples and disables detailed chart values"); chartWindow.Close();
            var monitor = new NetworkMonitor(); monitor.Update(null, null, 0, false); monitor.Delay("20 ms", "Offline probe");
            Check(!monitor.LegendVisible && !monitor.Chart.HasData, "Disconnected monitor hides legends, axes and offline probe samples");
            monitor.Update(1_000_000, 2_500_000, 3, true);
            Check(monitor.LegendVisible && monitor.Chart.HasData && UiFormat.Speed(2_500_000) == "2.50 MB/S", "Connected monitor reveals data and uses decimal MB/S consistently");
            monitor.Delay("45 ms", "Default metric sample");
            Check(monitor.LatencyValue == "45 ms" && !monitor.LatencyMonitoring && !monitor.Chart.SeriesVisible(2) && monitor.Chart.LatencyCount == 0, "Default latency values remain visible while the optional latency curve stays off");
            var changes = 0; monitor.LatencyMonitoringChanged += _ => changes++;
            monitor.SetLatencyMonitoring(true); Check(monitor.LatencyValue == "45 ms", "Starting curve collection preserves the current latency value");
            monitor.Delay("80 ms", "Curve sample"); monitor.Delay("Timeout", "Curve timeout");
            Check(monitor.LatencyMonitoring && monitor.Chart.LatencyCount == 2 && monitor.Chart.SeriesVisible(2), "Opting into the latency curve records successful and missing samples separately");
            monitor.SetLatencyMonitoring(false); Check(monitor.LatencyValue == "Timeout", "Stopping curve collection preserves the current latency value"); monitor.Delay("90 ms", "Stopped sample");
            Check(!monitor.LatencyMonitoring && monitor.Chart.LatencyCount == 0 && changes == 2, "Stopping collection clears old latency samples and prevents later curve points");
            monitor.Update(null, null, 0, false); Check(!monitor.LegendVisible && !monitor.Chart.HasData, "Disconnect restores the quiet chart state");
            Check(monitor.LatencyValue == "—" && monitor.LatencyBrush == Skin.SecondaryBrush && monitor.DurationValue == "—", "Disconnect removes the last latency emphasis and clears this session's duration");
            monitor.Delay("12 ms", "Late sample");
            Check(monitor.LatencyValue == "—" && monitor.LatencyBrush == Skin.SecondaryBrush, "A late latency sample cannot color or repopulate the disconnected overview");
            monitor.Update(0, 0, 0, true, duration: TimeSpan.FromSeconds(3661));
            Check(monitor.DurationValue == "01:01:01" && !monitor.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == UiText.Get("Active connections")), "Overview replaces active-connection counts with the current session duration");
            monitor.Delay("12 ms", "Success"); monitor.Update(null, null, 0, false); monitor.Update(0, 0, 0, true, duration: TimeSpan.FromSeconds(2));
            Check(monitor.DurationValue == "00:00:02" && monitor.LatencyValue == "—" && monitor.LatencyBrush == Skin.SecondaryBrush, "Reconnecting starts a fresh duration without retaining the old green latency");
            monitor.Update(0, 0, 0, true, duration: TimeSpan.FromHours(100)); Check(monitor.DurationValue == "100:00:00", "Session duration continues across days without wrapping back to zero");
            var headerSubscription = new Subscription { Name = "Header fixture", Url = "https://one.example/sub" };
            var foreign = new Subscription { Name = "Other fixture", Url = "https://two.example/sub" };
            var same = new[] { node with { Id = Guid.NewGuid(), Name = "Japan fixture", SubscriptionId = headerSubscription.Id }, node with { Id = Guid.NewGuid(), Name = "Singapore fixture", SubscriptionId = headerSubscription.Id } };
            var richOptions = (JsonObject)same[0].Options.DeepClone(); richOptions["plugin"] = "v2ray-plugin"; richOptions["plugin-opts"] = new JsonObject { ["mode"] = "websocket", ["host"] = "cdn.fixture.example", ["tls"] = true }; same[0] = same[0] with { Options = richOptions };
            var outsiders = new[] { node with { Id = Guid.NewGuid(), SubscriptionId = foreign.Id }, node with { Id = Guid.NewGuid() } };
            var headerState = state with { Nodes = [..same, ..outsiders], Subscriptions = [headerSubscription, foreign], SelectedNode = same[0].Id };
            new Vault(UserData.DirectoryPath).Write("settings.dat", headerState);
            var headerWindow = new MainWindow(); headerWindow.Show(); await headerWindow.Controller.InitializeAsync(); Layout(headerWindow);
            Check(headerWindow.NodeControl.Candidates.Count == 2 && headerWindow.NodeControl.Candidates.All(n => n.SubscriptionId == headerSubscription.Id), "Header candidate list excludes every other subscription and manual node");
            headerWindow.ShowPage("Nodes"); Layout(headerWindow); var selectedHeader = headerWindow.SubscriptionView.Groups.Single(g => g.Key == headerSubscription.Id.ToString()).Header;
            Button NodeAction(string tag) => headerWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Tag is string value && value == tag);
            Check(!headerWindow.GetVisualDescendants().OfType<Button>().Any(b => b.Tag is "edit-node") && !NodeAction("remove-selection").IsEffectivelyVisible && NodeAction("remove-selection").Content is TextBlock initialRemove && initialRemove.Text == UiText.Get("Remove node"), "Normal node selection keeps the toolbar Remove hidden and retains item actions in the right-click menu");
            var initialGroup = headerWindow.SubscriptionView.Groups.Single(g => g.Key == headerSubscription.Id.ToString());
            var initialNodeRow = initialGroup.NodeRows.For(same[0].Id.ToString());
            RightClick(headerWindow, initialNodeRow); var nodeMenu = initialNodeRow.ContextMenu!;
            Check(nodeMenu.IsOpen && !initialGroup.ContextMenu!.IsOpen && headerWindow.Controller.State.SelectedNode == same[0].Id && !headerWindow.OwnedWindows.Any(), "Right-click opens only the targeted node menu without opening its subscription menu, switching a node or showing an editor");
            SettleMenu(nodeMenu);
            var nodeEditAction = nodeMenu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == UiText.Get("Edit node"));
            var menuClicks = 0; nodeEditAction.Click += (_, _) => menuClicks++;
            Click(TopLevel.GetTopLevel(nodeEditAction)!, nodeEditAction);
            var retractingMenu = nodeMenu.GetVisualDescendants().OfType<PopupRevealSurface>().Single();
            Check(menuClicks == 1 && nodeMenu.IsOpen && !retractingMenu.IsHitTestVisible && !headerWindow.OwnedWindows.Any(), "A context action starts retracting before dispatch and cannot receive a second click during retraction");
            await Until(() => !nodeMenu.IsOpen && headerWindow.OpenDialogs.OfType<NodeEditor>().Any()); Layout(headerWindow);
            var nodeEditor = headerWindow.OpenDialogs.OfType<NodeEditor>().Single();
            Check(nodeEditor.IsVisible && nodeEditor.NameField.Text == same[0].Name, "The node context Edit opens the targeted node after its menu has retracted");
            var beforeNodeEdit = headerWindow.Controller.State;
            var advancedBefore = nodeEditor.Advanced.Text;
            nodeEditor.Advanced.Text = "{broken"; await nodeEditor.SubmitAsync();
            Check(!nodeEditor.Applied && nodeEditor.Notification.Showing && nodeEditor.Notification.IsError && ReferenceEquals(beforeNodeEdit, headerWindow.Controller.State), "Invalid node editing preserves the profile and reports its error in the editor notification");
            nodeEditor.NameField.Text = "Edited Japan fixture"; nodeEditor.ServerField.Text = "edited.jp.example"; nodeEditor.PortField.Value = 8443; nodeEditor.Advanced.Text = advancedBefore; await nodeEditor.SubmitAsync(); Layout(headerWindow);
            var editedNode = headerWindow.Controller.State.Nodes.Single(n => n.Id == same[0].Id);
            Check(nodeEditor.Applied && !nodeEditor.IsVisible && editedNode.Name == "Edited Japan fixture" && editedNode.Server == "edited.jp.example" && editedNode.Port == 8443 && editedNode.SubscriptionId == same[0].SubscriptionId && editedNode.CountryCode == same[0].CountryCode && editedNode.Options["password"]!.ToString() == same[0].Options["password"]!.ToString(), "Saving Edit changes the node parameters while preserving its identity, subscription, region and credentials");
            Check(editedNode.Options["plugin"]!.ToString() == "v2ray-plugin" && JsonNode.DeepEquals(editedNode.Options["plugin-opts"], richOptions["plugin-opts"]), "Structured node editing preserves nested advanced parameters that have no dedicated form field");
            Check(headerWindow.Controller.State.Nodes.Skip(1).SequenceEqual(beforeNodeEdit.Nodes.Skip(1)) && headerWindow.Controller.State.Rules.SequenceEqual(beforeNodeEdit.Rules) && new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.Nodes.Single(n => n.Id == editedNode.Id).Server == editedNode.Server && !headerWindow.Controller.Engine.IsRunning, "Node editing persists the intended change while preserving all other nodes and rules without connecting");
            same[0] = editedNode; headerState = headerWindow.Controller.State;
            initialNodeRow = initialGroup.NodeRows.For(same[0].Id.ToString());
            RightClick(headerWindow, initialNodeRow); nodeMenu = initialNodeRow.ContextMenu!; SettleMenu(nodeMenu);
            var menuRoot = TopLevel.GetTopLevel(nodeMenu)!;
            Check(nodeMenu.IsKeyboardFocusWithin, "A reopened context menu receives keyboard focus for navigation and Escape");
            menuRoot.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); menuRoot.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Until(() => !nodeMenu.IsOpen);
            Check(!headerWindow.OwnedWindows.Any() && ReferenceEquals(headerState, headerWindow.Controller.State), "Escape retracts a reopened node context menu without editing, switching or saving");
            RightClick(headerWindow, initialNodeRow); nodeMenu = initialNodeRow.ContextMenu!; SettleMenu(nodeMenu);
            Click(headerWindow, headerWindow.PageHeading); await Until(() => !nodeMenu.IsOpen);
            Check(!headerWindow.OwnedWindows.Any() && ReferenceEquals(headerState, headerWindow.Controller.State), "Clicking outside retracts the repeated context menu without dispatching a stale action");
            Click(headerWindow, selectedHeader);
            Check(selectedHeader.Classes.Contains("selected-card") && headerWindow.SubscriptionView.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("selected-card")) == 1 && headerWindow.SubscriptionView.Groups.All(g => !g.NodeRows.Backplate.IsVisible), "Selecting a subscription keeps exactly one highlighted object and clears every node selection backplate");
            Click(headerWindow, headerWindow.PageHeading);
            Check(!initialGroup.IsSelected && !selectedHeader.Classes.Contains("selected-card") && !NodeAction("remove-selection").IsEffectivelyEnabled && headerWindow.Controller.State.SelectedNode == same[0].Id, "Blank content click clears subscription selection without changing the connected-node choice");
            Click(headerWindow, selectedHeader);
            Check(NodeAction("remove-selection").Content is TextBlock subscriptionRemove && subscriptionRemove.Text == UiText.Get("Remove subscription") && headerWindow.Controller.State.SelectedNode == same[0].Id && !headerWindow.SubscriptionView.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Tag is "current-node") && headerWindow.SubscriptionView.GetVisualDescendants().OfType<Canvas>().SelectMany(c => c.Children.OfType<Border>()).Count(b => b.Background == MovingRows.CurrentNodeBrush) == 1, "Subscription selection retargets Remove while the current node retains its blue background without a checkmark");
            var nodeGroup = headerWindow.SubscriptionView.Groups.Single(g => g.Key == headerSubscription.Id.ToString());
            using (var nodeRevealClock = new UiAnimationClock(nodeGroup.Reveal)) { nodeRevealClock.Pulse(0); nodeRevealClock.Pulse(300); Layout(headerWindow); }
            Click(headerWindow, nodeGroup.NodeRows.For(same[1].Id.ToString())); Layout(headerWindow);
            Check(headerWindow.Controller.State.SelectedNode == same[0].Id && new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.SelectedNode == same[0].Id && !headerWindow.Controller.Busy && !headerWindow.OwnedWindows.Any(), "Single node click selects an operation target without switching the current node, saving selection, or opening an editor");
            Check(headerWindow.SubscriptionView.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("selected-card")) == 1 && !selectedHeader.Classes.Contains("selected-card") && !nodeGroup.IsSelected && NodeAction("remove-selection").Content is TextBlock nodeRemove && nodeRemove.Text == UiText.Get("Remove node"), "Choosing a node clears the whole subscription highlight and targets the visible Remove action");
            var beforeNodeRemoval = headerWindow.Controller.State;
            var removalRow=nodeGroup.NodeRows.For(same[1].Id.ToString()); RightClick(headerWindow,removalRow); SettleMenu(removalRow.ContextMenu!);
            removalRow.ContextMenu!.Items.OfType<MenuItem>().Single(i=>i.Header?.ToString()==UiText.Get("Remove node")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => !headerWindow.Controller.Busy && headerWindow.Controller.State.Nodes.Count == beforeNodeRemoval.Nodes.Count - 1); Layout(headerWindow);
            Check(headerWindow.Controller.State.SelectedNode == same[0].Id && headerWindow.Controller.State.Nodes.All(n => n.Id != same[1].Id) && headerWindow.Controller.State.Nodes.SequenceEqual(beforeNodeRemoval.Nodes.Where(n => n.Id != same[1].Id)) && headerWindow.Controller.State.Subscriptions.SequenceEqual(beforeNodeRemoval.Subscriptions) && headerWindow.Controller.State.Rules.SequenceEqual(beforeNodeRemoval.Rules), "Visible Remove deletes the singly selected node without first switching to it and preserves the current node, subscription and rules");
            await headerWindow.Controller.ApplyAsync(headerState); headerWindow.ShowPage("Nodes"); Layout(headerWindow);
            nodeGroup = headerWindow.SubscriptionView.Groups.Single(g => g.Key == headerSubscription.Id.ToString());
            Click(headerWindow, headerWindow.PageHeading); Click(headerWindow, nodeGroup.NodeRows.For(same[1].Id.ToString())); Click(headerWindow, nodeGroup.NodeRows.For(same[1].Id.ToString()));
            await Until(() => !headerWindow.Controller.Busy && headerWindow.Controller.State.SelectedNode == same[1].Id);
            Check(new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.SelectedNode == same[1].Id && !headerWindow.Controller.Engine.IsRunning, "Double node click persists the new current node without connecting a disconnected client");
            var firstNodeRow = nodeGroup.NodeRows.For(same[0].Id.ToString()); firstNodeRow.Focus();
            headerWindow.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); headerWindow.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); Layout(headerWindow);
            Check(headerWindow.Controller.State.SelectedNode == same[1].Id && firstNodeRow.Classes.Contains("selected-card"), "Space selects a node for operations without activating it");
            headerWindow.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); headerWindow.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Until(() => !headerWindow.Controller.Busy && headerWindow.Controller.State.SelectedNode == same[0].Id);
            Check(new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.SelectedNode == same[0].Id, "Enter activates the focused node as the keyboard equivalent of a double click");
            headerWindow.ShowPage("Settings"); Layout(headerWindow);
            Click(headerWindow, headerWindow.SettingsSections!.ButtonAt(3)); Layout(headerWindow);
            var restoreOption = headerWindow.PageView.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content is TextBlock t && t.Text == UiText.Get("Enable IPv6"));
            var savedRestore = restoreOption.IsChecked; var settingsOrigin = headerWindow.PageHeading.TranslatePoint(default, headerWindow)!.Value;
            restoreOption.IsChecked = !savedRestore; Layout(headerWindow);
            Check(headerWindow.SettingsDirty && !headerWindow.Notification.Showing && headerWindow.PageHeading.TranslatePoint(default, headerWindow)!.Value == settingsOrigin, "Settings edits keep their draft local without showing an early notification or moving the page");
            restoreOption.IsChecked = savedRestore; Layout(headerWindow);
            Check(!headerWindow.SettingsDirty && !headerWindow.Notification.Showing, "Restoring every original setting clears the dirty state and dismisses its notice");
            restoreOption.IsChecked = !savedRestore; headerWindow.ShowPage("Nodes"); headerWindow.ShowPage("Settings"); Layout(headerWindow);
            Check(headerWindow.SettingsDirty && restoreOption.IsChecked != savedRestore, "Unsaved settings survive page navigation");
            Click(headerWindow, headerWindow.SettingsSections!.ButtonAt(0)); Layout(headerWindow);
            var languageChoice = headerWindow.PageView.GetVisualDescendants().OfType<StableChoice>().Single(c => c.Items.Contains("English"));
            languageChoice.SelectedIndex = 1; Layout(headerWindow);
            Click(headerWindow, headerWindow.SettingsSections!.ButtonAt(3)); Layout(headerWindow);
            Check(headerWindow.SettingsDirty && restoreOption.IsChecked != savedRestore && headerWindow.PageView.GetVisualDescendants().OfType<CheckBox>().Contains(restoreOption), "Switching language translates the form while retaining the same pending controls");
            Click(headerWindow, headerWindow.SettingsSections!.ButtonAt(0)); Layout(headerWindow);
            languageChoice.SelectedIndex = 0; Layout(headerWindow);
            Click(headerWindow, headerWindow.SettingsSections!.ButtonAt(3)); Layout(headerWindow);
            var settingsSave = headerWindow.PageView.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock t && t.Text == UiText.Get("Save settings"));
            Click(headerWindow, settingsSave); await Until(() => !headerWindow.Controller.Busy); Layout(headerWindow);
            Check(!headerWindow.SettingsDirty && headerWindow.Controller.State.Transport.Ipv6 == (!savedRestore == true) && headerWindow.Notification.Message == UiText.Get("Settings saved") && !headerWindow.Notification.IsError, "Successful advanced settings save updates the encrypted profile and uses a positive top notification");
            Check(new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.Transport.Ipv6 == headerWindow.Controller.State.Transport.Ipv6, "Saved settings notification follows actual encrypted persistence");
            Click(headerWindow, headerWindow.SettingsSections!.ButtonAt(5)); Layout(headerWindow);
            var delayField = headerWindow.PageView.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == headerWindow.Controller.State.DelayUrl);
            var savedDelay = delayField.Text; delayField.Text = "invalid URL"; Click(headerWindow, settingsSave); await Until(() => !headerWindow.Controller.Busy); Layout(headerWindow);
            Check(headerWindow.SettingsDirty && headerWindow.Notification.IsError && headerWindow.Controller.State.DelayUrl == savedDelay, "Rejected settings retain the unsaved draft and report the error in the same notification overlay");
            delayField.Text = savedDelay; Layout(headerWindow); Check(!headerWindow.SettingsDirty, "Restoring invalid text returns the settings draft to clean");
            headerWindow.ShowPage("Nodes"); Layout(headerWindow);
            var keptRows = headerWindow.SubscriptionView.Groups.First().NodeRows.GetVisualDescendants().OfType<Button>().ToArray();
            var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var saveOperation = headerWindow.Controller.ExecuteAsync(() => saving.Task); Layout(headerWindow);
            Check(keptRows.All(b => !b.IsEffectivelyEnabled && b.Opacity == 1) && headerWindow.CaptureControl.Opacity == 1, "Saving a selected node blocks conflicting input without fading node rows or the capture selector");
            saving.SetResult(); await saveOperation; Layout(headerWindow);
            Check(keptRows.SequenceEqual(headerWindow.SubscriptionView.Groups.First().NodeRows.GetVisualDescendants().OfType<Button>()), "Completing a short save keeps the existing node row visuals attached");
            var workspaceBefore = headerWindow.PageHeading.TranslatePoint(default, headerWindow)!.Value;
            var notification = headerWindow.Notification;
            notification.Dismiss();
            using (var settleNotification = new UiAnimationClock(notification)) { settleNotification.Pulse(0); settleNotification.Pulse(400); Layout(headerWindow); }
            using (var notificationClock = new UiAnimationClock(notification))
            {
                headerWindow.ShowError("Synthetic notification"); notificationClock.Pulse(0); notificationClock.Pulse(120); Layout(headerWindow);
                Check(notification.Showing && notification.Progress is > 0 and < 1 && notification.Offset.Y < 0 && notification.Opacity is > 0 and < 1, "Notification slides down from above the client with intermediate opacity and position");
                notificationClock.Pulse(400); Layout(headerWindow);
                Check(notification.Progress == 1 && notification.Offset.Y == 0 && headerWindow.PageHeading.TranslatePoint(default, headerWindow)!.Value == workspaceBefore, "Fully displayed notification does not move or rebuild the workspace");
                notification.Show("Replacement notice"); Check(notification.Message == "Replacement notice" && notification.Progress == 1, "A new message replaces the visible notice without flashing or stacking");
                notification.Dismiss(); notificationClock.Pulse(410); notificationClock.Pulse(520);
                Check(!notification.Showing && notification.Progress is > 0 and < 1 && !notification.IsHitTestVisible, "Notification dismisses with a transition and releases input immediately");
                notification.Show("Interrupted dismissal"); var retainedProgress = notification.Progress; notificationClock.Pulse(530); notificationClock.Pulse(930); Layout(headerWindow);
                Check(retainedProgress is > 0 and < 1 && notification.Progress == 1 && notification.IsVisible, "A message arriving during dismissal continues from the displayed frame");
                notification.Dismiss(); notificationClock.Pulse(940); notificationClock.Pulse(1300);
                Check(!notification.IsVisible && notification.Progress == 0, "Dismissed notification leaves no persistent orange message");
            }
            notification.Show("Expiry fixture", TimeSpan.FromMilliseconds(80)); await Until(() => !notification.Showing);
            Check(!notification.Showing, "Notification lifetime expires automatically without user dismissal");
            headerWindow.NodeControl.SelectedIndex = 1; await Until(() => headerWindow.Controller.State.SelectedNode == same[1].Id && !headerWindow.Controller.Busy);
            Check(headerWindow.NodeControl.Candidates.Count == 2 && !headerWindow.Controller.Engine.IsRunning, "Header selection changes the saved node while staying within the subscription and disconnected");
            await headerWindow.Controller.ApplyAsync(headerState with { SelectedNode = outsiders[1].Id }); Layout(headerWindow);
            Check(!headerWindow.NodeControl.CanSwitch && headerWindow.NodeControl.Candidates.Count == 1 && !headerWindow.NodeControl.IsHitTestVisible, "A standalone imported node has no unrelated nodes in its header");
            await headerWindow.Controller.ApplyAsync(headerState with { SelectedNode = outsiders[0].Id }); Layout(headerWindow);
            Check(!headerWindow.NodeControl.CanSwitch && headerWindow.NodeControl.Candidates.Single().SubscriptionId == foreign.Id, "Choosing a different subscription rebuilds the header within its new boundary");
            headerWindow.Close(); await Until(() => !headerWindow.IsVisible);
            var choice = new StableChoice { ItemsSource = new[] { "First", "Second", "Third" }, SelectedIndex = 0, Width = 300, VerticalAlignment = VerticalAlignment.Top, Margin = new(20) };
            var choiceWindow = new Window { Width = 400, Height = 280, Background = Skin.ChromeBrush, Content = choice }; choiceWindow.Show(); Layout(choiceWindow);
            choice.IsDropDownOpen = true; Layout(choiceWindow);
            var popup = choice.GetVisualDescendants().OfType<Popup>().Single();
            var reveal = (PopupRevealSurface)popup.Child!;
            Check(popup.Placement == PlacementMode.BottomEdgeAlignedLeft && Math.Abs(popup.VerticalOffset+choice.Bounds.Height)<.1 && Math.Abs(reveal.Bounds.Width - choice.Bounds.Width) < 1,
                "Dropdown covers the complete trigger at the same width without a detached gap");
            Check(popup.IsUsingOverlayLayer && ReferenceEquals(reveal.Trigger,choice) && reveal.Header is { Bounds.Height: > 0 },
                "Dropdown uses the window compositor and retains its live unscaled header");
            using (var clock = new UiAnimationClock(reveal))
            {
                reveal.SetImmediately(0);
                Check(Math.Abs(reveal.VisibleBounds.Height-choice.Bounds.Height)<.1 && reveal.VisibleBounds.Top==0,
                    "The first expansion frame retains the complete original control surface");
                reveal.Progress = 1; clock.Pulse(0); clock.Pulse(60); Layout(choiceWindow);
                Check(reveal.Progress > 0 && reveal.Progress < 1 && reveal.RenderTransform is null
                    && reveal.VisibleBounds.Height>choice.Bounds.Height && reveal.VisibleBounds.Height<reveal.Bounds.Height,
                    "Dropdown grows as a continuous surface without stretching its text");
                clock.Pulse(300); Layout(choiceWindow); Check(Math.Abs(reveal.Progress - 1) < 0.001 && reveal.Clip is null, "Dropdown expansion settles at full height with no residual clipping");
            }
            var highlight = reveal.GetVisualDescendants().OfType<MenuSelectionPanel>().Single(); var options = highlight.Children.OfType<ComboBoxItem>().ToArray();
            Check(options.Length == 3 && options.All(c => c.Classes.Contains("moving-menu")), "Dropdown options share one animated highlight surface");
            using (var clock = new UiAnimationClock(highlight))
            {
                options[0].IsSelected = false; options[2].IsSelected = true; Layout(choiceWindow); clock.Pulse(0); clock.Pulse(60); Layout(choiceWindow);
                Check(highlight.HighlightBounds.Y > options[0].Bounds.Y && highlight.HighlightBounds.Y < options[2].Bounds.Y, "Dropdown selection moves through intermediate rows");
                var displayed = highlight.HighlightBounds.Y; options[2].IsSelected = false; options[1].IsSelected = true; Layout(choiceWindow);
                Check(Math.Abs(displayed - highlight.HighlightBounds.Y) < 0.1, "Interrupted dropdown selection continues from its displayed position");
                clock.Pulse(80); clock.Pulse(300); Layout(choiceWindow); Check(Math.Abs(highlight.HighlightBounds.Y - options[1].Bounds.Y) < 0.1, "Dropdown selection settles on the final option");
            }
            choice.Focus(); choiceWindow.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); choiceWindow.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Layout(choiceWindow);
            Check(!choice.IsDropDownOpen, "Escape dismisses a dropdown using normal keyboard handling");
            choice.IsDropDownOpen = true; Layout(choiceWindow);
            var headerPoint = reveal.TranslatePoint(new Point(reveal.Bounds.Width/2,choice.Bounds.Height/2),choiceWindow)!.Value;
            choiceWindow.MouseMove(headerPoint); choiceWindow.MouseDown(headerPoint,MouseButton.Left); choiceWindow.MouseUp(headerPoint,MouseButton.Left); Layout(choiceWindow);
            Check(!choice.IsDropDownOpen,"Clicking the retained header closes the expanded dropdown");
            choice.IsDropDownOpen = true; Layout(choiceWindow);
            using (var settle = new UiAnimationClock(reveal)) { settle.Pulse(0); settle.Pulse(300); Layout(choiceWindow); }
            Click(choiceWindow,options[1]);
            Check(!choice.IsDropDownOpen && choice.SelectedIndex==1,"Clicking a revealed option selects it and dismisses the continuous surface");
            choice.IsDropDownOpen = true; Layout(choiceWindow);
            choiceWindow.MouseDown(new Point(390,270),MouseButton.Left); choiceWindow.MouseUp(new Point(390,270),MouseButton.Left); Layout(choiceWindow);
            Check(!choice.IsDropDownOpen,"Clicking outside the overlay dismisses the dropdown");
            choice.VerticalAlignment = VerticalAlignment.Bottom; Layout(choiceWindow); choice.IsDropDownOpen=true; Layout(choiceWindow);
            Check(reveal.OpensUp && popup.Placement==PlacementMode.TopEdgeAlignedLeft && Math.Abs(popup.VerticalOffset-choice.Bounds.Height)<.1,
                "A dropdown near the window bottom grows upward while retaining its header position");
            using (var upward = new UiAnimationClock(reveal))
            {
                reveal.SetImmediately(0); Check(Math.Abs(reveal.VisibleBounds.Bottom-reveal.Bounds.Height)<.1 && Math.Abs(reveal.VisibleBounds.Height-choice.Bounds.Height)<.1,
                    "Upward expansion starts from the original control at the bottom of the surface");
                reveal.Progress=1; upward.Pulse(0); upward.Pulse(300); Layout(choiceWindow);
            }
            var headerY=reveal.TranslatePoint(new Point(0,reveal.Bounds.Height-choice.Bounds.Height),choiceWindow)!.Value.Y;
            Check(Math.Abs(headerY-choice.TranslatePoint(default,choiceWindow)!.Value.Y)<1,
                "An upward dropdown keeps the selected header at the trigger's exact vertical position");
            choice.IsDropDownOpen=false; choice.ItemsSource=Enumerable.Range(0,100).Select(i=>$"Option {i}").ToArray(); choice.SelectedIndex=0; Layout(choiceWindow);
            choice.IsDropDownOpen=true; Layout(choiceWindow);
            var optionScroll=reveal.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Check(reveal.Bounds.Height<=choiceWindow.Bounds.Height-4 && optionScroll.Extent.Height>optionScroll.Viewport.Height,
                "Long dropdowns retain scrolling within the available window height");
            choice.IsDropDownOpen=false; Layout(choiceWindow);
            for (var i = 0; i < 30; i++) { choice.IsDropDownOpen = true; Layout(choiceWindow); choice.IsDropDownOpen = false; Layout(choiceWindow); }
            Check(!choice.IsDropDownOpen && choice.IsEnabled, "Thirty rapid dropdown reopen cycles keep the control usable");
            Check(NativeCaption.CaptionColor(choiceWindow) == 0x242424, "Dialog caption color is derived from its background"); choiceWindow.Close();
            var surface = new DialogSurface { Child = Ui.Text("Transition fixture") }; var motionWindow = new Window { Content = surface }; motionWindow.Show(); Layout(motionWindow);
            using (var clock = new UiAnimationClock(surface, surface.Scale, surface.Offset))
            {
                clock.Pulse(0); surface.Dismiss(); clock.Pulse(1); clock.Pulse(201);
                surface.Reveal(); clock.Pulse(202); clock.Pulse(262); Layout(motionWindow);
                Check(surface.Opacity > 0 && surface.Opacity < 1 && surface.Scale.ScaleX > 0.985 && surface.Scale.ScaleX < 1, "Dialog opening has intermediate opacity and scale without resizing its layout");
                clock.Pulse(422); Layout(motionWindow); Check(surface.Opacity == 1 && surface.Scale.ScaleX == 1 && surface.Offset.Y == 0, "Dialog opening finishes at a fully opaque, unshifted state");
            }
            motionWindow.Close();
            var cardsBefore=new SettingsCards();cardsBefore.Children.Add(new Border { Child=new Border { Height=120 },Background=Skin.ChromeBrush });cardsBefore.Children.Add(new Border { Child=new Border { Height=180 },Background=Skin.ChromeBrush });
            var cardsAfter=new SettingsCards();cardsAfter.Children.Add(new Border { Child=new Border { Height=240 },Background=Skin.ChromeBrush });cardsAfter.Children.Add(new Border { Child=new Border { Height=96 },Background=Skin.ChromeBrush });
            var cardsLast=new SettingsCards();cardsLast.Children.Add(new Border { Child=new Border { Height=160 },Background=Skin.ChromeBrush });
            var cardHost=new ContentControl { Content=cardsBefore,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top };
            var cardWindow=new Window { Width=900,Height=620,Content=cardHost };cardWindow.Show();Layout(cardWindow);
            using(var cardsClock=new UiAnimationClock(cardsAfter,cardsLast))
            {
                var first=cardsBefore.Children[0].Bounds;cardsAfter.ContinueFrom(cardsBefore);cardHost.Content=cardsAfter;Layout(cardWindow);
                cardsClock.Pulse(0);Check(cardsAfter.Children[0].Bounds==first,"Settings section begins from the displayed card geometry");
                cardsClock.Pulse(60);Layout(cardWindow);var displayed=cardsAfter.Children[0].Bounds;
                Check(displayed.Height>180 && displayed.Height<240,"Settings cards pass through intermediate sizes without scaling their content");
                cardsLast.ContinueFrom(cardsAfter);cardHost.Content=cardsLast;Layout(cardWindow);
                Check(cardsLast.Children[0].Bounds==displayed,"A rapid section change continues from the current card shape");
                cardsClock.Pulse(70);cardsClock.Pulse(400);Layout(cardWindow);
                Check(cardsLast.Progress==1 && Math.Abs(cardsLast.Children[0].Bounds.Height-160)<.1,"Settings section settles to its final card size");
            }
            cardWindow.Close();
            await ProviderSearchChecks.RunAsync(output, Check);
            await InteractionRevisionChecks.RunAsync(output, Check);
            await OperationNotificationChecks.RunAsync(output, Check);
            await Revision0122Checks.RunAsync(output, Check);
            ProviderCardChecks.Run(output, Check);
            await Revision0127Checks.RunAsync(output, Check);
            await Revision0128Checks.RunAsync(output, Check);
            TagAlignmentChecks.Run(output, Check);
            await Revision0130Checks.RunAsync(output, Check);
            await Revision0131Checks.RunAsync(output, Check);
            Revision0132Checks.Run(output, Check);
            Revision0133Checks.Run(output, Check);
            await AiModelCatalogChecks.RunAsync(output,Check);
            await UpdatePreviewChecks.RunAsync(Path.Combine(output,"update-preview"),Check);
            var proxyAfter = new WindowsProxyRegistry().Read(); Check(proxyBefore.All(p => p.Value is null ? proxyAfter[p.Key] is null : p.Value.Same(proxyAfter[p.Key])), "All workflow checks leave Windows proxy settings unchanged");
            File.WriteAllText(Path.Combine(output, "workflow-checks.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, syntheticData = true, nativeTaskbarVerified = false, userNodeConnected = false, binary = VerificationStamp.Current() }, new JsonSerializerOptions { WriteIndented = true })); return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "workflow-failure.txt"), string.Join('\n', checks) + "\n" + error); return 1; }
    }
    private sealed class SubscriptionFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(); private readonly Task _run;
        internal string Url => "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/subscription";
        internal SubscriptionFixture(string body) { _listener.Start(); _run = Serve(body); }
        private async Task Serve(string body)
        {
            try { while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token); var stream = client.GetStream(); using var reader = new StreamReader(stream, leaveOpen: true);
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                var content = Encoding.UTF8.GetBytes(body); var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nSubscription-Userinfo: upload=0; download=1000; total=1000000; expire=1900000000\r\nContent-Length: " + content.Length + "\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, _stop.Token); await stream.WriteAsync(content, _stop.Token);
            } } catch (Exception error) when (error is OperationCanceledException or SocketException) { }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _run; _stop.Dispose(); }
    }
}
