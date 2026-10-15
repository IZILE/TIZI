using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tizi.Core;

public sealed record ImportDocument(List<ProxyNode> Nodes, List<RoutingRule> Rules, List<Subscription> Subscriptions, List<string> Warnings, string Format)
{
    public static string? RemoteUrl(string input) => RemoteSubscription(input)?.Url;
    public static Subscription? RemoteSubscription(string input)
    {
        input = Unwrap(input, out var name);
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0)
            return new Subscription { Url = SourceUpdates.WebUri(input).AbsoluteUri, Name = name ?? uri.Host };
        return null;
    }
    private static string Unwrap(string input, out string? name)
    {
        input = input.Trim(); name = null;
        static string? Title(string query, string? fragment = null)
        {
            var pairs = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split('=', 2)).Where(s => s.Length == 2).ToArray();
            foreach (var key in new[] { "remarks", "name", "title" })
                foreach (var pair in pairs.Where(p => p[0].Equals(key, StringComparison.OrdinalIgnoreCase)))
                    if (SubscriptionNaming.Clean(Uri.UnescapeDataString(pair[1].Replace('+', ' '))) is { } title) return title;
            return fragment is null ? null : SubscriptionNaming.Clean(Uri.UnescapeDataString(fragment));
        }
        static string Nested(string payload)
        {
            // A literal nested URI already owns its escaping. Decode only an encoded wrapper.
            var separator = payload.IndexOf("://", StringComparison.Ordinal);
            return separator > 0 && Uri.CheckSchemeName(payload[..separator]) ? payload : Uri.UnescapeDataString(payload);
        }
        for (var i = 0; i < 3; i++)
        {
            if (input.StartsWith("clash://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("clash-verge://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("tizi://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(input, UriKind.Absolute, out var command) || command.Host is not ("install-config" or "import")) throw new FormatException("This command is not a subscription import.");
                var values = command.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split('=', 2)).Where(s => s.Length == 2 && s[0].Equals("url", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (values.Length != 1) throw new FormatException("Import command requires one subscription URL.");
                name ??= Title(command.Query);
                input = Uri.UnescapeDataString(values[0][1]);
            }
            else if (input.StartsWith("shadowrocket://add/", StringComparison.OrdinalIgnoreCase)) input = Nested(input[19..]);
            else if (input.StartsWith("shadowrocket://config/add/", StringComparison.OrdinalIgnoreCase)) input = Nested(input[26..]);
            else if (input.StartsWith("sub://", StringComparison.OrdinalIgnoreCase))
            {
                var payload = input[6..]; var fragmentAt = payload.IndexOf('#');
                var fragment = fragmentAt < 0 ? null : payload[(fragmentAt + 1)..];
                if (fragmentAt >= 0) payload = payload[..fragmentAt];
                var queryAt = payload.IndexOf('?');
                name ??= Title(queryAt < 0 ? "" : payload[(queryAt + 1)..], fragment);
                if (queryAt >= 0) payload = payload[..queryAt];
                try { input = NodeImport.DecodeBase64(Uri.UnescapeDataString(payload)); }
                catch (FormatException) { throw new FormatException("Invalid encoded subscription URL."); }
            }
            else break;
        }
        if (input.StartsWith("shadowrocket://", StringComparison.OrdinalIgnoreCase)) throw new FormatException("This Shadowrocket command is not a node or subscription import.");
        return input;
    }
    public static ImportDocument Parse(string input, Guid? subscriptionId = null)
    {
        if (Encoding.UTF8.GetByteCount(input) > NodeImport.MaxBytes) throw new FormatException("Import is too large (4 MB maximum).");
        var text = Unwrap(input.Trim('\uFEFF', ' ', '\t', '\n', '\r'), out _);
        if (text.StartsWith("[General]", StringComparison.OrdinalIgnoreCase) || text.StartsWith("[Proxy]", StringComparison.OrdinalIgnoreCase) || text.Contains("\n[Proxy]", StringComparison.OrdinalIgnoreCase) || text.StartsWith("[Rule]", StringComparison.OrdinalIgnoreCase)) return Conf(text, subscriptionId);
        if (text.StartsWith('{') || text.StartsWith('['))
        {
            var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { MaxDepth = 24 });
            if (root is JsonObject config && (config["proxies"] is JsonArray || config["rules"] is JsonArray)) return Config(config, subscriptionId, "Clash JSON");
            if (root is JsonObject single && IsSubscription(single))
                return new([], [], [SubscriptionObject(single)], [], "Shadowrocket subscription JSON");
            var entries = root is JsonArray array ? array : (root as JsonObject)?["servers"] as JsonArray ?? (root as JsonObject)?["configs"] as JsonArray;
            if (entries is not null)
            {
                if (entries.Count > NodeImport.MaxNodes) throw new FormatException("Too many nodes.");
                var result = new ImportDocument([], [], [], [], "Shadowrocket / JSON");
                for (var i = 0; i < entries.Count; i++)
                {
                    try
                    {
                        var node = entries[i] as JsonObject ?? throw new FormatException("Invalid node object.");
                        if (IsSubscription(node)) result.Subscriptions.Add(SubscriptionObject(node));
                        else result.Nodes.Add(NodeImport.FromOptions(node, subscriptionId));
                    }
                    catch (Exception e) when (e is FormatException or JsonException or UriFormatException) { result.Warnings.Add($"JSON #{i + 1}: {SafeReason(e)}"); }
                }
                EnsureSome(result); return result;
            }
        }
        if (text.Contains("proxies:") || text.StartsWith("rules:")) return Config(NodeImport.ReadYaml(text), subscriptionId, "Clash YAML");
        return new(NodeImport.Parse(text, subscriptionId), [], [], [], text.Contains("proxies:") ? "Clash YAML" : "Share links / JSON");
    }
    private static bool IsSubscription(JsonObject node) => node["type"]?.ToString().Trim().ToLowerInvariant() is "subscribe" or "subscription";
    private static Subscription SubscriptionObject(JsonObject node)
    {
        string? First(params string[] keys) => keys.Select(k => node[k]?.ToString()?.Trim()).FirstOrDefault(s => !string.IsNullOrEmpty(s));
        var uri = SourceUpdates.WebUri(First("url", "host") ?? "");
        var name = First("remarks", "name", "title") ?? uri.Host;
        if (name.Length > 200 || name.Any(char.IsControl)) throw new FormatException("Invalid subscription name.");
        return new Subscription { Name = name, Url = uri.AbsoluteUri };
    }
    private static ImportDocument Config(JsonObject config, Guid? subscription, string format)
    {
        var result = new ImportDocument([], [], [], [], format);
        if (config["proxies"] is JsonArray nodes)
        {
            if (nodes.Count > NodeImport.MaxNodes) throw new FormatException("Too many nodes.");
            for (var i = 0; i < nodes.Count; i++)
                try { result.Nodes.Add(NodeImport.FromOptions(nodes[i] as JsonObject ?? throw new FormatException("Invalid node."), subscription)); }
                catch (FormatException e) { if (result.Warnings.Count < 100) result.Warnings.Add($"Node #{i + 1}: {SafeReason(e)}"); }
        }
        if (config["rules"] is JsonArray rules)
        {
            if (rules.Count > 10000) throw new FormatException("Too many rules.");
            try
            {
                var imported = RuleImport.Parse(config.ToJsonString());
                result.Rules.AddRange(imported.Rules.Where(r => r.ToCore() != "MATCH,TIZI"));
                if (imported.PolicyMappings.Count > 0) result.Warnings.Add("Proxy groups use their default action; proxy traffic follows the selected TIZI node.");
            }
            catch (FormatException e) { result.Warnings.Add("Rules: " + SafeReason(e)); }
        }
        if (config["proxy-groups"] is JsonArray groups && groups.Any(g => g?["type"]?.ToString() != "select")) result.Warnings.Add("Custom proxy groups require manual conversion.");
        if (config["proxy-providers"] is not null || config["rule-providers"] is not null || config["script"] is not null) result.Warnings.Add("Remote providers and scripts were not imported.");
        EnsureSome(result); return result;
    }
    private static ImportDocument Conf(string text, Guid? subscription)
    {
        var result = new ImportDocument([], [], [], [], "Shadowrocket .conf"); var section = ""; var skipped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); var lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++; var line = raw.Trim(); if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';') || line.StartsWith("//")) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Replace(" ", "").ToLowerInvariant(); continue; }
            try
            {
                if (section == "proxy")
                {
                    var pair = line.Split('=', 2); if (pair.Length != 2) throw new FormatException("Expected a named proxy.");
                    var fields = Csv(pair[1]); if (fields[0].ToLowerInvariant() is "direct" or "reject") continue;
                    if (fields.Count < 3) throw new FormatException("Incomplete proxy.");
                    var o = new JsonObject { ["name"] = pair[0].Trim(), ["type"] = fields[0].ToLowerInvariant(), ["server"] = fields[1], ["port"] = fields[2] };
                    var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var field in fields.Skip(3)) { var kv = field.Split('=', 2); if (kv.Length != 2) throw new FormatException("Unsupported positional proxy option."); extra[kv[0].Trim()] = kv[1].Trim(); }
                    foreach (var (key, value) in extra)
                    {
                        var target = key.ToLowerInvariant() switch { "encrypt-method" or "method" => "cipher", "username" or "password" or "uuid" or "protocol" or "obfs" or "flow" => key.ToLowerInvariant(), "protocol-param" => "protocol-param", "obfs-param" => "obfs-param", "sni" => o["type"]!.ToString() is "vmess" or "vless" ? "servername" : "sni", "alterid" => "alterId", "tls" or "skip-cert-verify" => key, "udp-relay" => "udp", "tfo" => "tfo", "ws" or "ws-path" or "ws-headers" => "", _ => throw new FormatException("Unsupported proxy option: " + key) };
                        if (target.Length == 0) continue;
                        o[target] = target is "tls" or "skip-cert-verify" or "udp" or "tfo" ? JsonValue.Create(value.ToLowerInvariant() is "true" or "1") : target == "alterId" && int.TryParse(value, out var aid) ? JsonValue.Create(aid) : JsonValue.Create(value);
                    }
                    if (extra.GetValueOrDefault("ws") is "true" or "1")
                    { o["network"] = "ws"; o["ws-opts"] = new JsonObject { ["path"] = extra.GetValueOrDefault("ws-path") ?? "/" }; if (extra.ContainsKey("ws-headers")) throw new FormatException("Custom WS headers require Clash YAML."); }
                    if (o["type"]!.ToString() == "ss" && extra.GetValueOrDefault("obfs") is "tls" or "http")
                    { o["plugin"] = "obfs"; o["plugin-opts"] = new JsonObject { ["mode"] = extra["obfs"], ["host"] = extra.GetValueOrDefault("obfs-param") ?? "" }; o.Remove("obfs"); o.Remove("obfs-param"); }
                    result.Nodes.Add(NodeImport.FromOptions(o, subscription));
                }
                else if (section == "rule")
                {
                    var fields = Csv(line); if (fields.Count != 3) throw new FormatException("Rule or final policy requires manual conversion.");
                    var action = fields[2].ToUpperInvariant(); if (action == "PROXY") action = "TIZI";
                    var rule = new RoutingRule(fields[0].ToUpperInvariant(), fields[1], action); ModelValidation.Rule(rule); result.Rules.Add(rule);
                }
                else skipped[section.Length == 0 ? "Unknown" : section] = skipped.GetValueOrDefault(section) + 1;
                if (result.Nodes.Count > NodeImport.MaxNodes || result.Rules.Count > 10000) throw new InvalidDataException("Configuration has too many entries.");
            }
            catch (Exception e) when (e is FormatException or UriFormatException) { if (result.Warnings.Count < 100) result.Warnings.Add($"Line {lineNumber}: {SafeReason(e)}"); }
        }
        foreach (var item in skipped) result.Warnings.Add($"[{item.Key}]: {item.Value} settings were not applied.");
        EnsureSome(result); return result;
    }
    private static List<string> Csv(string line)
    {
        var result = new List<string>(); var token = new StringBuilder(); var quoted = false;
        foreach (var c in line) { if (c == '"') quoted = !quoted; else if (c == ',' && !quoted) { result.Add(token.ToString().Trim()); token.Clear(); } else token.Append(c); }
        if (quoted) throw new FormatException("Unclosed quote."); result.Add(token.ToString().Trim()); return result;
    }
    private static string SafeReason(Exception e) => e is UriFormatException or JsonException ? "Invalid format." : e.Message.Length > 160 ? "Unsupported entry." : e.Message;
    private static void EnsureSome(ImportDocument document)
    { if (document.Nodes.Count + document.Rules.Count + document.Subscriptions.Count == 0) throw new FormatException("No supported nodes, subscriptions or rules found. Encrypted backups and proprietary databases cannot be imported."); }
}
