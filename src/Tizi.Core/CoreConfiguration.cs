using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Tizi.Core;

public sealed record EngineSession(int ApiPort, string Secret, string Config)
{
    public static EngineSession Create(AppState state, int apiPort, int inspectionPort = 0)
    {
        if (state.MixedPort is < 1024 or > 65535 || apiPort is < 1024 or > 65535 || state.MixedPort == apiPort) throw new FormatException("Choose distinct ports between 1024 and 65535.");
        if (inspectionPort != 0 && (inspectionPort is < 1024 or > 65535 || inspectionPort == apiPort || inspectionPort == state.MixedPort)) throw new FormatException("Choose distinct ports between 1024 and 65535.");
        if (state.Nodes.Count > NodeImport.MaxNodes) throw new FormatException("Too many nodes.");
        if (state.OutboundInterface.Length > 256 || state.OutboundInterface.Any(char.IsControl)) throw new FormatException("Invalid outbound interface.");
        var selected = state.Nodes.FirstOrDefault(n => n.Id == state.SelectedNode) ?? state.Nodes.FirstOrDefault();
        var names = new JsonArray();
        if (selected is not null) names.Add(selected.CoreName);
        foreach (var node in state.Nodes.Where(n => n != selected)) names.Add(node.CoreName);
        names.Add("DIRECT");
        var proxies = new JsonArray();
        foreach (var node in state.Nodes)
        {
            var checkedNode = NodeImport.FromOptions(node.Options);
            var options = (JsonObject)checkedNode.Options.DeepClone(); options["name"] = node.CoreName; proxies.Add(options);
        }
        ModelValidation.Rules(state.Rules);
        AdvancedValidation.Check(state);
        JsonArray groups;
        if (state.Tailscale.Enabled)
        {
            if (state.Tailscale.ThroughProxy && state.Nodes.Count == 0)
                throw new FormatException("Import a proxy node before enabling Tailscale transit.");
            proxies.Add(state.Tailscale.ToProxy(state.Transport.Udp));
            groups = new(new JsonObject { ["name"] = "TIZI", ["type"] = "select", ["proxies"] = new JsonArray(TailscaleSettings.ProxyName) });
            if (state.Tailscale.ThroughProxy)
            {
                // An unavailable phone or transit node must not silently select DIRECT.
                names.RemoveAt(names.Count - 1);
                groups.Add(new JsonObject { ["name"] = TailscaleSettings.TransitGroup, ["type"] = "select", ["proxies"] = names });
            }
        }
        else groups = new(new JsonObject { ["name"] = "TIZI", ["type"] = "select", ["proxies"] = names });
        var rules = new JsonArray();
        if (!state.Transport.Udp) rules.Add("NETWORK,udp,REJECT");
        foreach (var rule in state.Rules) rules.Add(rule.ToCore());
        if (!state.Rules.Any(r => r.Type == "MATCH"))
            foreach (var rule in new[] { "DOMAIN,localhost,DIRECT", "IP-CIDR,127.0.0.0/8,DIRECT,no-resolve", "IP-CIDR,10.0.0.0/8,DIRECT,no-resolve", "IP-CIDR,172.16.0.0/12,DIRECT,no-resolve", "IP-CIDR,192.168.0.0/16,DIRECT,no-resolve", "IP-CIDR6,::1/128,DIRECT,no-resolve", "IP-CIDR6,fc00::/7,DIRECT,no-resolve", "MATCH,TIZI" }) rules.Add(rule);
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var config = new JsonObject
        {
            ["mixed-port"] = state.MixedPort, ["allow-lan"] = false, ["bind-address"] = "127.0.0.1", ["mode"] = state.Mode.ToString().ToLowerInvariant(),
            ["log-level"] = state.Logs.Dns ? "debug" : state.Logs.Level, ["external-controller"] = "127.0.0.1:" + apiPort, ["secret"] = secret,
            ["ipv6"] = state.Transport.Ipv6, ["find-process-mode"] = "strict", ["unified-delay"] = true, ["tcp-concurrent"] = state.Transport.TcpConcurrent,
            ["disable-keep-alive"] = !state.Transport.KeepAlive, ["keep-alive-idle"] = state.Transport.KeepAliveIdle, ["keep-alive-interval"] = state.Transport.KeepAliveInterval,
            ["profile"] = new JsonObject { ["store-selected"] = false, ["store-fake-ip"] = false },
            ["proxies"] = proxies,
            ["proxy-groups"] = groups,
            ["rules"] = rules, ["geodata-mode"] = true, ["geo-auto-update"] = false,
            ["tun"] = new JsonObject { ["enable"] = state.Capture == CaptureMode.Tun, ["device"] = "TIZI", ["stack"] = state.Tunnel.Stack, ["mtu"] = state.Tunnel.Mtu, ["strict-route"] = state.Tunnel.StrictRoute, ["udp-timeout"] = state.Tunnel.UdpTimeout, ["auto-route"] = true, ["auto-detect-interface"] = state.OutboundInterface.Length == 0, ["dns-hijack"] = state.Tunnel.HijackDns ? new JsonArray("any:53", "tcp://any:53") : new JsonArray(), ["route-exclude-address"] = new JsonArray(state.Tunnel.ExcludedRoutes.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()) },
            // This resolver is internal: no DNS listening port or Windows DNS
            // setting is changed by System proxy / Local proxy mode.
            ["dns"] = new JsonObject { ["enable"] = true, ["ipv6"] = state.Dns.Ipv6 && state.Transport.Ipv6, ["enhanced-mode"] = state.Dns.Mode == "automatic" ? (state.Capture == CaptureMode.Tun ? "fake-ip" : "redir-host") : state.Dns.Mode, ["respect-rules"] = state.Dns.RespectRules, ["use-hosts"] = true, ["fake-ip-range"] = "198.19.0.1/16", ["nameserver"] = Array(state.Dns.Servers), ["proxy-server-nameserver"] = Array(state.Dns.ProxyServers), ["default-nameserver"] = Array(state.Dns.BootstrapServers) },
            ["hosts"] = new JsonObject(state.Dns.Hosts.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, JsonValue.Create(p.Value)))),
        };
        if (state.Tailscale.Enabled) config["dns"]!["respect-rules"] = true;
        if (state.Tailscale.Enabled && inspectionPort != 0)
            config["listeners"] = new JsonArray(new JsonObject { ["name"] = "TIZI-EXIT-CHECK", ["type"] = "mixed", ["listen"] = "127.0.0.1", ["port"] = inspectionPort, ["udp"] = false, ["proxy"] = TailscaleSettings.ProxyName });
        if (state.OutboundInterface.Length > 0) config["interface-name"] = state.OutboundInterface;
        // JSON is a YAML subset accepted by mihomo. Typed serialization quotes every credential.
        return new EngineSession(apiPort, secret, config.ToJsonString());
    }
    private static JsonArray Array(IEnumerable<string> values) => new(values.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
}
