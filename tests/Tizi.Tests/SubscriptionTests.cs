using System.Net;
using System.Text;
using Tizi.Core;

namespace Tizi.Tests;

public class SubscriptionTests
{
    private static ProxyNode Node(string name, string host = "first.example") => NodeImport.Parse("ss://YWVzLTEyOC1nY206dGVzdA==@" + host + ":443#" + Uri.EscapeDataString(name)).Single();
    private static HttpClient Feed(params ProxyNode[] nodes) => new(new FeedHandler(NodeExport.Json(nodes, [])));
    [Fact] public async Task RotatingEndpointKeepsLogicalNodeIdentityAndUpdatedParameters()
    {
        var sub = new Subscription { Name = "Test", Url = "https://example.test/sub" };
        var previous = Node("Japan 01") with { SubscriptionId = sub.Id, CountryCode = "JP" };
        var changed = Node("Japan 01", "rotated.example"); changed.Options["password"] = "rotated-password";
        using var http = Feed(changed);
        var result = await new SourceUpdates(http).RefreshSubscriptionAsync(sub, [previous]);
        var node = Assert.Single(result.Nodes);
        Assert.Equal(previous.Id, node.Id); Assert.Equal("rotated.example", node.Server); Assert.Equal("rotated-password", node.Options["password"]!.ToString()); Assert.Equal("JP", node.CountryCode);
    }
    [Fact] public async Task AmbiguousDuplicateLabelsDoNotBorrowAnArbitraryIdentity()
    {
        var sub = new Subscription { Name = "Test", Url = "https://example.test/sub" };
        var first = Node("Duplicate") with { SubscriptionId = sub.Id };
        var second = Node("Duplicate", "second.example") with { SubscriptionId = sub.Id };
        using var http = Feed(Node("Duplicate", "third.example"), Node("Duplicate", "fourth.example"));
        var result = await new SourceUpdates(http).RefreshSubscriptionAsync(sub, [first, second]);
        Assert.DoesNotContain(result.Nodes, n => n.Id == first.Id || n.Id == second.Id); Assert.Equal(2, result.Nodes.Select(n => n.Id).Distinct().Count());
    }
    [Fact] public async Task MatchingLabelsFromOtherSubscriptionsDoNotLeakIdentity()
    {
        var sub = new Subscription { Name = "Test", Url = "https://example.test/sub" };
        var unrelated = Node("Japan 01") with { SubscriptionId = Guid.NewGuid() };
        using var http = Feed(Node("Japan 01"));
        Assert.NotEqual(unrelated.Id, (await new SourceUpdates(http).RefreshSubscriptionAsync(sub, [unrelated])).Nodes.Single().Id);
    }
    [Fact] public async Task NumericAccountNoticesAreExcludedButOrdinaryNodeNamesRemain()
    {
        var sub = new Subscription { Name = "Test", Url = "https://example.test/sub" };
        using var http = Feed(Node("剩余流量：70.82% 212.47GB"), Node("过期时间：2026-10-18 17:13:59"), Node("Traffic Remaining: 10 GB"), Node("Japan 01"), Node("Expires: Japan special"));
        var result = await new SourceUpdates(http).RefreshSubscriptionAsync(sub, []);
        Assert.Equal(new[] { "Japan 01", "Expires: Japan special" }, result.Nodes.Select(n => n.Name));
    }
    [Fact] public async Task StatusOnlyFeedIsRejected()
    {
        using var http = Feed(Node("过期时间：2026-10-18 17:13:59"));
        await Assert.ThrowsAsync<FormatException>(() => new SourceUpdates(http).RefreshSubscriptionAsync(new Subscription { Name = "Test", Url = "https://example.test/sub" }, []));
    }
    sealed class FeedHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) });
    }
}
