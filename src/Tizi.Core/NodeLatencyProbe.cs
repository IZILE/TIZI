namespace Tizi.Core;

/// <summary>A short-lived, loopback-only engine for HTTP measurements while disconnected.</summary>
public sealed class NodeLatencyProbe : IAsyncDisposable
{
    private readonly EngineClient _engine = new();
    public string RuntimeDirectory => _engine.RuntimeDirectory;
    public int ProcessId => _engine.ProcessId;
    private NodeLatencyProbe() { }
    public static async Task<NodeLatencyProbe> StartAsync(AppState state, string binary, string runtimeRoot, CancellationToken cancellation = default)
    {
        var probe = new NodeLatencyProbe();
        try
        {
            // This copy never enters the saved profile, ProxyLease, or the main connection controller.
            var isolated = NetworkEnvironment.ConnectionSettings(state with
            {
                Capture = CaptureMode.LocalOnly, Mode = RouteMode.Direct, MixedPort = EngineClient.FreePort(),
                Rules = [], LastConnected = false, AutoUpdate = false, AutoUpdateProviders = false,
                Dns = state.Dns with { RespectRules = false }, Logs = state.Logs with { Level = "silent", Dns = false }
            });
            await probe._engine.StartAsync(isolated, binary, runtimeRoot, cancellation).ConfigureAwait(false);
            return probe;
        }
        catch (Exception error) { error.Data["LatencyProbeLog"] = string.Join('\n', probe._engine.RecentLogs); await probe.DisposeAsync().ConfigureAwait(false); throw; }
    }
    public Task<int> MeasureAsync(ProxyNode node, string url, int timeoutMs = 5000, CancellationToken cancellation = default) => _engine.DelayAsync(node, url, cancellation, timeoutMs);
    public ValueTask DisposeAsync() => _engine.DisposeAsync();
}
