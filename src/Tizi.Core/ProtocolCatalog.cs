using System.Text.Json.Nodes;

namespace Tizi.Core;

public sealed record ProtocolEntry(string Name, string? CoreType, bool Tls = false)
{
    public override string ToString() => Name;
}
public static class ProtocolCatalog
{
    public static readonly ProtocolEntry[] Supported =
    [
        new("Shadowsocks", "ss"), new("ShadowsocksR", "ssr"), new("VMess", "vmess"), new("VLESS", "vless"), new("Relay (GOST)", "gost-relay"),
        new("SOCKS5", "socks5"), new("SOCKS5 over TLS", "socks5", true), new("HTTP", "http"), new("HTTPS", "http", true), new("Trojan", "trojan", true),
        new("Hysteria", "hysteria", true), new("Hysteria2", "hysteria2", true), new("AnyTLS", "anytls", true), new("Sudoku", "sudoku"), new("TUIC", "tuic", true),
        new("SSH", "ssh"), new("WireGuard", "wireguard"), new("TrustTunnel", "trusttunnel", true), new("MASQUE", "masque", true), new("Snell", "snell"), new("Mieru", "mieru"),
    ];
    public static readonly string[] Pending = ["HTTP2 / HTTP3 (NaiveProxy)", "Juicity", "OpenConnect", "Brook", "Lua"];
    public static JsonObject Parameters(ProtocolEntry protocol) => protocol.CoreType switch
    {
        "ss" => new() { ["cipher"] = "aes-128-gcm", ["udp"] = true },
        "ssr" => new() { ["cipher"] = "aes-128-cfb", ["protocol"] = "origin", ["obfs"] = "plain" },
        "vmess" => new() { ["uuid"] = "", ["alterId"] = 0, ["cipher"] = "auto", ["tls"] = true },
        "vless" => new() { ["uuid"] = "", ["tls"] = true, ["servername"] = "" },
        "snell" => new() { ["psk"] = "", ["version"] = 3 },
        "wireguard" => new() { ["private-key"] = "", ["public-key"] = "", ["ip"] = "", ["udp"] = true },
        "mieru" => new() { ["transport"] = "TCP", ["multiplexing"] = "MULTIPLEXING_LOW" },
        "sudoku" => new() { ["key"] = "", ["aead-method"] = "chacha20-poly1305" },
        "masque" => new() { ["private-key"] = "", ["public-key"] = "", ["ip"] = "", ["udp"] = true },
        "ssh" => new() { ["host-key"] = new JsonArray() },
        "hysteria" => new() { ["auth-str"] = "", ["up"] = "10", ["down"] = "50" },
        "tuic" => new() { ["uuid"] = "", ["congestion-controller"] = "bbr" },
        _ => new(),
    };
}
