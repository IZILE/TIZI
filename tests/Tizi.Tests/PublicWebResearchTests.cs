using System.Net;
using System.Text;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class PublicWebResearchTests
{
    [Fact] public async Task ShortLocalizedQueriesRejectUnrelatedShoppingAndFollowPublicOfficialLinks()
    {
        var handler=new Pages();using var research=new PublicWebResearch(handler,(_,_)=>Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")}));
        var pages=await research.SearchAsync(ProviderSearchModes.BuildQuery(new()),default);
        Assert.Contains(pages,p=>p.Url=="https://official.example.test/"&&p.Text.Contains("Clash"));
        Assert.Contains(pages,p=>p.Url=="https://review.example.test/pricing"&&p.Text.Contains("monthly"));
        Assert.DoesNotContain(handler.Urls,u=>u.Host=="shopping.example.test"||u.Query.Contains("affiliate"));
        Assert.True(handler.Urls.Where(u=>u.Host=="www.bing.com").All(u=>u.Query.Contains("mkt=zh-CN")&&u.AbsoluteUri.Length<500&&!Uri.UnescapeDataString(u.Query).Contains("固定筛选条件")));
        Assert.True(pages.All(p=>p.Text.Length<=4000));
    }
    [Fact] public async Task PrivateDnsTargetsAreNotFetched()
    {
        var handler=new Pages();using var research=new PublicWebResearch(handler,(host,_)=>Task.FromResult(new[]{IPAddress.Parse(host=="official.example.test"?"192.168.1.1":"8.8.8.8")}));
        var pages=await research.SearchAsync("fixture",default);
        Assert.DoesNotContain(handler.Urls,u=>u.Host=="official.example.test");Assert.DoesNotContain(pages,p=>new Uri(p.Url).Host=="official.example.test");
    }
    [Fact] public async Task UserCancellationIsNotSwallowedAsAnEmptySearch()
    {
        using var token=new CancellationTokenSource();token.Cancel();using var research=new PublicWebResearch(new Pages(),(_,cancellation)=>Task.FromCanceled<IPAddress[]>(cancellation));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>research.SearchAsync("fixture",token.Token));
    }
    [Fact] public async Task SavedPublicProviderPagesAreRefetchedWhenSearchIndexIsEmpty()
    {
        var handler=new Pages{EmptyIndex=true};using var research=new PublicWebResearch(handler,(_,_)=>Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")}),["https://official.example.test/","https://official.example.test/user","https://official.example.test/?token=private"]);
        var pages=await research.SearchAsync("fixture",default);
        Assert.Contains(pages,p=>p.Url=="https://official.example.test/");Assert.DoesNotContain(handler.Urls,u=>u.AbsolutePath=="/user"||u.Query.Contains("token="));
    }
    private sealed class Pages:HttpMessageHandler
    {
        internal List<Uri> Urls=[];internal bool EmptyIndex;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var uri=request.RequestUri!;Urls.Add(uri);string text;
            if(uri.Host=="www.bing.com")text=EmptyIndex?"<rss><channel/></rss>":"<rss><channel><item><title>Furniture shopping</title><link>https://shopping.example.test/</link><description>Tables chairs desks</description></item><item><title>Clash 机场订阅 comparison</title><link>https://review.example.test/</link></item></channel></rss>";
            else if(uri.Host=="review.example.test")text="<html>Clash 机场订阅 compare public monthly plans and refund conditions. More published details available here.<a href='/pricing'>Prices</a><a href='https://official.example.test/auth/register?affiliate=discard'>Official</a></html>";
            else text="<html>Official personal browsing subscription for Clash and Shadowrocket. Monthly CNY 12 includes a 100 GB quota. Public plans and protocol guides.</html>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(text,Encoding.UTF8,"text/html")});
        }
    }
}
