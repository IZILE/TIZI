using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public class RevisionTests
{
    [Theory]
    [InlineData("clash://install-config?url=")]
    [InlineData("clash-verge://install-config?url=")]
    [InlineData("tizi://import?url=")]
    [InlineData("shadowrocket://add/")]
    public void ImportCommandsPreserveTheWholeEncodedFeedAddress(string prefix)
    {
        const string feed = "https://feed.example/link/test?clash=3&extend=1";
        Assert.Equal(feed, ImportDocument.RemoteUrl(prefix + Uri.EscapeDataString(feed)));
    }
    [Theory]
    [InlineData("clash://install-config?url=file%3A%2F%2F%2FC%3A%2Ftest")]
    [InlineData("clash://install-config?url=https%3A%2F%2Ffeed.example%2F&url=https%3A%2F%2Fother.example%2F")]
    [InlineData("clash://run?url=https%3A%2F%2Ffeed.example%2F")]
    public void NonImportCommandsAndAmbiguousAddressesCannotBecomeSubscriptions(string command)
    { Assert.Throws<FormatException>(() => ImportDocument.RemoteUrl(command) ?? throw new FormatException()); }

    [Fact] public async Task FullClashFeedKeepsNodesAndConvertibleRulesWithoutApplyingItsNetworkingSettings()
    {
        var node = NodeImport.Parse("ss://YWVzLTEyOC1nY206dGVzdA==@node.example:443#Japan").Single();
        var config = JsonNode.Parse(NodeExport.Json([node], []))!;
        config["rules"] = new JsonArray("DOMAIN,example.test,Proxy", "GEOIP,CN,DIRECT", "MATCH,Proxy");
        config["proxy-groups"] = new JsonArray(new JsonObject { ["name"] = "Proxy", ["type"] = "select", ["proxies"] = new JsonArray("Japan") });
        config["mixed-port"] = 9999; config["external-controller"] = "0.0.0.0:9090"; config["dns"] = new JsonObject { ["enable"] = true };
        var sub = new Subscription { Name = "Test", Url = "https://feed.example/sub" };
        using var http = new HttpClient(new Handler(config.ToJsonString()));
        var result = await new SourceUpdates(http).RefreshSubscriptionAsync(sub, [node with { SubscriptionId = sub.Id }]);
        Assert.Equal(node.Id, Assert.Single(result.Nodes).Id);
        Assert.True(JsonNode.DeepEquals(node.Options, result.Nodes[0].Options));
        Assert.Equal(new[] { "DOMAIN,example.test,TIZI", "GEOIP,CN,DIRECT" }, result.Rules.Select(r => r.ToCore()));
        Assert.Single(result.Warnings);
    }
    [Fact] public async Task APartlyUnsupportedFeedCannotReplaceWorkingNodes()
    {
        using var http = new HttpClient(new Handler("{\"proxies\":[{\"name\":\"Broken\",\"type\":\"unsupported\"}]}"));
        await Assert.ThrowsAsync<FormatException>(() => new SourceUpdates(http).RefreshSubscriptionAsync(new Subscription { Name = "Test", Url = "https://feed.example/sub" }, []));
    }
    [Fact] public void LegacyLatencyPreferencesKeepBulkSettingsAndDefaultToOneSecondCurveCollection()
    {
        var restored = JsonSerializer.Deserialize<LatencySettings>("{\"IntervalSeconds\":15,\"Concurrency\":4,\"TimeoutMs\":3500,\"Method\":\"HTTP\"}")!;
        Assert.Equal(1, restored.CurveIntervalSeconds); Assert.Equal(15, restored.IntervalSeconds);
        Assert.Equal(4, restored.Concurrency); Assert.Equal(3500, restored.TimeoutMs);
    }
    [Fact] public void LowestPriceUsesMonthlyEquivalentAndKeepsCurrenciesAndUnitsComparable()
    {
        ProviderPlan Plan(decimal? price, string period = "month", string currency = "CNY", string unit = "period") => new("Plan", price, currency, period, 100, "", "https://prices.example/") { PriceUnit = unit };
        ProviderSite Site(string name, params ProviderPlan[] plans) => new() { Name = name, Url = "https://" + name + ".example/", Profile = new("", plans.ToList(), [], "", "", [], DateTimeOffset.UtcNow) };
        var year = Site("year", Plan(120, "year"), Plan(20));
        var monthly = Site("monthly", Plan(12)); var usd = Site("usd", Plan(1, currency: "USD"));
        var once = Site("once", Plan(5, "once")); var perGb = Site("pergb", Plan(1, unit: "gb")); var unknown = Site("unknown", Plan(null));
        Assert.Equal(new[] { "year", "monthly", "once", "usd", "pergb", "unknown" }, ProviderDirectory.OrderByLowestPrice([unknown, perGb, usd, once, monthly, year]).Select(s => s.Name));
        Assert.Equal("year", ProviderDirectory.LowestPlan(year)!.Period);
        Assert.Equal("usd", ProviderDirectory.OrderByLowestPrice([year, usd], "USD")[0].Name);
    }
    [Fact] public void SearchRequestsFifteenEvidenceBackedProvidersAndPrefersChineseServices()
    {
        var request = ProviderDiscovery.Request(new(), ProviderSearchModes.BuildQuery(new()));
        Assert.Equal(15, request["text"]!["format"]!["schema"]!["properties"]!["providers"]!["maxItems"]!.GetValue<int>());
        Assert.Contains("中文", ProviderSearchModes.BuildQuery(new()));
    }
    sealed class Handler(string body) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }); }
}
