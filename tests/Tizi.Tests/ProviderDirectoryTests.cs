using System.Text.Json;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class ProviderDirectoryTests
{
    private static ProviderSite Site(string suffix = "") => new()
    {
        Name = "Example" + suffix, Url = "https://example.com/" + suffix, Source = "OpenAI web search",
        Profile = new("Synthetic fixture", [new("Monthly", 12, "CNY", "month", 100, "Monthly quota", "https://example.com/pricing") { PriceUnit = "period" }], ["Japan"], "Unverified", "Unverified", ["https://example.com/guide"], DateTimeOffset.UtcNow) { SupportedProtocols = ["Clash"], SubscriptionSource = "https://example.com/guide" }
    };
    [Fact] public void DirectoryRoundTripKeepsPlansAndPreservesExistingIdentity()
    {
        var site = Site(); var imported = ProviderDirectory.Import(ProviderDirectory.Export([site]), "Imported directory", [site]);
        Assert.Single(imported); Assert.Equal(site.Id, imported[0].Id); Assert.Equal(site.Profile!.Plans, imported[0].Profile!.Plans);
        Assert.Equal("Unverified", imported[0].Profile!.Verification);
        Assert.Single(ProviderDirectory.Merge([site], imported));
    }
    [Fact] public void CachedSnapshotSurvivesRestartAndRefreshDoesNotAccumulateSearchDuplicates()
    {
        var manual = Site("manual") with { Source = "Imported directory" }; var site = Site();
        var state = ProviderDirectory.CacheSearch(new AppState { Providers = [manual, site] }, [site], new() { ModeId = "budget" }, DateTimeOffset.UtcNow);
        var restored = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state))!;
        Assert.Equal("budget", restored.LastProviderSearch!.Criteria.ModeId); Assert.Single(restored.LastProviderSearch.Results);
        var next = ProviderDirectory.CacheSearch(restored, [Site("new")], new(), DateTimeOffset.UtcNow);
        Assert.Equal(3, next.Providers.Count); Assert.Contains(next.Providers, p => p.Id == manual.Id);
        Assert.Contains(next.Providers, p => p.Id == site.Id); Assert.Equal("Examplenew", next.Providers[0].Name);
        Assert.Throws<FormatException>(() => ProviderDirectory.CacheSearch(restored, [], new(), DateTimeOffset.UtcNow));
        Assert.Equal(2, restored.Providers.Count); Assert.Single(restored.LastProviderSearch.Results);
    }
    [Fact] public void SearchCacheKeepsOneHundredNewestDistinctWebsitesAndStableIdentities()
    {
        var old = Enumerable.Range(0, 100).Select(i => Site(i.ToString())).ToList();
        var incoming = Enumerable.Range(95, 20).Select(i => Site(i.ToString())).ToList();
        var state = ProviderDirectory.CacheSearch(new() { Providers = old }, incoming, new(), DateTimeOffset.Now);
        Assert.Equal(100, state.Providers.Count); Assert.Equal(20, state.LastProviderSearch!.Results.Count);
        Assert.Equal(old[95].Id, state.Providers[0].Id); Assert.Equal("Example95", state.Providers[0].Name);
        Assert.DoesNotContain(state.Providers, p => p.Name == "Example94"); Assert.Contains(state.Providers, p => p.Name == "Example0");
        var again = ProviderDirectory.CacheSearch(state, incoming, new(), DateTimeOffset.Now);
        Assert.Equal(state.Providers.Select(p => p.Id), again.Providers.Select(p => p.Id));
        Assert.Equal(100, JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(again))!.Providers.Count);
    }
    [Fact] public void MalformedProfilesAndUntrustedVerificationLabelsCannotBypassValidation()
    {
        var json = ProviderDirectory.Export([Site()]);
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        root["providers"]![0]!["profile"]!["supportedProtocols"] = null;
        Assert.Throws<FormatException>(() => ProviderDirectory.Import(root.ToJsonString(), "Import", []));
        root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        root["providers"]![0]!["profile"]!["verification"] = "Verified";
        Assert.Equal("Unverified", ProviderDirectory.Import(root.ToJsonString(), "Import", [])[0].Profile!.Verification);
        root["providers"]![0]!["profile"]!["plans"]![0]!["source"] = "http://127.0.0.1/";
        Assert.Throws<FormatException>(() => ProviderDirectory.Import(root.ToJsonString(), "Import", []));
    }
}
