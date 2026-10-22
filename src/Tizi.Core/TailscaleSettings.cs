using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Tizi.Core;

public sealed record TailscaleSettings
{
    public bool Enabled { get; init; }
    public bool ThroughProxy { get; init; } = true;
    public string Hostname { get; init; } = "tizi-windows";
    public string AuthKey { get; init; } = "";
    public string ControlUrl { get; init; } = "";
    public string ExitNode { get; init; } = "";
    public bool AcceptRoutes { get; init; }
    public bool AllowLanAccess { get; init; }
    public const string ProxyName = "TIZI-TAILSCALE";
    public const string TransitGroup = "TIZI-TRANSIT";
    public static bool NeedsProxyNode(AppState state) => state.Mode != RouteMode.Direct && (!state.Tailscale.Enabled || state.Tailscale.ThroughProxy);

    public static void Validate(TailscaleSettings settings)
    {
        if (!settings.Enabled) return;
        if (settings.AuthKey.Length is < 16 or > 4096 || settings.AuthKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new FormatException("Enter a Tailscale authentication key.");
        if (settings.Hostname.Length is < 1 or > 63 || settings.Hostname[0] == '-' || settings.Hostname[^1] == '-' || settings.Hostname.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new FormatException("Use letters, numbers and hyphens for the Tailscale device name.");
        if (!IPAddress.TryParse(settings.ExitNode, out var exit) || !IsTailnetAddress(exit))
            throw new FormatException("Enter the iPhone's Tailscale IP address (100.x.x.x or fd7a:115c:a1e0::/48).");
        if (settings.ControlUrl.Length > 512 || settings.ControlUrl.Any(char.IsWhiteSpace) ||
            (settings.ControlUrl.Length > 0 && (!Uri.TryCreate(settings.ControlUrl, UriKind.Absolute, out var control) || control.Scheme != "https" || control.UserInfo.Length > 0 || control.Fragment.Length > 0 || control.Query.Length > 0)))
            throw new FormatException("Enter an HTTPS Tailscale control server URL without login, query or fragment.");
    }
    private static bool IsTailnetAddress(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return ip.AddressFamily == AddressFamily.InterNetwork ? bytes[0] == 100 && bytes[1] is >= 64 and <= 127
            : ip.AddressFamily == AddressFamily.InterNetworkV6 && bytes.AsSpan(0, 6).SequenceEqual(new byte[] { 0xfd, 0x7a, 0x11, 0x5c, 0xa1, 0xe0 });
    }
    internal JsonObject ToProxy(bool udp) => new()
    {
        ["name"] = ProxyName, ["type"] = "tailscale", ["hostname"] = Hostname,
        ["auth-key"] = AuthKey, ["control-url"] = ControlUrl, ["state-dir"] = "tailscale",
        ["ephemeral"] = false, ["udp"] = udp, ["accept-routes"] = AcceptRoutes,
        ["exit-node"] = ExitNode, ["exit-node-allow-lan-access"] = AllowLanAccess,
        ["dialer-proxy"] = ThroughProxy ? TransitGroup : ""
    };
    public void ConfigureProcess(ProcessStartInfo process)
    {
        // Only the child engine receives this setting. DERP carries the encrypted
        // peer traffic through the selected proxy, rather than permitting a P2P path.
        process.Environment["TS_DEBUG_ALWAYS_USE_DERP"] = Enabled && ThroughProxy ? "true" : "false";
        if (Enabled)
            foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy" })
                process.Environment.Remove(name);
    }
}

/// <summary>The engine uses a private temporary state file; only its DPAPI-encrypted identity survives disconnect.</summary>
public static class TailscaleIdentityStore
{
    private const int MaximumStateBytes = 4 * 1024 * 1024;
    private static string IdentityFile(TailscaleSettings settings)
    {
        var server = settings.ControlUrl.Length == 0 ? "https://controlplane.tailscale.com" : settings.ControlUrl.TrimEnd('/');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server + "\0" + settings.AuthKey)));
        return "tailscale-identity-" + hash + ".dat";
    }
    private static string StatePath(string runtime) => Path.Combine(runtime, "tailscale", "tailscaled.state");
    public static void Restore(Vault vault, TailscaleSettings settings, string runtime)
    {
        if (!settings.Enabled || vault.Read<byte[]>(IdentityFile(settings)) is not { } state) return;
        if (state.Length > MaximumStateBytes) throw new InvalidDataException("Saved Tailscale identity is too large.");
        var path = StatePath(runtime); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, state);
        CryptographicOperations.ZeroMemory(state);
    }
    public static void Save(Vault vault, TailscaleSettings settings, string runtime)
    {
        var path = StatePath(runtime);
        if (!settings.Enabled || !File.Exists(path)) return;
        if (new FileInfo(path).Length > MaximumStateBytes) throw new InvalidDataException("Saved Tailscale identity is too large.");
        var state = File.ReadAllBytes(path);
        try { vault.Write(IdentityFile(settings), state); }
        finally { CryptographicOperations.ZeroMemory(state); }
    }
}
