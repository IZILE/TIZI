using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Tizi.Core;

public static class WireGuardImport
{
    public static JsonObject Normalize(JsonObject source, bool shadowrocket)
    {
        if (shadowrocket && (!string.IsNullOrEmpty(source["chain"]?.ToString()) || !string.IsNullOrEmpty(source["data"]?.ToString()))) throw new FormatException("WireGuard chaining or extended data requires manual conversion.");
        var o = (JsonObject)source.DeepClone();
        void Alias(string target, params string[] names)
        { if (o[target] is null) foreach (var key in names) if (o[key] is { } value) { o[target] = value.DeepClone(); break; } }
        Alias("private-key", "privateKey"); Alias("public-key", "publicKey"); Alias("pre-shared-key", "presharedKey", "preSharedKey");
        Alias("persistent-keepalive", "keepalive");
        if (shadowrocket) Alias("pre-shared-key", "password");
        foreach (var key in new[] { "privateKey", "publicKey", "presharedKey", "preSharedKey", "keepalive" }) o.Remove(key);
        if (shadowrocket)
            foreach (var key in new[] { "password", "uuid", "obfs", "obfsParam", "chain", "data", "ping", "flag", "title", "weight", "updated", "created" }) o.Remove(key);
        foreach (var key in new[] { "private-key", "public-key", "pre-shared-key" })
        {
            var value = o[key]?.ToString().Trim();
            if (string.IsNullOrEmpty(value)) { o.Remove(key); continue; }
            try { if (Convert.FromBase64String(value).Length != 32) throw new FormatException(); }
            catch (FormatException) { throw new FormatException("WireGuard " + key + " must be a Base64-encoded 32-byte key."); }
            o[key] = value;
        }
        if (o["private-key"] is null || (o["public-key"] is null && o["peers"] is null)) throw new FormatException("WireGuard private and public keys are required.");
        foreach (var key in new[] { "mtu", "persistent-keepalive" })
        {
            var value = o[key]?.ToString(); if (string.IsNullOrWhiteSpace(value)) { o.Remove(key); continue; }
            if (!int.TryParse(value, out var number) || number < (key == "mtu" ? 576 : 0) || number > 65535) throw new FormatException("Invalid WireGuard " + key + ".");
            o[key] = number;
        }
        foreach (var key in new[] { "udp", "remote-dns-resolve" })
            if (o[key] is { } value) o[key] = Flag(value.ToString());
        if (o["udp"] is null) o["udp"] = true;
        if (shadowrocket && o["ip"] is JsonValue)
        {
            var addresses = o["ip"]!.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); o.Remove("ip");
            foreach (var address in addresses)
            {
                if (!IPAddress.TryParse(address.Split('/')[0], out var parsed)) throw new FormatException("Invalid WireGuard interface address.");
                var key = parsed.AddressFamily == AddressFamily.InterNetwork ? "ip" : "ipv6";
                if (o[key] is not null) throw new FormatException("Multiple WireGuard addresses of the same family require manual configuration.");
                o[key] = address;
            }
        }
        foreach (var key in new[] { "ip", "ipv6" })
            if (o[key] is { } value)
            {
                var parts = value.ToString().Split('/');
                if (!IPAddress.TryParse(parts[0], out var ip) || (key == "ip") != (ip.AddressFamily == AddressFamily.InterNetwork) || parts.Length > 2 || (parts.Length == 2 && (!int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > (key == "ip" ? 32 : 128)))) throw new FormatException("Invalid WireGuard interface address.");
            }
        if (o["ip"] is null && o["ipv6"] is null) throw new FormatException("WireGuard requires an interface address.");
        if (o["dns"] is JsonValue dns)
            o["dns"] = new JsonArray(dns.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        if (o["dns"] is JsonArray servers)
        {
            if (servers.Count > 16 || servers.Any(s => !IPAddress.TryParse(s?.ToString(), out _))) throw new FormatException("Invalid WireGuard DNS address.");
            if (servers.Count == 0) o.Remove("dns"); else if (shadowrocket && o["remote-dns-resolve"] is null) o["remote-dns-resolve"] = true;
        }
        if (o["reserved"] is JsonValue reserved)
        {
            var text = reserved.ToString().Trim();
            if (text.Length == 0) o.Remove("reserved");
            else
            {
                byte[] values;
                try { values = text.Contains(',') ? text.Split(',').Select(x => byte.Parse(x.Trim())).ToArray() : Convert.FromBase64String(text); }
                catch (Exception e) when (e is FormatException or OverflowException) { throw new FormatException("WireGuard reserved must contain three bytes."); }
                o["reserved"] = new JsonArray(values.Select(x => (JsonNode?)JsonValue.Create((int)x)).ToArray());
            }
        }
        if (o["reserved"] is JsonArray bytes && (bytes.Count != 3 || bytes.Any(x => !byte.TryParse(x?.ToString(), out _)))) throw new FormatException("WireGuard reserved must contain three bytes.");
        return o;
    }
    private static bool Flag(string value) => value.ToLowerInvariant() switch { "1" or "true" => true, "0" or "false" => false, _ => throw new FormatException("Invalid WireGuard switch.") };
    public static ProxyNode Link(string input, Guid? subscription)
    {
        var uri = new Uri(input); if (uri.UserInfo.Length > 0) throw new FormatException("Invalid WireGuard endpoint.");
        var o = new JsonObject { ["type"] = "WireGuard", ["host"] = uri.Host.Trim('[', ']'), ["port"] = uri.Port,
            ["name"] = Uri.UnescapeDataString(uri.Fragment.TrimStart('#')) };
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "publicKey", "privateKey", "presharedKey", "preSharedKey", "ip", "ipv6", "mtu", "dns", "udp", "flag", "keepalive", "reserved", "remarks", "name", "remote-dns-resolve" };
        foreach (var item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2); var key = Uri.UnescapeDataString(pair[0]);
            if (!allowed.Contains(key)) throw new FormatException("Unsupported WireGuard link option.");
            var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "";
            // URI query '+' is part of a Base64 key, not form-encoded whitespace.
            var canonical = allowed.First(x => x.Equals(key, StringComparison.OrdinalIgnoreCase));
            o[canonical] = value;
        }
        return NodeImport.FromOptions(o, subscription);
    }
}
