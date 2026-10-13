using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

// Read a saved profile, run the actual startup loader against an isolated copy, and never connect.
internal static class ProfileViewChecks
{
    internal static async Task<int> RunAsync(string sourceDirectory, string output)
    {
        Directory.CreateDirectory(output); var checks = new List<string>();
        var originalFile = Path.Combine(sourceDirectory, "settings.dat");
        var before = SHA256.HashData(File.ReadAllBytes(originalFile));
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
        try
        {
            var source = new Vault(sourceDirectory).Read<AppState>("settings.dat") ?? throw new InvalidDataException("No saved profile");
            var expectedState = OperationFeedback.ClearSavedErrors(source);
            var isolated = source with { AutoUpdate = false, AutoUpdateProviders = false, LastConnected = false, RestoreConnection = false, StartHidden = false, CloseToTray = false };
            new Vault(UserData.DirectoryPath).Write("settings.dat", isolated);
            var window = new MainWindow(); window.Show(); await window.Controller.InitializeAsync();
            try
            {
                Check(window.Controller.Ready && !window.Controller.Connected && !window.Controller.Engine.IsRunning, "Actual startup loads the isolated saved profile without connecting");
                Check(JsonSerializer.Serialize(window.Controller.State.Nodes) == JsonSerializer.Serialize(source.Nodes) && window.Controller.State.Subscriptions.SequenceEqual(expectedState.Subscriptions) && window.Controller.State.Rules.SequenceEqual(source.Rules), "Startup preserves subscriptions, every node option and ordered rule while discarding legacy operation errors");
                window.ShowPage("Nodes"); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                foreach (var group in window.SubscriptionView.Groups)
                {
                    if (!group.Expanded) group.Disclosure.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    using var clock = new UiAnimationClock(group.Reveal); clock.Pulse(0); clock.Pulse(300);
                }
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var expectedGroups = source.Subscriptions.Count + (source.Nodes.Any(n => n.SubscriptionId is null || source.Subscriptions.All(s => s.Id != n.SubscriptionId)) ? 1 : 0);
                Check(window.SubscriptionView.Groups.Count == expectedGroups, "The node page renders every saved subscription and the separate manual group");
                foreach (var subscription in source.Subscriptions)
                {
                    var group = window.SubscriptionView.Groups.Single(g => g.Key == subscription.Id.ToString());
                    var expected = Math.Min(100, source.Nodes.Count(n => n.SubscriptionId == subscription.Id));
                    Check(group.NodeRows.GetVisualDescendants().OfType<Button>().Count(b => b.Tag is string) == expected, "A saved subscription renders its expected first page of nodes");
                }
                window.ShowPage("Rules"); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Check(window.PageView.GetVisualDescendants().OfType<MovingRows>().Single().GetVisualDescendants().OfType<Button>().Count(b => b.Tag is string) == Math.Min(100, source.Rules.Count), "The rule page renders the expected first page while retaining the full ruleset");
            }
            finally { await window.Controller.DisposeAsync(); window.Close(); }
            Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(originalFile))), "The original profile remains byte-for-byte unchanged");
            File.WriteAllText(Path.Combine(output, "profile-view-check.json"), JsonSerializer.Serialize(new { passed = checks.Count, nodes = source.Nodes.Count, subscriptions = source.Subscriptions.Count, rules = source.Rules.Count, checks, isolated = true, realNetworkUsed = false }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "profile-view-failure.txt"), string.Join('\n', checks) + "\n" + error); return 1; }
    }
}
