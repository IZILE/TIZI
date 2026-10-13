using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Tizi.Core;

namespace Tizi.Desktop;

// Isolated startup regression: local fixture nodes and a separate DPAPI vault.
// It never enables a system proxy, starts a TUN, or contacts a remote node.
internal static partial class ConnectionChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        Directory.CreateDirectory(output); File.Delete(Path.Combine(output, "connection-check-failure.txt"));
        var checks = new List<string>();
        void Check(bool passed, string description)
        { if (!passed) throw new InvalidOperationException(description); checks.Add(description); }
        var originalProxy = new WindowsProxyRegistry().Read();
        try
        {
            await CheckCancellationAsync(Check);
            await using (var fixture = new ProxyFixture())
            await using (var app = new AppController(preview: true))
            {
                var node = NodeImport.FromOptions(new JsonObject { ["name"] = "Local fixture", ["type"] = "http", ["server"] = "127.0.0.1", ["port"] = fixture.Port });
                var states = new List<ConnectionPhase>(); app.Changed += () => states.Add(app.ConnectionState);
                await app.ApplyAsync(new AppState { Nodes = [node], SelectedNode = node.Id, Capture = CaptureMode.LocalOnly, Mode = RouteMode.Global, MixedPort = EngineClient.FreePort(), DelayUrl = "http://example.test/connection-check", AutoUpdate = false });
                var connect = app.ExecuteAsync(app.ToggleAsync);
                await fixture.Observed.Task.WaitAsync(TimeSpan.FromSeconds(20));
                Check(app.Engine.IsRunning && app.Busy && !app.Connected, "Engine startup does not show Connected before the webpage check completes");
                Check(app.ConnectionState == ConnectionPhase.Connecting, "Connection state remains Connecting while the webpage check is pending");
                fixture.Reply.TrySetResult(true); await connect;
                Check(app.Connected && !app.Busy, "Successful proxy webpage response changes the application to Connected");
                Check(app.ConnectionState == ConnectionPhase.Connected, "Successful connection exposes the Connected button state");
                Check(app.State.LastConnected && new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.LastConnected, "Successful connection persists the restore-on-next-start intent");
                var duration = app.ConnectedDuration; await Task.Delay(40);
                Check(duration is not null && app.ConnectedDuration > duration && app.ConnectedDuration < TimeSpan.FromMinutes(1), "This session duration starts only after successful capture and advances while connected");
                var runtime = app.Engine.RuntimeDirectory;
                await app.ExecuteAsync(app.ToggleAsync);
                Check(!app.Connected && !app.Engine.IsRunning && !Directory.Exists(runtime), "Disconnect stops the engine and removes its temporary configuration");
                Check(states.Contains(ConnectionPhase.Disconnecting) && app.ConnectionState == ConnectionPhase.Disconnected, "Disconnecting is announced until cleanup completes, then state resets");
                Check(!app.State.LastConnected, "Explicit disconnect clears the next-start connection intent");
                Check(app.ConnectedDuration is null, "Disconnect clears the current session duration instead of accumulating a total");
            }
            await using (var fixture = new ProxyFixture())
            {
                var app = new AppController(preview: true);
                var node = NodeImport.FromOptions(new JsonObject { ["name"] = "Restore local fixture", ["type"] = "http", ["server"] = "127.0.0.1", ["port"] = fixture.Port });
                await app.ApplyAsync(new AppState { Nodes = [node], SelectedNode = node.Id, Capture = CaptureMode.LocalOnly, Mode = RouteMode.Global, MixedPort = EngineClient.FreePort(), DelayUrl = "http://example.test/connection-check", AutoUpdate = false, AutoUpdateProviders = false });
                fixture.Reply.TrySetResult(true); await app.ExecuteAsync(app.ToggleAsync); await app.DisposeAsync();
                Check(new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")!.LastConnected && !app.Engine.IsRunning, "Exiting while connected preserves intent and stops the old engine");
                await using var reopened = new AppController(preview: true); await reopened.InitializeAsync();
                Check(reopened.State.LastConnected && !reopened.Connected, "Reopened profile retains intent without claiming a connection before its check");
                await reopened.ExecuteAsync(reopened.ToggleAsync); Check(reopened.Connected, "Saved profile reconnects successfully to the local fixture"); await reopened.ExecuteAsync(reopened.ToggleAsync);
            }
            await using (var fixture = new ProxyFixture())
            {
                var first = NodeImport.FromOptions(new JsonObject { ["name"] = "Offline HTTP first", ["type"] = "http", ["server"] = "127.0.0.1", ["port"] = fixture.Port });
                var second = first with { Id = Guid.NewGuid(), Name = "Offline HTTP second" };
                var offlineState = new AppState { Nodes = [first, second], SelectedNode = second.Id, Capture = CaptureMode.Tun, Mode = RouteMode.Rule, Rules = [new("DOMAIN", "private.example", "REJECT")], LastConnected = true, DelayUrl = "http://example.test/latency", AutoUpdate = false, AutoUpdateProviders = false };
                var beforeJson = JsonSerializer.Serialize(offlineState);
                string probeRuntime;
                await using (var probe = await NodeLatencyProbe.StartAsync(offlineState, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "engine", "mihomo.exe")), Path.Combine(output, "offline-probe")))
                {
                    probeRuntime = probe.RuntimeDirectory;
                    var config = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(probeRuntime, "config.json")))!;
                    Check(config["tun"]!["enable"]!.GetValue<bool>() == false && config["bind-address"]!.GetValue<string>() == "127.0.0.1" && config["mode"]!.GetValue<string>() == "direct", "Disconnected HTTP latency starts only a temporary loopback engine even when the saved capture mode is TUN");
                    var delays = Task.WhenAll(new[] { first, second }.Select(n => probe.MeasureAsync(n, offlineState.DelayUrl)));
                    await fixture.Observed.Task.WaitAsync(TimeSpan.FromSeconds(20)); fixture.Reply.TrySetResult(true);
                    Check((await delays).All(ms => ms >= 0), "Disconnected HTTP latency measures both fixture nodes through their actual HTTP proxy outbound");
                    Check(JsonSerializer.Serialize(offlineState) == beforeJson, "Offline measurements do not change selected node, routing rules, TUN preference, or last-connection intent");
                }
                Check(!Directory.Exists(probeRuntime), "Finishing offline latency stops the temporary engine and removes its configuration");
                var offlineWindow = new MainWindow(true, offlineState); offlineWindow.Show(); offlineWindow.ShowPage("Nodes");
                try
                {
                    var profileBefore = offlineWindow.Controller.State;
                    await offlineWindow.TestLatencyAsync();
                    Check(!offlineWindow.TestingLatency && !offlineWindow.Controller.Connected && !offlineWindow.Controller.Engine.IsRunning && ReferenceEquals(profileBefore, offlineWindow.Controller.State), "The real Test latency action works while disconnected and leaves the main connection controller idle");
                    Check(offlineWindow.SubscriptionView.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Text?.EndsWith(" ms", StringComparison.Ordinal) == true) >= 2, "Offline measurement results appear on both node rows");
                }
                finally { offlineWindow.Close(); await offlineWindow.Controller.DisposeAsync(); }
            }
            await using (var fixture = new ProxyFixture())
            {
                var node = NodeImport.FromOptions(new JsonObject { ["name"] = "Cancel offline HTTP", ["type"] = "http", ["server"] = "127.0.0.1", ["port"] = fixture.Port });
                var cancelledWindow = new MainWindow(true, new AppState { Nodes = [node], SelectedNode = node.Id, Capture = CaptureMode.Tun, DelayUrl = "http://example.test/cancel", AutoUpdate = false, AutoUpdateProviders = false }); cancelledWindow.Show(); cancelledWindow.ShowPage("Nodes");
                try
                {
                    var measuring = cancelledWindow.TestLatencyAsync(); await fixture.Observed.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    cancelledWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock t && t.Text == UiText.Get("Cancel test")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await measuring;
                    Check(!cancelledWindow.TestingLatency && !cancelledWindow.Controller.Engine.IsRunning && !Directory.EnumerateDirectories(Path.Combine(UserData.DirectoryPath, "latency"), "session-*").Any(), "Cancelling disconnected HTTP measurement cleans up its engine and credentials without changing the connection");
                }
                finally { cancelledWindow.Close(); await cancelledWindow.Controller.DisposeAsync(); }
            }
            var validationState = new AppState { Capture = CaptureMode.Tun, Dns = new() { RespectRules = true, Hosts = new() { ["local.example"] = "127.0.0.1" } }, Tunnel = new() { ExcludedRoutes = ["192.0.2.0/24"], StrictRoute = true }, Transport = new() { Udp = false, KeepAlive = false }, Logs = new() { Dns = true } };
            foreach (var stack in new[] { "gvisor", "system", "mixed" })
            {
                var configFile = Path.Combine(output, "advanced-config.json"); File.WriteAllText(configFile, EngineSession.Create(validationState with { Tunnel = validationState.Tunnel with { Stack = stack } }, EngineClient.FreePort()).Config);
                var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "engine/mihomo.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-t", "-d", Path.Combine(AppContext.BaseDirectory, "engine"), "-f", Path.GetFullPath(configFile) }) start.ArgumentList.Add(arg);
                using var engine = System.Diagnostics.Process.Start(start)!; var read = engine.StandardOutput.ReadToEndAsync(); var errors = engine.StandardError.ReadToEndAsync(); await engine.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check(engine.ExitCode == 0, "Pinned mihomo validates advanced DNS/TCP/UDP/TUN configuration for " + stack + ": " + (engine.ExitCode == 0 ? "OK" : await read + await errors)); File.Delete(configFile);
            }
            await using (var fixture = new ProxyFixture())
            await using (var app = new AppController(preview: true))
            {
                var node = NodeImport.FromOptions(new JsonObject { ["name"] = "Failing local fixture", ["type"] = "http", ["server"] = "127.0.0.1", ["port"] = fixture.Port });
                await app.ApplyAsync(new AppState { Nodes = [node], SelectedNode = node.Id, Capture = CaptureMode.LocalOnly, Mode = RouteMode.Global, MixedPort = EngineClient.FreePort(), DelayUrl = "http://example.test/connection-check", AutoUpdate = false });
                fixture.Reply.TrySetResult(false);
                var rejected = false;
                try { await app.ExecuteAsync(app.ToggleAsync); } catch (IOException) { rejected = true; }
                Check(rejected && !app.Connected && !app.Engine.IsRunning, "Failed webpage checks reject the connection and stop the engine");
                Check(app.ConnectionState == ConnectionPhase.Failed && app.ConnectionFailure is { Length: > 0 }, "Failed connection retains a retryable error state and its diagnostics");
                Check(new Vault(UserData.DirectoryPath).Read<string[]>("last-connection-log.dat") is { Length: > 0 }, "Failure diagnostics are saved in the encrypted test vault");
                await using var recovery = new ProxyFixture(); recovery.Reply.TrySetResult(true);
                var working = node with { Options = new JsonObject { ["name"] = "Recovered local fixture", ["type"] = "http", ["server"] = "127.0.0.1", ["port"] = recovery.Port } };
                await app.ApplyAsync(app.State with { Nodes = [working], SelectedNode = working.Id });
                await app.ExecuteAsync(app.ToggleAsync);
                Check(app.Connected && app.ConnectionFailure is null && app.ConnectionState == ConnectionPhase.Connected, "Retry clears the failed state only after the new local connection succeeds");
                // Terminate only the synthetic local-only engine started by this test controller.
                using (var localEngine = System.Diagnostics.Process.GetProcessById(app.Engine.ProcessId))
                {
                    Check(app.State.Capture == CaptureMode.LocalOnly && localEngine.StartTime.ToUniversalTime().Ticks == app.Engine.ProcessStartTicks && string.Equals(localEngine.MainModule?.FileName, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "engine", "mihomo.exe")), StringComparison.OrdinalIgnoreCase), "Unexpected-exit fixture identifies only its own local-only engine");
                    localEngine.Kill(); await localEngine.WaitForExitAsync();
                }
                using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (app.ConnectionState != ConnectionPhase.Failed || app.Busy) await Task.Delay(10, exitTimeout.Token);
                Check(!app.Connected && app.ConnectionState == ConnectionPhase.Failed && app.ConnectionFailure == UiText.Get("Proxy engine stopped unexpectedly."), "An unexpected local engine exit retains the red error state instead of silently returning to idle");
            }
            var damaged = new byte[] { 1, 2, 3, 4 };
            await File.WriteAllBytesAsync(UserData.File("last-connection-log.dat"), damaged);
            await using (var app = new AppController(preview: true))
            {
                var settingsFailed = false; app.Error += _ => settingsFailed = true;
                await app.InitializeAsync(); await app.ApplyAsync(app.State with { UpdateMinutes = 45 });
                Check(!settingsFailed && app.State.UpdateMinutes == 45, "Unreadable optional diagnostics do not prevent saved settings from opening or being edited");
                Check(File.ReadAllBytes(UserData.File("last-connection-log.dat")).SequenceEqual(damaged), "Unreadable diagnostic file is preserved");
            }
            var window = new MainWindow();
            window.Show();
            try
            {
                async Task Until(Func<bool> condition)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    while (!condition()) await Task.Delay(10, timeout.Token);
                }
                await Until(() => window.Controller.State.Nodes.Count == 1);
                foreach (var capture in new[] { CaptureMode.SystemProxy, CaptureMode.Tun, CaptureMode.LocalOnly, CaptureMode.Tun, CaptureMode.SystemProxy })
                {
                    window.CaptureControl.SelectedIndex = (int)capture;
                    await Until(() => !window.Controller.Busy && window.Controller.State.Capture == capture);
                    Check(window.CaptureControl.SelectedIndex == (int)capture && new Vault(UserData.DirectoryPath).Read<AppState>("settings.dat")?.Capture == capture,
                        $"Toolbar mode {capture} survives the Busy refresh and is saved");
                }
                Check(!window.Controller.Engine.IsRunning, "Changing the capture selector never connects a proxy or enables TUN");
                var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var busy = window.Controller.ExecuteAsync(() => pending.Task);
                Check(window.ConnectAction.Phase == ConnectionPhase.Disconnected && !window.ConnectAction.IsEnabled, "Unrelated Busy operations disable the action without showing a false Connecting state");
                Check(window.ModeControl.GetVisualDescendants().OfType<Button>().All(b => b.Opacity == 1) && window.CaptureControl.Opacity == 1, "Short saves do not dim routing controls while their input is blocked");
                pending.SetResult(); await busy;
            }
            finally { window.Close(); }
            await using (var latencyFixture = new ProxyFixture { DelayMilliseconds = 1500 })
            {
                var latencyNode = NodeImport.FromOptions(new JsonObject { ["name"] = "Overview latency fixture", ["type"] = "http", ["server"] = "127.0.0.1", ["port"] = latencyFixture.Port });
                var latencyState = new AppState { Nodes = [latencyNode], SelectedNode = latencyNode.Id, Capture = CaptureMode.LocalOnly, Mode = RouteMode.Global, MixedPort = EngineClient.FreePort(), DelayUrl = "http://example.test/overview", AutoUpdate = false, AutoUpdateProviders = false, RestoreConnection = false, Latency = new() { IntervalSeconds = 5, CurveIntervalSeconds = 1, TimeoutMs = 5000 } };
                new Vault(UserData.DirectoryPath).Write("settings.dat", latencyState); latencyFixture.Reply.TrySetResult(true);
                var latencyWindow = new MainWindow(); latencyWindow.Show();
                async Task AwaitLatency(Func<bool> condition)
                {
                    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    while (!condition()) await Task.Delay(20, limit.Token);
                }
                try
                {
                    await latencyWindow.Controller.InitializeAsync();
                    await latencyWindow.Controller.ExecuteAsync(latencyWindow.Controller.ToggleAsync);
                    await AwaitLatency(() => latencyWindow.OverviewMonitor.LatencyValue?.EndsWith(" ms", StringComparison.Ordinal) == true);
                    var monitor = latencyWindow.OverviewMonitor; var samples = latencyFixture.RequestCount;
                    Check(!monitor.LatencyMonitoring && monitor.Chart.LatencyCount == 0 && latencyWindow.LatencySampleInterval == TimeSpan.FromSeconds(5), "A real local connection updates the overview latency value by default while leaving the curve empty and using the ordinary interval");
                    await Task.Delay(1800);
                    Check(latencyFixture.RequestCount == samples, "The default overview metric does not use the faster optional curve frequency");
                    monitor.SetLatencyMonitoring(true);
                    await AwaitLatency(() => monitor.Chart.LatencyCount >= 2);
                    Check(latencyWindow.LatencySampleInterval == TimeSpan.FromSeconds(1) && latencyFixture.RequestCount >= samples + 2 && latencyFixture.MaximumConcurrentRequests == 1, "Opting into the curve increases sample frequency without overlapping slow real HTTP probes");
                    var value = monitor.LatencyValue; monitor.SetLatencyMonitoring(false); samples = latencyFixture.RequestCount;
                    Check(monitor.LatencyValue == value && monitor.Chart.LatencyCount == 0 && !monitor.Chart.SeriesVisible(2) && latencyWindow.LatencySampleInterval == TimeSpan.FromSeconds(5), "Stopping the curve preserves the latency metric and restores the ordinary frequency");
                    await Task.Delay(1800);
                    Check(latencyFixture.RequestCount == samples && monitor.LatencyValue == value, "Stopping curve collection does not continue high-frequency requests or erase the current latency value");
                    await latencyWindow.Controller.ExecuteAsync(latencyWindow.Controller.ToggleAsync);
                    Check(monitor.LatencyValue == "—" && !monitor.Chart.HasData, "Disconnect resets both the current metric and optional curve");
                }
                finally { await latencyWindow.Controller.DisposeAsync(); latencyWindow.Close(); }
            }
            var after = new WindowsProxyRegistry().Read();
            Check(originalProxy.All(p => p.Value is null ? after[p.Key] is null : p.Value.Same(after[p.Key])), "Windows proxy settings remain unchanged throughout the startup checks");
            File.WriteAllText(Path.Combine(output, "connection-check.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, localFixturesOnly = true }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS {checks.Count} connection startup checks."); return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "connection-check-failure.txt"), error + "\n" + error.Data["LatencyProbeLog"]);
            Console.Error.WriteLine(error); return 1;
        }
    }
    private sealed class ProxyFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Task _run;
        private int _requests, _active, _maximum;
        internal int DelayMilliseconds { get; init; }
        internal int RequestCount => Volatile.Read(ref _requests);
        internal int MaximumConcurrentRequests => Volatile.Read(ref _maximum);
        internal TaskCompletionSource<bool> Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        internal ProxyFixture() { _listener.Start(); _run = RunAsync(); }
        private async Task RunAsync()
        {
            try { while (!_lifetime.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_lifetime.Token); _ = ServeAsync(client); } }
            catch (Exception e) when (e is OperationCanceledException or SocketException) { }
        }
        private async Task ServeAsync(TcpClient client)
        {
            var tracked = false;
            using (client)
            try
            {
                var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                async Task<string> Header()
                {
                    var header = new StringBuilder();
                    while (await reader.ReadLineAsync(_lifetime.Token) is { Length: > 0 } line) header.AppendLine(line);
                    return header.ToString();
                }
                var header = await Header(); if (header.Length == 0) return;
                Interlocked.Increment(ref _requests); var active = Interlocked.Increment(ref _active); tracked = true;
                int prior; do { prior = Volatile.Read(ref _maximum); if (active <= prior) break; } while (Interlocked.CompareExchange(ref _maximum, active, prior) != prior);
                Observed.TrySetResult(true);
                var success = await Reply.Task.WaitAsync(_lifetime.Token);
                if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds, _lifetime.Token);
                if (success && header.StartsWith("CONNECT", StringComparison.Ordinal))
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n"), _lifetime.Token);
                    header = await Header();
                }
                var response = success ? "HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n" : "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _lifetime.Token);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException) { }
            finally { if (tracked) Interlocked.Decrement(ref _active); }
        }
        public async ValueTask DisposeAsync() { _lifetime.Cancel(); _listener.Stop(); await _run; _lifetime.Dispose(); }
    }
}
