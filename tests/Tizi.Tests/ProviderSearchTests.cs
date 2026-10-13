using System.Text.Json;
using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public class ProviderSearchTests
{
    [Fact] public void SavedCriteriaReuseExactlyTheSameTemplateWithoutApplicationSecrets()
    {
        var options = new ProviderSearchOptions { ModeId = "budget", MonthlyBudget = 30, Currency = "CNY", Regions = "日本、新加坡", Protocols = ["Trojan", "Clash"], RequireTrial = true };
        var state = new AppState { Discovery = new() { ApiKey = "secret-never-in-query", Search = options } };
        var restored = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(ProviderSearchModes.BuildQuery(options), ProviderSearchModes.BuildQuery(restored.Discovery.Search));
        Assert.Contains("monthlyBudget", ProviderSearchModes.BuildQuery(options)); Assert.DoesNotContain(state.Discovery.ApiKey, ProviderSearchModes.BuildQuery(options));
        Assert.Equal("balanced", JsonSerializer.Deserialize<DiscoverySettings>("{}")!.Search.ModeId);
    }
    [Theory] [InlineData("balanced")] [InlineData("budget")] [InlineData("stability")] [InlineData("ip-quality")] [InlineData("trial")]
    public void PresetsKeepStableCriteriaAndProduceBoundedQueries(string mode)
    {
        var options = new ProviderSearchOptions { ModeId = mode, MonthlyBudget = 25, Regions = new string('日', 160), ExtraRequirements = new string('字', 500), Protocols = ProviderSearchModes.Protocols.ToList() };
        var query = ProviderSearchModes.BuildQuery(options); Assert.InRange(query.Length, 1, 4000);
        Assert.Contains("25", query); Assert.Contains("requireDocumentedTrial", query);
        Assert.NotNull(ProviderDiscovery.Request(new(), query));
    }
    [Fact] public void InvalidCriteriaFailBeforeAnyRequest()
    {
        Assert.Throws<FormatException>(() => ProviderSearchModes.BuildQuery(new() { ModeId = "unknown" }));
        Assert.Throws<FormatException>(() => ProviderSearchModes.BuildQuery(new() { MonthlyBudget = -1 }));
        Assert.Throws<FormatException>(() => ProviderSearchModes.BuildQuery(new() { Protocols = ["injected-protocol"] }));
    }
    private static JsonObject Provider(string url = "https://example.com/") => new()
    {
        ["name"] = "Example", ["url"] = url, ["description"] = "Synthetic service", ["serviceKind"] = "personal_subscription", ["protocols"] = new JsonArray("Clash"),
        ["subscriptionSource"] = url + "guide", ["regions"] = new JsonArray(), ["securityNotes"] = "Unverified", ["ipQualityNotes"] = "Unverified", ["sources"] = new JsonArray(url + "guide"), ["plans"] = new JsonArray()
    };
    private static string Response(JsonObject provider, params string[] sources) => new JsonObject
    {
        ["status"] = "completed", ["output"] = new JsonArray(new JsonObject { ["type"] = "web_search_call", ["status"] = "completed", ["action"] = new JsonObject { ["sources"] = new JsonArray(sources.Select(s => (JsonNode)new JsonObject { ["url"] = s }).ToArray()) } },
            new JsonObject { ["type"] = "message", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = new JsonObject { ["providers"] = new JsonArray(provider) }.ToJsonString() }) })
    }.ToJsonString();
    [Theory]
    [InlineData("gb", "CNY 25 / 200GB", "unknown")]
    [InlineData("gb", "USD 4 per GB", "gb")]
    [InlineData("gb", "", "unknown")]
    [InlineData("period", "CNY 18.8/month; 150GB", "period")]
    [InlineData("period", "USD 4 /GB", "unknown")]
    public void PackageQuotasCannotBePresentedAsPerGigabytePrices(string unit, string quote, string expected)
    {
        var provider = Provider();
        provider["plans"] = new JsonArray(new JsonObject { ["name"] = "Plan", ["price"] = 25, ["currency"] = "CNY", ["period"] = "month", ["priceUnit"] = unit, ["priceQuote"] = quote, ["trafficGb"] = 200, ["conditions"] = "Synthetic fixture", ["source"] = "https://example.com/guide" });
        var plan = Assert.Single(Assert.Single(ProviderDiscovery.ParseResponse(Response(provider, "https://example.com/guide"))).Profile!.Plans);
        Assert.Equal(expected, plan.PriceUnit); Assert.Equal(25, plan.Price);
    }
    [Fact] public void BusinessPoolsAndUnknownProductsAreExcludedDespiteVisitedSources()
    {
        var pool = Provider(); pool["serviceKind"] = "commercial_proxy_pool";
        Assert.Empty(ProviderDiscovery.ParseResponse(Response(pool, "https://example.com/guide")));
        foreach (var host in new[] { "oxylabs.io", "www.brightdata.com" })
        { var url = "https://" + host + "/"; Assert.Empty(ProviderDiscovery.ParseResponse(Response(Provider(url), url + "guide"))); }
    }
    [Fact] public void ActualCompatibilitySourceIsRequiredAlongsideAProtocol()
    {
        Assert.Single(ProviderDiscovery.ParseResponse(Response(Provider(), "https://example.com/guide")));
        var missing = Provider(); missing["subscriptionSource"] = "https://example.com/unvisited";
        Assert.Empty(ProviderDiscovery.ParseResponse(Response(missing, "https://example.com/guide")));
        var noProtocol = Provider(); noProtocol["protocols"] = new JsonArray();
        Assert.Empty(ProviderDiscovery.ParseResponse(Response(noProtocol, "https://example.com/guide")));
    }
    [Fact] public void MultipleDistinctResultsSurviveValidationAndCacheRoundTrip()
    {
        var providers = Enumerable.Range(1, 6).Select(i => Provider($"https://provider{i}.example/")).ToArray();
        var response = JsonNode.Parse(Response(providers[0], providers.Select(p => p["subscriptionSource"]!.ToString()).ToArray()))!;
        response["output"]![1]!["content"]![0]!["text"] = new JsonObject { ["providers"] = new JsonArray(providers.Select(p => (JsonNode)p.DeepClone()).ToArray()) }.ToJsonString();
        var found = ProviderDiscovery.ParseResponse(response.ToJsonString()); Assert.Equal(6, found.Count);
        var state = ProviderDirectory.CacheSearch(new(), found, new(), DateTimeOffset.UtcNow);
        var restored = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(6, restored.Providers.Count); Assert.Equal(6, restored.LastProviderSearch!.Results.Count);
        Assert.Equal(6, restored.Providers.Select(p => p.Url).Distinct().Count());
    }
    [Fact] public void ReviewSummaryAndRankRequireVisitedReviewSources()
    {
        var provider = Provider(); provider["reviewSummary"] = "Ranked feedback";
        provider["reviews"] = new JsonArray(new JsonObject { ["title"] = "User ranking", ["url"] = "https://reviews.example/list", ["date"] = "2026-10-01", ["summary"] = "Mixed user feedback", ["rank"] = 3 });
        var unsupported = Assert.Single(ProviderDiscovery.ParseResponse(Response(provider, "https://example.com/guide"))).Profile!;
        Assert.Empty(unsupported.Reviews); Assert.Empty(unsupported.ReviewSummary);
        var backed = Assert.Single(ProviderDiscovery.ParseResponse(Response((JsonObject)provider.DeepClone(), "https://example.com/guide", "https://reviews.example/list"))).Profile!;
        Assert.Equal(3, Assert.Single(backed.Reviews).Rank); Assert.Equal("Ranked feedback", backed.ReviewSummary);
    }
    [Fact] public void InvalidReviewDatesAndRanksAreOmittedWithoutInventingScores()
    {
        var provider = Provider(); provider["reviewSummary"] = "Feedback";
        provider["reviews"] = new JsonArray(new JsonObject { ["title"] = "User ranking", ["url"] = "https://reviews.example/list", ["date"] = "2099-10-01", ["summary"] = "Mixed feedback", ["rank"] = 0 });
        var review = Assert.Single(Assert.Single(ProviderDiscovery.ParseResponse(Response(provider, "https://example.com/guide", "https://reviews.example/list"))).Profile!.Reviews);
        Assert.Null(review.Date); Assert.Null(review.Rank);
    }
}
