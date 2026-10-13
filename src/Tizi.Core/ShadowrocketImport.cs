using System.Text.Json.Nodes;

namespace Tizi.Core;

/// <summary>Adapters for public sharing formats. This does not execute modules, scripts or MITM settings.</summary>
public static class ShadowrocketImport
{
    public static JsonObject Normalize(JsonObject input)
    {
        var result = (JsonObject)input.DeepClone();
        void Alias(string target, params string[] names)
        { if (result[target] is null) foreach (var name in names) if (result[name] is { } value) { result[target] = value.DeepClone(); break; } }
        Alias("name", "remarks", "remark", "title", "ps"); Alias("server", "host", "address"); Alias("cipher", "method", "encrypt-method");
        var type = result["type"]?.ToString().ToLowerInvariant();
        result["type"] = type switch { "shadowsocks" => "ss", "shadowsocksr" => "ssr", "socks" or "socks5-tls" or "socks5 over tls" => "socks5", "https" => "http", "hy2" => "hysteria2", "relay" => "gost-relay", _ => type };
        if (type == "https") result["tls"] = true;
        if (type is "socks5-tls" or "socks5 over tls") result["tls"] = true;
        // Shadowrocket's JSON export uses host, remarks and method; strip those aliases after conversion.
        if (input["server"] is null && input["host"] is not null)
        {
            Alias("uuid", "id"); Alias(type is "vmess" or "vless" ? "servername" : "sni", "peer", "sni"); Alias("alterId", "alter-id");
            if (type is "vmess" or "vless") { if (input["password"] is { } password) result["uuid"] = password.DeepClone(); result.Remove("password"); }
            if (input["obfs"]?.ToString() is "websocket" or "ws")
            { result["network"] = "ws"; result["ws-opts"] = new JsonObject { ["path"] = input["path"]?.ToString() ?? "/", ["headers"] = new JsonObject { ["Host"] = input["obfsParam"]?.ToString() ?? "" } }; result.Remove("obfs"); }
            else if (result["type"]?.ToString() == "ss" && input["obfs"]?.ToString() is "http" or "tls")
            { result["plugin"] = "obfs"; result["plugin-opts"] = new JsonObject { ["mode"] = input["obfs"]!.ToString(), ["host"] = input["obfsParam"]?.ToString() ?? "" }; result.Remove("obfs"); }
            if (result["tls"] is { } tls) result["tls"] = tls.ToString().ToLowerInvariant() is "true" or "1";
            if (result["alterId"] is { } aid && int.TryParse(aid.ToString(), out var number)) result["alterId"] = number;
            if (type is "vmess" or "vless") result.Remove("sni");
            foreach (var key in new[] { "host", "address", "id", "alter-id", "peer", "obfsParam", "path", "group", "updated", "created", "order", "weight", "flag", "country", "country-code", "uuidShadowrocket" }) result.Remove(key);
        }
        foreach (var key in new[] { "remarks", "remark", "title", "ps", "method", "encrypt-method" }) result.Remove(key);
        return result["type"]?.ToString() == "wireguard" ? WireGuardImport.Normalize(result, input["host"] is not null && input["server"] is null) : result;
    }
    public static ProxyNode VmessLink(string input, Guid? subscription)
    {
        var body = input[8..].Split('?', '#')[0]; var query = input.Contains('?') ? input.Split('?', 2)[1].Split('#')[0] : "";
        var q = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries)) { var pair = part.Split('=', 2); q[Uri.UnescapeDataString(pair[0])] = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : ""; }
        var decoded = body.Contains('@') ? Uri.UnescapeDataString(body) : NodeImport.DecodeBase64(body);
        var at = decoded.LastIndexOf('@'); if (at < 0) throw new FormatException("Invalid Shadowrocket VMess link.");
        var credential = decoded[..at].Split(':', 2); if (credential.Length != 2) throw new FormatException("Invalid VMess credentials.");
        var endpoint = new Uri("vmess://" + decoded[(at + 1)..]);
        var options = new JsonObject { ["type"] = "vmess", ["name"] = q.GetValueOrDefault("remarks") ?? (input.Contains('#') ? Uri.UnescapeDataString(input.Split('#', 2)[1]) : ""), ["server"] = endpoint.Host.Trim('[', ']'), ["port"] = endpoint.Port, ["uuid"] = credential[1], ["cipher"] = credential[0], ["alterId"] = int.TryParse(q.GetValueOrDefault("alterId"), out var aid) ? aid : 0, ["tls"] = q.GetValueOrDefault("tls") is "1" or "true" };
        if ((q.GetValueOrDefault("peer") ?? q.GetValueOrDefault("sni")) is { Length: > 0 } host) options["servername"] = host;
        if (q.GetValueOrDefault("obfs") is "websocket" or "ws")
        { options["network"] = "ws"; options["ws-opts"] = new JsonObject { ["path"] = q.GetValueOrDefault("path") ?? "/", ["headers"] = new JsonObject { ["Host"] = q.GetValueOrDefault("obfsParam") ?? "" } }; }
        if (q.GetValueOrDefault("allowInsecure") is "1" or "true") options["skip-cert-verify"] = true;
        return NodeImport.FromOptions(options, subscription);
    }
}
