using System.Net;
using System.Text;
using System.Text.Json;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class IpQualityTests
{
    private const string Own="""{"ip":"8.8.8.8","isp":{"asn":"AS15169","org":"Fixture ISP"},"location":{"country_code":"US"},"risk":{"is_datacenter":true,"risk_score":20}}""";
    private const string Proxy="""{"status":"ok","8.8.8.8":{"network":{"type":"Hosting","asn":"AS15169","provider":"Fixture ISP"},"location":{"country_code":"US"},"detections":{"risk":80,"confidence":99}}}""";
    private const string Registry="""{"data":{"located_resources":[{"resource":"8.8.8.0/24","location":"US"}]}}""";
    private sealed class Fixture(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> answer):HttpMessageHandler
    {
        internal List<string> Hosts { get; }=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        { lock(Hosts)Hosts.Add(request.RequestUri!.Host);return answer(request,cancellation); }
    }
    private static HttpResponseMessage Json(string json,HttpStatusCode status=HttpStatusCode.OK)=>new(status) { Content=new StringContent(json,Encoding.UTF8,"application/json") };
    private static Fixture Transport(string own=Own,string proxy=Proxy,string registry=Registry)=>new((request,_)=>Task.FromResult(Json(request.RequestUri!.Host switch { "api.ipquery.io"=>own,"proxycheck.io"=>proxy,"stat.ripe.net"=>registry,"api.ipify.org"=>"{\"ip\":\"8.8.8.8\"}",_=>"{}" })));
    [Fact] public async Task RawScoresAndAttributionSurviveMedianAndCountryCrossCheck()
    {
        using var transport=Transport();using var http=new HttpClient(transport);var service=new IpQualityService();
        var result=await service.QueryAsync(http,new());
        Assert.Equal(new double[] {20,80},result.Scores);Assert.Equal(50,result.Median);Assert.True(result.ScoreConflict);
        Assert.True(result.NativeReference);Assert.Equal("Hosting",result.Type);Assert.Equal(3,result.Sources.Count);
        Assert.DoesNotContain(99,result.Scores); // Confidence is not a risk score.
        await service.QueryAsync(http,new());Assert.Equal(1,transport.Hosts.Count(h=>h=="proxycheck.io"));Assert.Equal(1,transport.Hosts.Count(h=>h=="stat.ripe.net"));
    }
    [Fact] public async Task AFailedSourceNeverBecomesZeroOrFalseCleanliness()
    {
        using var transport=Transport(proxy:"not JSON");using var http=new HttpClient(transport);
        var result=await new IpQualityService().QueryAsync(http,new());
        Assert.Single(result.Scores);Assert.Equal(20,result.Median);Assert.Null(result.Sources.Single(s=>s.Name=="Proxycheck").Risk);
    }
    [Fact] public async Task GeoAndTypeDisagreementsRemainUnknownInsteadOfBeingMajorityGuesses()
    {
        using var transport=Transport(proxy:Proxy.Replace("Hosting","Residential").Replace("\"US\"","\"JP\""));using var http=new HttpClient(transport);
        var result=await new IpQualityService().QueryAsync(http,new());Assert.Null(result.Type);Assert.Null(result.NativeReference);Assert.Equal("US",result.RegisteredCountry);
    }
    [Fact] public async Task MissingRegistrationNeverTurnsIntoNative()
    {
        using var transport=Transport(registry:"{}");using var http=new HttpClient(transport);
        var result=await new IpQualityService().QueryAsync(http,new());Assert.Null(result.NativeReference);Assert.Null(result.RegisteredCountry);
    }
    [Theory][InlineData(-1)][InlineData(101)] public async Task OutOfRangeScoresAreExcluded(double score)
    {
        using var transport=Transport(proxy:Proxy.Replace("\"risk\":80","\"risk\":"+score));using var http=new HttpClient(transport);
        var result=await new IpQualityService().QueryAsync(http,new());Assert.Equal(new double[] {20},result.Scores);
    }
    [Fact] public async Task QuotaResponseStopsRepeatedRequestsToThatProvider()
    {
        var count=0;
        using var transport=new Fixture((request,_)=>Task.FromResult(request.RequestUri!.Host=="proxycheck.io" ? Limited() : Json(request.RequestUri.Host=="api.ipquery.io" ? Own : Registry)));
        HttpResponseMessage Limited() { count++;return Json("{}",HttpStatusCode.TooManyRequests); }
        using var http=new HttpClient(transport);var service=new IpQualityService();
        await service.QueryAsync(http,new());await service.QueryAsync(http,new() { ProxyCheckKey="synthetic-key-never-sent" });Assert.Equal(1,count);
    }
    [Fact] public async Task CancellationStopsAnInspectionWithoutPublishingAnEmptyScore()
    {
        using var stop=new CancellationTokenSource();
        using var transport=new Fixture(async(_,token)=> { stop.Cancel();await Task.Delay(10000,token);return Json(Own); });using var http=new HttpClient(transport);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new IpQualityService().QueryAsync(http,new(),stop.Token));
    }
    [Fact] public async Task ExtraApiKeyGoesInPostBodyAndNetworkAbuseShareIsNotInMedian()
    {
        var sawSecret=false;
        using var transport=new Fixture(async(request,_)=>
        {
            if(request.RequestUri!.Host=="api.ipapi.is")
            {
                Assert.Equal(HttpMethod.Post,request.Method);Assert.DoesNotContain("synthetic-key",request.RequestUri.ToString());
                sawSecret=(await request.Content!.ReadAsStringAsync()).Contains("synthetic-key-never-sent");
                return Json("""{"ip":"8.8.8.8","is_datacenter":true,"location":{"country_code":"US"},"company":{"abuser_score":"0.99 (High)"}}""");
            }
            return Json(request.RequestUri.Host switch { "api.ipquery.io"=>Own,"proxycheck.io"=>Proxy,_=>Registry });
        });
        using var http=new HttpClient(transport);var result=await new IpQualityService().QueryAsync(http,new() { IpApiKey="synthetic-key-never-sent" });
        Assert.True(sawSecret);Assert.Equal(new double[] {20,80},result.Scores);Assert.Null(result.Sources.Single(s=>s.Name=="ipapi.is").Risk);
    }
    [Fact] public void ANodeInspectionCannotInheritDirectRoutingOrSystemNetworking()
    {
        var node=NodeImport.Parse("ss://YWVzLTEyOC1nY206Zml4dHVyZQ@fixture.example:443#Fixture").Single();
        var original=new AppState { Nodes=[node],SelectedNode=node.Id,Mode=RouteMode.Direct,Capture=CaptureMode.Tun,Rules=[new("MATCH","","DIRECT")] };
        var inspection=NodeQualityProbe.Configuration(original,node,24567);
        using var config=JsonDocument.Parse(EngineSession.Create(inspection,24568).Config);
        Assert.Equal("global",config.RootElement.GetProperty("mode").GetString());Assert.False(config.RootElement.GetProperty("tun").GetProperty("enable").GetBoolean());
        Assert.Equal("127.0.0.1",config.RootElement.GetProperty("bind-address").GetString());Assert.Single(inspection.Nodes);Assert.Empty(inspection.Rules);
        Assert.Equal(CaptureMode.Tun,original.Capture);Assert.Equal(RouteMode.Direct,original.Mode);Assert.Single(original.Rules);
    }
}
