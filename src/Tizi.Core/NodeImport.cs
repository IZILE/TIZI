using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace Tizi.Core;

public static class NodeImport
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public const int MaxNodes = 3000;
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
        { "ss", "ssr", "vmess", "vless", "trojan", "hysteria", "hysteria2", "tuic", "socks5", "http", "wireguard", "snell", "anytls", "ssh", "mieru", "sudoku", "masque", "trusttunnel", "gost-relay" };
    public static List<ProxyNode> Parse(string text, Guid? subscription = null)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new FormatException("Import is too large (4 MB maximum).");
        text = text.Trim('\uFEFF', ' ', '\r', '\n', '\t');
        if (text.Length == 0) throw new FormatException("Nothing to import.");
        if (!text.Contains("://") && !text.StartsWith('{') && !text.Contains("proxies:"))
        {
            try { var decoded = DecodeBase64(text); if (decoded.Contains("://")) text = decoded.Trim(); }
            catch (FormatException) { }
        }
        List<ProxyNode> nodes;
        if (text.StartsWith('{') || text.StartsWith('['))
        {
            var json = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { MaxDepth = 24 });
            if (json is JsonObject o && o["proxies"] is JsonArray array) nodes = array.Select(item => FromOptions(item as JsonObject ?? throw new FormatException("Invalid node."), subscription)).ToList();
            else if (json is JsonArray list) nodes = list.Select(item => FromOptions(item as JsonObject ?? throw new FormatException("Invalid node."), subscription)).ToList();
            else nodes = [FromOptions(json as JsonObject ?? throw new FormatException("Invalid node."), subscription)];
        }
        else if (text.Split('\n').FirstOrDefault()?.Contains("://") == true)
            nodes = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(line => !line.StartsWith('#')).Select(line => ParseUri(line, subscription)).ToList();
        else
        {
            var yaml = new YamlStream(); yaml.Load(new StringReader(text));
            if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode map) throw new FormatException("Expected a Clash YAML configuration.");
            var pair = map.Children.FirstOrDefault(p => p.Key is YamlScalarNode { Value: "proxies" });
            if (pair.Value is not YamlSequenceNode sequence) throw new FormatException("No proxies found. Provider-only configurations need a direct node subscription.");
            if (sequence.Children.Count > MaxNodes) throw new FormatException("Too many nodes.");
            var budget = 100000;
            nodes = sequence.Children.Select(item => FromOptions(ConvertYaml(item, 0, new HashSet<YamlNode>(ReferenceEqualityComparer.Instance), ref budget) as JsonObject ?? throw new FormatException("Invalid node."), subscription)).ToList();
        }
        if (nodes.Count is 0 or > MaxNodes) throw new FormatException("Import must contain between 1 and 3000 nodes.");
        return nodes;
    }
    public static ProxyNode FromOptions(JsonObject original, Guid? subscription = null)
    {
        var options = ShadowrocketImport.Normalize(original);
        var name = options["name"]?.ToString()?.Trim();
        options.Remove("name");
        var type = options["type"]?.ToString().ToLowerInvariant() ?? "";
        options["type"] = type;
        if (!AllowedTypes.Contains(type)) throw new FormatException("Unsupported proxy protocol: " + type);
        var server = options["server"]?.ToString().Trim() ?? "";
        if (server.Length is 0 or > 253 || server.Any(char.IsWhiteSpace) || server.Any(c => c is '/' or '\\' or '\0')) throw new FormatException("Invalid server address.");
        if (!int.TryParse(options["port"]?.ToString(), out var port) || port is < 1 or > 65535) throw new FormatException("Invalid server port.");
        options["server"] = server; options["port"] = port;
        // File-backed material must be explicitly managed by TIZI, never read from a subscription.
        RejectFileReferences(options);
        name = string.IsNullOrWhiteSpace(name) ? $"{server}:{port}" : name;
        if (name.Length > 200 || name.Any(c => c is '\n' or '\r' or '\0')) throw new FormatException("Invalid node name.");
        var region = original["country-code"]?.ToString() ?? original["country"]?.ToString() ?? original["flag"]?.ToString(); options.Remove("country-code"); options.Remove("country"); options.Remove("flag");
        return new ProxyNode { Name = name, Options = options, SubscriptionId = subscription, CountryCode = NodeRegion.Match(name, region) };
    }
    private static void RejectFileReferences(JsonNode? node)
    {
        if (node is JsonObject o)
            foreach (var pair in o)
            {
                if (pair.Key is "private-key-path" or "certificate-path" or "ca" or "dialer-proxy" || pair.Key.EndsWith("-path", StringComparison.OrdinalIgnoreCase)) throw new FormatException("File references and external proxy chaining are not supported in node imports.");
                RejectFileReferences(pair.Value);
            }
        else if (node is JsonArray a) foreach (var item in a) RejectFileReferences(item);
    }
    public static string DecodeBase64(string text)
    {
        var value = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace('-', '+').Replace('_', '/');
        if (value.Length % 4 != 0) value = value.PadRight(value.Length + (4 - value.Length % 4), '=');
        return Encoding.UTF8.GetString(System.Convert.FromBase64String(value));
    }
    private static ProxyNode ParseUri(string input, Guid? subscription)
    {
        if (input.Length > 32768) throw new FormatException("Node link is too long.");
        var scheme = input.Split("://", 2)[0].ToLowerInvariant();
        if (scheme is "wg" or "wireguard") return WireGuardImport.Link(input, subscription);
        if (scheme == "vmess")
        {
            if (input.Contains('?') || !DecodeBase64(input[8..].Split('#')[0]).TrimStart().StartsWith('{')) return ShadowrocketImport.VmessLink(input, subscription);
            var v = JsonNode.Parse(DecodeBase64(input[8..])) as JsonObject ?? throw new FormatException("Invalid VMess link.");
            var o = new JsonObject { ["name"] = v["ps"]?.ToString(), ["type"] = "vmess", ["server"] = v["add"]?.ToString(), ["port"] = v["port"]?.ToString(), ["uuid"] = v["id"]?.ToString(), ["alterId"] = int.TryParse(v["aid"]?.ToString(), out var aid) ? aid : 0, ["cipher"] = v["scy"]?.ToString() ?? "auto", ["tls"] = v["tls"]?.ToString() == "tls" };
            if (v["sni"]?.ToString() is { Length: > 0 } sni) o["servername"] = sni;
            var net = v["net"]?.ToString();
            if (net is "ws" or "grpc" or "h2" or "http")
            {
                o["network"] = net;
                if (net == "ws") o["ws-opts"] = new JsonObject { ["path"] = v["path"]?.ToString() ?? "/", ["headers"] = new JsonObject { ["Host"] = v["host"]?.ToString() ?? "" } };
                if (net == "grpc") o["grpc-opts"] = new JsonObject { ["grpc-service-name"] = v["path"]?.ToString() ?? "" };
            }
            return FromOptions(o, subscription);
        }
        if (scheme == "ssr")
        {
            var decoded = DecodeBase64(input[6..]); var parts = decoded.Split("/?", 2); var fields = parts[0].Split(':');
            if (fields.Length < 6) throw new FormatException("Invalid ShadowsocksR link.");
            var offset = fields.Length - 5; var query = parts.Length > 1 ? Query(parts[1]) : new Dictionary<string, string>();
            var o = new JsonObject { ["type"] = "ssr", ["server"] = string.Join(':', fields[..offset]).Trim('[', ']'), ["port"] = fields[offset], ["protocol"] = fields[offset + 1], ["cipher"] = fields[offset + 2], ["obfs"] = fields[offset + 3], ["password"] = DecodeBase64(fields[offset + 4]), ["udp"] = true };
            foreach (var (key, target) in new[] { ("remarks", "name"), ("obfsparam", "obfs-param"), ("protoparam", "protocol-param") }) if (query.TryGetValue(key, out var value) && value.Length > 0) o[target] = DecodeBase64(value);
            return FromOptions(o, subscription);
        }
        if (scheme == "ss")
        {
            var fragment = input.IndexOf('#'); var name = fragment >= 0 ? Uri.UnescapeDataString(input[(fragment + 1)..]) : null;
            var link = fragment >= 0 ? input[..fragment] : input;
            var queryStart = link.IndexOf('?');
            var query = queryStart >= 0 ? Query(link[(queryStart + 1)..]) : new Dictionary<string, string>();
            var body = (queryStart >= 0 ? link[5..queryStart] : link[5..]).TrimEnd('/');
            if (!body.Contains('@')) body = DecodeBase64(body);
            var at = body.LastIndexOf('@'); if (at < 0) throw new FormatException("Invalid Shadowsocks link.");
            var credentials = body[..at]; credentials = credentials.Contains(':') ? Uri.UnescapeDataString(credentials) : DecodeBase64(Uri.UnescapeDataString(credentials));
            var colon = credentials.IndexOf(':'); if (colon < 0) throw new FormatException("Invalid Shadowsocks credentials.");
            var endpoint = new Uri("ss://" + body[(at + 1)..]);
            var o = new JsonObject { ["name"] = name, ["type"] = "ss", ["server"] = endpoint.Host.Trim('[', ']'), ["port"] = endpoint.Port, ["cipher"] = credentials[..colon], ["password"] = credentials[(colon + 1)..], ["udp"] = true };
            if (query.TryGetValue("plugin", out var plugin))
            {
                var parts = plugin.Split(';');
                if (parts[0] is not ("obfs-local" or "simple-obfs" or "v2ray-plugin")) throw new FormatException("Unsupported Shadowsocks plugin.");
                o["plugin"] = parts[0] == "v2ray-plugin" ? "v2ray-plugin" : "obfs";
                var opts = new JsonObject();
                foreach (var part in parts.Skip(1)) { var kv = part.Split('=', 2); opts[kv[0] == "obfs" ? "mode" : kv[0].Replace("obfs-", "")] = kv.Length == 2 ? JsonValue.Create(kv[1]) : JsonValue.Create(true); }
                o["plugin-opts"] = opts;
            }
            return FromOptions(o, subscription);
        }
        if (scheme is "socks" or "socks5" && !input[ (scheme.Length + 3).. ].Contains('@') && !input[(scheme.Length + 3)..].Split('?')[0].Contains(':'))
        {
            var suffix = input[(scheme.Length + 3)..].Split('?', '#')[0];
            var decoded = DecodeBase64(suffix); input = "socks5://" + decoded + input[(scheme.Length + 3 + suffix.Length)..]; scheme = "socks5";
        }
        if (scheme == "socks") { input = "socks5://" + input[8..]; scheme = "socks5"; }
        if (scheme is not ("vless" or "trojan" or "hysteria" or "hysteria2" or "hy2" or "tuic" or "socks5" or "http" or "https" or "anytls")) throw new FormatException("Unsupported node link. Import Clash YAML for other protocols.");
        var uri = new Uri(input); var q = Query(uri.Query.TrimStart('?'));
        var type = scheme switch { "hy2" => "hysteria2", "https" => "http", _ => scheme };
        var user = Uri.UnescapeDataString(uri.UserInfo);
        var node = new JsonObject { ["name"] = Uri.UnescapeDataString(uri.Fragment.TrimStart('#')) is { Length: > 0 } label ? label : q.GetValueOrDefault("remarks"), ["type"] = type, ["server"] = uri.Host.Trim('[', ']'), ["port"] = uri.Port >= 0 ? uri.Port : scheme is "trojan" or "hysteria" or "hysteria2" or "hy2" or "tuic" or "https" or "anytls" ? 443 : 0, ["udp"] = true };
        if (type == "vless") node["uuid"] = user;
        else if (type is "http" or "socks5") { var pair = uri.UserInfo.Split(':', 2); if (user.Length > 0) { node["username"] = Uri.UnescapeDataString(pair[0]); node["password"] = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : ""; } }
        else if (type == "tuic") { var pair = uri.UserInfo.Split(':', 2); if (pair.Length != 2) throw new FormatException("TUIC requires UUID and password."); node["uuid"] = Uri.UnescapeDataString(pair[0]); node["password"] = Uri.UnescapeDataString(pair[1]); }
        else node["password"] = user;
        var security = q.GetValueOrDefault("security");
        if (type is "vless" or "http" or "socks5") node["tls"] = security is "tls" or "reality" || scheme == "https";
        if (q.GetValueOrDefault("sni") is { Length: > 0 } host) node[type is "vless" or "http" or "socks5" ? "servername" : "sni"] = host;
        if (q.GetValueOrDefault("flow") is { Length: > 0 } flow) node["flow"] = flow;
        if (q.GetValueOrDefault("fp") is { Length: > 0 } fp) node["client-fingerprint"] = fp;
        if (security == "reality") node["reality-opts"] = new JsonObject { ["public-key"] = q.GetValueOrDefault("pbk") ?? "", ["short-id"] = q.GetValueOrDefault("sid") ?? "" };
        if (q.GetValueOrDefault("allowInsecure") == "1" || q.GetValueOrDefault("insecure") == "1") node["skip-cert-verify"] = true;
        if (q.GetValueOrDefault("alpn") is { Length: > 0 } alpn) node["alpn"] = new JsonArray(alpn.Split(',').Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        if (q.GetValueOrDefault("type") is "ws" or "grpc" or "http" or "h2")
        {
            node["network"] = q["type"];
            if (q["type"] == "ws") node["ws-opts"] = new JsonObject { ["path"] = q.GetValueOrDefault("path") ?? "/", ["headers"] = new JsonObject { ["Host"] = q.GetValueOrDefault("host") ?? "" } };
            if (q["type"] == "grpc") node["grpc-opts"] = new JsonObject { ["grpc-service-name"] = q.GetValueOrDefault("serviceName") ?? "" };
        }
        if (type == "hysteria2" && q.GetValueOrDefault("obfs") is { Length: > 0 } obfs) { node["obfs"] = obfs; node["obfs-password"] = q.GetValueOrDefault("obfs-password") ?? ""; }
        if (type == "hysteria") { node.Remove("password"); node["auth-str"] = q.GetValueOrDefault("auth") ?? user; node["up"] = q.GetValueOrDefault("upmbps") ?? "10"; node["down"] = q.GetValueOrDefault("downmbps") ?? "50"; if (q.GetValueOrDefault("obfs") is { Length: > 0 } hObfs) node["obfs"] = hObfs; }
        return FromOptions(node, subscription);
    }
    private static Dictionary<string, string> Query(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in value.Split('&', StringSplitOptions.RemoveEmptyEntries)) { var pair = item.Split('=', 2); result[Uri.UnescapeDataString(pair[0])] = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : ""; }
        return result;
    }
    internal static JsonObject ReadYaml(string text)
    {
        var yaml = new YamlStream(); yaml.Load(new StringReader(text));
        if (yaml.Documents.Count != 1) throw new FormatException("Expected one YAML document.");
        var budget = 100000;
        return ConvertYaml(yaml.Documents[0].RootNode, 0, new HashSet<YamlNode>(ReferenceEqualityComparer.Instance), ref budget) as JsonObject ?? throw new FormatException("Expected a YAML mapping.");
    }
    private static JsonNode? ConvertYaml(YamlNode node, int depth, HashSet<YamlNode> active, ref int budget)
    {
        if (--budget < 0 || depth > 24 || !active.Add(node)) throw new FormatException("YAML is too complex or contains circular aliases.");
        try
        {
            if (node is YamlScalarNode s)
            {
                if (s.Style == YamlDotNet.Core.ScalarStyle.Plain)
                {
                    if (s.Value is "null" or "~" || s.Value is null) return null;
                    if (bool.TryParse(s.Value, out var flag)) return JsonValue.Create(flag);
                    if (long.TryParse(s.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)) return JsonValue.Create(number);
                }
                return JsonValue.Create(s.Value ?? "");
            }
            if (node is YamlSequenceNode seq) { var array = new JsonArray(); foreach (var child in seq.Children) array.Add(ConvertYaml(child, depth + 1, active, ref budget)); return array; }
            if (node is YamlMappingNode map)
            {
                var obj = new JsonObject();
                foreach (var pair in map.Children) { if (pair.Key is not YamlScalarNode { Value: { } key } || obj.ContainsKey(key)) throw new FormatException("Invalid YAML mapping."); obj[key] = ConvertYaml(pair.Value, depth + 1, active, ref budget); }
                return obj;
            }
            throw new FormatException("Unsupported YAML value.");
        }
        finally { active.Remove(node); }
    }
}
