using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class Revision0126Checks
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        void Layout(Control top) { top.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        async Task Until(Func<bool> done) { using var stop = new CancellationTokenSource(8000); while (!done()) await Task.Delay(10, stop.Token); }
        CheckBox Option(MainWindow top, string key) => top.PageView.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content is TextBlock t && t.Text == UiText.Get(key));
        void Section(MainWindow top, int index)
        {
            Layout(top); var button = top.SettingsSections!.ButtonAt(index); var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), top)!.Value;
            top.MouseMove(point); top.MouseDown(point, Avalonia.Input.MouseButton.Left); top.MouseUp(point, Avalonia.Input.MouseButton.Left); Layout(top);
        }
        void Exit(MainWindow top) => typeof(MainWindow).GetMethod("Exit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(top, null);
        void Capture(Control top, string name) { Layout(top); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var frame = top.CaptureRenderedFrame(); frame?.Save(Path.Combine(output, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
        var node = NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@node.example:443#Fixture").Single();
        var site = new ProviderSite { Name = "示例机场 · Fixture", Url = "https://airport.example/", Profile = new("套餐与来源展示使用合成数据，内容随主窗口宽度自动排列。", Enumerable.Range(1, 3).Select(i => new ProviderPlan("示例套餐 " + i, 10m * i, "CNY", "month", 100m * i, "示例套餐说明", "https://airport.example/pricing")).ToList(), ["JP", "SG"], "", "", ["https://airport.example/pricing"], DateTimeOffset.Now) { SupportedProtocols = ["Shadowsocks"], ReviewSummary = "此处为本地合成的评价摘要。" } };
        var state = new AppState { Nodes = [node], SelectedNode = node.Id, Providers = [site], Rules = [new("DOMAIN", "example.test", "DIRECT")], AutoUpdate = false, AutoUpdateProviders = false, RestoreConnection = false, StartAtLogin = false, StartHidden = false, CloseToTray = false };
        var vault = new Vault(UserData.DirectoryPath); vault.Write("settings.dat", state);
        var window = new MainWindow(false); window.Show(); await Until(() => window.Controller.Ready); window.Rail.Select("Settings"); Layout(window);
        var startupCalls = new List<(bool enabled, bool hidden)>(); window.Controller.LoginStartupWriter = (enabled, _, hidden) => startupCalls.Add((enabled, hidden));
        var restore = Option(window, "Restore the previous connection on startup");
        var login = Option(window, "Start TIZI when I sign in"); var hidden = Option(window, "Start in the tray");
        var close = window.PageView.GetVisualDescendants().OfType<StableChoice>().Single(c => c.Items.Contains("Exit application"));
        try
        {
            check(!window.PageView.GetVisualDescendants().OfType<Button>().Any(b => b.Tag is "settings-save" && b.IsVisible), "Option-only General has no visible Save button");
            restore.IsChecked = true; restore.IsChecked = false; restore.IsChecked = true; close.SelectedIndex = 1;
            login.IsChecked = true; hidden.IsChecked = true;
            await Until(() => !window.GeneralSettingsSaving); Layout(window);
            var saved = vault.Read<AppState>("settings.dat")!;
            check(saved.RestoreConnection && saved.CloseToTray && saved.StartAtLogin && saved.StartHidden && !window.SettingsDirty && !window.Notification.Showing,
                "Rapid General choices persist their final values without a draft or success-notice spam");
            check(startupCalls.Last() == (true, true) && saved.Nodes.Select(n => n.Id).SequenceEqual(state.Nodes.Select(n => n.Id)) && saved.Rules.SequenceEqual(state.Rules), "Automatic General saves update startup through the same writer while preserving nodes and rules; this check substitutes the registry writer");
            window.ShowPage("Nodes"); check(!window.Notification.Showing, "Leaving an automatically saved option-only page produces no unsaved warning");
            window.ShowPage("Settings"); Section(window, 4);
            var hosts = window.PageView.GetVisualDescendants().OfType<TextBox>().Single(t => Avalonia.Automation.AutomationProperties.GetLabeledBy(t) is TextBlock label && label.Text == UiText.Get("Local DNS mappings (JSON)")); var validHosts = hosts.Text; hosts.Text = "{";
            Section(window, 0); restore.IsChecked = false;
            await Until(() => !window.GeneralSettingsSaving);
            check(window.SettingsDirty && vault.Read<AppState>("settings.dat")!.Dns.Hosts.Count == state.Dns.Hosts.Count && !vault.Read<AppState>("settings.dat")!.RestoreConnection, "General autosave ignores invalid advanced drafts and saves only the chosen preferences");
            Section(window, 4);
            check(ReferenceEquals(hosts, window.PageView.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "{")), "The independent advanced draft remains editable after an automatic General save");
            hosts.Text = validHosts; check(!window.SettingsDirty, "Restoring the advanced input clears only its manual draft");
            Section(window, 0);
            var profileBytes = File.ReadAllBytes(UserData.File("settings.dat")); var callsBefore = startupCalls.Count;
            using (var locked = new FileStream(UserData.File("settings.dat"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                login.IsChecked = false; await Until(() => !window.GeneralSettingsSaving);
            }
            check(window.Controller.State.StartAtLogin && login.IsChecked == true && window.Notification.IsError && profileBytes.SequenceEqual(File.ReadAllBytes(UserData.File("settings.dat"))), "A failed automatic profile write restores the durable option and reports one error without losing saved data");
            check(startupCalls.Skip(callsBefore).SequenceEqual(new[] { (false, true), (true, true) }), "A failed preference write also restores the previous startup command through the substituted writer");
            window.Notification.Dismiss(); Capture(window, "settings-general-autosave.png");

            window.Rail.Select("Providers"); Layout(window); window.SearchControl.Text = "Fixture"; Layout(window);
            var list = window.PageView; var listRows = list.GetVisualDescendants().OfType<MovingRows>().Single();
            var originalCard = listRows.For(site.Id.ToString());
            await window.OpenProviderDetailsAsync(site.Id.ToString());
            var details = window.ProviderDetailsView!;
            using (var clock = new UiAnimationClock(details, details.Offset))
            {
                clock.Pulse(0); Layout(window); details.Reveal(); clock.Pulse(0); clock.Pulse(60);
                check(details.Opacity is > 0 and < 1 && details.Offset.X is > 0 and < 12, "Airport second-layer navigation has an intermediate fade and slide without scaling its text");
                clock.Pulse(220); Layout(window);
            }
            foreach (var width in new[] { 880d, 1180d, 1920d })
            {
                window.Width = width; Layout(window);
                var cards = details.GetVisualDescendants().OfType<SettingsCards>().Single();
                check(ReferenceEquals(window.PageView, details) && !window.OwnedWindows.Any() && details.Bounds.Width <= window.ClientSize.Width && details.Bounds.Height <= window.ClientSize.Height && cards.Columns == (width == 880 ? 1 : 2), "Airport details uses the main content viewport with responsive plan columns at width " + width);
                Capture(window, "provider-details-" + (int)width + ".png");
            }
            window.Width = 1180; Layout(window); await window.Controller.ApplyAsync(window.Controller.State with { UpdateMinutes = 35 }); Layout(window);
            check(ReferenceEquals(window.PageView, details), "A background state refresh does not replace the active second-layer page with the airport list");
            await window.ReturnProviderListAsync(); Layout(window);
            check(window.ProviderDetailsView is null && window.SearchControl.Text == "Fixture" && ReferenceEquals(originalCard, window.PageView.GetVisualDescendants().OfType<MovingRows>().Single().For(site.Id.ToString())) && originalCard.Classes.Contains("selected-card"), "Returning from airport details retains the list filter, card instance and selected item");
            await window.OpenProviderDetailsAsync(site.Id.ToString()); var returning = window.ReturnProviderListAsync(); window.Rail.Select("Overview"); await returning;
            check(window.CurrentPage == "Overview" && window.ProviderDetailsView is null, "Navigating away during the return transition cannot restore a stale airport overlay or list");

            foreach (var kind in new[] { "Notice", "Warning", "Error" })
            {
                window.Notification.Show(kind == "Warning" ? UiText.Get("Unsaved changes") : "示例通知 · " + kind, isError: kind == "Error", isWarning: kind == "Warning");
                using var clock = new UiAnimationClock(window.Notification); clock.Pulse(0); clock.Pulse(400); Layout(window);
                check(window.Notification.Glyph.Page == kind && window.Notification.Glyph.FilledBody is null && window.Notification.Glyph.Width == 24 && NavigationGlyph.StrokeWidth == 1.5, "Notification " + kind + " uses the sidebar vector glyph family, common canvas and round stroke");
                var noticeTitle = window.Notification.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == UiText.Get("Notice"));
                var glyphCenter = window.Notification.Glyph.TranslatePoint(new Point(0, window.Notification.Glyph.Bounds.Height / 2), window)!.Value.Y;
                var titleCenter = noticeTitle.TranslatePoint(new Point(0, noticeTitle.Bounds.Height / 2), window)!.Value.Y;
                check(Math.Abs(glyphCenter - titleCenter) < 1.01, "Notification " + kind + " glyph and title share one vertical center independently of body length (" + glyphCenter + ", " + titleCenter + ")");
                Capture(window, "notification-" + kind.ToLowerInvariant() + ".png");
            }
            check(!typeof(Window).IsAssignableFrom(window.CreateImportEditor().GetType()) && NativeCaption.HideCaptionIcon(new Window { Icon = window.Icon }), "Import and generic secondary windows both hide their small caption icons while retaining their assigned shell icons");
            window.Notification.Dismiss();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = window.Controller.ExecuteAsync(async () => { await release.Task; await window.Controller.ApplyAsync(window.Controller.State with { Nodes = [.. window.Controller.State.Nodes, node with { Id = Guid.NewGuid(), Name = "Concurrent import" }] }); });
            window.ShowPage("Settings"); Section(window, 0);
            restore = Option(window, "Restore the previous connection on startup"); restore.IsChecked = true;
            Exit(window); Layout(window);
            check(window.IsVisible && !window.OwnedWindows.Any() && window.GeneralSettingsSaving, "Exiting immediately after a choice waits for its automatic save without a discard prompt");
            release.SetResult(); await operation; await Until(() => !window.IsVisible);
            saved = vault.Read<AppState>("settings.dat")!;
            check(saved.RestoreConnection && saved.Nodes.Count == 2 && saved.Rules.SequenceEqual(state.Rules), "The queued choice merges the latest imported state and is durable before shutdown");
            var reopened = new MainWindow(false); reopened.Show(); await Until(() => reopened.Controller.Ready); reopened.ShowPage("Settings"); Layout(reopened);
            check(reopened.Controller.State.RestoreConnection && !reopened.SettingsDirty && !reopened.Notification.Showing && !reopened.OwnedWindows.Any(), "A fresh window reopens the automatically saved choices without an unsaved warning or stuck exit dialog");
            Exit(reopened); await Until(() => !reopened.IsVisible);
            var preview = new MainWindow(true, state) { Width = 1032, Height = 722 }; preview.Show(); preview.ShowPage("Overview"); Layout(preview);
            try
            {
                var monitor = preview.GetVisualDescendants().OfType<NetworkMonitor>().Single();
                monitor.Update(320_000, 2_150_000, 0, true, "31 ms", TimeSpan.FromMinutes(12));
                var display = new IpQualityDisplay("家庭宽带", "18/100", "原生 IP",
                    "IP 类型 · 设计预览\n示例来源 A：家庭宽带\n示例来源 B：家庭宽带\n示例来源 C：数据中心",
                    "风控参考分 · 设计预览\n示例评分：12、18、95\n中位数：18 / 100\n各来源分歧较大\n所有数值均为界面演示数据",
                    "原生 IP · 设计预览\n原生判定的依据、来源及查询时间在此显示\n此示例不代表真实出口 IP", 18, true);
                monitor.IpQuality.Update(display, true); Layout(preview);
                var firstLegend = preview.GetVisualDescendants().OfType<Button>().Single(b => b.Tag is "chart-series-0");
                var badges = monitor.IpQuality;
                var legendText = firstLegend.GetVisualDescendants().OfType<TextBlock>().Single();
                check(badges.Badges.All(b => b.Background is ISolidColorBrush brush && brush.Color.A == 255 && ((TextBlock)b.Child!).Foreground is ISolidColorBrush ink&&legendText.Foreground is ISolidColorBrush legendInk&&ink.Color==legendInk.Color && ((TextBlock)b.Child!).FontSize == legendText.FontSize), "IP result pills share the traffic legend typography and keep fully opaque emphasis backgrounds");
                check(badges.TranslatePoint(new Point(badges.Bounds.Width, 0), preview)!.Value.X < firstLegend.TranslatePoint(default, preview)!.Value.X && Math.Abs(badges.TranslatePoint(default, preview)!.Value.Y - firstLegend.TranslatePoint(default, preview)!.Value.Y) < 1,
                    "The three compact IP result pills precede the chart legend on the same row");
                Capture(preview, "overview-ip-label-design.png");
                var scoreBadge = badges.Badges[1]; ToolTip.SetIsOpen(scoreBadge, true); Layout(preview);
                await Task.Delay(360); Layout(preview);
                var tip = (Control)ToolTip.GetTip(scoreBadge)!;
                if (tip.GetVisualAncestors().OfType<ToolTip>().FirstOrDefault() is { } popup)
                {
                    popup.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    // Freeze the tooltip's completed opacity for the headless renderer's separate animation clock.
                    using var shown = popup.SetValue(Visual.OpacityProperty, 1d, Avalonia.Data.BindingPriority.Animation);
                    File.WriteAllText(Path.Combine(output, "ip-hover-layout.json"), System.Text.Json.JsonSerializer.Serialize(new
                    {
                        open = ToolTip.GetIsOpen(scoreBadge), visible = popup.IsVisible, opacity = popup.Opacity,
                        bounds = popup.Bounds.ToString(), desired = popup.DesiredSize.ToString(),
                        contentMatches = ReferenceEquals(popup.Content, tip), content = (tip as TextBlock)?.Text,
                        textBounds = tip.Bounds.ToString(), textDesired = tip.DesiredSize.ToString(),
                        root = TopLevel.GetTopLevel(popup)?.GetType().Name
                    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    using var frame = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Math.Ceiling(popup.Bounds.Width), (int)Math.Ceiling(popup.Bounds.Height)), new Vector(96, 96));
                    frame.Render(popup); frame.Save(Path.Combine(output, "overview-ip-hover-design.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
                check(ToolTip.GetShowDelay(scoreBadge) == 300 && Avalonia.Automation.AutomationProperties.GetHelpText(scoreBadge)?.Contains("12、18、95", StringComparison.Ordinal) == true, "IP result details stay in the hover and keyboard-accessible description rather than adding a permanent information panel");
                ToolTip.SetIsOpen(scoreBadge, false);
                monitor.Update(null, null, 0, false); check(!badges.IsVisible && !badges.HasResult, "Disconnected monitoring clears IP result visuals instead of retaining an old colored result");
            }
            finally { preview.Close(); }
        }
        finally { if (window.IsVisible) { Exit(window); await Until(() => !window.IsVisible); } }
    }
}
