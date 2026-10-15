using System.Text.Json.Nodes;

namespace Tizi.Core;

public enum RouteMode { Rule, Global, Direct }
public enum CaptureMode { SystemProxy, Tun, LocalOnly }
public sealed record ProxyNode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? SubscriptionId { get; init; }
    public required string Name { get; init; }
    public required JsonObject Options { get; init; }
    public string Type => Options["type"]?.ToString() ?? "";
    public string Server => Options["server"]?.ToString() ?? "";
    public int Port => int.TryParse(Options["port"]?.ToString(), out var port) ? port : 0;
    public string CoreName => "node-" + Id.ToString("N");
    public string? CountryCode { get; init; }
    public string? SourceName { get; init; }
}
public sealed record Subscription
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required string Url { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? LastError { get; init; }
    public SubscriptionUsage? Usage { get; init; }
    public string? Website { get; init; }
}
public sealed record SubscriptionUsage(long? Upload, long? Download, long? Total, DateTimeOffset? ExpiresAt, bool NoExpiry = false)
{
    public long? Used => Upload.HasValue && Download.HasValue ? (long)Math.Min((decimal)Upload.Value + Download.Value, long.MaxValue) : null;
    public long? Remaining => Total is > 0 && Used.HasValue ? Math.Max(0, Total.Value - Used.Value) : null;
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && expiry <= now;
}
public sealed record ProviderSite
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required string Url { get; init; }
    public string Region { get; init; } = "";
    public string? Source { get; init; }
    public DateTimeOffset? CheckedAt { get; init; }
    public int? StatusCode { get; init; }
    public int? LatencyMs { get; init; }
    public string? Error { get; init; }
    public ProviderProfile? Profile { get; init; }
}
public sealed record ProviderPlan(string Name, decimal? Price, string Currency, string Period, decimal? TrafficGb, string Conditions, string Source)
{
    public string PriceUnit { get; init; } = "unknown";
    public string PriceQuote { get; init; } = "";
}
public sealed record ProviderProfile(string Description, List<ProviderPlan> Plans, List<string> Regions, string SecurityNotes, string IpQualityNotes, List<string> Sources, DateTimeOffset ResearchedAt)
{
    public string ReviewSummary { get; init; } = "";
    public List<ProviderReview> Reviews { get; init; } = [];
    public string Verification { get; init; } = "Unverified";
    public List<string> SupportedProtocols { get; init; } = [];
    public string SubscriptionSource { get; init; } = "";
}
public sealed record ProviderReview(string Title, string Url, string? Date, string Summary, int? Rank);
public sealed record DiscoverySettings
{
    public string Provider { get; init; } = "OpenAI";
    public string Protocol { get; init; } = "responses";
    public string Endpoint { get; init; } = "";
    public Dictionary<string, AiApiProfile> Profiles { get; init; } = new();
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "gpt-5.4-mini";
    public string Query { get; init; } = "查找面向个人日常上网、提供 Shadowsocks / Trojan / VLESS / WireGuard 或 Clash 订阅的机场服务，比较官网价格、流量额度、地区与退款政策。排除面向爬虫或企业数据采集的住宅代理池。";
    public ProviderSearchOptions Search { get; init; } = new();
}
public sealed record AiApiProfile(string Protocol, string Endpoint, string Model, string ApiKey);
public sealed record RoutingRule(string Type, string Value, string Action)
{
    public bool NoResolve { get; init; }
    public static readonly string[] Types = ["DOMAIN", "DOMAIN-SUFFIX", "DOMAIN-KEYWORD", "IP-CIDR", "IP-CIDR6", "DST-PORT", "PROCESS-NAME", "GEOIP", "MATCH"];
    public string ToCore() => (Type == "MATCH" ? $"MATCH,{Action}" : $"{Type},{Value},{Action}") + (NoResolve ? ",no-resolve" : "");
}
public sealed record ProviderSearchCache(DateTimeOffset SearchedAt, ProviderSearchOptions Criteria, List<ProviderSite> Results);
public sealed record AppState
{
    public List<ProxyNode> Nodes { get; init; } = [];
    public List<Subscription> Subscriptions { get; init; } = [];
    public List<ProviderSite> Providers { get; init; } = [];
    public List<RoutingRule> Rules { get; init; } = [];
    public string RuleSourceName { get; init; } = "";
    public DateTimeOffset? RulesUpdatedAt { get; init; }
    public DateTimeOffset? ProvidersSearchedAt { get; init; }
    public string? ProviderSearchError { get; init; }
    public ProviderSearchCache? LastProviderSearch { get; init; }
    public Guid? SelectedNode { get; init; }
    public RouteMode Mode { get; init; } = RouteMode.Rule;
    public CaptureMode Capture { get; init; } = CaptureMode.SystemProxy;
    public int MixedPort { get; init; } = 7897;
    public string OutboundInterface { get; init; } = "";
    public bool AutoUpdate { get; init; } = true;
    public bool AutoUpdateProviders { get; init; }
    public bool AutomaticWebsiteChecksOptIn { get; init; }
    public int ProviderUpdateMinutes { get; init; } = 30;
    public int UpdateMinutes { get; init; } = 30;
    public string CatalogUrl { get; init; } = "";
    public DateTimeOffset? CatalogUpdatedAt { get; init; }
    public string DelayUrl { get; init; } = "https://www.gstatic.com/generate_204";
    public DiscoverySettings Discovery { get; init; } = new();
    public bool StartAtLogin { get; init; }
    public bool StartHidden { get; init; } = true;
    public bool CloseToTray { get; init; }
    public bool RestoreConnection { get; init; } = true;
    public bool LastConnected { get; init; }
    public TunnelSettings Tunnel { get; init; } = new();
    public DnsSettings Dns { get; init; } = new();
    public TransportSettings Transport { get; init; } = new();
    public LatencySettings Latency { get; init; } = new();
    public LogSettings Logs { get; init; } = new();
    public IpQualitySettings IpQuality { get; init; } = new();
    public bool AutoSwitchOnTimeout { get; init; }
}
public static class UserData
{
    public static string DirectoryPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TIZI");
    public static string File(string name) => Path.Combine(DirectoryPath, name);
}
public static class ModelValidation
{
    public static void Rules(IReadOnlyList<RoutingRule> rules)
    {
        if (rules.Count > 10000) throw new FormatException("Maximum 10000 rules.");
        for (var i = 0; i < rules.Count; i++)
        {
            Rule(rules[i]);
            if (rules[i].Type == "MATCH" && i != rules.Count - 1) throw new FormatException("A final rule must be the last rule.");
        }
    }
    public static void Rule(RoutingRule rule)
    {
        if (!RoutingRule.Types.Contains(rule.Type) || rule.Action is not ("TIZI" or "DIRECT" or "REJECT")) throw new FormatException("Unsupported rule type or action.");
        if (rule.Type == "MATCH") { if (rule.Value.Length != 0 || rule.NoResolve) throw new FormatException("Invalid final rule."); return; }
        if (string.IsNullOrWhiteSpace(rule.Value) || rule.Value.Length > 512 || rule.Value.Any(c => c is ',' or '\r' or '\n' or '\0')) throw new FormatException("Invalid rule value.");
        if (rule.NoResolve && rule.Type is not ("IP-CIDR" or "IP-CIDR6" or "GEOIP")) throw new FormatException("no-resolve requires an IP rule.");
        if (rule.Type == "GEOIP" && (rule.Value.Length > 40 || rule.Value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))) throw new FormatException("Invalid GeoIP category.");
        if (rule.Type is "IP-CIDR" or "IP-CIDR6")
        {
            var parts = rule.Value.Split('/');
            if (parts.Length != 2 || !System.Net.IPAddress.TryParse(parts[0], out var ip) || !int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128)) throw new FormatException("Invalid IP network.");
            if ((rule.Type == "IP-CIDR6") != (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)) throw new FormatException("IP rule and address family do not match.");
        }
        if (rule.Type == "DST-PORT" && (!int.TryParse(rule.Value, out var port) || port is < 1 or > 65535)) throw new FormatException("Invalid destination port.");
    }
}
