using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class LogViewChecks
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        static void Layout(Control window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        var state = new AppState { AutoUpdate = false, AutoUpdateProviders = false, RestoreConnection = false, Logs = new() { MaxEntries = 20000, Proxy = true, Dns = true } };
        var window = new MainWindow(true, state); window.Show(); Layout(window);
        var logs = (ActivityLog)typeof(MainWindow).GetField("_logs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var refresh = typeof(MainWindow).GetMethod("RefreshLog", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var filter = (SegmentedChoice)typeof(MainWindow).GetField("_logFilter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        logs.Settings = state.Logs;
        for (var i = 0; i < 20000; i++) logs.Add((i % 3 == 0 ? "[TCP] " : i % 3 == 1 ? "[DNS] " : "[Application] ") + $"fixture-record-{i:00000} · " + new string('x', 160));
        try
        {
            window.ShowPage("Logs"); Layout(window); var view = (LogView)window.PageView;
            check(view.Entries.Count == 20000 && view.GetRealizedContainers().Count() is > 0 and < 200 && view.FormattedRows < 200,
                "Twenty thousand log records stay available while only viewport rows are formatted and realized");
            var fullCopy = (string)typeof(MainWindow).GetMethod("VisibleLogText", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            check(fullCopy.Split('\n').Length == 20000 && fullCopy.Contains("fixture-record-00000", StringComparison.Ordinal) && fullCopy.Contains("fixture-record-19999", StringComparison.Ordinal),
                "Copying the current log category includes every record, including those outside the viewport");
            foreach (var index in new[] { 1, 2, 3, 0 })
            {
                var button = filter.ButtonAt(index); var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
                var category = index == 1 ? LogCategory.Proxy : index == 2 ? LogCategory.Dns : index == 3 ? LogCategory.Application : (LogCategory?)null;
                check(view.Entries.Count == logs.Entries.Count(e => category is null || e.Category == category)
                    && view.Entries.All(e => category is null || e.Category == category) && view.GetRealizedContainers().Count() < 200,
                    "The actual log category button filters all retained records without realizing the entire category: " + index);
            }
            view.ScrollIntoView(view.Entries[^1]); Layout(window);
            check(view.GetRealizedContainers().Any(c => ReferenceEquals(c.DataContext, view.Entries[^1])), "The last of twenty thousand log records remains reachable by scrolling");
            var formats = view.FormattedRows; var offset = view.Scroll?.Offset;
            window.ShowPage("Overview"); Layout(window); window.ShowPage("Logs"); Layout(window);
            check(ReferenceEquals(view, window.PageView) && view.FormattedRows - formats < 200 && view.Scroll?.Offset == offset,
                "Revisiting unchanged logs retains the view and scroll position without formatting the full buffer again");
            var selected = view.GetVisualDescendants().OfType<SelectableTextBlock>().First(t => t.Text?.Contains("fixture-record", StringComparison.Ordinal) == true);
            selected.SelectionStart = 0; selected.SelectionEnd = 10;
            logs.Add("[Application] new-tail-after-selection"); refresh.Invoke(window, [false]);
            check(view.Pending && !view.Entries[^1].Message.Contains("new-tail-after-selection", StringComparison.Ordinal), "Incoming logs do not replace text while the user has selected a log fragment");
            selected.SelectionEnd = 0; refresh.Invoke(window, [false]); Layout(window);
            check(!view.Pending && view.Entries.Count == 20000 && view.Entries[^1].Message.Contains("new-tail-after-selection", StringComparison.Ordinal),
                "Clearing text selection applies the pending log update and keeps the configured retention limit");
            filter.SelectedIndex = 2; refresh.Invoke(window, [true]); logs.Clear(LogCategory.Dns); refresh.Invoke(window, [true]); Layout(window);
            check(view.Entries.Count == 0 && logs.Entries.All(e => e.Category != LogCategory.Dns) && logs.Entries.Count > 0,
                "Clearing the DNS category removes only DNS records from both storage and the visible log view");
        }
        finally { window.Close(); await window.Controller.DisposeAsync(); }
    }
}
