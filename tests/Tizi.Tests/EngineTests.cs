using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class EngineTests
{
    private static string Binary
    {
        get { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "engine/mihomo.exe"))) directory = directory.Parent; return directory is null ? throw new FileNotFoundException("Test engine not found.") : Path.Combine(directory.FullName, "engine/mihomo.exe"); }
    }
    [Fact] public async Task RealEngineRoutesDirectAndProxyThenReloadsWithoutSystemChanges()
    {
        var before = new WindowsProxyRegistry().Read();
        var directory = Path.Combine(Path.GetTempPath(), "TIZI-core-test-" + Guid.NewGuid().ToString("N"));
        await using var origin = new LocalServer(false); await using var upstream = new LocalServer(true);
        var node = NodeImport.FromOptions(new JsonObject { ["name"] = "Local test proxy", ["type"] = "http", ["server"] = "127.0.0.1", ["port"] = upstream.Port });
        var state = new AppState { Mode = RouteMode.Global, Capture = CaptureMode.LocalOnly, MixedPort = EngineClient.FreePort(), Nodes = [node], SelectedNode = node.Id };
        var logs = new List<string>(); await using var engine = new EngineClient(); engine.Log += line => { lock (logs) logs.Add(line); };
        try
        {
            await engine.StartAsync(state, Binary, directory); Assert.True(engine.IsRunning);
            using var anonymous = new HttpClient(new SocketsHttpHandler { UseProxy = false }); using var unauthorized = await anonymous.GetAsync($"http://127.0.0.1:{engine.ApiPort}/version"); Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            using var client = new HttpClient(new SocketsHttpHandler { Proxy = new WebProxy($"http://127.0.0.1:{state.MixedPort}"), UseProxy = true }) { Timeout = TimeSpan.FromSeconds(10) };
            var url = $"http://127.0.0.1:{origin.Port}/test";
            Assert.Equal("through-test-proxy", await client.GetStringAsync(url)); Assert.True(upstream.Requests > 0); Assert.Equal(0, origin.Requests);
            await engine.CheckConnectionAsync(url);
            var delay = await engine.DelayAsync(node, url); Assert.InRange(delay, 0, 5000);
            await engine.SetModeAsync(RouteMode.Direct); Assert.Equal("through-direct", await client.GetStringAsync(url)); Assert.True(origin.Requests > 0);
            await engine.CheckConnectionAsync(url);
            await engine.ReloadAsync(state with { Mode = RouteMode.Rule, Rules = [new RoutingRule("DOMAIN", "127.0.0.1", "REJECT")] });
            // Reload preserves authenticated API access and a live process.
            Assert.True(engine.IsRunning); await engine.ConnectionsAsync(); await engine.CloseConnectionsAsync();
            await engine.SetModeAsync(RouteMode.Global); await engine.SelectNodeAsync(node); Assert.Equal("through-test-proxy", await client.GetStringAsync(url));
            var runtime = engine.RuntimeDirectory; await engine.StopAsync(); Assert.False(engine.IsRunning); Assert.False(Directory.Exists(runtime));
            var after = new WindowsProxyRegistry().Read(); foreach (var key in before.Keys) Assert.True(before[key] is null ? after[key] is null : before[key]!.Same(after[key]));
        }
        finally { await engine.StopAsync(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public async Task SubscriptionUpdateRetainsStableIdsAndDoesNotMutateOnFailure()
    {
        using var http = new HttpClient(new FixtureHandler()); var updates = new SourceUpdates(http); var sub = new Subscription { Name = "Test", Url = "https://example.com/nodes" };
        var one = await updates.SubscriptionAsync(sub, []); var two = await updates.SubscriptionAsync(sub, one); Assert.Equal(one[0].Id, two[0].Id);
        await Assert.ThrowsAsync<HttpRequestException>(() => updates.SubscriptionAsync(sub with { Url = "https://example.com/fail" }, one)); Assert.Single(one);
    }
    [Fact] public async Task OccupiedProxyPortCannotBeReportedAsReady()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TIZI-port-test-" + Guid.NewGuid().ToString("N"));
        var listener = new TcpListener(IPAddress.Any, 0) { ExclusiveAddressUse = true }; listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var engine = new EngineClient();
        try
        {
            await Assert.ThrowsAsync<IOException>(() => engine.StartAsync(new AppState { Mode = RouteMode.Direct, Capture = CaptureMode.LocalOnly, MixedPort = port }, Binary, directory));
            Assert.False(engine.IsRunning); Assert.Equal(0, engine.ProcessId);
        }
        finally { listener.Stop(); await engine.StopAsync(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public async Task GeoIpRulesLoadOnReloadAndFreshStartFromBundledDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TIZI-geodata-test-" + Guid.NewGuid().ToString("N"));
        var state = new AppState { Mode = RouteMode.Direct, Capture = CaptureMode.LocalOnly, MixedPort = EngineClient.FreePort() };
        var rules = RuleImport.Parse("GEOIP,CN,DIRECT,no-resolve\nMATCH,DIRECT").Rules;
        await using var engine = new EngineClient();
        try
        {
            await engine.StartAsync(state, Binary, directory);
            Assert.False(File.Exists(Path.Combine(engine.RuntimeDirectory, "geoip.dat")));
            await engine.ReloadAsync(state with { Rules = rules });
            Assert.True(File.Exists(Path.Combine(engine.RuntimeDirectory, "geoip.dat")));
            await engine.ConnectionsAsync(); await engine.StopAsync();
            await engine.StartAsync(state with { Rules = rules }, Binary, directory);
            Assert.True(File.Exists(Path.Combine(engine.RuntimeDirectory, "geoip.dat")));
            Assert.True(engine.IsRunning);
        }
        finally { await engine.StopAsync(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(request.RequestUri!.AbsolutePath == "/fail" ? new HttpResponseMessage(HttpStatusCode.BadGateway) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ss://YWVzLTEyOC1nY206c2VjcmV0@host.example:8388#test") });
    }
    private sealed class LocalServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0); private readonly CancellationTokenSource _lifetime = new(); private readonly Task _run; private readonly bool _proxy; private int _requests;
        public int Requests => _requests;
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public LocalServer(bool proxy) { _proxy = proxy; _listener.Start(); _run = RunAsync(); }
        private async Task RunAsync()
        {
            try { while (!_lifetime.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_lifetime.Token); _ = ReplyAsync(client); } }
            catch (Exception e) when (e is OperationCanceledException or SocketException) { }
        }
        private async Task ReplyAsync(TcpClient client)
        {
            using (client)
            try
            {
                var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                async Task<string> Header() { var header = new StringBuilder(); while (await reader.ReadLineAsync(_lifetime.Token) is { Length: > 0 } line) header.AppendLine(line); return header.ToString(); }
                var header = await Header(); if (header.Length == 0) return;
                if (_proxy && header.StartsWith("CONNECT")) { await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n"), _lifetime.Token); header = await Header(); }
                Interlocked.Increment(ref _requests); var body = _proxy ? "through-test-proxy" : "through-direct";
                await Task.Delay(10, _lifetime.Token); // Real networks have nonzero delay; mihomo treats 0 ms as invalid.
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"), _lifetime.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException) { }
        }
        public async ValueTask DisposeAsync() { _lifetime.Cancel(); _listener.Stop(); await _run; _lifetime.Dispose(); }
    }
}
