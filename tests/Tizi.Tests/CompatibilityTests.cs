using System.Text;
using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public class CompatibilityTests
{
    private static readonly string Private = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray());
    private static readonly string Public = Convert.ToBase64String(Enumerable.Repeat((byte)43, 32).ToArray());
    private static readonly string Shared = Convert.ToBase64String(Enumerable.Repeat((byte)44, 32).ToArray());
    private static string WgLink => $"wg://192.0.2.1:51820?privateKey={Uri.EscapeDataString(Private)}&publicKey={Uri.EscapeDataString(Public)}&presharedKey={Uri.EscapeDataString(Shared)}&ip=10.0.0.2&mtu=1419&dns=10.0.0.1,10.0.0.3&udp=1&flag=GB";
    [Fact] public void WireGuardShadowrocketUrlJsonAndQrAgree()
    {
        var json = new JsonObject { ["type"] = "WireGuard", ["host"] = "192.0.2.1", ["port"] = "51820", ["title"] = "", ["ip"] = "10.0.0.2", ["mtu"] = "1419", ["dns"] = "10.0.0.1,10.0.0.3", ["udp"] = 1, ["privateKey"] = Private, ["publicKey"] = Public, ["password"] = Shared, ["flag"] = "GB", ["reserved"] = "", ["keepalive"] = "", ["uuid"] = "metadata-id", ["obfs"] = "none" };
        var one = ImportDocument.Parse(WgLink).Nodes.Single(); var two = ImportDocument.Parse(json.ToJsonString()).Nodes.Single();
        Assert.True(JsonNode.DeepEquals(one.Options, two.Options)); Assert.Equal("GB", one.CountryCode); Assert.Equal("GB", two.CountryCode);
        Assert.IsType<JsonArray>(one.Options["dns"]); Assert.True(one.Options["remote-dns-resolve"]!.GetValue<bool>()); Assert.False(one.Options.ContainsKey("uuid"));
        var qr = QrImport.Decode(NodeExport.QrPng(WgLink)); Assert.Equal(WgLink, Assert.Single(qr));
        Assert.True(JsonNode.DeepEquals(one.Options, ImportDocument.Parse(qr[0]).Nodes[0].Options));
    }
    [Fact] public void WireGuardIpv6ReservedKeepaliveAndLiteralPlusKeys()
    {
        var key = Convert.ToBase64String(Enumerable.Repeat((byte)255, 32).ToArray());
        var node = NodeImport.Parse($"wg://[2001:db8::1]:51820?privateKey={key}&publicKey={Public}&ip=10.0.0.2,fd00::2/128&reserved=1,2,255&keepalive=25&udp=0").Single();
        Assert.Equal(key, node.Options["private-key"]!.ToString()); Assert.Equal("fd00::2/128", node.Options["ipv6"]!.ToString()); Assert.Equal(25, node.Options["persistent-keepalive"]!.GetValue<int>()); Assert.False(node.Options["udp"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(node.Options, NodeImport.Parse(NodeExport.ShareLink(node)).Single().Options));
    }
    [Theory] [InlineData("privateKey=bad&publicKey=bad&ip=10.0.0.2")] [InlineData("privateKey=x")] [InlineData("unknown=x")]
    public void InvalidWireGuardFailsWithoutEchoingKey(string query)
    { var exception = Assert.Throws<FormatException>(() => NodeImport.Parse("wg://192.0.2.1:51820?" + query)); Assert.DoesNotContain("privateKey=", exception.Message); }
    [Fact] public void ConfigAndSubscriptionExportsRoundtripAndExcludeApplicationSecrets()
    {
        var nodes = NodeImport.Parse(WgLink); var rules = new[] { new RoutingRule("DOMAIN-SUFFIX", "example.com", "DIRECT") };
        foreach (var content in new[] { NodeExport.Json(nodes, rules), NodeExport.Yaml(nodes, rules) })
        {
            var restored = ImportDocument.Parse(content); Assert.True(JsonNode.DeepEquals(nodes[0].Options, restored.Nodes[0].Options)); Assert.Equal(rules, restored.Rules); Assert.Empty(restored.Warnings); Assert.Equal("GB", restored.Nodes[0].CountryCode);
            Assert.DoesNotContain("Discovery", content); Assert.DoesNotContain("ApiKey", content); Assert.DoesNotContain("external-controller", content);
        }
        var subs = new[] { new Subscription { Name = "Subscription", Url = "https://example.com/sub?token=example" } };
        var imported = ImportDocument.Parse(NodeExport.Subscriptions(subs)); Assert.Equal(subs[0].Url, imported.Subscriptions[0].Url); Assert.Empty(imported.Nodes);
    }
    [Theory]
    [InlineData("ss://YWVzLTEyOC1nY206c2VjcmV0@host.example:8388#test")]
    [InlineData("trojan://pass@example.com:443?sni=cert.example#test")]
    [InlineData("hy2://pass@example.com:443?obfs=salamander&obfs-password=secret#test")]
    [InlineData("tuic://29c295b8-5724-4c41-8cf1-ceae43986a58:pass@example.com:443#test")]
    [InlineData("socks5://user%3Aname:pa%3Ass%2520@host.example:1080#test")]
    [InlineData("https://user:pass@example.com:443?sni=cert.example#test")]
    [InlineData("vless://29c295b8-5724-4c41-8cf1-ceae43986a58@example.com:443?security=reality&pbk=key&sid=ab&fp=chrome&type=ws&path=%2Fproxy&host=cdn.example#test")]
    [InlineData("anytls://pass@example.com:443?sni=cert.example#test")]
    [InlineData("hysteria://example.com:443?auth=pass&upmbps=10&downmbps=50#test")]
    [InlineData("socks5://user:pass@example.com:443?security=tls&sni=cert.example#test")]
    public void CommonShareLinksRoundtrip(string link)
    { var original = NodeImport.Parse(link).Single(); var restored = NodeImport.Parse(NodeExport.ShareLink(original)).Single(); Assert.Equal(original.Name, restored.Name); Assert.True(JsonNode.DeepEquals(original.Options, restored.Options)); }
    [Fact] public void LiteralPercentAndColonInSsPasswordStayUnchanged()
    {
        var password = "complex%2F:字+"; var body = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-128-gcm:" + password));
        var node = NodeImport.Parse("ss://" + body + "@example.com:443#test").Single(); Assert.Equal(password, node.Options["password"]!.ToString()); Assert.Equal(password, NodeImport.Parse(NodeExport.ShareLink(node)).Single().Options["password"]!.ToString());
    }
    [Fact] public void ShareLinkRejectsAdvancedParametersInsteadOfLosingThem()
    { var node = NodeImport.Parse("trojan://pass@example.com:443#test").Single(); node.Options["udp-over-tcp"] = true; Assert.Throws<FormatException>(() => NodeExport.ShareLink(node)); Assert.Contains("udp-over-tcp", NodeExport.Json([node], [])); }
    [Fact] public void ShadowrocketVmessUsesPasswordForUuidAndTrojanKeepsSni()
    {
        var vmess = NodeImport.FromOptions(new JsonObject { ["type"] = "Vmess", ["host"] = "example.com", ["port"] = "443", ["uuid"] = "metadata", ["password"] = "protocol-uuid", ["sni"] = "cert.example", ["tls"] = "1" }); Assert.Equal("protocol-uuid", vmess.Options["uuid"]!.ToString()); Assert.Equal("cert.example", vmess.Options["servername"]!.ToString());
        var trojan = NodeImport.FromOptions(new JsonObject { ["type"] = "Trojan", ["host"] = "example.com", ["port"] = "443", ["password"] = "pass", ["sni"] = "cert.example" }); Assert.Equal("cert.example", trojan.Options["sni"]!.ToString());
    }
    [Fact] public void ShadowrocketUrlsUnwrapWithoutExecutingCommands()
    {
        Assert.Equal("https://example.com/sub?token=abc", ImportDocument.RemoteUrl("shadowrocket://add/" + Uri.EscapeDataString("https://example.com/sub?token=abc")));
        Assert.Equal("wireguard", ImportDocument.Parse("shadowrocket://add/" + Uri.EscapeDataString(WgLink)).Nodes.Single().Type);
        Assert.Throws<FormatException>(() => ImportDocument.RemoteUrl("shadowrocket://connect"));
    }
    [Fact] public void ConfRulesAndSkippedSectionsAreExplicit()
    {
        var doc = ImportDocument.Parse("[Proxy]\nSample = ss, example.com, 443, encrypt-method=aes-128-gcm, password=pass\n[Rule]\nDOMAIN-SUFFIX,example.org,DIRECT\n[MITM]\nhostname=example.org\n");
        Assert.Single(doc.Nodes); Assert.Single(doc.Rules); Assert.Contains(doc.Warnings, s => s.Contains("mitm"));
    }
    [Fact] public void RegionMatchesMetadataAndNamesWithoutUsingServerAddress()
    { Assert.Equal("GB", NodeRegion.Match("a", "GB")); Assert.Equal("JP", NodeRegion.Match("Tokyo 01")); Assert.Equal("SG", NodeRegion.Match("新加坡 02")); Assert.Equal("AU", NodeRegion.Match("Australia")); Assert.Null(NodeRegion.Match("Unknown node 01")); }
    [Fact] public void UsageDistinguishesMissingUnlimitedAndExpired()
    {
        var usage = SubscriptionMetadata.Parse("upload=10; download=20; total=100; expire=1700000000")!; Assert.Equal(30, usage.Used); Assert.Equal(70, usage.Remaining); Assert.True(usage.IsExpired(DateTimeOffset.Now));
        Assert.True(SubscriptionMetadata.Parse("expire=0")!.NoExpiry); Assert.Null(SubscriptionMetadata.Parse("upload=bad; total=-1")?.Used);
    }
    [Fact] public void DiscoveryRequiresActualSearchSourcesAndDropsUnbackedPrices()
    {
        var provider = new JsonObject { ["name"] = "Example", ["url"] = "https://example.com/", ["description"] = "Example only", ["regions"] = new JsonArray(), ["securityNotes"] = "Unverified", ["ipQualityNotes"] = "Unverified", ["sources"] = new JsonArray("https://example.com/prices"), ["plans"] = new JsonArray(new JsonObject { ["name"] = "Plan", ["price"] = 10, ["currency"] = "CNY", ["period"] = "month", ["trafficGb"] = 100, ["conditions"] = "", ["source"] = "https://example.com/unvisited" }) };
        var text = new JsonObject { ["providers"] = new JsonArray(provider) }.ToJsonString();
        provider["serviceKind"] = "personal_subscription"; provider["protocols"] = new JsonArray("Clash"); provider["subscriptionSource"] = "https://example.com/prices";
        text = new JsonObject { ["providers"] = new JsonArray(provider.DeepClone()) }.ToJsonString();
        var root = new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(new JsonObject { ["type"] = "message", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }) }) };
        Assert.Throws<FormatException>(() => ProviderDiscovery.ParseResponse(root.ToJsonString()));
        ((JsonArray)root["output"]!).Insert(0, new JsonObject { ["type"] = "web_search_call", ["status"] = "completed", ["action"] = new JsonObject { ["sources"] = new JsonArray(new JsonObject { ["url"] = "https://example.com/prices" }) } });
        Assert.Empty(ProviderDiscovery.ParseResponse(root.ToJsonString()).Single().Profile!.Plans);
        var request = ProviderDiscovery.Request(new DiscoverySettings { ApiKey = "never-serialize-this" }, "test"); Assert.DoesNotContain("never-serialize-this", request.ToJsonString()); Assert.Equal("required", request["tool_choice"]!.ToString());
    }
    [Fact] public void UnitPricesAreNeverPresentedAsMonthlyPlanCost()
    {
        var plan = new ProviderPlan("Unit price", 4m, "USD", "month", null, "per GB", "https://example.com/prices") { PriceUnit = "gb" };
        Assert.Null(ProviderDiscovery.MonthlyEquivalent(plan));
        Assert.Equal(10m, ProviderDiscovery.MonthlyEquivalent(plan with { Price = 120m, Period = "year", PriceUnit = "period" }));
        Assert.Null(ProviderDiscovery.MonthlyEquivalent(plan with { PriceUnit = "unknown" }));
    }
}
