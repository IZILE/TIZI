using System.Diagnostics;
using System.Security.Principal;
using System.Threading.Channels;
using Avalonia.Threading;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed partial class AppController : IAsyncDisposable
{
    private readonly Vault _vault;
    private readonly ProxyLease _lease;
    private readonly HttpClient _http = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8), MaxConnectionsPerServer = 4 }) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _operation = new(1), _save = new(1), _updates = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _logTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Channel<string> _logBuffer = Channel.CreateBounded<string>(new BoundedChannelOptions(1000) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private bool _closing, _connectionReady;
    private Task? _initialization;
    internal bool Ready { get; private set; }
    internal string? LoadFailure { get; private set; }
    internal bool ProfileExists { get; private set; }
    private DateTimeOffset _lastAuto = DateTimeOffset.MinValue;
    private DateTimeOffset _lastProviderAuto = DateTimeOffset.MinValue;
    internal EngineClient Engine { get; } = new();
    internal AppState State { get; private set; } = new();
    internal bool Busy { get; private set; }
    internal bool Updating { get; private set; }
    private readonly object _searchGate = new();
    private Task? _searchTask;
    private CancellationTokenSource? _searchCancellation;
    private ProviderSearchOptions? _lastSearchOptions;
    internal bool SearchBusy { get; private set; }
    internal bool SearchCancelling { get; private set; }
    internal bool SearchCancelled { get; private set; }
    internal string? SearchError { get; private set; }
    internal CancellationToken SearchCancellation { get; private set; }
    // A supplied transport lets isolated checks exercise the real search workflow without an API call.
    internal Func<HttpClient>? SearchHttpClientFactory { private get; set; }
    internal Func<HttpClient>? SourceHttpClientFactory { private get; set; }
    internal Action<bool, string, bool> LoginStartupWriter { private get; set; } = LoginStartup.Set;
    internal bool Connected => _connectionReady && Engine.IsRunning;
    private long? _connectedTimestamp;
    internal TimeSpan? ConnectedDuration => Connected && _connectedTimestamp is { } started ? System.Diagnostics.Stopwatch.GetElapsedTime(started) : null;
    private ConnectionPhase? _connectionTransition;
    private CancellationTokenSource? _connectionCancellation;
    internal string? ConnectionFailure { get; private set; }
    internal ConnectionPhase ConnectionState => _connectionTransition ?? (ConnectionFailure is not null ? ConnectionPhase.Failed : Connected ? ConnectionPhase.Connected : ConnectionPhase.Disconnected);
    internal CancellationToken Cancellation => _lifetime.Token;
    internal event Action? Changed;
    internal event Action<string>? Logged;
    internal event Action<string>? Error;
    internal AppController(bool preview = false)
    {
        _qualityPreview=preview;
        InitializeAutomaticSwitch();
        Ready = preview;
        _vault = new Vault(UserData.DirectoryPath); _lease = new ProxyLease(new WindowsProxyRegistry(), _vault);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) }; _timer.Tick += async (_, _) => await AutoUpdateAsync();
        Engine.Log += message => _logBuffer.Writer.TryWrite(message);
        _logTimer.Tick += (_, _) => { for (var i = 0; i < 200 && _logBuffer.Reader.TryRead(out var line); i++) Logged?.Invoke(UiText.Get(line)); };
        Engine.UnexpectedExit += () => Dispatcher.UIThread.Post(async () =>
        {
            if (_closing || !_connectionReady) return; // Startup failures are handled by ToggleAsync.
            _connectionReady = false; _connectedTimestamp = null;
            ConnectionFailure = UiText.Get("Proxy engine stopped unexpectedly.");
            await RunAsync(async () => { try { _lease.Restore(); await StoreConnectionLogAsync(ConnectionFailure); } finally { await Engine.StopAsync(); } });
            Error?.Invoke(ConnectionFailure);
        });
        if (!preview) { _timer.Start(); _logTimer.Start(); }
    }
    internal Task InitializeAsync() => _initialization ??= LoadAsync();
    private async Task LoadAsync()
    {
        try
        {
            ProfileExists = _vault.Exists("settings.dat");
            var loaded = await Task.Run(() => { _lease.Restore(); return _vault.Read<AppState>("settings.dat"); });
            if (loaded is not null) State = OperationFeedback.ClearSavedErrors(loaded with { IpQuality=IpQualitySettings.Normalize(loaded.IpQuality) });
            LoadFailure = null;
            Ready = true; NotifyChanged();
        }
        catch (Exception e)
        {
            LoadFailure = UiText.Get("Saved settings could not be opened. The original file has been kept.");
            Logged?.Invoke(LoadFailure + " " + e.GetType().Name); NotifyChanged(); Error?.Invoke(LoadFailure); return;
        }
        try
        {
            if (await Task.Run(() => _vault.Read<string[]>("last-connection-log.dat")) is { } previous)
                foreach (var line in previous) Logged?.Invoke(line);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        { Logged?.Invoke(UiText.Get("The previous connection log could not be read. Saved settings have been kept.")); }
    }
    internal void SetPreview(AppState state) { State = OperationFeedback.ClearSavedErrors(state); Ready = true; }
    internal async Task RunAsync(Func<Task> action)
    {
        try { await ExecuteAsync(action); }
        catch (OperationCanceledException) { if (!_closing) Error?.Invoke(UiText.Get("The operation timed out or was cancelled.")); }
        catch (Exception error) { Error?.Invoke(error.Message); }
    }
    internal async Task ExecuteAsync(Func<Task> action)
    {
        if (_closing) throw new InvalidOperationException(UiText.Get("Application is closing."));
        if (!Ready) throw new InvalidOperationException(UiText.Get("Loading saved settings…"));
        if (!await _operation.WaitAsync(0)) throw new InvalidOperationException(UiText.Get("Another operation is in progress."));
        Busy = true; NotifyChanged();
        try { await action(); }
        finally { Busy = false; NotifyChanged(); _operation.Release(); }
    }
    internal async Task ApplyAsync(AppState state, bool reload = false)
    {
        if (_closing) return;
        state = OperationFeedback.ClearSavedErrors(state with { IpQuality=IpQualitySettings.Normalize(state.IpQuality) });
        if (Connected && (state.MixedPort != State.MixedPort || state.OutboundInterface != State.OutboundInterface))
            throw new InvalidOperationException(UiText.Get("Disconnect before changing the local port or outbound interface."));
        ModelValidation.Rules(state.Rules);
        AdvancedValidation.Check(state);
        if (reload && Connected)
        {
            if (state.Nodes.Count == 0 && state.Mode != RouteMode.Direct) throw new InvalidOperationException(UiText.Get("Disconnect before removing the last node."));
            await Engine.ReloadAsync(NetworkEnvironment.ConnectionSettings(state), _lifetime.Token);
        }
        // Keep the visible state on the last durable profile if a write fails.
        await SaveStateAsync(state); State = state; NotifyChanged();
    }
    internal async Task UpdateGeneralPreferencesAsync(GeneralPreferences preferences)
    {
        // Read the current profile after obtaining the operation gate, so a quick
        // series of choices cannot overwrite another completed import or edit.
        await _operation.WaitAsync(_lifetime.Token);
        try
        {
            if (_closing) throw new InvalidOperationException(UiText.Get("Application is closing."));
            if (!Ready) throw new InvalidOperationException(UiText.Get("Loading saved settings…"));
            var previous = State;
            if (preferences == GeneralPreferences.From(previous)) return;
            Busy = true; NotifyChanged();
            var next = previous with { StartAtLogin = preferences.StartAtLogin, StartHidden = preferences.StartHidden, RestoreConnection = preferences.RestoreConnection, CloseToTray = preferences.CloseToTray };
            var startupChanged = next.StartAtLogin != previous.StartAtLogin || next.StartHidden != previous.StartHidden;
            var executable = Environment.ProcessPath ?? throw new IOException("Application path unavailable.");
            if (startupChanged) LoginStartupWriter(next.StartAtLogin, executable, next.StartHidden);
            try { await ApplyAsync(next); }
            catch
            {
                if (startupChanged) LoginStartupWriter(previous.StartAtLogin, executable, previous.StartHidden);
                throw;
            }
        }
        finally { Busy = false; NotifyChanged(); _operation.Release(); }
    }
    private async Task SaveAsync()
    {
        await SaveStateAsync(State);
    }
    private async Task SaveStateAsync(AppState state)
    {
        await _save.WaitAsync();
        try { await Task.Run(() => _vault.Write("settings.dat", state)); }
        finally { _save.Release(); }
    }
    internal async Task ToggleAsync()
    {
        if (DesktopChecks.Active) { Error?.Invoke("Desktop verification mode: node connections are disabled."); return; }
        using var cancellation = !Connected ? CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token) : null;
        _connectionCancellation = cancellation;
        ConnectionFailure = null;
        _connectionTransition = Connected ? ConnectionPhase.Disconnecting : ConnectionPhase.Connecting;
        NotifyChanged();
        try { await ToggleConnectionAsync(cancellation?.Token ?? _lifetime.Token); }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
            ConnectionFailure = null;
            if (!_closing)
            {
                await ApplyAsync(State with { LastConnected = false });
                Logged?.Invoke(UiText.Get("Connection cancelled."));
            }
        }
        catch (Exception error) { if (!_closing) ConnectionFailure = UiText.Get(error.Message); throw; }
        finally { _connectionCancellation = null; _connectionTransition = null; NotifyChanged(); }
    }
    internal bool CancelConnection()
    {
        // Cancellation must bypass the operation gate held by the connect task.
        if (_closing || _connectionTransition != ConnectionPhase.Connecting || _connectionCancellation is not { } cancellation) return false;
        _connectionTransition = ConnectionPhase.Cancelling;
        cancellation.Cancel(); NotifyChanged(); return true;
    }
    private async Task ToggleConnectionAsync(CancellationToken cancellation)
    {
        if (Connected) { _connectionReady = false; _connectedTimestamp = null; try { _lease.Restore(); } finally { await Engine.StopAsync(); } await ApplyAsync(State with { LastConnected = false }); Logged?.Invoke(UiText.Get("Disconnected.")); return; }
        cancellation.ThrowIfCancellationRequested();
        if (State.Nodes.Count == 0 && State.Mode != RouteMode.Direct) throw new InvalidOperationException(UiText.Get("Import a node before connecting."));
        if (State.Capture == CaptureMode.Tun && !IsAdministrator) throw new InvalidOperationException(UiText.Get("TUN requires administrator rights."));
        if (State.Capture == CaptureMode.Tun && NetworkEnvironment.OtherTunnels() is { Length: > 0 } tunnels)
            throw new InvalidOperationException(UiText.Format("Another VPN is using a default route: {0}. Turn off its TUN mode before enabling TIZI TUN.", string.Join(", ", tunnels)));
        var connection = NetworkEnvironment.ConnectionSettings(State);
        var checkingNode = false;
        try
        {
            Logged?.Invoke(UiText.Get("Checking the node before changing system networking…"));
            await Engine.StartAsync(connection, Path.Combine(AppContext.BaseDirectory, "engine/mihomo.exe"), Path.Combine(UserData.DirectoryPath, "run"), cancellation, deferTun: true);
            StartWatchdog();
            checkingNode = true;
            try { await Engine.CheckConnectionAsync(State.DelayUrl, cancellation); }
            catch (Exception error) when (!cancellation.IsCancellationRequested && error is (HttpRequestException or OperationCanceledException))
            {
                if (State.DelayUrl == "https://www.cloudflare.com/cdn-cgi/trace") throw;
                await Engine.CheckConnectionAsync("https://www.cloudflare.com/cdn-cgi/trace", cancellation);
            }
            checkingNode = false;
            cancellation.ThrowIfCancellationRequested();
            if (State.Capture == CaptureMode.Tun) await Engine.EnableTunAsync(connection, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (State.Capture == CaptureMode.SystemProxy) _lease.Acquire(State.MixedPort);
            await StoreConnectionLogAsync(UiText.Get("Proxy engine ready."));
            cancellation.ThrowIfCancellationRequested();
            _connectedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(); _connectionReady = true;
            await ApplyAsync(State with { LastConnected = true });
            cancellation.ThrowIfCancellationRequested();
        }
        catch (Exception error)
        {
            _connectionReady = false; _connectedTimestamp = null;
            var message = checkingNode && error is (HttpRequestException or OperationCanceledException) ? UiText.Get("The selected node did not pass the connection check. System proxy and TUN were not enabled. Try another node; details are in Logs.") : UiText.Get(error.Message);
            if (error.Message.StartsWith("TUN could not be enabled.", StringComparison.Ordinal)) message = UiText.Get("TUN could not be enabled. Check administrator rights and the connection log.");
            var cancelled = cancellation.IsCancellationRequested;
            var diagnostic = error is ProxySettingsRejectedException rejection ? error.Message + " [" + string.Join(", ", rejection.MismatchedOptions) + "]" : error.Message;
            try { await StoreConnectionLogAsync(cancelled ? UiText.Get("Connection cancelled.") : diagnostic); }
            finally { try { _lease.Restore(); } finally { await Engine.StopAsync(); } }
            if (cancelled) throw new OperationCanceledException(cancellation);
            throw new IOException(message, error);
        }
    }
    private Task StoreConnectionLogAsync(string result)
    {
        var log = new[] { DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " · " + result }.Concat(Engine.RecentLogs).ToArray();
        return Task.Run(() => { try { _vault.Write("last-connection-log.dat", log); } catch (IOException) { } catch (UnauthorizedAccessException) { } });
    }
    internal async Task SelectAsync(ProxyNode node)
    {
        node = State.Nodes.FirstOrDefault(n => n.Id == node.Id) ?? throw new InvalidOperationException(UiText.Get("This node has changed. Reopen it to edit."));
        if (Connected) await Engine.SelectNodeAsync(node, _lifetime.Token);
        await ApplyAsync(State with { SelectedNode = node.Id });
    }
    internal async Task ModeAsync(RouteMode mode)
    {
        if (Connected) await Engine.SetModeAsync(mode, _lifetime.Token);
        await ApplyAsync(State with { Mode = mode });
    }
    internal Task<ImportDocument> ParseImportAsync(string text, CancellationToken cancellation = default) => Task.Run(() => ImportDocument.Parse(text), cancellation.CanBeCanceled ? cancellation : _lifetime.Token);
    internal async Task ImportAsync(string text) => await ApplyImportAsync(await ParseImportAsync(text));
    internal async Task ApplyImportAsync(ImportDocument document, Func<ImportDocument, Task<bool>>? review = null, CancellationToken cancellation = default, Action? beforeCommit = null)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellation);
        stop.Token.ThrowIfCancellationRequested();
        var nodes = document.Nodes.ToList(); var subscriptions = new List<Subscription>();
        var rules = document.Rules.ToList(); var warnings = document.Warnings.ToList();
        foreach (var imported in document.Subscriptions.DistinctBy(s => SourceUpdates.Normalize(s.Url)))
        {
            var subscription = State.Subscriptions.FirstOrDefault(s => SourceUpdates.Normalize(s.Url) == SourceUpdates.Normalize(imported.Url)) ?? imported;
            if (SubscriptionNaming.IsPlaceholder(subscription) && !SubscriptionNaming.IsPlaceholder(imported)) subscription = subscription with { Name = imported.Name };
            var refreshed = await new SourceUpdates(_http).RefreshSubscriptionAsync(subscription, State.Nodes, stop.Token);
            nodes.AddRange(refreshed.Nodes); subscriptions.Add(refreshed.Subscription); rules.AddRange(refreshed.Rules); warnings.AddRange(refreshed.Warnings);
        }
        var replaced = subscriptions.Select(s => s.Id).ToHashSet();
        var allNodes = State.Nodes.Where(n => !replaced.Contains(n.SubscriptionId ?? Guid.Empty)).Concat(nodes).ToList();
        var allSubscriptions = State.Subscriptions.Where(s => !replaced.Contains(s.Id)).Concat(subscriptions).ToList();
        if (allNodes.Count > NodeImport.MaxNodes) throw new FormatException("Maximum 3000 nodes.");
        if (allSubscriptions.Count > 100) throw new FormatException("Maximum 100 subscriptions.");
        var prepared = new ImportDocument(nodes, rules, subscriptions, warnings.Distinct().ToList(), document.Format);
        if (prepared.Warnings.Count > 0 && review is not null && !await review(prepared)) throw new ImportReviewCancelledException();
        stop.Token.ThrowIfCancellationRequested();
        var selected = allNodes.Any(n => n.Id == State.SelectedNode) ? State.SelectedNode : allNodes.FirstOrDefault()?.Id;
        beforeCommit?.Invoke();
        await ApplyAsync(State with { Nodes = allNodes, Rules = RuleImport.Merge(State.Rules, rules), Subscriptions = allSubscriptions, SelectedNode = selected }, reload: true);
        await Task.Run(() => _vault.Write("last-import-report.dat", prepared.Warnings));
        Logged?.Invoke(UiText.Format("Imported {0} nodes.", nodes.Count));
    }
    internal Task AddSubscriptionAsync(string name, string url, Func<ImportDocument, Task<bool>>? review = null, CancellationToken cancellation = default, Action? beforeCommit = null)
    {
        var subscription = ImportDocument.RemoteSubscription(url) ?? throw new FormatException("Enter a valid HTTP or HTTPS address without a fragment or embedded login.");
        if (!string.IsNullOrWhiteSpace(name) && !name.Equals(new Uri(subscription.Url).Host, StringComparison.OrdinalIgnoreCase)) subscription = subscription with { Name = name };
        return ApplyImportAsync(new([], [], [subscription], [], "Subscription URL"), review, cancellation, beforeCommit);
    }
    internal async Task UpdateSubscriptionAsync(Guid id)
    {
        var subscription = State.Subscriptions.Single(s => s.Id == id);
        try
        {
            using var fixtureHttp = SourceHttpClientFactory?.Invoke();
            var result = await new SourceUpdates(fixtureHttp ?? _http).RefreshSubscriptionAsync(subscription, State.Nodes, _lifetime.Token);
            var nodes = State.Nodes.Where(n => n.SubscriptionId != id).Concat(result.Nodes).ToList();
            if (nodes.Count > NodeImport.MaxNodes) throw new FormatException("Maximum 3000 nodes.");
            await ApplyAsync(State with { Nodes = nodes, Subscriptions = State.Subscriptions.Select(s => s.Id == id ? result.Subscription : s).ToList(), SelectedNode = nodes.Any(n => n.Id == State.SelectedNode) ? State.SelectedNode : nodes.FirstOrDefault()?.Id }, reload: true);
        }
        catch (Exception error) when (!_lifetime.IsCancellationRequested)
        {
            var message = SubscriptionUpdateFailure(subscription, error);
            Logged?.Invoke(message); throw new IOException(message, error);
        }
    }
    private static string SubscriptionUpdateFailure(Subscription subscription, Exception error) => UiText.Format("Subscription {0}: {1}", subscription.Name,
        UiText.Get(error is HttpRequestException or OperationCanceledException or TimeoutException ? "Connection failed" : "Update rejected; previous nodes kept"));
    internal Task DiscoverAsync(ProviderSearchOptions options)
    {
        TaskCompletionSource completion;
        CancellationTokenSource cancellation;
        string query;
        lock (_searchGate)
        {
            // Reserve the task before raising Changed, so repeated clicks and reentrant handlers join it.
            if (_searchTask is not null) return _searchTask;
            if (_closing) return Task.FromException(new InvalidOperationException(UiText.Get("Application is closing.")));
            if (!Ready) return Task.FromException(new InvalidOperationException(UiText.Get("Loading saved settings…")));
            options = ProviderSearchModes.Validate(options); query = ProviderSearchModes.BuildQuery(options);
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _searchCancellation = cancellation; _searchTask = completion.Task; _lastSearchOptions = options;
            SearchCancellation = cancellation.Token; SearchBusy = true; SearchCancelling = SearchCancelled = false; SearchError = null;
        }
        _ = DiscoverCoreAsync(options, query, cancellation, completion);
        return completion.Task;
    }
    internal async Task SaveSearchCriteriaAsync(ProviderSearchOptions options, CancellationToken cancellation = default)
    {
        options = ProviderSearchModes.Validate(options);
        if (_closing) throw new InvalidOperationException(UiText.Get("Application is closing."));
        if (!Ready) throw new InvalidOperationException(UiText.Get("Loading saved settings…"));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellation);
        await CommitLatestAsync(state => state with { Discovery = state.Discovery with { Search = options } }, linked.Token);
    }
    internal Task RetrySearchAsync()
    {
        ProviderSearchOptions options;
        lock (_searchGate) options = _lastSearchOptions ?? State.Discovery.Search;
        return DiscoverAsync(options);
    }
    internal void CancelSearch()
    {
        CancellationTokenSource? cancellation;
        lock (_searchGate)
        {
            cancellation = _searchCancellation;
            if (cancellation is null || cancellation.IsCancellationRequested) return;
            SearchCancelling = true;
        }
        // Completion can dispose the source between leaving the gate and this call.
        try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
        if (!_closing) NotifyChanged();
    }
    private async Task DiscoverCoreAsync(ProviderSearchOptions options, string query, CancellationTokenSource cancellation, TaskCompletionSource completion)
    {
        var token = cancellation.Token; Exception? failure = null; var cancelled = false;
        try
        {
            NotifyChanged();
            var requestState = await CommitLatestAsync(state => state with { Discovery = state.Discovery with { Search = options }, ProviderSearchError = null }, token);
            token.ThrowIfCancellationRequested();
            using var http = SearchHttpClientFactory?.Invoke() ?? new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(12) }) { Timeout = Timeout.InfiniteTimeSpan };
            // No controller operation lock is held while the remote search is pending.
            var candidates=requestState.Providers.SelectMany(p=>new[]{p.Url}.Concat(p.Profile?.Sources??[]).Concat(new[]{p.Profile?.SubscriptionSource??""})).Distinct().ToArray();
            var found = await new ProviderDiscovery(http,candidates:candidates).SearchAsync(requestState.Discovery, query, cancellation: token);
            await CommitLatestAsync(state => ProviderDirectory.CacheSearch(state, found, options, DateTimeOffset.Now)
                with { Discovery = state.Discovery with { Query = query } }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled = true; }
        catch (Exception error)
        {
            failure = error is OperationCanceledException ? new TimeoutException("Search timed out; previous results kept.", error) : error;
            SearchError = failure.Message;
        }
        finally
        {
            lock (_searchGate)
            {
                _searchTask = null; _searchCancellation = null; SearchCancellation = CancellationToken.None;
                SearchBusy = SearchCancelling = false; SearchCancelled = cancelled;
            }
            cancellation.Dispose();
            if (!_closing) NotifyChanged();
        }
        if (cancelled) completion.TrySetCanceled(token);
        else if (failure is not null)
        {
            if (!_closing) { Logged?.Invoke(failure.Message); Error?.Invoke(failure.Message); }
            completion.TrySetException(failure);
        }
        else completion.TrySetResult();
    }
    private async Task<AppState> CommitLatestAsync(Func<AppState, AppState?> edit, CancellationToken cancellation, bool reload = false)
    {
        await _operation.WaitAsync(cancellation);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (_closing) throw new OperationCanceledException(cancellation);
            // Read State after acquiring the gate; never write a pre-request snapshot over later edits.
            var next = edit(State); if (next is null) return State;
            Busy = true; NotifyChanged();
            await ApplyAsync(next, reload); return next;
        }
        finally { Busy = false; _operation.Release(); if (!_closing) NotifyChanged(); }
    }
    internal async Task UpdateAsync(bool automatic = false, bool subscriptions = true, bool providers = true)
    {
        if (_closing || !await _updates.WaitAsync(0)) return;
        Updating = true; NotifyChanged();
        var notifications = new List<string>();
        void Failure(string message, bool notify = true) { if (notify) notifications.Add(message); Logged?.Invoke(message); }
        async Task ApplyUpdateAsync(Func<AppState, AppState?> edit, bool reload = false)
        {
            if (automatic) await CommitLatestAsync(edit, _lifetime.Token, reload);
            else if (edit(State) is { } next) await ApplyAsync(next, reload);
        }
        try
        {
            using var fixtureHttp = SourceHttpClientFactory?.Invoke();
            var sources = new SourceUpdates(fixtureHttp ?? _http);
            foreach (var subscription in subscriptions ? State.Subscriptions.ToArray() : [])
            {
                try
                {
                    var previous = State.Nodes.Where(n => n.SubscriptionId == subscription.Id).ToArray();
                    var refresh = await sources.RefreshSubscriptionAsync(subscription, previous, _lifetime.Token);
                    await ApplyUpdateAsync(latest =>
                    {
                        // Removing or editing this subscription while HTTP is pending invalidates that response.
                        if (!latest.Subscriptions.Contains(subscription) || !latest.Nodes.Where(n => n.SubscriptionId == subscription.Id).SequenceEqual(previous)) return null;
                        var all = latest.Nodes.Where(n => n.SubscriptionId != subscription.Id).Concat(refresh.Nodes).ToList();
                        if (all.Count > NodeImport.MaxNodes) throw new FormatException("Maximum 3000 nodes.");
                        var selected = all.Any(n => n.Id == latest.SelectedNode) ? latest.SelectedNode : all.FirstOrDefault()?.Id;
                        return latest with { Nodes = all, SelectedNode = selected, Subscriptions = latest.Subscriptions.Select(s => s.Id == subscription.Id ? refresh.Subscription : s).ToList() };
                    }, reload: true);
                }
                catch (Exception e) when (!_lifetime.IsCancellationRequested) { Failure(SubscriptionUpdateFailure(subscription, e), notify: !automatic); }
            }
            if (providers && !string.IsNullOrWhiteSpace(State.CatalogUrl))
            {
                try
                {
                    var url = State.CatalogUrl;
                    var previous = State.Providers.Where(p => p.Source == url).ToArray();
                    var remote = await sources.CatalogAsync(url, State.Providers, _lifetime.Token);
                    await ApplyUpdateAsync(latest =>
                    {
                        if (url != latest.CatalogUrl || !latest.Providers.Where(p => p.Source == url).SequenceEqual(previous)) return null;
                        var retained = latest.Providers.Where(p => p.Source != url).ToList(); var retainedUrls = retained.Select(p => SourceUpdates.Normalize(p.Url)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        return latest with { Providers = [.. retained, .. remote.Where(p => !retainedUrls.Contains(SourceUpdates.Normalize(p.Url)))], CatalogUpdatedAt = DateTimeOffset.Now };
                    });
                }
                catch (Exception) when (!_lifetime.IsCancellationRequested) { Failure(UiText.Get("Directory update failed; previous entries kept."), notify: !automatic); }
            }
            var updated = new List<ProviderSite>();
            // Four probes at a time; do not hold up editing or generate a traffic burst.
            foreach (var batch in (providers ? State.Providers.ToArray() : []).Chunk(4)) updated.AddRange(await Task.WhenAll(batch.Select(p => sources.CheckAsync(p, _lifetime.Token))));
            var checkedSites = updated.ToDictionary(p => p.Id);
            foreach (var site in updated.Where(p => p.Error is not null)) Failure(UiText.Format("Website access check {0}: {1}", site.Name, UiText.Get(site.Error!)), notify: !automatic);
            if (checkedSites.Count > 0)
                await ApplyUpdateAsync(latest => latest with { Providers = latest.Providers.Select(p => checkedSites.TryGetValue(p.Id, out var found) && p.Url == found.Url ? p with { CheckedAt = found.CheckedAt, StatusCode = found.StatusCode, LatencyMs = found.LatencyMs, Error = null } : p).ToList() });
            if (subscriptions) _lastAuto = DateTimeOffset.Now;
            if (providers) _lastProviderAuto = DateTimeOffset.Now;
        }
        finally
        {
            Updating = false; NotifyChanged(); _updates.Release();
            if (!_closing && notifications.Count > 0)
                Error?.Invoke(string.Join('\n', notifications.Take(5)) + (notifications.Count > 5 ? "\n" + UiText.Format("{0} additional failures. See Logs.", notifications.Count - 5) : ""));
        }
    }
    private async Task AutoUpdateAsync()
    {
        if (_closing || !Ready || Busy || Updating) return;
        var subscriptions = State.AutoUpdate && DateTimeOffset.Now - _lastAuto >= TimeSpan.FromMinutes(Math.Clamp(State.UpdateMinutes, 5, 1440));
        var providers = State.AutomaticWebsiteChecksOptIn && State.AutoUpdateProviders && DateTimeOffset.Now - _lastProviderAuto >= TimeSpan.FromMinutes(Math.Clamp(State.ProviderUpdateMinutes, 5, 1440));
        // HTTP waits are background work. Only their short atomic commits acquire the operation gate.
        if (subscriptions || providers)
            try { await UpdateAsync(true, subscriptions, providers); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception error) when (!_closing) { Logged?.Invoke(error.Message); }
    }
    private void StartWatchdog()=>StartProxyMonitor("--watchdog",Engine.ProcessId,Engine.ProcessStartTicks,Engine.RuntimeDirectory);
    private void StartProxyMonitor(string mode,int engineId,long engineTicks,string runtime)
    {
        var exe = Environment.ProcessPath ?? throw new IOException("Application path unavailable.");
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return;
        using var parent = Process.GetCurrentProcess();
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { mode, parent.Id.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString(), engineId.ToString(), engineTicks.ToString(), UserData.DirectoryPath, runtime }) info.ArgumentList.Add(arg);
        using var child = Process.Start(info) ?? throw new IOException("Could not start proxy recovery monitor.");
    }
    internal static bool IsAdministrator
    {
        get { if (!OperatingSystem.IsWindows()) return false; using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
    }
    public async ValueTask DisposeAsync()
    {
        await StopAutomaticSwitchAsync();
        if (Ready && !_closing && State.LastConnected != Connected) { State = State with { LastConnected = Connected }; await SaveAsync(); }
        _closing = true; _connectionReady = false; _connectedTimestamp = null; _timer.Stop(); _logTimer.Stop(); _lifetime.Cancel();
        _qualityCancellation?.Cancel();await _qualityTask;
        await _operation.WaitAsync(); await _updates.WaitAsync();
        try { try { _lease.Restore(); } finally { await Engine.DisposeAsync(); } }
        finally { _operation.Release(); _updates.Release(); _http.Dispose(); _lifetime.Dispose(); }
    }
}
