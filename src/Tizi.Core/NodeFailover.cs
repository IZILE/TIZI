using System.Net;
using System.Net.Sockets;

namespace Tizi.Core;

/// <summary>Require repeated failures; preserve a cooldown across automatic node changes.</summary>
public sealed class NodeFailureTracker
{
    private Guid? _node;
    private int _failures;
    private DateTimeOffset _retryAfter;
    public void ResetObservation() { _node = null; _failures = 0; }
    public void Reset() { ResetObservation(); _retryAfter = default; }
    public bool Observe(Guid node, bool unavailable, DateTimeOffset now)
    {
        if (_node != node) { _node = node; _failures = 0; }
        if (!unavailable) { _failures = 0; return false; }
        _failures++;
        if (_failures < 2 || now < _retryAfter) return false;
        _failures = 0; _retryAfter = now.AddSeconds(30); return true;
    }
}

public static class NodeFailover
{
    public static string? Region(ProxyNode node) => NodeRegion.Match(node.Name, node.CountryCode);
    public static bool SameRegion(ProxyNode current, ProxyNode candidate) => Region(current) is { } region && Region(candidate) == region;
    public static bool CanCommit(AppState state, Guid failedNode, ProxyNode testedCandidate, bool connected) =>
        state.AutoSwitchOnTimeout && connected && state.Mode != RouteMode.Direct && state.SelectedNode == failedNode
        && state.Nodes.FirstOrDefault(node => node.Id == failedNode) is { } current
        && state.Nodes.Any(node => node == testedCandidate) && testedCandidate.Id != failedNode && SameRegion(current, testedCandidate);
    public static bool IsUnavailable(Exception error, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested) return false;
        return error switch
        {
            TimeoutException => true,
            OperationCanceledException => true,
            HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout or HttpStatusCode.ServiceUnavailable } => true,
            SocketException { SocketErrorCode: SocketError.TimedOut or SocketError.ConnectionRefused or SocketError.HostUnreachable or SocketError.NetworkUnreachable } => true,
            _ => error.InnerException is { } inner && IsUnavailable(inner, cancellation)
        };
    }
    public static async Task<ProxyNode?> FindAvailableAsync(AppState state, ProxyNode failed,
        Func<ProxyNode, CancellationToken, Task<int>> measure, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!state.AutoSwitchOnTimeout || state.Mode == RouteMode.Direct || state.SelectedNode != failed.Id || Region(failed) is null) return null;
        var candidates = state.Nodes.Where(node => node.Id != failed.Id && SameRegion(failed, node))
            .DistinctBy(node => node.Id).OrderBy(node => node.SubscriptionId == failed.SubscriptionId ? 0 : 1).ToArray();
        if (candidates.Length == 0) return null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var limit = new SemaphoreSlim(Math.Clamp(state.Latency.Concurrency, 1, 4));
        async Task<ProxyNode?> Probe(ProxyNode node)
        {
            try
            {
                await limit.WaitAsync(stop.Token).ConfigureAwait(false);
                try { return await measure(node, stop.Token).ConfigureAwait(false) > 0 ? node : null; }
                finally { limit.Release(); }
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or TimeoutException or SocketException or InvalidOperationException)
            { return null; }
        }
        var pending = candidates.Select(Probe).ToList();
        ProxyNode? available = null;
        try
        {
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending).ConfigureAwait(false); pending.Remove(finished);
                if (await finished.ConfigureAwait(false) is { } candidate) { available = candidate; break; }
            }
        }
        finally { stop.Cancel(); await Task.WhenAll(pending).ConfigureAwait(false); }
        cancellation.ThrowIfCancellationRequested(); return available;
    }
}
