using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Tizi.Core;

namespace Tizi.Desktop;

// Called by the headless workflow suite, never by the normal application.
// All HTTP responses are in-memory fixtures and the encrypted profile must be under output/settings.
internal static class ProviderSearchChecks
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        var isolated = Path.Combine(Path.GetFullPath(output), "settings");
        if (!Path.GetFullPath(UserData.DirectoryPath).Equals(isolated, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Provider search checks require an isolated output/settings profile.");

        var transport = new FixtureTransport();
        await using var app = new AppController(preview: true) { SearchHttpClientFactory = transport.CreateClient };
        var options = new ProviderSearchOptions { ModeId = "budget", MonthlyBudget = 30, Protocols = ["Clash"] };
        var previous = new ProviderSite { Name = "Previously saved fixture", Url = "https://saved.provider.example/" };
        app.SetPreview(new AppState
        {
            AutoUpdate = false, AutoUpdateProviders = false, Providers = [previous],
            Discovery = new() { ApiKey = "synthetic-key-never-sent", Model = "fixture-model", Search = options }
        });

        var first = transport.Enqueue();
        var search = app.DiscoverAsync(options); var duplicate = app.DiscoverAsync(options);
        check(ReferenceEquals(search, duplicate), "Repeated provider search requests join the same task before the first HTTP request");
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(8));
        check(app.SearchBusy && !app.Busy && transport.Requests == 1 && app.SearchCancellation.CanBeCanceled,
            "A pending provider request has independent cancellable search state and holds no global operation");
        check(app.State.Providers.SequenceEqual(new[] { previous }), "Previously saved providers stay visible throughout a pending search");

        var edited = new ProviderSite { Name = "Added during search", Url = "https://edited.provider.example/" };
        var node = NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@node.example:443#Synthetic")[0];
        var rule = new RoutingRule("DOMAIN", "edited.example", "DIRECT");
        await app.ExecuteAsync(() => app.ApplyAsync(app.State with
        {
            Mode = RouteMode.Direct, Nodes = [node], SelectedNode = node.Id, Rules = [rule], Providers = [previous, edited],
            Discovery = app.State.Discovery with { Model = "model-edited-during-search" }
        }));
        check(!search.IsCompleted && !app.Busy && app.SearchBusy, "An unrelated serialized operation can finish while provider HTTP is still pending");
        first.Release(Response("new.provider.example")); await search;
        check(!app.SearchBusy && !app.Busy && app.State.Nodes.Single() == node && app.State.Rules.Single() == rule
            && app.State.SelectedNode == node.Id && app.State.Mode == RouteMode.Direct && app.State.Discovery.Model == "model-edited-during-search",
            "Provider completion merges into the latest nodes, rules, selection, mode and search settings");
        check(app.State.Providers.Any(p => p.Id == previous.Id) && app.State.Providers.Any(p => p.Id == edited.Id)
            && app.State.Providers.Any(p => p.Url == "https://new.provider.example/"),
            "Successful provider search retains both old results and entries added during the request");
        var persisted = new Vault(isolated).Read<AppState>("settings.dat")!;
        check(persisted.Nodes.Single().Id == node.Id && JsonNode.DeepEquals(persisted.Nodes.Single().Options, node.Options) && persisted.Rules.Single() == rule && persisted.Providers.Count == 3,
            "The merged provider result and concurrent edits are persisted together in the isolated encrypted profile");

        var kept = app.State.Providers.ToArray();
        var failures = new List<string>(); app.Error += failures.Add;
        var failedReply = transport.Enqueue(); var failed = app.DiscoverAsync(options);
        await failedReply.Started.Task.WaitAsync(TimeSpan.FromSeconds(8)); failedReply.Release("{}", HttpStatusCode.ServiceUnavailable);
        try { await failed; throw new InvalidOperationException("The synthetic HTTP failure was not reported."); }
        catch (HttpRequestException) { }
        check(!app.SearchBusy && !app.Busy && app.SearchError?.Contains("503", StringComparison.Ordinal) == true
            && app.State.ProviderSearchError is null && app.State.Providers.SequenceEqual(kept) && failures.Count == 1 && failures[0] == app.SearchError,
            "Provider HTTP failure keeps saved results and emits one notification while retaining only a runtime retry error");
        check(new Vault(isolated).Read<AppState>("settings.dat")!.ProviderSearchError is null,
            "Provider search failure is never persisted into the encrypted content profile");

        var retryReply = transport.Enqueue(); var retry = app.RetrySearchAsync(); var duplicateRetry = app.RetrySearchAsync();
        check(ReferenceEquals(retry, duplicateRetry), "Repeated retry clicks reuse one provider request");
        await retryReply.Started.Task.WaitAsync(TimeSpan.FromSeconds(8)); retryReply.Release(Response("retry.provider.example")); await retry;
        check(app.SearchError is null && app.State.ProviderSearchError is null && app.State.Providers.Count == kept.Length + 1,
            "Successful retry clears the error and retains earlier cached providers");

        kept = app.State.Providers.ToArray();
        var lateReply = transport.Enqueue(ignoreCancellation: true); var cancelled = app.DiscoverAsync(options);
        await lateReply.Started.Task.WaitAsync(TimeSpan.FromSeconds(8)); var token = app.SearchCancellation;
        app.CancelSearch();
        check(token.IsCancellationRequested && app.SearchBusy && app.SearchCancelling
            && ReferenceEquals(cancelled, app.DiscoverAsync(options)),
            "Cancellation signals the active request and repeated clicks cannot start another request before it finishes");
        lateReply.Release(Response("late.provider.example")); await ExpectCancelled(cancelled);
        check(!app.SearchBusy && !app.Busy && app.SearchCancelled && app.SearchError is null && app.State.Providers.SequenceEqual(kept),
            "A late successful response after cancellation cannot replace or append saved providers");

        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = app.ExecuteAsync(() => releaseOperation.Task); var requestsBefore = transport.Requests;
        try
        {
            var queued = app.DiscoverAsync(options); app.CancelSearch(); await ExpectCancelled(queued);
            check(app.Busy && !app.SearchBusy && transport.Requests == requestsBefore,
                "Cancelling a search waiting for its short initial commit leaves the existing operation busy and sends no HTTP request");
        }
        finally { releaseOperation.TrySetResult(); await operation; }

        CheckDialogStates(options, check);
    }
    private static void CheckDialogStates(ProviderSearchOptions options, Action<bool, string> check)
    {
        var cancellations = 0;
        var dialog = new ProviderSearchDialog(options, _ => Task.CompletedTask, _ => Task.CompletedTask, () => cancellations++);
        dialog.Show();
        dialog.SetSearchState(true, null);
        check(!dialog.SearchButton.IsEnabled && !dialog.SaveButton.IsEnabled && dialog.CancelButton.IsEnabled
            && dialog.StatusText == UiText.Get("Searching providers… Previous results remain available."),
            "The provider search dialog displays pending feedback, blocks duplicate submissions and offers cancel");
        dialog.SetSearchState(true, null, cancelling: true);
        check(!dialog.CancelButton.IsEnabled && dialog.StatusText == UiText.Get("Cancelling search…"),
            "Cancelling provider search has explicit progress and cannot be repeated");
        dialog.SetSearchState(false, "Search timed out; previous results kept.");
        check(dialog.SearchButton.IsEnabled && dialog.SaveButton.IsEnabled && dialog.ErrorText is null
            && ((TextBlock)dialog.SearchButton.Content!).Text == UiText.Get("Retry search · uses API credits") && dialog.Criteria.ModeId == options.ModeId,
            "Provider search failure keeps editable criteria and a retry action without duplicating the notification inline");
        dialog.SetSearchState(false, null, cancelled: true);
        check(dialog.ErrorText is null && dialog.StatusText == UiText.Get("Search cancelled; previous results kept."),
            "User cancellation has neutral feedback rather than a failure message");
        dialog.SetSearchState(true, null); dialog.Close();
        check(cancellations == 1 && dialog.Cancellation.IsCancellationRequested, "Closing the modeless search window cancels the active search and its criteria session");
    }
    private static async Task ExpectCancelled(Task task)
    {
        try { await task; throw new InvalidOperationException("The synthetic provider search was not cancelled."); }
        catch (OperationCanceledException) { }
    }
    private static string Response(string host)
    {
        var url = "https://" + host + "/"; var source = url + "guide";
        var provider = new
        {
            name = "Synthetic provider", url, description = "In-memory search fixture", serviceKind = "personal_subscription",
            protocols = new[] { "Clash" }, subscriptionSource = source, regions = new[] { "Fixture" }, securityNotes = "Unverified",
            ipQualityNotes = "Unverified", sources = new[] { source }, plans = Array.Empty<object>(), reviewSummary = "", reviews = Array.Empty<object>()
        };
        return JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new object[]
            {
                new { type = "web_search_call", status = "completed", action = new { sources = new[] { new { url = source } } } },
                new { type = "message", content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(new { providers = new[] { provider } }) } } }
            }
        });
    }
    private sealed class FixtureTransport
    {
        private readonly Queue<FixtureReply> _replies = [];
        internal int Requests { get; private set; }
        internal FixtureReply Enqueue(bool ignoreCancellation = false)
        {
            var reply = new FixtureReply(ignoreCancellation); _replies.Enqueue(reply); return reply;
        }
        internal HttpClient CreateClient() => new(new FixtureHandler(this)) { Timeout = Timeout.InfiniteTimeSpan };
        private sealed class FixtureHandler(FixtureTransport transport) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri?.AbsoluteUri != "https://api.openai.com/v1/responses" || transport._replies.Count == 0)
                    throw new InvalidOperationException("Unexpected in-memory provider search request.");
                transport.Requests++; var reply = transport._replies.Dequeue(); reply.Started.TrySetResult();
                return reply.IgnoreCancellation ? await reply.Response.Task : await reply.Response.Task.WaitAsync(cancellationToken);
            }
        }
    }
    private sealed class FixtureReply(bool ignoreCancellation)
    {
        internal bool IgnoreCancellation { get; } = ignoreCancellation;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Release(string body, HttpStatusCode status = HttpStatusCode.OK) => Response.TrySetResult(new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
