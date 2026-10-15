using System.Text;
using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class SubscriptionImportTests
{
    private const string Url = "https://feed.example.test/link/synthetic?shadowrocket=1";
    private const string Links = "ss://YWVzLTEyOC1nY206c3ludGhldGlj@jp.example.test:443#Japan\nvmess://eyJwcyI6IkhvbmcgS29uZyIsImFkZCI6ImhrLmV4YW1wbGUudGVzdCIsInBvcnQiOiI0NDMiLCJpZCI6IjI5YzI5NWI4LTU3MjQtNGM0MS04Y2YxLWNlYWU0Mzk4NmE1OCIsImFpZCI6IjAiLCJuZXQiOiJ0Y3AifQ==";
    [Theory]
    [InlineData("Subscribe")]
    [InlineData("subscription")]
    [InlineData(" Subscribe ")]
    public void SingleSubscriptionObjectUsesHostAndTitleInsteadOfNodePort(string type)
    {
        var json = new JsonObject { ["type"] = type, ["host"] = Url, ["url"] = "", ["remarks"] = "", ["name"] = "", ["title"] = "Example VPN", ["port"] = "0", ["password"] = "", ["udp"] = 1 };
        foreach (var payload in new[] { json.ToJsonString(), "[" + json.ToJsonString() + "]", "{\"servers\":[" + json.ToJsonString() + "]}" })
        {
            var document = ImportDocument.Parse(payload); var subscription = Assert.Single(document.Subscriptions);
            Assert.Equal(Url, subscription.Url); Assert.Equal("Example VPN", subscription.Name); Assert.Empty(document.Nodes); Assert.Empty(document.Warnings);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShadowrocketStatusPreambleAndUrlSafeBase64KeepEveryNode(bool encoded)
    {
        var raw = "STATUS=剩余流量：183 GB\r\nREMARKS=Example VPN\r\n" + Links;
        var input = encoded ? Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_') : raw;
        var document = ImportDocument.Parse(input);
        Assert.Equal(new[] { "ss", "vmess" }, document.Nodes.Select(n => n.Type)); Assert.Empty(document.Warnings);
        Assert.Equal("synthetic", document.Nodes[0].Options["password"]!.ToString());
    }
    [Theory]
    [InlineData("STATUS=0 GB")]
    [InlineData("STATUS=0 GB\n")]
    [InlineData("STATUS=0 GB\nss://broken")]
    public void StatusDoesNotHideEmptyOrInvalidFeed(string payload) => Assert.Throws<FormatException>(() => ImportDocument.Parse(payload));
    [Fact] public void SubscriptionCommandsAndUrlFilePayloadResolveSameEndpoint()
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(Url)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        foreach (var input in new[] { "sub://" + encoded + "#Example%20VPN\r\n", "shadowrocket://add/sub://" + encoded + "#Example%20VPN", "clash://install-config?url=" + Uri.EscapeDataString(Url), Url })
            Assert.Equal(Url, ImportDocument.RemoteUrl(input));
    }
    [Theory]
    [InlineData("file:///C:/private")]
    [InlineData("https://user:password@feed.example.test/feed")]
    public void SingleSubscriptionObjectStillRejectsUnsafeUrls(string url) => Assert.Throws<FormatException>(() => ImportDocument.Parse(new JsonObject { ["type"] = "Subscribe", ["host"] = url }.ToJsonString()));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WebsiteShadowrocketRemarksAreSeparatedFromBase64AndSurviveWrapping(bool urlSafe)
    {
        var url = Url + "&token=a%2Bb%252Fc";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(url));
        if (urlSafe) encoded = encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var sub = "sub://" + encoded + "?remarks=" + Uri.EscapeDataString("Example VPN 100% + 中文") + "#Unused";
        foreach (var payload in new[] { sub, "shadowrocket://add/" + sub, "shadowrocket://add/" + Uri.EscapeDataString(sub), "shadowrocket://config/add/" + sub })
        {
            var imported = ImportDocument.RemoteSubscription(payload)!;
            Assert.Equal(url, imported.Url); Assert.Equal("Example VPN 100% + 中文", imported.Name);
        }
    }
    [Fact] public void CommandNamesAndFragmentsDecodeOnceWithoutChangingUrlEscapes()
    {
        var url = Url + "&token=a%2Bb%252Fc";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(url));
        Assert.Equal("Example VPN", ImportDocument.RemoteSubscription("sub://" + encoded + "?remarks=Example+VPN")!.Name);
        Assert.Equal("100%20 VPN", ImportDocument.RemoteSubscription("shadowrocket://add/sub://" + encoded + "#100%2520%20VPN")!.Name);
        var clash = ImportDocument.RemoteSubscription("clash://install-config?url=" + Uri.EscapeDataString(url) + "&name=Example%20VPN")!;
        Assert.Equal(url, clash.Url); Assert.Equal("Example VPN", clash.Name);
        Assert.Equal(url, ImportDocument.RemoteUrl("shadowrocket://add/" + url));
        Assert.Equal("feed.example.test", ImportDocument.RemoteSubscription(Url)!.Name);
    }
    [Theory]
    [InlineData("sub://bad!payload?remarks=Example")]
    [InlineData("shadowrocket://add/sub://%%%?remarks=Example")]
    public void InvalidEncodedLinkReportsAnActionableError(string link) => Assert.Equal("Invalid encoded subscription URL.", Assert.Throws<FormatException>(() => ImportDocument.RemoteSubscription(link)).Message);
}
