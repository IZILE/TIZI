using System.Text;
using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public class ImportTests
{
    private const string Ss = "ss://YWVzLTEyOC1nY206c2VjcmV0@host.example:8388#My%20node";
    [Fact] public void Sip002AndBase64ListsPreserveCredentials()
    {
        var node = Assert.Single(NodeImport.Parse(Ss)); Assert.Equal("My node", node.Name); Assert.Equal("secret", node.Options["password"]!.ToString()); Assert.Equal(8388, node.Port);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(Ss + "\n" + Ss)); Assert.Equal(2, NodeImport.Parse(encoded).Count);
    }
    [Fact] public void LegacyShadowsocksAndIpv6()
    {
        var body = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-256-gcm:pw@[2001:db8::1]:443"));
        var node = Assert.Single(NodeImport.Parse("ss://" + body + "#legacy")); Assert.Equal("2001:db8::1", node.Server); Assert.Equal("pw", node.Options["password"]!.ToString());
    }
    [Fact] public void ShadowsocksRAndObfsPluginFields()
    {
        var ssr = "example.com:443:auth_aes128_sha1:aes-128-cfb:tls1.2_ticket_auth:cGFzcw/?remarks=TXkgU1NS";
        var node = Assert.Single(NodeImport.Parse("ssr://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(ssr)))); Assert.Equal("My SSR", node.Name); Assert.Equal("pass", node.Options["password"]!.ToString());
        var obfs = Assert.Single(NodeImport.Parse(Ss + "-obfs"));
        var plugin = Assert.Single(NodeImport.Parse("ss://YWVzLTEyOC1nY206c2VjcmV0@host.example:8388?plugin=obfs-local%3Bobfs%3Dhttp%3Bobfs-host%3Dcdn.example#obfs"));
        Assert.Equal("http", plugin.Options["plugin-opts"]!["mode"]!.ToString()); Assert.Equal("cdn.example", plugin.Options["plugin-opts"]!["host"]!.ToString());
    }
    [Theory]
    [InlineData("trojan://pass@example.com:443?sni=cert.example#test", "trojan")]
    [InlineData("hy2://pass@example.com:443?obfs=salamander&obfs-password=secret#test", "hysteria2")]
    [InlineData("tuic://29c295b8-5724-4c41-8cf1-ceae43986a58:pass@example.com:443#test", "tuic")]
    [InlineData("socks5://name:password@127.0.0.1:1080#test", "socks5")]
    [InlineData("https://name:password@example.com:443#test", "http")]
    public void CommonLinks(string uri, string type) { Assert.Equal(type, Assert.Single(NodeImport.Parse(uri)).Type); }
    [Theory]
    [InlineData("trojan://synthetic@example.com:0#invalid")]
    [InlineData("hy2://synthetic@example.com:0#invalid")]
    [InlineData("https://synthetic@example.com:0#invalid")]
    public void ExplicitZeroPortIsRejected(string link) => Assert.Throws<FormatException>(() => NodeImport.Parse(Ss + "\n" + link));
    [Fact] public void OmittedDefaultPortStillImports() => Assert.Equal(443, Assert.Single(NodeImport.Parse("trojan://synthetic@example.com#default")).Port);
    [Fact] public void VlessRealityFieldsAndWsPath()
    {
        var node = Assert.Single(NodeImport.Parse("vless://29c295b8-5724-4c41-8cf1-ceae43986a58@example.com:443?security=reality&pbk=key&sid=ab&fp=chrome&type=ws&path=%2Fproxy&host=cdn.example#reality"));
        Assert.True(node.Options["tls"]!.GetValue<bool>()); Assert.Equal("key", node.Options["reality-opts"]!["public-key"]!.ToString()); Assert.Equal("/proxy", node.Options["ws-opts"]!["path"]!.ToString());
    }
    [Fact] public void VmessJsonBase64()
    {
        var text = "{\"ps\":\"vmess\",\"add\":\"example.com\",\"port\":\"443\",\"id\":\"29c295b8-5724-4c41-8cf1-ceae43986a58\",\"aid\":\"0\",\"net\":\"ws\",\"tls\":\"tls\",\"path\":\"/v\"}";
        var node = Assert.Single(NodeImport.Parse("vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)))); Assert.Equal("vmess", node.Type); Assert.Equal(443, node.Port); Assert.Equal(0, node.Options["alterId"]!.GetValue<int>());
    }
    [Fact] public void YamlImportOnlyCopiesNodesAndPreservesScalarTypes()
    {
        var nodes = NodeImport.Parse("mixed-port: 9999\nexternal-controller: 0.0.0.0:9090\nsecret: remote-secret\nproxies:\n  - name: sample\n    type: ss\n    server: example.com\n    port: 443\n    cipher: aes-128-gcm\n    password: 'true'\n    udp: true\n");
        var node = Assert.Single(nodes); Assert.True(node.Options["udp"]!.GetValue<bool>()); Assert.Equal("true", node.Options["password"]!.GetValue<string>()); Assert.False(node.Options.ContainsKey("external-controller"));
    }
    [Theory]
    [InlineData("file:///C:/secret")]
    [InlineData("ss://bad")]
    [InlineData("vless://id@example.com:0")]
    [InlineData("{\"type\":\"ss\",\"server\":\"host\",\"port\":443,\"private-key-path\":\"C:/secret\"}")]
    [InlineData("proxies:\n - &loop {name: a, type: ss, server: a, port: 443, password: *loop}")]
    public void InvalidOrUnsafeImportsAreRejected(string input) { Assert.ThrowsAny<Exception>(() => NodeImport.Parse(input)); }
    [Fact] public void ConfigKeepsSecretsQuotedAndLocalOnly()
    {
        var node = Assert.Single(NodeImport.Parse(Ss)); var state = new AppState { Nodes = [node], SelectedNode = node.Id, Capture = CaptureMode.LocalOnly };
        var session = EngineSession.Create(state, 19092); var json = JsonNode.Parse(session.Config)!;
        Assert.Equal("127.0.0.1:19092", json["external-controller"]!.ToString()); Assert.False(json["allow-lan"]!.GetValue<bool>()); Assert.False(json["tun"]!["enable"]!.GetValue<bool>()); Assert.Equal(64, session.Secret.Length); Assert.DoesNotContain("remote-secret", session.Config);
        Assert.Equal(node.CoreName, json["proxies"]![0]!["name"]!.ToString()); Assert.Contains("MATCH,TIZI", session.Config);
    }
    [Theory] [InlineData("DOMAIN-SUFFIX", "example.com", "TIZI")] [InlineData("IP-CIDR", "10.0.0.0/8", "DIRECT")] [InlineData("DST-PORT", "443", "REJECT")]
    public void ValidRules(string type, string value, string action) => ModelValidation.Rule(new RoutingRule(type, value, action));
    [Theory] [InlineData("DOMAIN", "bad,REJECT", "TIZI")] [InlineData("IP-CIDR", "1.2.3.4/128", "DIRECT")] [InlineData("DST-PORT", "65536", "REJECT")] [InlineData("UNKNOWN", "example.com", "TIZI")]
    public void InvalidRules(string type, string value, string action) => Assert.Throws<FormatException>(() => ModelValidation.Rule(new RoutingRule(type, value, action)));
    [Fact] public void CatalogDeduplicatesAndPreservesKnownIdentity()
    {
        var prior = new ProviderSite { Name = "Original", Url = "https://example.com/" };
        var list = SourceUpdates.ParseCatalog("{\"version\":1,\"providers\":[{\"name\":\"Updated\",\"url\":\"https://example.com\"},{\"name\":\"Duplicate\",\"url\":\"https://example.com/\"}]}", "https://catalog.example/list.json", [prior]);
        Assert.Equal(prior.Id, Assert.Single(list).Id); Assert.Equal("Updated", list[0].Name);
    }
    [Theory]
    [InlineData("{\"version\":2,\"providers\":[]}")]
    [InlineData("{\"version\":1,\"providers\":[{\"name\":\"local\",\"url\":\"https://127.0.0.1/\"}]}")]
    [InlineData("{\"version\":1,\"providers\":[{\"name\":\"file\",\"url\":\"file:///secret\"}]}")]
    public void InvalidCatalog(string json) => Assert.ThrowsAny<Exception>(() => SourceUpdates.ParseCatalog(json, "https://catalog.example", []));
}
