using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;

namespace Tizi.Core;

public sealed record ConnectionInfo(string Id, string Host, string Network, string Rule, string Chain, string Process, long Upload, long Download);
public sealed record ConnectionSnapshot(long Upload, long Download, List<ConnectionInfo> Connections);
public sealed class EngineClient : IAsyncDisposable
{
    private Process? _process;
    private HttpClient? _api;
    private CancellationTokenSource? _lifetime;
    private string? _runtime;
    private string? _bundleDirectory;
    private string[] _sensitive = [];
    private CaptureMode _capture;
    private int _apiPort;
    private int _mixedPort;
    private string _secret = "";
    private readonly ConcurrentQueue<string> _recentLogs = new();
    private bool _tunDeferred;
    private readonly SemaphoreSlim _gate = new(1);
    public event Action<string>? Log;
    public event Action? UnexpectedExit;
    public bool IsRunning => _process is { HasExited: false } && _api is not null;
    public int ProcessId => _process?.Id ?? 0;
    public int ApiPort => _apiPort;
    public long ProcessStartTicks => _process?.StartTime.ToUniversalTime().Ticks ?? 0;
    public string RuntimeDirectory => _runtime ?? "";
    public string[] RecentLogs => _recentLogs.ToArray();
    public const string ExpectedHash = "04f8d7fc2b314771e1ecbf15951d59f0e1cb7914503c88cfb6972ff6a824e314";
    public const string ExpectedGeoIpHash = "116cc0f03d48991962f7f9cdbbcf53d45d89777f9c3cd1a663210853e5df6093";

    public async Task StartAsync(AppState state, string binary, string runtimeRoot, CancellationToken cancellation = default, bool deferTun = false)
    {
        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (_process is not null) throw new InvalidOperationException("The proxy engine is already running.");
            var reservation = new TcpListener(IPAddress.Loopback, state.MixedPort) { ExclusiveAddressUse = true };
            try { reservation.Start(); }
            catch (SocketException e) { throw new IOException("The local proxy port could not be opened. Close the application using this port or choose another port in Settings.", e); }
            finally { reservation.Stop(); }
            if (!File.Exists(binary)) throw new FileNotFoundException("The bundled proxy engine is missing.");
            await using (var stream = File.OpenRead(binary))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation).ConfigureAwait(false)).Equals(ExpectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Proxy engine integrity check failed.");
            _runtime = Path.Combine(Path.GetFullPath(runtimeRoot), "session-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_runtime);
            _bundleDirectory = Path.GetDirectoryName(Path.GetFullPath(binary));
            await EnsureGeodataAsync(state, cancellation).ConfigureAwait(false);
            _capture = state.Capture;
            _mixedPort = state.MixedPort;
            _tunDeferred = state.Capture == CaptureMode.Tun && deferTun;
            _recentLogs.Clear();
            var session = EngineSession.Create(state, FreePort(state.MixedPort));
            if (_tunDeferred)
            {
                var staged = JsonNode.Parse(session.Config)!; staged["tun"]!["enable"] = false;
                session = session with { Config = staged.ToJsonString() };
            }
            _apiPort = session.ApiPort; _secret = session.Secret;
            _sensitive = state.Nodes.SelectMany(n => Secrets(n.Options)).Append(session.Secret).Where(s => s.Length >= 4).Distinct().ToArray();
            var path = Path.Combine(_runtime, "config.json"); await File.WriteAllTextAsync(path, session.Config, cancellation).ConfigureAwait(false);
            var info = ProcessInfo(binary, _runtime, path);
            var validation = new ProcessStartInfo(info.FileName) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = _runtime };
            foreach (var arg in new[] { "-t", "-d", _runtime, "-f", path }) validation.ArgumentList.Add(arg);
            using (var check = Process.Start(validation) ?? throw new IOException("Could not validate the proxy configuration."))
            {
                var stdout = check.StandardOutput.ReadToEndAsync(cancellation); var stderr = check.StandardError.ReadToEndAsync(cancellation);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(15));
                try { await check.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
                catch { try { check.Kill(true); } catch (InvalidOperationException) { } throw; }
                var result = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
                if (check.ExitCode != 0) throw new InvalidDataException("Proxy configuration rejected: " + Redact(result));
            }
            cancellation.ThrowIfCancellationRequested();
            _lifetime = new CancellationTokenSource();
            _process = Process.Start(info) ?? throw new IOException("Could not launch the proxy engine.");
            _ = ReadLogAsync(_process.StandardOutput, _lifetime.Token); _ = ReadLogAsync(_process.StandardError, _lifetime.Token);
            _api = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) }) { BaseAddress = new Uri($"http://127.0.0.1:{session.ApiPort}"), Timeout = TimeSpan.FromSeconds(12) };
            _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Secret);
            using var ready = CancellationTokenSource.CreateLinkedTokenSource(cancellation); ready.CancelAfter(TimeSpan.FromSeconds(15));
            while (true)
            {
                ready.Token.ThrowIfCancellationRequested();
                if (_process.HasExited) throw new IOException("The proxy engine exited during startup. See the log.");
                try { using var response = await _api.GetAsync("/version", ready.Token).ConfigureAwait(false); if (response.IsSuccessStatusCode) break; }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!ready.IsCancellationRequested) { }
                await Task.Delay(100, ready.Token).ConfigureAwait(false);
            }
            await VerifyListenersAsync(state.MixedPort, _capture == CaptureMode.Tun && !_tunDeferred, cancellation).ConfigureAwait(false);
            if ((state.Nodes.FirstOrDefault(n => n.Id == state.SelectedNode) ?? state.Nodes.FirstOrDefault()) is { } selected)
                await SelectNodeAsync(selected, cancellation).ConfigureAwait(false);
            // The global selector must choose the app's selected group too.
            if (state.Mode == RouteMode.Global) await SelectGlobalAsync(cancellation).ConfigureAwait(false);
            var tracked = _process; _ = MonitorAsync(tracked, _lifetime.Token);
            PublishLog("Proxy engine ready.");
        }
        catch { await StopInternalAsync().ConfigureAwait(false); throw; }
        finally { _gate.Release(); }
    }
    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await StopInternalAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private async Task StopInternalAsync()
    {
        _lifetime?.Cancel();
        if (_capture == CaptureMode.Tun && _api is not null)
            try { using var response = await _api.PatchAsJsonAsync("/configs", new { tun = new { enable = false } }).ConfigureAwait(false); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
        var process = _process; _process = null;
        if (process is not null)
        {
            try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync().ConfigureAwait(false); } }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
        _api?.Dispose(); _api = null; _lifetime?.Dispose(); _lifetime = null;
        if (_runtime is { } folder)
        {
            try { Directory.Delete(folder, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            _runtime = null;
        }
        _sensitive = []; _bundleDirectory = null;
    }
    private static ProcessStartInfo ProcessInfo(string binary, string runtime, string config)
    {
        var info = new ProcessStartInfo(binary) { WorkingDirectory = runtime, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in new[] { "-d", runtime, "-f", config }) info.ArgumentList.Add(arg);
        return info;
    }
    private async Task ReadLogAsync(StreamReader reader, CancellationToken cancellation)
    {
        try { while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line) PublishLog(Redact(line)); }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
    }
    private async Task MonitorAsync(Process process, CancellationToken cancellation)
    {
        try { await process.WaitForExitAsync(cancellation).ConfigureAwait(false); if (!cancellation.IsCancellationRequested) { PublishLog("Proxy engine stopped unexpectedly."); UnexpectedExit?.Invoke(); } }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
    }
    public async Task SetModeAsync(RouteMode mode, CancellationToken cancellation = default)
    {
        var api = Api; using var response = await api.PatchAsJsonAsync("/configs", new { mode = mode.ToString().ToLowerInvariant() }, cancellation).ConfigureAwait(false); response.EnsureSuccessStatusCode();
        if (mode == RouteMode.Global) await SelectGlobalAsync(cancellation).ConfigureAwait(false);
    }
    public async Task ReloadAsync(AppState state, CancellationToken cancellation = default)
    {
        if (state.Capture != _capture) throw new InvalidOperationException("Disconnect before changing capture mode.");
        await EnsureGeodataAsync(state, cancellation).ConfigureAwait(false);
        var session = EngineSession.Create(state, _apiPort);
        var config = JsonNode.Parse(session.Config)!.AsObject(); config["secret"] = _secret;
        var payload = config.ToJsonString();
        _sensitive = _sensitive.Concat(state.Nodes.SelectMany(n => Secrets(n.Options))).Where(s => s.Length >= 4).Distinct().ToArray();
        using var response = await Api.PutAsJsonAsync("/configs?force=true", new { path = "", payload }, cancellation).ConfigureAwait(false); response.EnsureSuccessStatusCode();
        if (_runtime is { } folder) await File.WriteAllTextAsync(Path.Combine(folder, "config.json"), payload, cancellation).ConfigureAwait(false);
        if (state.Mode == RouteMode.Global) await SelectGlobalAsync(cancellation).ConfigureAwait(false);
    }
    private async Task EnsureGeodataAsync(AppState state, CancellationToken cancellation)
    {
        if (!state.Rules.Any(r => r.Type == "GEOIP")) return;
        if (_runtime is null || _bundleDirectory is null) throw new InvalidOperationException("The proxy engine is not running.");
        var destination = Path.Combine(_runtime, "geoip.dat");
        if (File.Exists(destination)) return; // This private session's database was already verified.
        var source = Path.Combine(_bundleDirectory, "geoip.dat");
        if (!File.Exists(source)) throw new FileNotFoundException("The bundled GeoIP database is missing.");
        await using (var stream = File.OpenRead(source))
            if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation).ConfigureAwait(false)).Equals(ExpectedGeoIpHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GeoIP database integrity check failed.");
        File.Copy(source, destination);
    }
    private async Task SelectGlobalAsync(CancellationToken cancellation) { using var response = await Api.PutAsJsonAsync("/proxies/GLOBAL", new { name = "TIZI" }, cancellation).ConfigureAwait(false); response.EnsureSuccessStatusCode(); }
    private void PublishLog(string line)
    {
        _recentLogs.Enqueue(line); while (_recentLogs.Count > 128) _recentLogs.TryDequeue(out _);
        Log?.Invoke(line);
    }
    private async Task VerifyListenersAsync(int mixedPort, bool requireTun, CancellationToken cancellation)
    {
        using var ready = CancellationTokenSource.CreateLinkedTokenSource(cancellation); ready.CancelAfter(TimeSpan.FromSeconds(15));
        var portReady = false;
        try
        {
            while (true)
            {
                using var response = await Api.GetAsync("/configs", ready.Token).ConfigureAwait(false); response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ready.Token).ConfigureAwait(false));
                var config = document.RootElement;
                portReady = config.TryGetProperty("mixed-port", out var port) && port.GetInt32() == mixedPort;
                var tunReady = !requireTun || (config.TryGetProperty("tun", out var tun) && tun.TryGetProperty("enable", out var enabled) && enabled.GetBoolean());
                if (portReady && tunReady) return;
                var detail = RecentLogs.LastOrDefault(l => l.Contains("Start TUN listening error", StringComparison.OrdinalIgnoreCase));
                if (requireTun && detail is not null) throw new IOException("TUN could not be enabled. Check administrator rights and the connection log. " + detail);
                await Task.Delay(100, ready.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new IOException(portReady && requireTun ? "TUN could not be enabled. Check administrator rights and the connection log." : "The local proxy port could not be opened. Close the application using this port or choose another port in Settings."); }
    }
    public async Task EnableTunAsync(AppState state, CancellationToken cancellation = default)
    {
        if (_capture != CaptureMode.Tun || !_tunDeferred) throw new InvalidOperationException("TUN is not waiting for activation.");
        var tun = JsonNode.Parse(EngineSession.Create(state, _apiPort).Config)!["tun"]!.DeepClone();
        using var response = await Api.PatchAsJsonAsync("/configs", new JsonObject { ["tun"] = tun }, cancellation).ConfigureAwait(false); response.EnsureSuccessStatusCode();
        await VerifyListenersAsync(state.MixedPort, true, cancellation).ConfigureAwait(false);
        _tunDeferred = false;
    }
    public async Task SelectNodeAsync(ProxyNode node, CancellationToken cancellation = default)
    { using var response = await Api.PutAsJsonAsync("/proxies/TIZI", new { name = node.CoreName }, cancellation).ConfigureAwait(false); response.EnsureSuccessStatusCode(); }
    public async Task<int> DelayAsync(ProxyNode node, string url, CancellationToken cancellation = default, int timeoutMs = 5000)
    {
        SourceUpdates.WebUri(url);
        if (timeoutMs is < 500 or > 15000) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
        using var response = await Api.GetAsync($"/proxies/{node.CoreName}/delay?timeout={timeoutMs}&expected=200-299&url=" + Uri.EscapeDataString(url), cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Latency test failed (HTTP {(int)response.StatusCode}): " + Redact(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)));
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false), cancellationToken: cancellation).ConfigureAwait(false); return doc.RootElement.GetProperty("delay").GetInt32();
    }
    public async Task CheckConnectionAsync(string url, CancellationToken cancellation = default)
    {
        SourceUpdates.WebUri(url);
        using var client = new HttpClient(new SocketsHttpHandler { Proxy = new WebProxy("http://127.0.0.1:" + _mixedPort), UseProxy = true, ConnectTimeout = TimeSpan.FromSeconds(6) }) { Timeout = TimeSpan.FromSeconds(8) };
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
    public async Task<ConnectionSnapshot> ConnectionsAsync(CancellationToken cancellation = default)
    {
        using var response = await Api.GetAsync("/connections", cancellation).ConfigureAwait(false); response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false), cancellationToken: cancellation).ConfigureAwait(false);
        var root = doc.RootElement; var list = new List<ConnectionInfo>();
        if (root.TryGetProperty("connections", out var connections) && connections.ValueKind == JsonValueKind.Array)
            foreach (var connection in connections.EnumerateArray().Take(2000))
            {
                var meta = connection.GetProperty("metadata"); var host = Text(meta, "host"); if (host.Length == 0) host = Text(meta, "destinationIP");
                list.Add(new ConnectionInfo(Text(connection, "id"), host + ":" + Text(meta, "destinationPort"), Text(meta, "network"), Text(connection, "rule") + " " + Text(connection, "rulePayload"), connection.TryGetProperty("chains", out var chains) ? string.Join(" → ", chains.EnumerateArray().Select(c => c.ToString())) : "", Text(meta, "process"), Number(connection, "upload"), Number(connection, "download")));
            }
        return new ConnectionSnapshot(Number(root, "uploadTotal"), Number(root, "downloadTotal"), list);
    }
    public async Task CloseConnectionsAsync(string? id = null, CancellationToken cancellation = default)
    { using var response = await Api.DeleteAsync("/connections" + (id is null ? "" : "/" + Uri.EscapeDataString(id)), cancellation).ConfigureAwait(false); response.EnsureSuccessStatusCode(); }
    private static string Text(JsonElement value, string key) => value.TryGetProperty(key, out var text) ? text.ToString() : "";
    private static long Number(JsonElement value, string key) => value.TryGetProperty(key, out var n) && n.TryGetInt64(out var result) ? result : 0;
    private HttpClient Api => _api ?? throw new InvalidOperationException("Connect the proxy engine first.");
    private string Redact(string line)
    {
        foreach (var secret in _sensitive) line = line.Replace(secret, "••••", StringComparison.Ordinal);
        return line.Length > 4000 ? line[..4000] : line;
    }
    private static IEnumerable<string> Secrets(JsonNode? root)
    {
        if (root is JsonObject o) foreach (var p in o)
        { if (p.Key is "password" or "uuid" or "private-key" or "public-key" or "pre-shared-key" or "obfs-password" or "psk" or "auth-str" or "key" or "token") yield return p.Value?.ToString() ?? ""; else foreach (var s in Secrets(p.Value)) yield return s; }
        else if (root is JsonArray array) foreach (var item in array) foreach (var s in Secrets(item)) yield return s;
    }
    public static int FreePort(int except = 0)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); if (port != except) return port; }
        throw new IOException("No available local port.");
    }
    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(false); _gate.Dispose(); }
}
