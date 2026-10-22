using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class UiChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); File.Delete(Path.Combine(output, "ui-checks.json")); var report = new List<string>(); var count = 0;
        void Check(bool ok, string description) { if (!ok) throw new InvalidOperationException(description); count++; report.Add("PASS " + description); }
        void Layout(Control window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Capture(Control window, string file)
        {
            var motion = window.GetVisualDescendants().OfType<SelectionIndicator>().SelectMany(i => new Avalonia.Animation.Animatable[] { i, i.Position }).ToList();
            motion.AddRange(window.GetVisualDescendants().OfType<SettingsCards>());
            motion.AddRange(window.GetVisualDescendants().OfType<NavigationGlyph>());
            motion.AddRange(window.GetVisualDescendants().OfType<DialogOverlay>());
            motion.AddRange(window.GetVisualDescendants().Select(v => v.RenderTransform).OfType<Avalonia.Animation.Animatable>());
            if (window is ContentControl { Content: DialogSurface surface }) motion.AddRange(new Avalonia.Animation.Animatable[] { surface, surface.Scale, surface.Offset });
            using var clock = new UiAnimationClock(motion.Distinct().ToArray()); clock.Pulse(0); clock.Pulse(300); Layout(window);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var frame = window.CaptureRenderedFrame() ?? throw new IOException("No rendered frame."); frame.Save(Path.Combine(output, file), new PngBitmapEncoderOptions());
        }
        void Click(Control window, Control target)
        {
            CenteredDialogChecks.Ready(window);
            var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
        }
        var nodes = NodeImport.Parse("ss://YWVzLTEyOC1nY206ZGVtby1wYXNzd29yZA@jp.example.net:443#Tokyo%20%7C%2001\nss://YWVzLTEyOC1nY206ZGVtby1wYXNzd29yZA@sg.example.net:8443#Singapore%20%7C%2002\nvless://29c295b8-5724-4c41-8cf1-ceae43986a58@us.example.net:443?security=tls&sni=us.example.net#Los%20Angeles%20%7C%2003");
        var subscription = new Subscription { Name = "示例订阅 · Example", Url = "https://example.com/subscription", Usage = new(0, 89L * 1024 * 1024 * 1024, 300L * 1024 * 1024 * 1024, new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero)) };
        nodes[0] = nodes[0] with { SubscriptionId = subscription.Id }; nodes[1] = nodes[1] with { SubscriptionId = subscription.Id };
        var provider = new ProviderSite { Name = "示例官网 · Example", Url = "https://example.com/", Region = "日本 · 新加坡 / Japan · Singapore", Profile = new("示例介绍，用于检查卡片换行和价格信息。Synthetic fixture for readable descriptions and prices.", [new("Monthly plan", 19.8m, "CNY", "month", 100, "Synthetic fixture", "https://example.com/pricing") { PriceUnit = "period", PriceQuote = "CNY 19.8/month" }], ["Japan", "Singapore"], "Unverified", "Unverified", ["https://example.com/guide"], DateTimeOffset.UtcNow) { SupportedProtocols = ["Clash", "Shadowrocket", "VLESS"] } };
        var providers = Enumerable.Range(1, 3).Select(i => provider with { Id = Guid.NewGuid(), Name = $"示例机场 {i} · Example {i}", Url = $"https://provider{i}.example/" }).ToList();
        var state = new AppState { Nodes = nodes, Subscriptions = [subscription], SelectedNode = nodes[0].Id, Providers = providers, ProvidersSearchedAt = DateTimeOffset.Now, Rules = [new RoutingRule("DOMAIN-SUFFIX", "example.org", "DIRECT")] };
        try
        {
            var iconRail = new NavigationRail();
            var iconWindow = new Window { Width = 260, Height = 420, Background = Skin.ChromeBrush, Content = iconRail };
            iconWindow.Show(); Layout(iconWindow);
            var glyphs = NavigationRail.Pages.Select(iconRail.GlyphFor).ToArray();
            Check(glyphs.All(g => g.SelectionFill == (g.Page == "Overview" ? 1 : 0)), "Sidebar starts with only the active icon filled");
            Check(iconRail.GlyphFor("Logs").FilledBody!.FillContains(new Point(6, 9))
                && !iconRail.GlyphFor("Logs").FilledBody!.FillContains(new Point(12, 7)), "Filled document retains transparent text counters");
            Check(iconRail.GlyphFor("Settings").FilledBody!.FillContains(new Point(12, 4))
                && !iconRail.GlyphFor("Settings").FilledBody!.FillContains(new Point(12, 12)), "Filled gear retains its transparent center");
            Check(iconRail.GlyphFor("Nodes").FilledBody!.FillContains(new Point(14, 6))
                && !iconRail.GlyphFor("Nodes").FilledBody!.FillContains(new Point(8, 6)), "Filled servers retain transparent indicator slots");
            using (var fillClock = new UiAnimationClock(glyphs))
            {
                fillClock.Pulse(0); iconRail.Select("Logs"); fillClock.Pulse(1); fillClock.Pulse(61);
                Check(iconRail.GlyphFor("Logs").SelectionFill is > 0 and < 1 && iconRail.GlyphFor("Overview").SelectionFill is > 0 and < 1,
                    "Incoming and outgoing sidebar icons fill and empty simultaneously");
                var displayedFill = glyphs.Select(g => g.SelectionFill).ToArray(); iconRail.Select("Settings");
                Check(glyphs.Select(g => g.SelectionFill).SequenceEqual(displayedFill), "Interrupted sidebar fill preserves the displayed frame");
                fillClock.Pulse(62); fillClock.Pulse(90); displayedFill = glyphs.Select(g => g.SelectionFill).ToArray(); iconRail.Select("Logs");
                Check(glyphs.Select(g => g.SelectionFill).SequenceEqual(displayedFill), "Sidebar selection can reverse mid-fill without a jump");
                fillClock.Pulse(91); fillClock.Pulse(391);
                Check(glyphs.All(g => Math.Abs(g.SelectionFill - (g.Page == "Logs" ? 1 : 0)) < .0001), "Rapid sidebar retarget settles with exactly one filled icon");
                iconRail.Select("Nodes"); iconRail.Select("Logs"); fillClock.Pulse(392); fillClock.Pulse(700);
                Check(glyphs.All(g => Math.Abs(g.SelectionFill - (g.Page == "Logs" ? 1 : 0)) < .0001), "Same-frame away and back cancels abandoned fill targets");
                iconRail.Compact(true); Layout(iconWindow);
                Check(glyphs.All(g => g.Bounds.Size == new Size(24, 24)) && iconRail.GlyphFor("Logs").SelectionFill == 1,
                    "Compact sidebar retains icon geometry and the filled selected state");
            }
            iconWindow.Close();
            foreach (var message in new uint[] { 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6 }) Check(NativeCaption.Suppress(message, 3), "Caption icon mouse shortcut suppressed");
            foreach (var hit in new nint[] { 2, 8, 9, 20 }) Check(!NativeCaption.Suppress(0xA1, hit), "Caption drag and native caption buttons retained");
            Check(!NativeCaption.Suppress(0x112, 0xF100), "Keyboard system menu retained");
            var defaultWindow = new MainWindow(true, state); defaultWindow.Show(); Layout(defaultWindow);
            Capture(defaultWindow, "overview-default.png");
            defaultWindow.OverviewMonitor.Update(1_000_000, 2_500_000, 0, true, latency: "45 ms", duration: TimeSpan.FromSeconds(4982));
            Capture(defaultWindow, "overview-default-sample.png");
            var gridNodes = nodes.Concat(Enumerable.Range(1, 16).Select(i => nodes[0] with { Id = Guid.NewGuid(), Name = $"日本 {i:00} | IEPL · Example", SubscriptionId = subscription.Id })).ToList();
            defaultWindow.Controller.SetPreview(state with { Nodes = gridNodes }); defaultWindow.ShowPage("Nodes"); Layout(defaultWindow);
            Capture(defaultWindow, "nodes-grid-default.png");
            defaultWindow.Width = 880; Layout(defaultWindow); Capture(defaultWindow, "nodes-grid-narrow.png");
            defaultWindow.Width = 1640; Layout(defaultWindow); Capture(defaultWindow, "nodes-grid-wide.png"); defaultWindow.Close();
            foreach (var language in new[] { "zh-CN", "en" })
            foreach (var width in new[] { 880d, 1180d, 1640d, 1920d })
            {
                UiText.Language = language;
                var window = new MainWindow(true, state) { Width = width, Height = 780 }; window.Show(); Layout(window);
                // Show() fits to the real desktop. Headless tests intentionally exercise all requested sizes.
                window.Width = width; Layout(window);
                var connect = window.ConnectAction;
                Check(connect.Parent is Grid toolbar && toolbar.Children.Count == 3, $"{language}/{width}: node choice, mode controls and connection button retain their toolbar layout");
                var brand = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "TIZI");
                var brandPoint = brand.TranslatePoint(default,window)!.Value;
                var connectionCenter = connect.TranslatePoint(new Point(0,connect.Bounds.Height/2),window)!.Value.Y;
                var brandHost = (WordmarkHost)brand.Parent!;
                Check(brand.FontSize == 36 && Math.Abs(brandHost.Bounds.Height-connect.Bounds.Height)<.1
                    && Math.Abs(brandHost.TranslatePoint(default,window)!.Value.Y+brandHost.Bounds.Height/2-connectionCenter)<.6,
                    $"{language}/{width}: compact TIZI shares the control center without changing the header layout");
                var brandCenter = brandHost.TranslatePoint(brandHost.InkBounds.Center, window)!.Value.X;
                var railGlyph = window.Rail.GlyphFor("Overview"); var railCenter = railGlyph.TranslatePoint(new(railGlyph.Bounds.Width/2,0),window)!.Value.X;
                Check(Math.Abs(brandCenter-railCenter)<.1 && brandHost.TranslatePoint(new(brandHost.InkBounds.Right,0),window)!.Value.X+8<=window.ModeControl.TranslatePoint(default,window)!.Value.X,
                    $"{language}/{width}: TIZI aligns with sidebar icons and never overlaps the mode controls");
                var nodeChoice = window.NodeControl;
                Check(nodeChoice.Candidates.Count == 2 && nodeChoice.Candidates.All(n => n.SubscriptionId == subscription.Id), $"{language}/{width}: header only offers the current subscription's nodes");
                var nodeCenter = nodeChoice.TranslatePoint(new Point(nodeChoice.Bounds.Width / 2, 0), window)!.Value.X;
                void CheckToolbarSurface(string page)
                {
                    var surface = window.ContentSurface;
                    var surfaceLeft = surface.TranslatePoint(default, window)!.Value.X;
                    var surfaceRight = surfaceLeft + surface.Bounds.Width;
                    var modeStart = window.ModeControl.TranslatePoint(default, window)!.Value.X;
                    var connectEnd = connect.TranslatePoint(new Point(connect.Bounds.Width, 0), window)!.Value.X;
                    var center = nodeChoice.TranslatePoint(new Point(nodeChoice.Bounds.Width / 2, 0), window)!.Value.X;
                    Check(Math.Abs((modeStart - surfaceLeft) - (surfaceRight - connectEnd)) <= 1,
                        $"{language}/{width}/{page}: toolbar ends have equal insets from the content surface");
                    Check(Math.Abs(center - (surfaceLeft + surfaceRight) / 2) <= 1,
                        $"{language}/{width}/{page}: node choice centers on the actual content panel");
                }
                CheckToolbarSurface("initial");
                Check(double.IsNaN(nodeChoice.Width) && nodeChoice.Bounds.Width <= 340, $"{language}/{width}: node choice fits its content within a compact maximum");
                var titleLine = nodeChoice.GetVisualDescendants().OfType<Grid>().Single(g => g.Tag is "header-node-title");
                var detailsLine = nodeChoice.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Tag is "header-node-details");
                var nameLine = nodeChoice.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Tag is "header-node-name");
                Check(Math.Abs(nameLine.TranslatePoint(default, nodeChoice)!.Value.X - detailsLine.TranslatePoint(default, nodeChoice)!.Value.X) < 0.1, $"{language}/{width}: node name and subscription details share the same left edge");
                Check(nameLine.FontFamily.Name == UiFonts.Interface.Name && detailsLine.FontFamily.Name == UiFonts.Interface.Name && nameLine.FontSize >= 15 && detailsLine.FontSize >= 12.5, $"{language}/{width}: node text uses a consistent readable Windows UI font");
                Check(new Typeface(nameLine.FontFamily).GlyphTypeface.FamilyName.Contains("YaHei", StringComparison.OrdinalIgnoreCase), $"{language}/{width}: the selected UI font resolves to the actual YaHei glyph face");
                Check(nodeChoice.Bounds.Width >= Math.Min(280, nodeChoice.MaxWidth) && nodeChoice.Bounds.Height >= 58,
                    $"{language}/{width}: header node choice reserves two readable rows within the available toolbar width");
                nodeChoice.IsDropDownOpen = true; Layout(window);
                var nodePopup = nodeChoice.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single();
                var nodePopupRows = nodePopup.Child!.GetVisualDescendants().OfType<ComboBoxItem>().ToArray();
                Check(nodePopupRows.Length == 2, $"{language}/{width}: node dropdown contains the same subscription choices");
                foreach (var popupRow in nodePopupRows)
                {
                    var popupName = popupRow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Tag is "header-node-name");
                    var popupDetails = popupRow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Tag is "header-node-details");
                    var selectedX = nameLine.TranslatePoint(default, nodeChoice)!.Value.X;
                    var optionX = popupName.TranslatePoint(default, nodePopup.Child!)!.Value.X;
                    Check(Math.Abs(selectedX - optionX) <= 1 && Math.Abs(popupDetails.TranslatePoint(default, nodePopup.Child!)!.Value.X - optionX) < .1, $"{language}/{width}: dropdown and selected node share their text columns ({selectedX:0.##}/{optionX:0.##})");
                }
                nodeChoice.IsDropDownOpen = false; Layout(window);
                var captionActions = window.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is string tag && tag.StartsWith("caption-")).ToArray();
                Check(captionActions.Length == 3 && captionActions.All(b => b.TranslatePoint(default, window)!.Value.Y < 3), $"{language}/{width}: menu and window controls occupy the topmost row");
                var nodeLeft = nodeChoice.TranslatePoint(default, window)!.Value.X;
                var captureEnd = window.CaptureControl.TranslatePoint(new Point(window.CaptureControl.Bounds.Width, 0), window)!.Value.X;
                Check(nodeLeft - captureEnd >= 8, $"{language}/{width}: centered node choice leaves a gap after the mode controls");
                var buttonCenterY = connect.TranslatePoint(new Point(0, connect.Bounds.Height / 2), window)!.Value.Y;
                var modeGroup = (StackPanel)window.ModeControl.Parent!;
                var modeCenterY = modeGroup.TranslatePoint(new Point(0, modeGroup.Bounds.Height / 2), window)!.Value.Y;
                Check(Math.Abs(buttonCenterY - modeCenterY) < 0.6, $"{language}/{width}: connection action aligns with the center of the mode group");
                var modeLeft = window.ModeControl.TranslatePoint(default, window)!.Value.X;
                var captureLeft = window.CaptureControl.TranslatePoint(default, window)!.Value.X;
                Check(modeLeft >= window.Rail.Bounds.Width + 24 && captureLeft >= window.Rail.Bounds.Width + 24, $"{language}/{width}: mode control projections stay inside the flat part of the content surface, clear of its left corner");
                var arrow = window.CaptureControl.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "choiceArrow");
                var arrowLeft = arrow.TranslatePoint(default, window.CaptureControl)!.Value.X;
                Check(window.CaptureControl.Bounds.Width - arrowLeft - arrow.Bounds.Width >= 11 && Math.Abs(arrow.TranslatePoint(new Point(0, arrow.Bounds.Height / 2), window.CaptureControl)!.Value.Y - window.CaptureControl.Bounds.Height / 2) < 0.6, $"{language}/{width}: dropdown arrow has an even inset and centers vertically inside its control");
                var initialBounds = new Rect(connect.TranslatePoint(default, window)!.Value, connect.Bounds.Size);
                var phases = Enum.GetValues<ConnectionPhase>();
                Check(!connect.GetVisualDescendants().OfType<TextBlock>().Any(), $"{language}/{width}: connection button uses the logo without a visible text caption");
                var buttonFace = connect.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "buttonFace");
                connect.Logo.PreviewTime=0;
                using (var stateClock = new UiAnimationClock(connect.Logo, buttonFace))
                {
                    var time = 0;
                    foreach (var phase in phases.Append(ConnectionPhase.Disconnected))
                    {
                        connect.SetState(phase, false); stateClock.Pulse(time); stateClock.Pulse(time + 1000); Layout(window); time += 1200;
                        Check(new Rect(connect.TranslatePoint(default, window)!.Value, connect.Bounds.Size) == initialBounds, $"{language}/{width}/{phase}: changing status keeps button size and position");
                        var logoCenter = connect.Logo.TranslatePoint(new Point(connect.Logo.Bounds.Width / 2, connect.Logo.Bounds.Height / 2), connect)!.Value;
                        Check(Math.Abs(logoCenter.X - connect.Bounds.Width / 2) < 0.6 && Math.Abs(logoCenter.Y - connect.Bounds.Height / 2) < 0.6, $"{language}/{width}/{phase}: logo centers inside the button");
                        Check(connect.Logo.Strokes().SelectMany(p => new[] { p.Start, p.End }).All(p => p.X >= 2 && p.X <= 94 && p.Y >= 2 && p.Y <= 34), $"{language}/{width}/{phase}: visible logo paths fit without clipping");
                        if (phase == ConnectionPhase.Disconnected)
                        {
                            var points = connect.Logo.Strokes().SelectMany(p => new[] { p.Start, p.End }).ToArray();
                            var playRatio = (points.Max(p => p.X) - points.Min(p => p.X)) / (points.Max(p => p.Y) - points.Min(p => p.Y));
                            var strokes=connect.Logo.Strokes().Chunk(2).Select(pair=>(Start:pair[0].Start,End:pair[1].End)).ToArray();var lengths=strokes.Select(s=>Math.Sqrt(Math.Pow(s.End.X-s.Start.X,2)+Math.Pow(s.End.Y-s.Start.Y,2))).ToArray();
                            var massCenter=strokes.Select((s,i)=>(s.Start.X+s.End.X)/2*lengths[i]).Sum()/lengths.Sum();
                            Check(strokes.Length == 3 && playRatio is >= .67 and <= .70 && lengths.Select(v=>Math.Round(v,2)).Distinct().Count()==3
                                && Math.Abs(massCenter-48)<.01 && Math.Abs(strokes[0].End.Y-18)<.01,
                                $"{language}/{width}: approved narrow play glyph has unequal sides, a centered right tip and balanced stroke mass");
                        }
                        if (phase == ConnectionPhase.Connecting)
                        {
                            connect.Logo.PreviewTime = 0.9; var first = connect.Logo.Curves(); connect.Logo.PreviewTime = 1.25; var rotated = connect.Logo.Curves();
                            Check(first.Count(c => c.Opacity > 0.99) == 6 && rotated.Count(c => c.Opacity > 0.99) == 6 && first[0].Start != rotated[0].Start && rotated[0].Control1 != rotated[0].Start + (rotated[0].End - rotated[0].Start) * (1d / 3), $"{language}/{width}: connecting uses three continuously rotating curves, each with two contiguous construction pieces");
                            connect.Logo.PreviewTime = 0;
                        }
                        if (phase == ConnectionPhase.Connected) Check(connect.Logo.Strokes().Length == 6 && connect.Logo.Fracture == 0, $"{language}/{width}: success forms the C1 diagonal ladder with two rails and three rungs");
                        if (phase == ConnectionPhase.Failed)
                        {
                            var broken = connect.Logo.Curves();
                            Check(broken.Count(c => c.Opacity > 0.99) == 6 && connect.Logo.Fracture == 1, $"{language}/{width}: failed state cuts a visible middle fracture through the upright C1 ladder");
                            Check(new[] { 1, 2 }.All(i => Math.Abs(broken[i].Start.X-broken[i].End.X)<.01) && new[] { 3, 4, 5 }.All(i => Math.Abs(broken[i].Start.Y-broken[i].End.Y)<.01), $"{language}/{width}: the cracked ladder keeps vertical rails and level rungs");
                            Check(ConnectionLogo.CrackPoints.Select(p => p.Y).Distinct().Count() >= 3, $"{language}/{width}: crack uses a jagged line rather than a straight empty gap");
                            Check(connect.Background is ISolidColorBrush red && red.Color.R > 180 && red.Color.G < 90 && red.Color.B < 100, $"{language}/{width}: connection failure uses a red action background");
                        }
                        Check(AutomationProperties.GetName(connect) == ConnectionButton.StateText(phase) && ToolTip.GetTip(connect) is string tip && tip.Contains(ConnectionButton.StateText(phase)), $"{language}/{width}/{phase}: accessibility and tooltip announce current state");
                        Check(connect.IsEnabled == (phase is ConnectionPhase.Disconnected or ConnectionPhase.Connecting or ConnectionPhase.Connected or ConnectionPhase.Failed), $"{language}/{width}/{phase}: connecting permits cancellation while cleanup prevents repeated actions");
                        Check(connect.Opacity == 1, $"{language}/{width}/{phase}: logo transitions do not flash the whole button");
                        Check(connect.Logo.Waiting == (phase == ConnectionPhase.Connecting), $"{language}/{width}/{phase}: idle animation runs only while a connection is pending");
                        Check(phase == ConnectionPhase.Connected ? connect.Logo.Assembly == 1 : connect.Logo.Assembly < 1, $"{language}/{width}/{phase}: only a confirmed connection shows the completed ladder");
                    }
                    connect.SetState(ConnectionPhase.Connecting, true); stateClock.Pulse(time); stateClock.Pulse(time + 160);
                    var progress = connect.Logo.Assembly;
                    Check(progress > 0 && progress < 0.78, $"{language}/{width}: ladder assembly has intermediate frames");
                    connect.SetState(ConnectionPhase.Disconnected, false);
                    Check(Math.Abs(connect.Logo.Assembly - progress) < 0.001, $"{language}/{width}: an interrupted connection reverses from the currently displayed ladder");
                    stateClock.Pulse(time + 170); stateClock.Pulse(time + 1100);
                    Check(connect.Logo.Assembly == 0 && !connect.Logo.Waiting, $"{language}/{width}: cancelled connections return to the play glyph and stop the idle timer");
                    time += 1200;
                    connect.SetState(ConnectionPhase.Connected, false); stateClock.Pulse(time); stateClock.Pulse(time + 1000);
                    var complete = connect.Logo.Curves(); var blue = ((ISolidColorBrush)buttonFace.Background!).Color;
                    connect.SetState(ConnectionPhase.Failed, false);
                    Check(connect.Logo.Curves().SequenceEqual(complete) && connect.Logo.Fracture == 0 && ((ISolidColorBrush)buttonFace.Background!).Color == blue, $"{language}/{width}: failure begins at the complete blue ladder without an instantaneous shape or color change");
                    stateClock.Pulse(time + 1010); stateClock.Pulse(time + 1200);
                    var midCrack = connect.Logo.Fracture; var middleColor = ((ISolidColorBrush)buttonFace.Background!).Color;
                    Check(midCrack is > 0 and < 1 && middleColor != blue && middleColor != Color.Parse("#DF3D4B"), $"{language}/{width}: cracking and blue-to-red background have synchronized intermediate frames (fracture {midCrack:0.###}, background {middleColor})");
                    var middleShape = connect.Logo.Curves();var middleVelocity=connect.Logo.Velocities(); connect.SetState(ConnectionPhase.Connecting, true);
                    Check(connect.Logo.Curves().SequenceEqual(middleShape) && Math.Abs(connect.Logo.Fracture - midCrack) < 0.0001, $"{language}/{width}: retry during a crack starts from the visible shape and fracture amount");
                    Check(connect.Logo.Velocities().Zip(middleVelocity,(a,b)=>Math.Abs(a-b)).Max()<.0001,$"{language}/{width}: retargeting also preserves every primary stroke velocity");
                    stateClock.Pulse(time + 1210); stateClock.Pulse(time + 2300);
                    Check(connect.Logo.Fracture == 0 && connect.Logo.Waiting, $"{language}/{width}: retry smoothly removes the fracture and restores looping arcs");
                    connect.SetState(ConnectionPhase.Disconnected, false); stateClock.Pulse(time + 2310); stateClock.Pulse(time + 3400);
                }
                var captureRight = window.CaptureControl.TranslatePoint(new Point(window.CaptureControl.Bounds.Width, 0), window)!.Value.X;
                Check(window.ConnectAction.TranslatePoint(default, window)!.Value.X - captureRight >= 24, $"{language}/{width}: primary connection action stays separate from mode controls");
                var railButton = window.Rail.For("Overview"); var railGrid = (Grid)railButton.Content!;
                var iconLeft = railGrid.Children[0].TranslatePoint(default, window.Rail)!.Value.X;
                var captionLeft = railGrid.Children[1].TranslatePoint(default, window.Rail)!.Value.X;
                var previousBottom = double.NaN;
                foreach (var item in NavigationRail.Pages)
                {
                    var button = window.Rail.For(item); var row = (Grid)button.Content!; var icon = row.Children[0]; var caption = row.Children[1];
                    Check(Math.Abs(icon.TranslatePoint(default, window.Rail)!.Value.X - iconLeft) < 0.1, $"{language}/{width}: {item} icon column aligned");
                    var point = button.TranslatePoint(default, window.Rail)!.Value;
                    Check(Math.Abs(icon.TranslatePoint(new Point(12, 12), button)!.Value.Y - button.Bounds.Height / 2) < 0.1, $"{language}/{width}: {item} icon vertically centered");
                    if (caption.IsVisible) { Check(Math.Abs(caption.TranslatePoint(default, window.Rail)!.Value.X - captionLeft) < 0.1, $"{language}/{width}: {item} text column aligned"); var offset = caption.TranslatePoint(new Point(0, caption.Bounds.Height / 2), button)!.Value.Y - button.Bounds.Height / 2; Check(Math.Abs(offset) <= 0.6, $"{language}/{width}: {item} text vertically centered (rounding offset {offset:0.###} px)"); }
                    if (!double.IsNaN(previousBottom)) Check(Math.Abs(point.Y - previousBottom - 4) < 0.1, $"{language}/{width}: {item} consistent row spacing");
                    previousBottom = point.Y + button.Bounds.Height;
                }
                foreach (var page in NavigationRail.Pages)
                {
                    Click(window, window.Rail.For(page)); Check(window.CurrentPage == page, $"{language}/{width}: {page} pointer navigation");
                    CheckToolbarSurface(page);
                    Check(window.SearchControl.IsVisible == (page is "Nodes" or "Providers" or "Rules" or "Connections"), $"{language}/{width}/{page}: local search is available on data lists and hidden on monitoring and settings");
                    if (page == "Rules")
                    {
                        Check(window.SearchControl.CornerRadius == new CornerRadius(18) && window.SearchControl.Input.PlaceholderText == UiText.Get("Search rules"), $"{language}/{width}: rule search uses the rounded UI surface and localized prompt");
                        window.SearchControl.Text = "missing.synthetic"; Layout(window);
                        Check(window.SearchControl.Clear.IsVisible && window.PageView.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == UiText.Get("No matching items")), $"{language}/{width}: rule search filters immediately with a clear action");
                        Click(window, window.SearchControl.Clear); Check(window.SearchControl.Text == "" && !window.SearchControl.Clear.IsVisible, $"{language}/{width}: clear search restores the rule list");
                        using (var focusClock = new UiAnimationClock(window.SearchControl))
                        {
                            focusClock.Pulse(0); window.SearchControl.Text = "example.org"; Click(window, window.SearchControl.Input); focusClock.Pulse(10); focusClock.Pulse(170);
                            Check(window.SearchControl.Input.IsFocused, $"{language}/{width}: clicking rule search gives it keyboard focus");
                            Click(window, window.PageHeading); focusClock.Pulse(180); focusClock.Pulse(360);
                            Check(!window.SearchControl.IsKeyboardFocusWithin && window.SearchControl.Text == "example.org" && window.SearchControl.BorderBrush is ISolidColorBrush focusBorder && focusBorder.Color == Color.Parse("#20FFFFFF"), $"{language}/{width}: clicking outside rule search removes its blue focus ring and preserves the filter");
                        }
                        window.SearchControl.Text = ""; Layout(window);
                    }
                    if (page is "Overview" or "Settings" or "Logs") Check(!window.GetVisualDescendants().OfType<Button>().Any(b => b.IsVisible && b.Content is TextBlock t && t.Text is { } text && (text == UiText.Get("Previous") || text == UiText.Get("Next"))), $"{language}/{width}/{page}: pagination is hidden outside data lists");
                    Check(window.Rail.Indicator.Destination == window.Rail.TargetBounds(page), $"{language}/{width}: {page} shared selection target");
                    Check(window.GetVisualDescendants().OfType<SegmentedChoice>().All(choice => choice.Indicator.Background is ISolidColorBrush brush && brush.Color == Color.Parse("#2979F5")), $"{language}/{width}/{page}: mode, settings tabs and log filters retain blue control emphasis");
                    if (page == "Settings")
                    {
                        Check(window.SettingsSections?.Count == 6, $"{language}/{width}: settings have six functional sections");
                        var checkedOptions = window.GetVisualDescendants().OfType<CheckBox>().Where(c => c.IsChecked == true).ToArray();
                        Check(checkedOptions.Length > 0 && checkedOptions.All(c => Motion.GetBackground(c.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "check")) is ISolidColorBrush brush && brush.Color == Color.Parse("#0A84FF")), $"{language}/{width}: checked settings options retain their blue fill");
                        Check(window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content is TextBlock t && t.Text == UiText.Get("Start in the tray")).IsEnabled, $"{language}/{width}: hidden login startup remains selectable without login startup");
                        var closing = window.GetVisualDescendants().OfType<StableChoice>().Single(c => c.Items.Contains("Exit application"));
                        Check(!window.SettingsDirty, $"{language}/{width}: opened settings start with a clean draft");
                        foreach (var choice in new[] { 1, 0 })
                        {
                            closing.SelectedIndex = choice; Layout(window);
                            Check(closing.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == UiText.Get(closing.Items[choice]!.ToString()!)), $"{language}/{width}: closing choice {choice} is translated");
                            Check(!window.SettingsDirty, $"{language}/{width}: option-only General choices do not create a manual-save draft");
                            if (choice == 1) Check(!window.Notification.Showing, $"{language}/{width}: General choices do not show an unsaved notification");
                        }
                        for (var section = 0; section < 6; section++)
                        {
                            Click(window, window.SettingsSections!.ButtonAt(section)); Layout(window);
                            var cards = window.GetVisualDescendants().OfType<SettingsCards>().Single();
                            Check(window.PageView.GetVisualDescendants().OfType<Button>().Single(b => b.Tag is "settings-save").IsVisible == (section != 0), $"{language}/{width}/{section}: only settings sections with editable values show a Save button");
                            using(var cardsClock=new UiAnimationClock(cards)) { cardsClock.Pulse(0);cardsClock.Pulse(300);Layout(window); }
                            Check(cards.Columns == (width == 880 ? 1 : 2), $"{language}/{width}/{section}: settings adapt to readable card columns");
                            Check(cards.Children.All(c => c.Bounds.Width >= 280), $"{language}/{width}/{section}: settings cards retain readable width");
                            Check(Math.Abs(cards.TranslatePoint(default, window)!.Value.X - window.PageHeading.TranslatePoint(default, window)!.Value.X) < 1 && cards.Bounds.Width >= window.PageView.Bounds.Width - 32, $"{language}/{width}/{section}: settings cards align to the page heading and use the available width");
                            Check(window.GetVisualDescendants().OfType<TextBox>().Where(c => c.AcceptsReturn).All(c => c.VerticalContentAlignment == Avalonia.Layout.VerticalAlignment.Top), $"{language}/{width}/{section}: every multiline settings editor is top aligned");
                            var form = window.PageView.GetVisualDescendants().OfType<Control>().ToArray();
                            Check(form.OfType<TextBox>().All(t => t.TextAlignment == TextAlignment.Left) && form.OfType<NumericUpDown>().All(n => n.TextAlignment == TextAlignment.Left) && form.OfType<StableChoice>().All(c => c.HorizontalContentAlignment == Avalonia.Layout.HorizontalAlignment.Left), $"{language}/{width}/{section}: text, numeric and choice fields all align left");
                            if (section == 2)
                            {
                                var outbound = window.GetVisualDescendants().OfType<StableChoice>().Single(c => c.Items.Contains("Automatic (physical gateway)"));
                                Check(outbound.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == UiText.Get("Automatic (physical gateway)")), $"{language}/{width}: automatic outbound interface is translated");
                            }
                        }
                        Click(window, window.SettingsSections!.ButtonAt(4)); Layout(window);
                    }
                    if (page is "Nodes" or "Providers")
                    {
                        var more = window.GetVisualDescendants().OfType<Button>().Single(b => b.Tag is "more-actions");
                        var triggerSize = more.Bounds.Size; Click(window, more);
                        var actions = window.ActiveMoreSheet!; Layout(window);
                        Check(actions.IsVisible && TopLevel.GetTopLevel(actions) == window && !window.OwnedWindows.Any() && more.ContextMenu is null && more.Bounds.Size == triggerSize && more.Opacity == 0, $"{language}/{width}/{page}: More uses one in-window surface and retains the original toolbar layout");
                        Check(actions.Actions.Count == (page == "Nodes" ? 6 : 5) && actions.Actions.All(b => Math.Abs(b.Bounds.Width - actions.Actions[0].Bounds.Width) < .1 && b.Bounds.Height >= 40), $"{language}/{width}/{page}: More actions use equally wide, readable buttons");
                        Check(actions.TargetBounds.Right <= window.ClientSize.Width - 15 && actions.TargetBounds.Bottom <= window.ClientSize.Height - 15, $"{language}/{width}/{page}: expanded More stays within the client area");
                        using (var actionClock = new UiAnimationClock(actions))
                        {
                            actions.SetImmediately(0); Layout(window);
                            Check(actions.VisibleBounds == actions.SourceBounds && actions.SourceBounds.Size == triggerSize, $"{language}/{width}/{page}: More starts at the exact trigger rectangle");
                            if (width == 1180 && language == "zh-CN" && page == "Nodes") Capture(window, "more-morph-start.png");
                            actions.Open(); actionClock.Pulse(0); actionClock.Pulse(60); Layout(window);
                            Check(actions.Progress is > 0 and < 1 && actions.VisibleBounds.Width > triggerSize.Width && actions.VisibleBounds.Height > triggerSize.Height && actions.RenderTransform is null, $"{language}/{width}/{page}: More has continuous intermediate shape frames without scaling its text");
                            if (width == 1180 && language == "zh-CN" && page == "Nodes") Capture(window, "more-morph-middle.png");
                            actionClock.Pulse(350); Layout(window);
                            Check(actions.VisibleBounds == actions.TargetBounds, $"{language}/{width}/{page}: More settles at its expanded rounded page");
                            if (width == 1180) Capture(window, $"more-{page.ToLowerInvariant()}-{language}.png");
                        }
                        Check(!actions.GetVisualDescendants().OfType<Button>().Any(b => b.Content is TextBlock t && t.Text == UiText.Get("Close")), $"{language}/{width}/{page}: More omits the redundant footer close button");
                        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Layout(window);
                        var closeWait=System.Diagnostics.Stopwatch.StartNew();
                        while(actions.IsVisible && closeWait.ElapsedMilliseconds<2000) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(2); }
                        Dispatcher.UIThread.RunJobs();
                        Check(!actions.IsVisible && window.IsEnabled && more.Opacity == 1 && window.ActiveMoreSheet is null, $"{language}/{width}/{page}: closing More restores the original button and removes the overlay");
                    }
                    if (page == "Nodes")
                    {
                        var group = window.SubscriptionView.Groups.Single(g => g.Key == subscription.Id.ToString());
                        Check(group.Expanded, $"{language}/{width}: selected subscription opens initially");
                        var quota = group.GetVisualDescendants().OfType<ProgressBar>().Single();
                        var quotaBottom = quota.TranslatePoint(new Point(0, quota.Bounds.Height), group)!.Value.Y;
                        var firstRowTop = group.NodeRows.TranslatePoint(default, group)!.Value.Y;
                        Check(quota.Bounds.Height >= 6 && firstRowTop - quotaBottom >= 15.9, $"{language}/{width}: usage track is readable and leaves space before the selected node ({quota.Bounds.Height:0.##} px track; {firstRowTop - quotaBottom:0.##} px gap)");
                        Check(window.SubscriptionView.Groups.Count == 2, $"{language}/{width}: manual nodes stay in a separate group");
                        Check(group.Bounds.Width > width / 2, $"{language}/{width}: subscription spans the content column");
                        var header = window.PageHeading;
                        var point = header.TranslatePoint(default, window)!.Value;
                        Check(point.X >= window.Rail.Bounds.Right, $"{language}/{width}: page heading stays outside navigation ({point.X})");
                        var buttons = group.NodeRows.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is string).ToArray();
                        Check(buttons.Length == 2 && buttons.All(b => Math.Abs(b.Bounds.Width-group.NodeRows.Bounds.Width)<1)
                            && buttons[1].Bounds.Y>=buttons[0].Bounds.Bottom+8,
                            $"{language}/{width}: subscriptions with few nodes retain the original full-width horizontal rows ({string.Join(';',buttons.Select(b=>b.Bounds.ToString()))})");
                        var faces = buttons.Select(b => b.GetVisualDescendants().OfType<Border>().Single(face => face.Name == "buttonFace")).ToArray();
                        using(var visibleClock=new UiAnimationClock(group.Reveal)){visibleClock.Pulse(0);visibleClock.Pulse(300);Layout(window);}
                        using (var selectionClock = new UiAnimationClock(faces.Cast<Avalonia.Animation.Animatable>().Append(group.NodeRows.Indicator).Append(group.NodeRows.Indicator.Position).ToArray()))
                        {
                            selectionClock.Pulse(0); selectionClock.Pulse(200);
                            group.NodeRows.Select((string)buttons[1].Tag!); selectionClock.Pulse(210); selectionClock.Pulse(270);
                            Check(!group.NodeRows.Backplate.IsVisible, $"{language}/{width}: clicking a node does not create a gray selection fill");
                            var hoverPoint=buttons[1].TranslatePoint(new Point(buttons[1].Bounds.Width/2,buttons[1].Bounds.Height/2),window)!.Value;window.MouseMove(hoverPoint);Layout(window);selectionClock.Pulse(280);selectionClock.Pulse(410);Layout(window);
                            Check(faces[1].BorderBrush is ISolidColorBrush outline&&outline.Color==Color.Parse("#2979F5")&&faces[1].Background is ISolidColorBrush tint&&tint.Color==Color.Parse("#182979F5"), $"{language}/{width}: hovering a node immediately shows a blue outline and a light blue overlay above its original background (over: {buttons[1].IsPointerOver}, border: {faces[1].BorderBrush}, fill: {faces[1].Background})");
                            window.MouseMove(new Point(1,1));Layout(window);selectionClock.Pulse(420);selectionClock.Pulse(570);Layout(window);
                            group.NodeRows.Select((string)buttons[0].Tag!); selectionClock.Pulse(580); selectionClock.Pulse(730);
                            Check(faces.All(face => face.Background is ISolidColorBrush brush && brush.Color.A == 0), $"{language}/{width}: reversing node selection does not create a gray background flash");
                        }
                        Check(group.GetVisualDescendants().OfType<Button>().Any(b => b.Content is TextBlock t && t.Text == UiText.Get("Subscription URL")), $"{language}/{width}: subscription provides its address instead of duplicate details");
                        foreach (var action in group.GetVisualDescendants().OfType<Button>().Where(b => b.Content is TextBlock text && new[] { UiText.Get("Subscription URL"), UiText.Get("Update") }.Contains(text.Text)))
                        {
                            var center = action.TranslatePoint(new Point(0, action.Bounds.Height / 2), group)!.Value.Y;
                            var titleCenter = group.Header.TranslatePoint(new Point(0, group.Header.Bounds.Height / 2), group)!.Value.Y;
                            Check(Math.Abs(center - titleCenter) < 0.6, $"{language}/{width}: subscription actions center on their heading");
                        }
                        using var revealClock = new UiAnimationClock(group.Reveal); revealClock.Pulse(0); revealClock.Pulse(200); Layout(window);
                        Click(window, group.Header); Check(group.Expanded && group.IsSelected && !group.NodeRows.Backplate.IsVisible, $"{language}/{width}: header click selects the entire subscription without collapsing it or retaining a node highlight");
                        using (var groupClock = new UiAnimationClock(group))
                        {
                            groupClock.Pulse(0); groupClock.Pulse(200);
                            Check(group.Background is ISolidColorBrush fill && fill.Color == ((ISolidColorBrush)Skin.ChromeBrush).Color, $"{language}/{width}: an expanded subscription retains the neutral background even when it is the operation target");
                        }
                        Click(window, group.Disclosure); Check(!group.Expanded, $"{language}/{width}: the separate subscription arrow collapses its group");
                        Check(group.NodeRows.IsEnabled && ReferenceEquals(buttons[0], group.NodeRows.GetVisualDescendants().OfType<Button>().First(b => b.Tag is string)), $"{language}/{width}: collapse retains undimmed node rows without recreating content");
                        revealClock.Pulse(210); revealClock.Pulse(260); Layout(window);
                        Check(group.Reveal.Progress > 0 && group.Reveal.Progress < 1, $"{language}/{width}: collapse passes through intermediate heights");
                        var shown = group.Reveal.Progress; Click(window, group.Disclosure);
                        Check(group.Expanded && Math.Abs(group.Reveal.Progress - shown) < 0.001, $"{language}/{width}: interrupted expansion starts from the displayed height");
                        revealClock.Pulse(270); revealClock.Pulse(500); Layout(window);
                        Check(Math.Abs(group.Reveal.Progress - 1) < 0.001, $"{language}/{width}: expansion settles without a flashback");
                        window.ShowPage("Overview"); Layout(window); window.ShowPage("Nodes"); Layout(window);
                        Check(window.SubscriptionView.Groups.Single(g => g.Key == subscription.Id.ToString()).Expanded, $"{language}/{width}: expansion survives navigation");
                    }
                    if (page == "Logs")
                    {
                        Check(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Clear" && language == "zh-CN"), $"{language}/{width}: log toolbar translated");
                        Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == (language == "zh-CN" ? "应用" : "Application")), $"{language}/{width}: application log category follows the current language");
                    }
                    if (page == "Providers")
                    {
                        Check(providers.All(p => window.PageView.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == p.Name)), $"{language}/{width}: all provider comparison cards are present");
                        var providerRows = window.PageView.GetVisualDescendants().OfType<MovingRows>().Single(); providerRows.Select(providers[0].Id.ToString()); Layout(window);
                        var selectedCard = providerRows.For(providers[0].Id.ToString());
                        var face=selectedCard.GetVisualDescendants().OfType<Border>().First(border=>border.Name=="buttonFace");
                        Check(!providerRows.Backplate.IsVisible, $"{language}/{width}: selecting an airport retains its action target without a persistent gray fill");
                        using(var hoverClock=new UiAnimationClock(face))
                        {
                            window.MouseMove(selectedCard.TranslatePoint(new(200,60),window)!.Value);
                            hoverClock.Pulse(0);hoverClock.Pulse(200);Layout(window);
                            Check(face.Background is ISolidColorBrush hoverFill&&hoverFill.Color==Color.Parse("#414141")&&face.Opacity==1&&face.BorderThickness==default,
                                $"{language}/{width}: hovering an airport fills its card with the shared neutral emphasis without a selection outline");
                            window.MouseMove(new(4,100));hoverClock.Pulse(210);hoverClock.Pulse(420);Layout(window);
                            Check(face.Opacity==0&&!providerRows.Backplate.IsVisible,
                                $"{language}/{width}: leaving a selected airport restores its ordinary background");
                        }
                    }
                    if (page is "Nodes" or "Rules" or "Providers") Check(window.GetVisualDescendants().OfType<Button>().Count(b => b.Tag is "remove-selection") == 1, $"{language}/{width}/{page}: Remove is visible in the page toolbar");
                    if (page is "Overview" or "Nodes" or "Providers" or "Settings")
                    {
                        window.Height = 620; Layout(window);
                        var scrolls = window.PageView.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.Content is SettingsCards or ContentControl { Content: SettingsCards }).ToList();
                        if (window.PageView is ScrollViewer pageScroll) scrolls.Insert(0, pageScroll);
                        foreach (var scroll in scrolls)
                        {
                            Check(!scroll.AllowAutoHide, $"{language}/{width}/{page}: scrollbar remains available without automatic hiding");
                            var content = scroll.Content is ContentControl { Content: Control child } ? child : scroll.Content as Control;
                            if (content is null) continue;
                            foreach (var bar in scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>().Where(b => b.Orientation == Avalonia.Layout.Orientation.Vertical && b.IsVisible && b.Bounds.Height > 0))
                            {
                                var contentRight = content.TranslatePoint(new Point(content.Bounds.Width, 0), window)!.Value.X;
                                var barLeft = bar.TranslatePoint(default, window)!.Value.X;
                                Check(barLeft - contentRight >= 4, $"{language}/{width}/{page}: scrollbar stays within its existing right gutter without covering content ({barLeft - contentRight:0.##} px)");
                            }
                        }
                        if (page is "Overview" or "Settings") Check(scrolls.Any(s => s.Extent.Height > s.Viewport.Height + 1), $"{language}/{width}/{page}: minimum window exercises overflow");
                        window.Height = 780; Layout(window);
                    }
                    foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible && b.Bounds.Width > 0 && b.Content is TextBlock))
                    {
                        var label = (TextBlock)button.Content!;
                        var desired = new FormattedText(label.Text ?? "", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(label.FontFamily, label.FontStyle, label.FontWeight), label.FontSize, Skin.LabelBrush);
                        Check(desired.WidthIncludingTrailingWhitespace + button.Padding.Left + button.Padding.Right <= button.Bounds.Width + 1.1, $"{language}/{width}/{page}: button fits {label.Text}");
                    }
                    if (width == 1180 && page is "Overview" or "Nodes" or "Providers" or "Settings")
                    {
                        Capture(window, $"{page.ToLowerInvariant()}-{language}.png");
                    }
                    if (width == 1920 && page is "Settings" or "Rules") Capture(window, $"{page.ToLowerInvariant()}-wide-{language}.png");
                }
                // Revisit every page, especially Overview, after its content was detached.
                // A first pass alone cannot detect reparenting the shared overview panel.
                for (var cycle = 0; cycle < 3; cycle++)
                foreach (var page in NavigationRail.Pages.Reverse().Concat(NavigationRail.Pages))
                {
                    window.ShowPage(page); Layout(window);
                    Check(window.CurrentPage == page, $"{language}/{width}: navigation revisit {cycle + 1}/{page}");
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout(window);
                }
                window.ShowPage("Nodes"); window.Rail.Select("Nodes"); Layout(window);
                using var clock = new UiAnimationClock(window.Rail.Indicator.Position, window.Rail.Indicator);
                var origin = window.Rail.Indicator.Position.Y;
                Click(window, window.Rail.For("Logs")); Click(window, window.Rail.For("Nodes"));
                Check(Math.Abs(window.Rail.Indicator.Position.Y - origin) < 0.1, $"{language}/{width}: rapid away/back preserves displayed highlight");
                Check(window.Rail.Indicator.Destination == window.Rail.TargetBounds("Nodes"), $"{language}/{width}: rapid retarget keeps final destination");
                clock.Pulse(0); clock.Pulse(200); Layout(window);
                Check(Math.Abs(window.Rail.Indicator.Position.Y - origin) < 0.1, $"{language}/{width}: cancelled animation stays cancelled on later frames");
                Click(window, window.Rail.For("Providers")); clock.Pulse(210); clock.Pulse(250); Layout(window);
                Check(window.Rail.Indicator.Position.Y > origin && window.Rail.Indicator.Position.Y < window.Rail.Indicator.Destination.Y, $"{language}/{width}: navigation has intermediate positions");
                var displayed = window.Rail.Indicator.Position.Y; Click(window, window.Rail.For("Settings"));
                Check(Math.Abs(window.Rail.Indicator.Position.Y - displayed) < 0.1, $"{language}/{width}: mid-flight retarget does not jump");
                clock.Pulse(260); clock.Pulse(500); Layout(window);
                Check(Math.Abs(window.Rail.Indicator.Position.Y - window.Rail.Indicator.Destination.Y) < 0.1, $"{language}/{width}: retarget settles on final page");
                Check(window.ModeControl.ButtonAt(0).Bounds.Width == window.ModeControl.ButtonAt(1).Bounds.Width, $"{language}/{width}: equal mode segment sizes");
                if (width == 1180)
                {
                    window.ShowPage("Nodes"); Layout(window);
                    var exportNode = window.Controller.State.Nodes.First(n => n.Id == window.Controller.State.SelectedNode);
                    var exportGroup = window.SubscriptionView.Groups.Single(g => g.Key == (exportNode.SubscriptionId?.ToString() ?? "manual"));
                    if (!exportGroup.NodesShown) { Click(window, exportGroup.Disclosure); Layout(window); }
                    Click(window, exportGroup.NodeRows.For(exportNode.Id.ToString())); Layout(window);
                    var export = window.CreateExportDialog(); export.Show(); Layout(export);
                    var selectors = export.GetVisualDescendants().OfType<StableChoice>().ToArray(); Check(selectors.Length == 2, $"{language}: export scope and format available");
                    selectors[1].SelectedIndex = 1; Layout(export); Check(export.GetVisualDescendants().OfType<TextBox>().Any(t => t.Text?.Contains("proxies:") == true), $"{language}: YAML export preview");
                    selectors[1].SelectedIndex = 2; Layout(export); Check(export.GetVisualDescendants().OfType<TextBox>().Any(t => t.Text?.StartsWith("ss://") == true), $"{language}: share link export preview");
                    selectors[1].SelectedIndex = 3; Layout(export); Check(export.GetVisualDescendants().OfType<Image>().Any(i => i.Source is not null), $"{language}: QR export preview");
                    Capture(export, $"export-{language}.png");
                    export.Close();
                    var editor = new NodeEditor(_ => Task.CompletedTask); editor.Show(); Layout(editor);
                    Check(!editor.GetVisualDescendants().OfType<Expander>().Any() && editor.AdvancedReveal.Progress == 0 && editor.AdvancedButton.CornerRadius == new CornerRadius(18), $"{language}: advanced JSON starts as a rounded disclosure instead of the framework's black expander");
                    var port = editor.GetVisualDescendants().OfType<NumericUpDown>().Single();
                    var stepper = port.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_SpinnerPanel");
                    var arrows = stepper.GetVisualDescendants().OfType<RepeatButton>().ToArray();
                    Check(port.CornerRadius == new CornerRadius(18) && stepper.Bounds.Width == 28 && stepper.Bounds.Height == 28 && arrows.Length == 2 && arrows.All(a => a.Bounds.Height == 14), $"{language}: numeric input has a matching rounded field and balanced paired stepper buttons");
                    var protocol = editor.GetVisualDescendants().OfType<ComboBox>().First();
                    var choiceChevron = protocol.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "choiceArrow");
                    var stepperChevrons = stepper.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().ToArray();
                    Check(stepperChevrons.Length == 2 && stepperChevrons.All(p => ReferenceEquals(p.Data, choiceChevron.Data) && p.Bounds.Size == choiceChevron.Bounds.Size && p.StrokeThickness == choiceChevron.StrokeThickness && p.StrokeLineCap == choiceChevron.StrokeLineCap && p.StrokeJoin == choiceChevron.StrokeJoin && ((ISolidColorBrush)p.Stroke!).Color == ((ISolidColorBrush)choiceChevron.Stroke!).Color), $"{language}: dropdown and numeric arrows use the same geometry, size, stroke and rounded ends");
                    var choiceCenter = choiceChevron.TranslatePoint(new Point(4, 2), editor)!.Value.X;
                    var stepperCenters = stepperChevrons.Select(p => p.TranslatePoint(new Point(4, 2), editor)!.Value.X).ToArray();
                    Check(stepperCenters.All(x => Math.Abs(x - choiceCenter) < 0.1), $"{language}: dropdown and paired numeric arrows share one vertical alignment line ({choiceCenter:0.##}/{string.Join('/', stepperCenters.Select(x => x.ToString("0.##")))})");
                    var numberText = port.GetVisualDescendants().OfType<TextBox>().Single();
                    var numberPresenter = numberText.GetVisualDescendants().OfType<TextPresenter>().Single();
                    var numberLeft = numberPresenter.TranslatePoint(default, port)!.Value.X + numberPresenter.TextLayout.TextLines[0].Start;
                    var choiceLeft = protocol.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "choiceContent").TranslatePoint(default, protocol)!.Value.X;
                    Check(numberText.TextAlignment == TextAlignment.Left && protocol.HorizontalContentAlignment == Avalonia.Layout.HorizontalAlignment.Left && Math.Abs(numberLeft - choiceLeft) < 1, $"{language}: numeric and choice contents share one left inset ({numberLeft:0.##}/{choiceLeft:0.##})");
                    Capture(editor, $"numeric-{language}.png");
                    var originalPort = port.Value; Click(editor, arrows[0]); Check(port.Value == originalPort + port.Increment, $"{language}: upper numeric button increments the value ({originalPort}/{port.Value}/{port.Increment}; enabled={arrows[0].IsEffectivelyEnabled})");
                    Click(editor, arrows[1]); Check(port.Value == originalPort, $"{language}: lower numeric button decrements the value");
                    foreach (var editorWidth in new[] { 700d, 480d })
                    {
                        editor.Width = editorWidth; Layout(editor);
                        for (var i = 0; i < ProtocolCatalog.Supported.Length; i++)
                        {
                            protocol.SelectedIndex = i; Layout(editor);
                            var starts = editor.GetVisualDescendants().OfType<Grid>().Where(g => g.Tag is "labeled-row").Select(g => g.Children[1].TranslatePoint(default, editor)!.Value.X).ToArray();
                            Check(starts.Length >= 4 && starts.All(x => Math.Abs(x - starts[0]) < .1), $"{language}/{editorWidth}/{ProtocolCatalog.Supported[i].Name}: all base and protocol-specific fields share one left column");
                        }
                    }
                    editor.Width = 700; protocol.SelectedIndex = 0; Layout(editor);
                    using (var advancedClock = new UiAnimationClock(editor.AdvancedReveal))
                    {
                        Click(editor, editor.AdvancedButton); advancedClock.Pulse(0); advancedClock.Pulse(70); Layout(editor);
                        Check(editor.AdvancedReveal.Progress is > 0 and < 1 && editor.AdvancedReveal.Bounds.Height > 0 && editor.Advanced.RenderTransform is null, $"{language}: advanced JSON expands continuously without stretching the editor text");
                        advancedClock.Pulse(300); Layout(editor); Capture(editor, $"node-advanced-{language}.png");
                        Click(editor, editor.AdvancedButton); advancedClock.Pulse(310); advancedClock.Pulse(380);
                        Check(editor.AdvancedReveal.Progress is > 0 and < 1, $"{language}: advanced JSON closes through intermediate heights");
                        advancedClock.Pulse(600); Layout(editor);
                    }
                    editor.Close();
                    var criteria = new ProviderSearchOptions { ModeId = "budget", MonthlyBudget = 30, Currency = "CNY", Regions = "Japan", Protocols = ["Clash"], RequireTrial = true };
                    var saved = criteria; var searched = false;
                    var search = new ProviderSearchDialog(criteria, options => { saved = options; return Task.CompletedTask; }, _ => { searched = true; return Task.CompletedTask; });
                    search.Show(); Layout(search);
                    Check(search.Criteria.ModeId == "budget" && search.Budget.Value == 30 && search.Trial.IsChecked == true && search.Criteria.Protocols.SequenceEqual(new[] { "Clash" }), $"{language}: search dialog restores saved criteria");
                    foreach (var choice in Enumerable.Range(0, ProviderSearchModes.All.Length))
                    { search.Mode.SelectedIndex = choice; Layout(search); Check(search.Criteria.MonthlyBudget == 30 && search.Criteria.Regions == "Japan", $"{language}: mode switch preserves filters"); }
                    foreach (var button in search.GetVisualDescendants().OfType<Button>().Where(b => b.Content is TextBlock && b.IsVisible && b.Bounds.Width > 0))
                    {
                        var label = (TextBlock)button.Content!; var text = new FormattedText(label.Text ?? "", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(label.FontFamily), label.FontSize, Skin.LabelBrush);
                        Check(text.WidthIncludingTrailingWhitespace + button.Padding.Left + button.Padding.Right <= button.Bounds.Width + 1.1, $"{language}: search action fits {label.Text}");
                    }
                    Capture(search, $"search-{language}.png");
                    Click(search, search.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock t && t.Text == UiText.Get("Save criteria")));
                    Check(saved.ModeId == "trial" && !searched, $"{language}: saving criteria makes no search API call");
                    var reopened = new ProviderSearchDialog(saved, _ => Task.CompletedTask, _ => Task.CompletedTask); reopened.Show(); Layout(reopened);
                    Check(reopened.Criteria.ModeId == "trial" && reopened.Criteria.MonthlyBudget == 30, $"{language}: reusable criteria retained on reopen"); reopened.Close();
                }
                window.Close();
            }
            File.WriteAllLines(Path.Combine(output, "ui-checks.txt"), report); File.WriteAllText(Path.Combine(output, "ui-checks.json"), JsonSerializer.Serialize(new { passed = count, languages = new[] { "zh-CN", "en" }, widths = new[] { 880, 1180, 1640, 1920 }, sampleData = true, binary = VerificationStamp.Current() }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS {count} UI checks."); return 0;
        }
        catch (Exception error) { File.WriteAllLines(Path.Combine(output, "ui-checks.txt"), report.Append("FAIL " + error)); Console.Error.WriteLine(error); return 1; }
    }
}
