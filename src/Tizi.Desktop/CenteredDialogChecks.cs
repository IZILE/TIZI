using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class CenteredDialogChecks
{
    internal static void Ready(Control window)
    {
        if (window is CenteredDialog { Overlay: { Retracting: false } overlay })
        { using var clock = new UiAnimationClock(overlay); overlay.SetImmediately(0); overlay.Reveal(); clock.Pulse(0); clock.Pulse(320); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        static void Layout(Control top) { top.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        static void Click(Control top, Control control)
        {
            Ready(top); Layout(top); var point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
            top.MouseMove(point); top.MouseDown(point, MouseButton.Left); top.MouseUp(point, MouseButton.Left); Layout(top);
        }
        static async Task Until(Func<bool> done) { using var timeout = new CancellationTokenSource(8000); while (!done()) await Task.Delay(10, timeout.Token); }
        static void Capture(Control top, string file) { Layout(top); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var frame = top.CaptureRenderedFrame(); frame?.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
        var frames = Path.Combine(output, "dialog-morph-frames"); Directory.CreateDirectory(frames);
        var state = new AppState { AutoUpdate = false, AutoUpdateProviders = false, RestoreConnection = false, CloseToTray = false };
        new Vault(UserData.DirectoryPath).Write("settings.dat", state);
        var window = new MainWindow(false); window.Show(); await Until(() => window.Controller.Ready); window.Rail.Select("Logs"); Layout(window);
        var trigger = window.PageView.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content is TextBlock text && text.Text == UiText.Get("Log settings"))
            ?? window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock text && text.Text == UiText.Get("Log settings"));
        try
        {
            Click(window, trigger);
            var dialog = window.OpenDialogs.Single(); Layout(dialog); var overlay = dialog.Overlay!;
            check(TopLevel.GetTopLevel(dialog) == window && !window.OwnedWindows.Any(),
                "Log settings is a borderless centered sheet without a caption icon or minimize, maximize and close controls");
            var point = trigger.TranslatePoint(default, window)!.Value;
            check(overlay.SourceBounds.Position == point && overlay.SourceBounds.Size == trigger.Bounds.Size && trigger.Opacity == 0,
                "The actual Log settings button supplies the starting rectangle and is hidden only while its sheet is present");
            using (var clock = new UiAnimationClock(overlay))
            {
                overlay.SetImmediately(0); overlay.Reveal(); clock.Pulse(0);
                for (var frame = 0; frame <= 12; frame++) { clock.Pulse(frame * 25); Layout(dialog); Capture(dialog, Path.Combine(frames, $"open-{frame:00}.png")); }
                check(overlay.VisibleBounds == overlay.TargetBounds && Math.Abs(overlay.TargetBounds.Center.X - window.ClientSize.Width / 2) < .1 && Math.Abs(overlay.TargetBounds.Center.Y - window.ClientSize.Height / 2) < .1,
                    "The expanded sheet finishes at the main window center in both axes rather than staying beside the button");
                Capture(window, Path.Combine(output, "dialog-owner-design.png")); Capture(dialog, Path.Combine(output, "dialog-centered-design.png"));
                var option = dialog.GetVisualDescendants().OfType<CheckBox>().First(); var before = option.IsChecked; Click(dialog, option);
                check(dialog.IsVisible && option.IsChecked != before, "Clicking a setting inside the sheet changes its draft and does not dismiss it");
                dialog.MouseDown(new(12, 12), MouseButton.Left); dialog.MouseUp(new(12, 12), MouseButton.Left); Layout(dialog);
                check(overlay.Retracting && dialog.IsVisible && !overlay.GetVisualDescendants().OfType<Grid>().First().IsHitTestVisible,
                    "An outside click begins reverse motion and blocks repeated dialog actions until it is removed");
                clock.Pulse(325); clock.Pulse(410); Layout(dialog);
                check(overlay.VisibleBounds.Width > overlay.SourceBounds.Width && overlay.VisibleBounds.Width < overlay.TargetBounds.Width,
                    "Closing exposes an intermediate shape between the centered sheet and its source button");
                Capture(dialog, Path.Combine(frames, "close-middle.png"));
            }
            await Until(() => !dialog.IsVisible); Layout(window);
            check(trigger.Opacity == 1 && !window.OwnedWindows.Any() && window.IsEnabled && window.Controller.State.Logs == state.Logs,
                "Outside dismissal restores the source button and owner input without saving an abandoned settings draft");
            for (var i = 0; i < 6; i++)
            {
                Click(window, trigger); dialog = window.OpenDialogs.Single(); Ready(dialog);
                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); dialog.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); await Until(() => !dialog.IsVisible);
            }
            Layout(window); check(!window.OwnedWindows.Any() && trigger.Opacity == 1 && trigger.IsEffectivelyEnabled,
                "Six open and Escape-dismiss cycles leave no modal input host, invisible button or disabled content behind");
            foreach (var width in new[] { 880d, 1180d, 1920d })
            {
                window.Width = width; Layout(window); Click(window, trigger); dialog = window.OpenDialogs.Single(); Ready(dialog); Layout(dialog); overlay = dialog.Overlay!;
                check(overlay.TargetBounds.Left >= 24 && overlay.TargetBounds.Right <= window.ClientSize.Width - 24 && overlay.TargetBounds.Top >= 24 && overlay.TargetBounds.Bottom <= window.ClientSize.Height - 24 && Math.Abs(overlay.TargetBounds.Center.X - window.ClientSize.Width / 2) < .1,
                    "The centered sheet stays within a 24-pixel inset at owner width " + width);
                await dialog.CloseSheetAsync();
            }
            var accepted = new CenteredDialog { Width = 480, Height = 240, Title = "Fixture confirmation", Content = Ui.Value("Synthetic confirmation") };
            var acceptance = accepted.ShowDialog<bool>(window); Ready(accepted); accepted.Close(true); check(await acceptance, "A confirmation retains its true result across the reverse animation");
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var editor = new EditorDialog("Import nodes", [new("text", "Name", "fixture")], async (_, session) => { session.BeginCommit(); await gate.Task; });
            var shown = editor.ShowDialog(window); Ready(editor); var saving = editor.SubmitAsync();
            editor.MouseDown(new(12, 12), MouseButton.Left); editor.MouseUp(new(12, 12), MouseButton.Left); Layout(editor);
            check(editor.IsVisible && editor.Overlay is { Retracting: false } && editor.Session.IsCommitting && !editor.Token.IsCancellationRequested,
                "Clicking outside during an atomic save keeps the sheet visible and does not cancel the write");
            gate.SetResult(); await saving; await shown; check(editor.Applied && !window.OwnedWindows.Any(), "The guarded sheet retracts after its save finishes and releases its owner");

            var inventory = new (string Name, Func<CenteredDialog> Create)[]
            {
                ("import", window.CreateImportEditor),
                ("provider-directory", window.CreateProviderDirectoryEditor),
                ("export", window.CreateExportDialog),
                ("node-editor", () => new NodeEditor(_ => Task.CompletedTask)),
                ("rule-editor", () => new RuleEditor(new("DOMAIN", "fixture.example", "TIZI"), _ => Task.CompletedTask)),
                ("provider-search", () => new ProviderSearchDialog(new(), _ => Task.CompletedTask, _ => Task.CompletedTask)),
                ("ai-settings", () => new AiSearchSettingsDialog(new(), _ => Task.CompletedTask)),
                ("confirmation", () => new CenteredDialog { Title = UiText.Get("Unsaved settings"), Content = Ui.Value("Synthetic unsaved draft"), Width = 480, Height = 200 })
            };
            var covered = new HashSet<Type>(); var chrome = new List<object>();
            window.Width = 1180; Layout(window);
            foreach (var item in inventory)
            {
                var sheet = item.Create(); covered.Add(sheet.GetType());
                check(!typeof(Window).IsAssignableFrom(sheet.GetType()),
                    item.Name + " has no caption decoration or app icon before a platform window can be shown");
                // Reapplying an owner icon must not reintroduce a small icon through a future call site.
                
                DialogOrigin.Remember(trigger);
                var visible = sheet.ShowDialog(window); Layout(sheet);
                var motion=sheet.Overlay!;var motionFolder=Path.Combine(frames,item.Name);Directory.CreateDirectory(motionFolder);
                using(var clock=new UiAnimationClock(motion))
                {
                    motion.SetImmediately(0);motion.Reveal();clock.Pulse(0);
                    foreach(var ms in new[]{0,8,16,24,32,64,128,264})
                    {
                        clock.Pulse(ms);Layout(sheet);
                        check(motion.SurfaceColor.R<100&&motion.SurfaceColor.G<100&&motion.SurfaceColor.B<100,
                            item.Name+" retains its visible gray surface without a white morph frame at "+ms+" ms");
                        check(!motion.HasSourcePreview&&motion.SourceContentOpacity==0&&motion.SurfaceColor==Skin.Chrome
                            &&motion.GetVisualChildren().Single() is Grid{Opacity:1,RenderTransform:null},
                            item.Name+" draws one opaque live page with no source snapshot or scaled copy at "+ms+" ms");
                        check(trigger.IsEffectivelyEnabled&&!window.OwnedWindows.Any(),
                            item.Name+" opens without disabled background styling or an extra native window at "+ms+" ms");
                        Capture(window,Path.Combine(motionFolder,ms.ToString("D3")+".png"));
                    }
                }
                Ready(sheet); Layout(sheet);
                check(TopLevel.GetTopLevel(sheet) == window && !window.OwnedWindows.Any() && sheet.Overlay is not null,
                    item.Name + " stays icon-free after showing and uses the shared centered client-area header");
                Capture(sheet, Path.Combine(output, "dialog-header-" + item.Name + ".png"));
                chrome.Add(new { item.Name, Type = sheet.GetType().Name, HasCaptionIcon = false,
                    Decorations = "Client area", Header = sheet.Title });
                await sheet.CloseSheetAsync(); await visible;
            }
            var dialogTypes = typeof(CenteredDialog).Assembly.GetTypes().Where(type => !type.IsAbstract && typeof(CenteredDialog).IsAssignableFrom(type)).ToHashSet();
            check(dialogTypes.SetEquals(covered), "Every concrete application dialog type is exercised by the icon-free header inventory");
            check(typeof(CenteredDialog).Assembly.GetTypes().Where(type => !type.IsAbstract && typeof(Window).IsAssignableFrom(type))
                .All(type => type == typeof(MainWindow)),
                "No production secondary Window class bypasses the shared icon-free dialog chrome");
            File.WriteAllText(Path.Combine(output, "dialog-header-audit.json"), JsonSerializer.Serialize(chrome, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.Close(); await Until(() => !window.IsVisible); }
    }
}
