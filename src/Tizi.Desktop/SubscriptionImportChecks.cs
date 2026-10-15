using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

// Explicit opt-in verification against user-provided samples. The normal app
// never reads a sample manifest. Reports contain counts, not subscription URLs.
internal static class SubscriptionImportChecks
{
    private sealed record Sample(string Label, string File);
    internal static async Task<int> RunAsync(string manifest, string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<object>(); var checks = 0; var proxy = new WindowsProxyRegistry().Read();
        var stage = "manifest"; var label = "";
        void Check(bool valid, string operation) { stage = operation; if (!valid) throw new InvalidDataException(operation); checks++; }
        async Task Finish(Task task, MainWindow window)
        {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (watch.Elapsed >= TimeSpan.FromSeconds(40)) throw new TimeoutException();
                foreach (var review in window.OpenDialogs.Where(d => d.Title == UiText.Get("Import review")).ToArray())
                {
                    CenteredDialogChecks.Ready(review); review.UpdateLayout();
                    var accept = review.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock t && t.Text == UiText.Get("Import supported items"));
                    accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                await Task.Delay(10);
            }
            await task;
        }
        MainWindow? window = null;
        try
        {
            var directory = Path.GetDirectoryName(manifest)!;
            var samples = JsonSerializer.Deserialize<List<Sample>>(File.ReadAllText(manifest), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Check(samples.Count is > 0 and <= 20, "bounded sample manifest");
            UiText.Language = "zh-CN";
            foreach (var sample in samples)
            {
                label = sample.Label;
                var file = Path.GetFullPath(Path.Combine(directory, sample.File));
                Check(file.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "sample stays in its directory");
                var text = File.ReadAllText(file); var state = new AppState { AutoUpdate = false, AutoUpdateProviders = false, StartHidden = false, RestoreConnection = false, Rules = [new("DOMAIN", "keep.example.test", "DIRECT")] };
                window = new MainWindow(true, state); window.Show(); await window.Controller.ApplyAsync(state); window.UpdateLayout();
                stage = "text editor submit";
                var editor = window.CreateImportEditor(); var shown = editor.ShowDialog(window); editor.Field("text").Text = text;
                await Finish(editor.SubmitAsync(), window);
                Check(editor.Applied && editor.ErrorText is null, "text editor committed"); await shown;
                var saved = window.Controller.State;
                Check(saved.Subscriptions.Count == 1 && saved.Nodes.Count > 0 && saved.Nodes.All(n => n.SubscriptionId == saved.Subscriptions[0].Id), "one subscription owns every imported node");
                Check(saved.Subscriptions[0].Usage is not null && saved.Subscriptions[0].UpdatedAt is not null, "real source usage and refresh time retained");
                var ids = saved.Nodes.Select(n => n.Id).ToHashSet(); var selection = saved.SelectedNode;
                stage = "file payload";
                await Finish(window.ImportPayloadAsync(Encoding.UTF8.GetBytes(text), false), window);
                Check(window.Controller.State.Subscriptions.Count == 1 && window.Controller.State.Nodes.Select(n => n.Id).ToHashSet().SetEquals(ids), "file import refreshes without duplicates or identity loss");
                stage = "QR payload";
                await Finish(window.ImportPayloadAsync(NodeExport.QrPng(text), true), window);
                Check(window.Controller.State.Subscriptions.Count == 1 && window.Controller.State.Nodes.Select(n => n.Id).ToHashSet().SetEquals(ids) && window.Controller.State.SelectedNode == selection, "QR import preserves node identities and selection");
                var persisted = new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!;
                var current = window.Controller.State;
                Check(persisted.Nodes.Count == current.Nodes.Count && persisted.Nodes.All(n => current.Nodes.Any(old => n.Id == old.Id && JsonNode.DeepEquals(n.Options, old.Options))) && persisted.Rules.Contains(state.Rules[0]), "encrypted persistence retains every parameter and existing rules");
                Check(!window.Controller.Engine.IsRunning && !window.Controller.Connected && !window.Controller.Busy && window.IsEnabled, "import leaves application usable without connecting");
                results.Add(new { label, passed = true, nodes = saved.Nodes.Count, rules = saved.Rules.Count, formats = new[] { "text editor", "file payload", "QR payload" }, parametersPreserved = true, duplicateFree = true, usageRetained = true });
                window.Close(); await window.Controller.DisposeAsync(); window = null;
            }
            var after = new WindowsProxyRegistry().Read();
            Check(proxy.All(p => p.Value is null ? after[p.Key] is null : p.Value.Same(after[p.Key])), "Windows proxy unchanged");
            File.WriteAllText(Path.Combine(output, "subscription-imports.json"), JsonSerializer.Serialize(new { passed = checks, cases = results, connected = false, credentialsIncluded = false, binary = VerificationStamp.Current() }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "subscription-import-failure.json"), JsonSerializer.Serialize(new { label, stage, error = error.GetType().Name, checks, completed = results }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
        finally { if (window is not null) { window.Close(); await window.Controller.DisposeAsync(); } }
    }
}
