using Tizi.Core;

namespace Tizi.Tests;

public class OperationFeedbackTests
{
    [Fact]
    public void LegacyFailuresAreClearedWithoutChangingSavedContentOrPreferences()
    {
        var usage = new SubscriptionUsage(12, 34, 100, DateTimeOffset.UtcNow.AddDays(5));
        var subscription = new Subscription { Name = "Synthetic", Url = "https://feed.example/", LastError = "Connection failed", Usage = usage, UpdatedAt = DateTimeOffset.UtcNow, Website = "https://site.example/" };
        var node = NodeImport.Parse("ss://YWVzLTEyOC1nY206dGVzdA==@node.example:443#Fixture").Single() with { SubscriptionId = subscription.Id };
        var site = new ProviderSite { Name = "Fixture", Url = "https://site.example/", Error = "Timeout", CheckedAt = DateTimeOffset.UtcNow, StatusCode = 200, LatencyMs = 12 };
        var state = new AppState { Subscriptions = [subscription], Nodes = [node], SelectedNode = node.Id, Providers = [site], ProviderSearchError = "Search failed", Rules = [new("DOMAIN", "example.test", "DIRECT")], Mode = RouteMode.Direct, MixedPort = 8888, RuleSourceName = "Saved rules", Discovery = new() { Model = "fixture" } };
        var clean = OperationFeedback.ClearSavedErrors(state);
        Assert.Null(clean.Subscriptions.Single().LastError); Assert.Null(clean.Providers.Single().Error); Assert.Null(clean.ProviderSearchError);
        Assert.Equal(subscription with { LastError = null }, clean.Subscriptions.Single()); Assert.Equal(site with { Error = null }, clean.Providers.Single());
        Assert.Same(state.Nodes, clean.Nodes); Assert.Same(state.Rules, clean.Rules); Assert.Same(state.Discovery, clean.Discovery);
        Assert.Equal(state.SelectedNode, clean.SelectedNode); Assert.Equal(state.Mode, clean.Mode); Assert.Equal(state.MixedPort, clean.MixedPort); Assert.Equal(state.RuleSourceName, clean.RuleSourceName);
        Assert.Equal("Connection failed", subscription.LastError); Assert.Equal("Timeout", site.Error); Assert.Equal("Search failed", state.ProviderSearchError);
    }

    [Fact]
    public void CleanStateIsReturnedWithoutAllocatingOrReplacingCollections()
    {
        var state = new AppState(); Assert.Same(state, OperationFeedback.ClearSavedErrors(state));
    }

    [Fact]
    public void UnaffectedRecordsRetainTheirIdentityDuringLegacyCleanup()
    {
        var failed = new Subscription { Name = "Old", Url = "https://old.example/", LastError = "Timeout" };
        var good = new Subscription { Name = "Good", Url = "https://good.example/" };
        var site = new ProviderSite { Name = "Good", Url = "https://site.example/" };
        var state = new AppState { Subscriptions = [failed, good], Providers = [site] };
        var clean = OperationFeedback.ClearSavedErrors(state);
        Assert.Same(good, clean.Subscriptions[1]); Assert.Same(state.Providers, clean.Providers);
    }
}
