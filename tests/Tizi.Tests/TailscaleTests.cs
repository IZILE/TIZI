using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class TailscaleTests
{
    private static TailscaleSettings Phone => new() { Enabled = true, AuthKey = "fixture-auth-never-valid-A", ExitNode = "100.100.101.102" };
    private static ProxyNode Node(string name) => NodeImport.FromOptions(new JsonObject { ["name"] = name, ["type"] = "http", ["server"] = "127.0.0.1", ["port"] = 18080 });
    [Fact]
    public void TransitHasOnlyThePhoneAsFinalProxyAndCannotSelectDirect()
    {
        var a = Node("A"); var b = Node("B");
        var state = new AppState { Nodes = [a, b], SelectedNode = b.Id, Tailscale = Phone };
        var config = JsonNode.Parse(EngineSession.Create(state, 18081, 18082).Config)!;
        var groups = config["proxy-groups"]!.AsArray();
        Assert.Equal(new[] { TailscaleSettings.ProxyName }, groups[0]!["proxies"]!.AsArray().Select(n => n!.ToString()));
        Assert.Equal(new[] { b.CoreName, a.CoreName }, groups[1]!["proxies"]!.AsArray().Select(n => n!.ToString()));
        var phone = config["proxies"]!.AsArray().Last()!;
        Assert.Equal(TailscaleSettings.TransitGroup, phone["dialer-proxy"]!.ToString());
        Assert.Equal(Phone.ExitNode, phone["exit-node"]!.ToString());
        Assert.Equal("tailscale", phone["state-dir"]!.ToString());
        Assert.True(config["dns"]!["respect-rules"]!.GetValue<bool>());
        var inspector = Assert.Single(config["listeners"]!.AsArray())!;
        Assert.Equal("127.0.0.1", inspector["listen"]!.ToString());
        Assert.Equal(TailscaleSettings.ProxyName, inspector["proxy"]!.ToString());
        var process = new ProcessStartInfo(); process.Environment["HTTPS_PROXY"] = "http://127.0.0.1:9"; Phone.ConfigureProcess(process);
        Assert.Equal("true", process.Environment["TS_DEBUG_ALWAYS_USE_DERP"]);
        Assert.False(process.Environment.ContainsKey("HTTPS_PROXY"));
    }
    [Fact]
    public void DirectTailscaleDoesNotNeedAnOrdinaryProxyNode()
    {
        var state = new AppState { Tailscale = Phone with { ThroughProxy = false } };
        var config = JsonNode.Parse(EngineSession.Create(state, 18081).Config)!;
        Assert.Single(config["proxies"]!.AsArray()); Assert.Single(config["proxy-groups"]!.AsArray());
        Assert.Empty(config["proxies"]![0]!["dialer-proxy"]!.ToString());
        Assert.False(TailscaleSettings.NeedsProxyNode(state));
        Assert.Throws<FormatException>(() => EngineSession.Create(state with { Tailscale = Phone }, 18081));
    }
    [Fact]
    public void DisabledFeatureKeepsTheOriginalRouteAndAcceptsIncompleteDraftFields()
    {
        var node = Node("A"); var state = new AppState { Nodes = [node] };
        var config = JsonNode.Parse(EngineSession.Create(state, 18081).Config)!;
        Assert.Single(config["proxies"]!.AsArray());
        Assert.Equal(new[] { node.CoreName, "DIRECT" }, config["proxy-groups"]![0]!["proxies"]!.AsArray().Select(n => n!.ToString()));
        TailscaleSettings.Validate(new() { AuthKey = "unfinished", ExitNode = "unfinished" });
        Assert.False(JsonSerializer.Deserialize<AppState>("{}")!.Tailscale.Enabled);
        Assert.False(NodeQualityProbe.Configuration(state with { Tailscale = Phone }, node, 18081).Tailscale.Enabled);
    }
    [Theory]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("fd7a:115c:a1e0::1")]
    public void AcceptsTailnetExitAddresses(string ip) => TailscaleSettings.Validate(Phone with { ExitNode = ip });
    [Theory]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("example.com")]
    [InlineData("100.100.101.102:443")]
    [InlineData("auto:any")]
    [InlineData("::ffff:100.64.0.1")]
    public void RefusesNonTailnetOrAmbiguousExitAddresses(string ip) => Assert.Throws<FormatException>(() => TailscaleSettings.Validate(Phone with { ExitNode = ip }));
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://example.com/?token=key")]
    [InlineData("https://example.com/#fragment")]
    public void RefusesMalformedControlServerUrls(string url) => Assert.Throws<FormatException>(() => TailscaleSettings.Validate(Phone with { ControlUrl = url }));
    [Fact]
    public void RestoresEncryptedIdentityAndSeparatesAuthenticationKeys()
    {
        var root = Path.Combine(Path.GetTempPath(), "tizi-tailscale-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vault = new Vault(Path.Combine(root, "vault")); var runtime = Path.Combine(root, "runtime");
            Directory.CreateDirectory(Path.Combine(runtime, "tailscale"));
            const string state = "synthetic private device identity";
            File.WriteAllText(Path.Combine(runtime, "tailscale", "tailscaled.state"), state);
            TailscaleIdentityStore.Save(vault, Phone, runtime);
            var stored = Assert.Single(Directory.GetFiles(vault.DirectoryPath));
            Assert.DoesNotContain(state, Encoding.UTF8.GetString(File.ReadAllBytes(stored)));
            var restored = Path.Combine(root, "restored"); TailscaleIdentityStore.Restore(vault, Phone, restored);
            Assert.Equal(state, File.ReadAllText(Path.Combine(restored, "tailscale", "tailscaled.state")));
            var other = Path.Combine(root, "other");
            TailscaleIdentityStore.Restore(vault, Phone with { AuthKey = "fixture-auth-never-valid-B" }, other);
            Assert.False(Directory.Exists(other));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
