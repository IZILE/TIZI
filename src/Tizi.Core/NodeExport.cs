using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using ZXing;
using ZXing.QrCode;
using FormatException = System.FormatException;

namespace Tizi.Core;

/// <summary>Explicit, credential-bearing exports. App settings and API keys are never serialized.</summary>
public static class NodeExport
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    private static string Escape(string s) => Uri.EscapeDataString(s);
    public static JsonObject Configuration(IEnumerable<ProxyNode> nodes, IEnumerable<RoutingRule> rules)
    {
        var proxies = new JsonArray(); var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var o = (JsonObject)node.Options.DeepClone(); var name = node.Name; var suffix = 2;
            while (!names.Add(name)) name = node.Name + " (" + suffix++ + ")";
            o["name"] = name; if (node.CountryCode is { } country) o["country-code"] = country; proxies.Add(o);
        }
        var list = new JsonArray(); foreach (var rule in rules) { ModelValidation.Rule(rule); list.Add(rule.ToCore()); } if (!list.Any(r => r?.ToString().StartsWith("MATCH,") == true)) list.Add("MATCH,TIZI");
        return new JsonObject { ["mixed-port"] = 7897, ["allow-lan"] = false, ["mode"] = "rule", ["proxies"] = proxies,
            ["proxy-groups"] = new JsonArray(new JsonObject { ["name"] = "TIZI", ["type"] = "select", ["proxies"] = new JsonArray(proxies.Select(p => (JsonNode?)JsonValue.Create(p!["name"]!.ToString())).ToArray()) }), ["rules"] = list };
    }
    public static string Json(IEnumerable<ProxyNode> nodes, IEnumerable<RoutingRule> rules) => Configuration(nodes, rules).ToJsonString(Pretty);
    public static string Yaml(IEnumerable<ProxyNode> nodes, IEnumerable<RoutingRule> rules)
    {
        YamlNode ConvertNode(JsonNode? value)
        {
            if (value is JsonObject o) { var map = new YamlMappingNode(); foreach (var p in o) map.Add(new YamlScalarNode(p.Key), ConvertNode(p.Value)); return map; }
            if (value is JsonArray a) return new YamlSequenceNode(a.Select(ConvertNode));
            if (value is null) return new YamlScalarNode("null");
            var element = JsonSerializer.SerializeToElement(value);
            return new YamlScalarNode(value.ToString()) { Style = element.ValueKind == JsonValueKind.String ? ScalarStyle.DoubleQuoted : ScalarStyle.Plain };
        }
        var stream = new YamlStream(new YamlDocument(ConvertNode(Configuration(nodes, rules)))); using var writer = new StringWriter(CultureInfo.InvariantCulture); stream.Save(writer, false); return writer.ToString();
    }
    public static string Subscriptions(IEnumerable<Subscription> subscriptions)
    {
        var array = new JsonArray(); foreach (var s in subscriptions) array.Add(new JsonObject { ["type"] = "subscribe", ["remarks"] = s.Name, ["url"] = SourceUpdates.WebUri(s.Url).AbsoluteUri });
        return array.ToJsonString(Pretty);
    }
    public static string ShareLink(ProxyNode node)
    {
        var o = node.Options; string V(string k, string fallback = "") => o[k]?.ToString() ?? fallback;
        bool Flag(string k) => V(k).Equals("true", StringComparison.OrdinalIgnoreCase) || V(k) == "1";
        var host = node.Server.Contains(':') ? "[" + node.Server + "]" : node.Server;
        var endpoint = host + ":" + node.Port; var q = new Dictionary<string, string>();
        void Put(string key, string value) { if (value.Length > 0) q[key] = value; }
        string Build(string scheme, string? credentials = null) => scheme + "://" + (credentials is null ? "" : credentials + "@") + endpoint + (q.Count > 0 ? "?" + string.Join('&', q.Select(p => Escape(p.Key) + "=" + Escape(p.Value))) : "") + "#" + Escape(node.Name);
        string link;
        if (node.Type == "wireguard")
        {
            Put("privateKey", V("private-key")); Put("publicKey", V("public-key")); Put("presharedKey", V("pre-shared-key"));
            Put("ip", string.Join(',', new[] { V("ip"), V("ipv6") }.Where(s => s.Length > 0))); Put("mtu", V("mtu")); Put("keepalive", V("persistent-keepalive"));
            if (o["dns"] is JsonArray dns) Put("dns", string.Join(',', dns.Select(x => x!.ToString())));
            if (o["reserved"] is JsonArray reserved) Put("reserved", string.Join(',', reserved.Select(x => x!.ToString())));
            if (o["remote-dns-resolve"] is not null) Put("remote-dns-resolve", Flag("remote-dns-resolve") ? "1" : "0");
            Put("udp", Flag("udp") ? "1" : "0"); Put("flag", node.CountryCode ?? ""); link = Build("wg");
        }
        else if (node.Type == "ss")
        {
            if (V("plugin") is { Length: > 0 } plugin)
            {
                var parts = new List<string> { plugin == "obfs" ? "obfs-local" : plugin };
                if (o["plugin-opts"] is JsonObject opts) foreach (var p in opts) parts.Add((plugin == "obfs" ? p.Key == "mode" ? "obfs" : "obfs-" + p.Key : p.Key) + "=" + p.Value?.ToString());
                Put("plugin", string.Join(';', parts));
            }
            link = Build("ss", B64(V("cipher") + ":" + V("password")).TrimEnd('='));
        }
        else if (node.Type == "ssr")
        {
            Put("remarks", B64(node.Name).TrimEnd('=')); Put("obfsparam", B64(V("obfs-param")).TrimEnd('=')); Put("protoparam", B64(V("protocol-param")).TrimEnd('='));
            link = "ssr://" + B64(endpoint + ":" + V("protocol") + ":" + V("cipher") + ":" + V("obfs") + ":" + B64(V("password")).TrimEnd('=') + "/?" + string.Join('&', q.Select(p => p.Key + "=" + p.Value))).TrimEnd('=');
        }
        else if (node.Type == "vmess")
        {
            var v = new JsonObject { ["v"] = "2", ["ps"] = node.Name, ["add"] = node.Server, ["port"] = node.Port, ["id"] = V("uuid"), ["aid"] = V("alterId", "0"), ["scy"] = V("cipher", "auto"), ["net"] = V("network", "tcp"), ["tls"] = Flag("tls") ? "tls" : "", ["sni"] = V("servername"),
                ["path"] = o["ws-opts"]?["path"]?.ToString() ?? o["grpc-opts"]?["grpc-service-name"]?.ToString() ?? "", ["host"] = o["ws-opts"]?["headers"]?["Host"]?.ToString() ?? "" };
            link = "vmess://" + B64(v.ToJsonString());
        }
        else if (node.Type is "vless" or "trojan" or "hysteria" or "hysteria2" or "tuic" or "socks5" or "http" or "anytls")
        {
            Put("sni", V(node.Type is "vless" or "http" or "socks5" ? "servername" : "sni"));
            if (Flag("tls")) Put("security", o["reality-opts"] is null ? "tls" : "reality");
            if (o["reality-opts"] is JsonObject reality) { Put("security", "reality"); Put("pbk", reality["public-key"]?.ToString() ?? ""); Put("sid", reality["short-id"]?.ToString() ?? ""); }
            Put("flow", V("flow")); Put("fp", V("client-fingerprint")); if (Flag("skip-cert-verify")) Put("allowInsecure", "1");
            if (o["alpn"] is JsonArray alpn) Put("alpn", string.Join(',', alpn.Select(x => x!.ToString())));
            Put("type", V("network")); Put("path", o["ws-opts"]?["path"]?.ToString() ?? ""); Put("host", o["ws-opts"]?["headers"]?["Host"]?.ToString() ?? ""); Put("serviceName", o["grpc-opts"]?["grpc-service-name"]?.ToString() ?? "");
            if (node.Type == "hysteria2") { Put("obfs", V("obfs")); Put("obfs-password", V("obfs-password")); }
            if (node.Type == "hysteria") { Put("auth", V("auth-str")); Put("upmbps", V("up")); Put("downmbps", V("down")); Put("obfs", V("obfs")); }
            var credential = node.Type switch { "vless" => Escape(V("uuid")), "tuic" => Escape(V("uuid")) + ":" + Escape(V("password")), "socks5" or "http" => V("username").Length == 0 && V("password").Length == 0 ? null : Escape(V("username")) + ":" + Escape(V("password")), "hysteria" => null, _ => Escape(V("password")) };
            link = Build(node.Type == "http" && Flag("tls") ? "https" : node.Type, credential);
        }
        else throw new FormatException("This protocol has no supported share link. Export JSON or YAML instead.");
        // Refuse links that would silently discard advanced settings.
        var restored = NodeImport.Parse(link).Single();
        foreach (var pair in o)
        {
            if (pair.Value is null || pair.Value.ToString().Length == 0) continue;
            if (!JsonNode.DeepEquals(pair.Value, restored.Options[pair.Key])) throw new FormatException("A share link cannot preserve all parameters. Export JSON or YAML instead.");
        }
        return link;
    }
    public static byte[] QrPng(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > 1800) throw new FormatException("This configuration is too large for a reliable QR code. Export a file instead.");
        var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 768, 768, new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 4, [EncodeHintType.CHARACTER_SET] = "UTF-8" });
        using var bitmap = new SKBitmap(matrix.Width, matrix.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(SKColors.White); using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false }; for (var y = 0; y < matrix.Height; y++) for (var x = 0; x < matrix.Width; x++) if (matrix[x, y]) canvas.DrawPoint(x, y, paint); }
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
}
