using System.Net;
using System.Text;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class SubscriptionNamingTests
{
    private const string Links = "ss://YWVzLTEyOC1nY206c3ludGhldGlj@jp.example.test:443#Japan";
    private static readonly Subscription Placeholder = new() { Name = "FEED.example.test", Url = "https://feed.example.test/sub" };
    private sealed class Feed(string text, string? profile, string? filename, string? website) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8) };
            if (profile is not null) response.Headers.TryAddWithoutValidation("profile-title", profile);
            if (filename is not null) response.Content.Headers.TryAddWithoutValidation("content-disposition", filename);
            if (website is not null) response.Headers.TryAddWithoutValidation("profile-web-page-url", website);
            return Task.FromResult(response);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawAndEncodedMetadataRepairOldDomainNameAndKeepManualName(bool encoded)
    {
        var text = "STATUS=10 GB\r\nREMARKS=Example VPN\r\n" + Links;
        if (encoded) text = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var http = new HttpClient(new Feed(text, null, null, null));
        var result = await new SourceUpdates(http).RefreshSubscriptionAsync(Placeholder, []);
        Assert.Equal("Example VPN", result.Subscription.Name); Assert.Equal(Placeholder.Id, result.Subscription.Id);
        var custom = await new SourceUpdates(http).RefreshSubscriptionAsync(Placeholder with { Name = "My choice" }, result.Nodes);
        Assert.Equal("My choice", custom.Subscription.Name); Assert.Equal(result.Nodes[0].Id, custom.Nodes[0].Id);
    }
    [Theory]
    [InlineData("attachment; filename=Example_V2.yaml", "https://example.top/", "EXAMPLE")]
    [InlineData("attachment; filename=node.txt", "https://example.top/", "EXAMPLE")]
    [InlineData("attachment; filename=nodes.txt", "https://panel.example.co.uk/", "EXAMPLE")]
    [InlineData("attachment; filename=ExampleVPN.yaml", null, "ExampleVPN")]
    [InlineData("attachment; filename*=UTF-8''Example%20VPN.yaml", null, "Example VPN")]
    [InlineData("attachment; filename=node.txt", null, "FEED.example.test")]
    [InlineData("attachment; filename=node.txt", "http://127.0.0.1/", "FEED.example.test")]
    [InlineData("attachment; filename=node.txt", "https://user:pass@example.top/", "FEED.example.test")]
    public async Task DownloadFilenameAndWebsiteProvideUsefulNames(string filename, string? website, string expected)
    {
        using var http = new HttpClient(new Feed(Links, null, filename, website));
        Assert.Equal(expected, (await new SourceUpdates(http).RefreshSubscriptionAsync(Placeholder, [])).Subscription.Name);
    }
    [Fact] public async Task ExplicitProfileTitleWinsAndBadTitleFallsBackToRemarks()
    {
        var text = "REMARKS=Body name\n" + Links;
        using var http = new HttpClient(new Feed(text, "base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Profile 中文")), "attachment; filename=File.yaml", "https://example.top/"));
        Assert.Equal("Profile 中文", (await new SourceUpdates(http).RefreshSubscriptionAsync(Placeholder, [])).Subscription.Name);
        using var invalid = new HttpClient(new Feed(text, "base64:broken!", null, null));
        Assert.Equal("Body name", (await new SourceUpdates(invalid).RefreshSubscriptionAsync(Placeholder, [])).Subscription.Name);
    }
}
