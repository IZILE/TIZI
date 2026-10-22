using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Tizi.Core;

public sealed record TunnelSettings
{
    public string Stack { get; init; } = "gvisor";
    public int Mtu { get; init; } = 1500;
    public bool StrictRoute { get; init; }
    public bool HijackDns { get; init; } = true;
    public int UdpTimeout { get; init; } = 300;
    public List<string> ExcludedRoutes { get; init; } = [];
}
public sealed record DnsSettings
{
    public string Mode { get; init; } = "automatic";
    public bool Ipv6 { get; init; } = true;
    public bool RespectRules { get; init; }
    public List<string> Servers { get; init; } = ["https://dns.alidns.com/dns-query", "https://doh.pub/dns-query"];
    public List<string> ProxyServers { get; init; } = ["https://dns.alidns.com/dns-query", "https://doh.pub/dns-query"];
    public List<string> BootstrapServers { get; init; } = ["223.5.5.5", "119.29.29.29"];
    public Dictionary<string, string> Hosts { get; init; } = [];
}
public sealed record TransportSettings
{
    public bool Ipv6 { get; init; } = true;
    public bool TcpConcurrent { get; init; } = true;
    public bool KeepAlive { get; init; } = true;
    public int KeepAliveIdle { get; init; } = 15;
    public int KeepAliveInterval { get; init; } = 15;
    public bool Udp { get; init; } = true;
}
public sealed record LatencySettings
{
    public string Method { get; init; } = "HTTP";
    public int TimeoutMs { get; init; } = 5000;
    public int Concurrency { get; init; } = 4;
    public int IntervalSeconds { get; init; } = 15;
    public int CurveIntervalSeconds { get; init; } = 1;
}
public sealed record LogSettings
{
    public string Level { get; init; } = "info";
    public bool Proxy { get; init; } = true;
    public bool Dns { get; init; }
    public bool Persist { get; init; }
    public bool AutoDelete { get; init; } = true;
    public int RetentionDays { get; init; } = 7;
    public int MaxEntries { get; init; } = 5000;
}
public static class AdvancedValidation
{
    public static void Check(AppState state)
    {
        TailscaleSettings.Validate(state.Tailscale);
        if (state.Tunnel.Stack is not ("gvisor" or "system" or "mixed") || state.Tunnel.Mtu is < 1280 or > 9000 || state.Tunnel.UdpTimeout is < 10 or > 3600) throw new FormatException("Invalid tunnel settings.");
        if (state.Tunnel.ExcludedRoutes.Count > 100) throw new FormatException("Too many excluded routes.");
        foreach (var route in state.Tunnel.ExcludedRoutes) { var pair = route.Split('/'); if (pair.Length != 2 || !IPAddress.TryParse(pair[0], out var ip) || !int.TryParse(pair[1], out var prefix) || prefix < 0 || prefix > (ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128)) throw new FormatException("Invalid excluded route."); }
        if (state.Dns.Mode is not ("automatic" or "fake-ip" or "redir-host")) throw new FormatException("Invalid DNS mode.");
        foreach (var list in new[] { state.Dns.Servers, state.Dns.ProxyServers, state.Dns.BootstrapServers })
        {
            if (list.Count is < 1 or > 16) throw new FormatException("Enter between 1 and 16 DNS servers.");
            foreach (var server in list)
            {
                if (server.Length > 512 || server.Any(char.IsWhiteSpace) || server.Contains('@') || server.Contains('#')) throw new FormatException("Invalid DNS server.");
                if (server == "system" || IPAddress.TryParse(server, out _)) continue;
                if (!Uri.TryCreate(server.Contains("://") ? server : "udp://" + server, UriKind.Absolute, out var uri) || uri.Scheme is not ("udp" or "tcp" or "tls" or "https" or "quic") || string.IsNullOrWhiteSpace(uri.Host)) throw new FormatException("Invalid DNS server.");
            }
        }
        if (state.Dns.BootstrapServers.Any(s => !IPAddress.TryParse(s, out _))) throw new FormatException("Bootstrap DNS must use IP addresses.");
        if (state.Dns.Hosts.Count > 500 || state.Dns.Hosts.Any(p => p.Key.Length is < 1 or > 253 || p.Key.Any(char.IsWhiteSpace) || !IPAddress.TryParse(p.Value, out _))) throw new FormatException("Invalid local DNS mapping.");
        if (state.Transport.KeepAliveIdle is < 1 or > 600 || state.Transport.KeepAliveInterval is < 1 or > 600) throw new FormatException("Invalid TCP keepalive interval.");
        if (state.Latency.Method is not ("HTTP" or "TCP" or "ICMP") || state.Latency.TimeoutMs is < 500 or > 15000 || state.Latency.Concurrency is < 1 or > 16 || state.Latency.IntervalSeconds is < 5 or > 300 || state.Latency.CurveIntervalSeconds is < 1 or > 300) throw new FormatException("Invalid latency test settings.");
        if (state.Logs.Level is not ("silent" or "error" or "warning" or "info" or "debug") || state.Logs.RetentionDays is < 1 or > 365 || state.Logs.MaxEntries is < 100 or > 20000) throw new FormatException("Invalid log settings.");
    }
}
public static class NodeTransport
{
    public static string Summary(ProxyNode node)
    {
        var carrier = node.Type is "wireguard" or "hysteria" or "hysteria2" or "tuic" or "masque" ? "UDP" : node.Options["network"]?.ToString() is "ws" or "http" or "h2" or "grpc" ? "TCP" : node.Type == "mieru" ? node.Options["transport"]?.ToString().ToUpperInvariant() ?? "TCP" : "TCP";
        var udp = node.Options["udp"]?.ToString() is "true" or "1" || node.Type is "wireguard" or "hysteria" or "hysteria2" or "tuic";
        return udp && carrier != "UDP" ? carrier + " / UDP" : carrier;
    }
    public static bool HasTcpEndpoint(ProxyNode node) => node.Type is not ("wireguard" or "hysteria" or "hysteria2" or "tuic" or "masque") && node.Options["transport"]?.ToString() != "UDP";
    public static async Task<int> MeasureAsync(ProxyNode node, LatencySettings options, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(options.TimeoutMs);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        if (options.Method == "TCP")
        {
            if (!HasTcpEndpoint(node)) throw new NotSupportedException("UDP endpoint · TCP test unavailable");
            using var socket = new TcpClient(); await socket.ConnectAsync(node.Server, node.Port, timeout.Token).ConfigureAwait(false);
        }
        else
        {
            using var ping = new System.Net.NetworkInformation.Ping(); var result = await ping.SendPingAsync(node.Server, TimeSpan.FromMilliseconds(options.TimeoutMs), cancellationToken: timeout.Token).ConfigureAwait(false);
            if (result.Status != System.Net.NetworkInformation.IPStatus.Success) throw new TimeoutException();
            return (int)result.RoundtripTime;
        }
        return (int)Math.Max(1, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
