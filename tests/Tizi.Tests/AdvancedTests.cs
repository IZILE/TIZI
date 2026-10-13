using System.Text.Json;
using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class AdvancedTests
{
    [Fact] public void OlderSettingsReceiveUsableDefaultsWithoutLosingNodesOrCapture()
    {
        var state = JsonSerializer.Deserialize<AppState>("""{"Capture":1,"StartHidden":true,"Rules":[{"Type":"MATCH","Value":"","Action":"DIRECT"}]}""")!;
        AdvancedValidation.Check(state); Assert.Equal(CaptureMode.Tun, state.Capture); Assert.True(state.StartHidden); Assert.True(state.RestoreConnection); Assert.False(state.LastConnected);
        Assert.Equal(1500, state.Tunnel.Mtu); Assert.Equal("HTTP", state.Latency.Method); Assert.Equal("info", state.Logs.Level);
    }
    [Fact] public void AdvancedOptionsReachEngineConfiguration()
    {
        var state = new AppState { Capture = CaptureMode.Tun, Tunnel = new() { Stack = "mixed", Mtu = 1400, StrictRoute = true, HijackDns = false, UdpTimeout = 90, ExcludedRoutes = ["192.168.1.0/24"] }, Transport = new() { Ipv6 = false, TcpConcurrent = false, KeepAlive = false, KeepAliveIdle = 40, KeepAliveInterval = 20, Udp = false }, Dns = new() { Mode = "redir-host", RespectRules = true, Servers = ["https://dns.example/dns-query"], Hosts = new() { ["example.test"] = "127.0.0.1" } }, Logs = new() { Dns = true } };
        var config = JsonNode.Parse(EngineSession.Create(state, 19001).Config)!;
        Assert.Equal("mixed", config["tun"]!["stack"]!.ToString()); Assert.Equal(1400, config["tun"]!["mtu"]!.GetValue<int>()); Assert.True(config["tun"]!["strict-route"]!.GetValue<bool>());
        Assert.Empty(config["tun"]!["dns-hijack"]!.AsArray()); Assert.Equal(90, config["tun"]!["udp-timeout"]!.GetValue<int>());
        Assert.False(config["ipv6"]!.GetValue<bool>()); Assert.False(config["tcp-concurrent"]!.GetValue<bool>()); Assert.True(config["disable-keep-alive"]!.GetValue<bool>());
        Assert.Equal("NETWORK,udp,REJECT", config["rules"]![0]!.ToString()); Assert.Equal("127.0.0.1", config["hosts"]!["example.test"]!.ToString()); Assert.Equal("debug", config["log-level"]!.ToString());
        Assert.Equal("https://dns.example/dns-query", config["dns"]!["nameserver"]![0]!.ToString());
    }
    [Fact] public void InvalidAdvancedOptionsAreRejected()
    {
        foreach (var state in new[] { new AppState { Tunnel = new() { Mtu = 1 } }, new AppState { Tunnel = new() { ExcludedRoutes = ["1.2.3.4/99"] } }, new AppState { Dns = new() { Servers = ["file:///secret"] } }, new AppState { Dns = new() { BootstrapServers = ["https://dns.example/query"] } }, new AppState { Latency = new() { Concurrency = 0 } }, new AppState { Logs = new() { RetentionDays = 0 } } }) Assert.Throws<FormatException>(() => AdvancedValidation.Check(state));
    }
    [Fact] public void LogsClassifyFilterAndExpireWithoutTouchingConfiguration()
    {
        var log = new ActivityLog { Settings = new() { Proxy = true, Dns = true, RetentionDays = 7 } }; var now = DateTimeOffset.Now;
        log.Add("[TCP] 127.0.0.1 --> example.test", now); log.Add("[DNS] example.test A", now); log.Add("Connected.", now);
        Assert.Equal(new[] { LogCategory.Proxy, LogCategory.Dns, LogCategory.Application }, log.Entries.Select(e => e.Category));
        log.Add("old", now.AddDays(-10)); Assert.Equal(3, log.Entries.Count);
        log.Clear(LogCategory.Dns); Assert.Equal(2, log.Entries.Count); Assert.Contains(log.Entries, e => e.Category == LogCategory.Proxy);
        log.Settings = log.Settings with { AutoDelete = false, Proxy = false }; log.Add("old retained", now.AddDays(-20)); log.Add("[TCP] ignored"); Assert.Equal(3, log.Entries.Count);
    }
    [Fact] public void LogSizeStaysBoundedUnderBurstTraffic()
    {
        var log = new ActivityLog { Settings = new() { MaxEntries = 100, AutoDelete = false } };
        for (var i = 0; i < 10000; i++) log.Add("Application event " + i);
        Assert.Equal(100, log.Entries.Count); Assert.EndsWith("9999", log.Entries.Last().Message);
    }
    [Fact] public async Task TcpLatencyDoesNotTryToConnectUdpOnlyWireGuard()
    {
        var node = new ProxyNode { Name = "Synthetic UDP", Options = new() { ["type"] = "wireguard", ["server"] = "192.0.2.1", ["port"] = 51820, ["udp"] = true } };
        Assert.Equal("UDP", NodeTransport.Summary(node)); Assert.False(NodeTransport.HasTcpEndpoint(node));
        Assert.Equal("TCP / UDP", NodeTransport.Summary(node with { Options = new() { ["type"] = "ss", ["udp"] = true } }));
        Assert.Equal("TCP", NodeTransport.Summary(node with { Options = new() { ["type"] = "ss", ["udp"] = false } }));
        await Assert.ThrowsAsync<NotSupportedException>(() => NodeTransport.MeasureAsync(node, new() { Method = "TCP" }, default));
    }
}
