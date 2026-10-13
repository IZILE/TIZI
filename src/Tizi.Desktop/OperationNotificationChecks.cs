using System.Net;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

// Exercises temporary failures only against in-memory HTTP and an isolated encrypted profile.
internal static class OperationNotificationChecks
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        if (!Path.GetFullPath(UserData.DirectoryPath).Equals(Path.Combine(Path.GetFullPath(output), "settings"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Notification checks require isolated settings.");
        var subscription = new Subscription { Name = "Synthetic subscription", Url = "https://feed.example/", LastError = "Connection failed" };
        var node = NodeImport.Parse("ss://YWVzLTEyOC1nY206dGVzdA==@node.example:443#Fixture").Single() with { SubscriptionId = subscription.Id };
        var provider = new ProviderSite { Name = "Synthetic website", Url = "https://website.example/", Error = "Timeout" };
        var state = new AppState { AutoUpdate = false, AutoUpdateProviders = false, RestoreConnection = false, Subscriptions = [subscription], Nodes = [node], SelectedNode = node.Id, Rules = [new("DOMAIN", "example.test", "DIRECT")], Providers = [provider], ProviderSearchError = "Previous search failure", Discovery = new() { ApiKey = "synthetic-key-never-sent" } };
        var vault = new Vault(UserData.DirectoryPath); vault.Write("settings.dat", state);
        var original = File.ReadAllBytes(UserData.File("settings.dat"));
        for (var startup = 0; startup < 2; startup++)
        {
            await using var loaded = new AppController(preview: true); var startupErrors = new List<string>(); loaded.Error += startupErrors.Add;
            await loaded.InitializeAsync();
            check(loaded.State.Subscriptions.Single().LastError is null && loaded.State.Providers.Single().Error is null && loaded.State.ProviderSearchError is null && startupErrors.Count == 0 && File.ReadAllBytes(UserData.File("settings.dat")).SequenceEqual(original),
                "Legacy operation failures are neither displayed nor replayed on isolated startup " + (startup + 1) + ", and loading does not rewrite saved content");
        }
        var window = new MainWindow(true, state); window.Show(); window.ShowPage("Nodes"); Layout(window);
        check(!window.Notification.Showing && !window.PageView.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == UiText.Get("Connection failed")),
            "A legacy subscription failure never appears under the subscription card or as a stale startup notification");
        var errors = new List<string>(); window.Controller.Error += errors.Add;
        var logs = new List<string>(); window.Controller.Logged += logs.Add;
        window.Controller.SourceHttpClientFactory = () => new(new FailureHandler());
        await window.Controller.ApplyAsync(window.Controller.State);
        var beforeFailure = File.ReadAllBytes(UserData.File("settings.dat"));
        await window.Controller.RunAsync(() => window.Controller.UpdateSubscriptionAsync(subscription.Id)); Layout(window);
        check(errors.Count == 1 && window.Notification.Showing && window.Notification.Message.Contains(subscription.Name, StringComparison.Ordinal)
            && window.Controller.State.Subscriptions.Single().LastError is null && window.Controller.State.Nodes.Single().Id == node.Id
            && window.Controller.State.Rules.SequenceEqual(state.Rules) && File.ReadAllBytes(UserData.File("settings.dat")).SequenceEqual(beforeFailure),
            "A synthetic manual subscription failure produces one named top notification and leaves nodes, rules and the encrypted profile unchanged");
        window.Notification.Dismiss(); errors.Clear(); logs.Clear();
        await window.Controller.RunAsync(() => window.Controller.UpdateAsync(automatic: true, subscriptions: true, providers: false)); Layout(window);
        check(errors.Count == 0 && !window.Notification.Showing && logs.Count == 1 && logs[0].Contains(subscription.Name, StringComparison.Ordinal)
            && window.Controller.State.Subscriptions.Single().LastError is null
            && File.ReadAllBytes(UserData.File("settings.dat")).SequenceEqual(beforeFailure),
            "A synthetic automatic subscription failure is logged silently without a notification, persistent card error or profile change");
        await window.Controller.RunAsync(() => window.Controller.UpdateAsync(subscriptions: true, providers: false)); Layout(window);
        check(errors.Count == 1 && window.Notification.Showing && window.Notification.Message.Contains(subscription.Name, StringComparison.Ordinal)
            && File.ReadAllBytes(UserData.File("settings.dat")).SequenceEqual(beforeFailure),
            "Manually refreshing all subscriptions still reports the same failed subscription after a silent automatic refresh");
        await window.Controller.ApplyAsync(window.Controller.State with { CatalogUrl = "https://catalog.example/" });
        errors.Clear(); window.Notification.Dismiss(); window.ShowPage("Providers");
        await window.Controller.RunAsync(() => window.Controller.UpdateAsync(automatic: true, subscriptions: true, providers: true)); Layout(window);
        check(errors.Count == 1 && window.Notification.Showing && window.Notification.Message.Contains(provider.Name, StringComparison.Ordinal)
            && !window.Notification.Message.Contains(subscription.Name, StringComparison.Ordinal)
            && window.Controller.State.Providers.Single().Error is null && window.Controller.State.Providers.Single().CheckedAt is not null
            && vault.Read<AppState>("settings.dat")!.Providers.Single().Error is null,
            "A combined automatic update does not leak silent subscription failures into directory and website notifications");
        check(!window.PageView.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(UiText.Get("Connection failed"), StringComparison.Ordinal) == true),
            "The airport page does not repeat operation failures inside its saved cards or result count");
        typeof(MainWindow).GetMethod("SearchProviders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);
        var search = window.ProviderSearchWindow!; window.Notification.Dismiss(); errors.Clear();
        window.Controller.SearchHttpClientFactory = () => new(new FailureHandler());
        try { await window.Controller.DiscoverAsync(window.Controller.State.Discovery.Search); }
        catch (HttpRequestException) { }
        Layout(search);
        check(errors.Count == 1 && search.Notification.Showing && !window.Notification.Showing && search.ErrorText is null
            && search.SearchButton.IsEnabled && window.Controller.State.ProviderSearchError is null,
            "An in-memory search failure appears in the visible search window's top notification with retry available and no duplicate inline error");
        search.Close(); window.Close(); await window.Controller.DisposeAsync();
    }
    private static void Layout(TopLevel window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private sealed class FailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head) throw new HttpRequestException("Synthetic website transport failure");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
        }
    }
}
