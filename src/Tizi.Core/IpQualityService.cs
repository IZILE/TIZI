using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Tizi.Core;

public sealed record IpQualitySettings
{
    public bool Enabled { get; init; } = true;
    public string ProxyCheckKey { get; init; } = "";
    public string IpApiKey { get; init; } = "";
    public static IpQualitySettings Normalize(IpQualitySettings? settings)=>(settings??new()) with { ProxyCheckKey=settings?.ProxyCheckKey??"",IpApiKey=settings?.IpApiKey??"" };
}
public sealed record IpQualitySource(string Name,string? Type,string? Country,string? Asn,string? Owner,double? Risk,string Status);
public sealed record IpQualityResult(string Ip,List<IpQualitySource> Sources,string? RegisteredCountry,DateTimeOffset CheckedAt)
{
    public double[] Scores => Sources.Where(s=>s.Risk is >=0 and <=100).Select(s=>s.Risk!.Value).Order().ToArray();
    public double? Median => Scores is { Length:>0 } values ? values.Length%2==1 ? values[values.Length/2] : (values[values.Length/2-1]+values[values.Length/2])/2 : null;
    public bool ScoreConflict => Scores.Length>1 && Scores.Max()-Scores.Min()>=35;
    public string? Type { get { var types=Sources.Select(s=>s.Type).Where(t=>t is not null).Distinct().ToArray();return types.Length==1 ? types[0] : null; } }
    public bool? NativeReference
    {
        get
        {
            var countries=Sources.Select(s=>s.Country).Where(c=>c is not null).Distinct().ToArray();
            return RegisteredCountry is not null && countries.Length==1 ? countries[0]==RegisteredCountry : null;
        }
    }
}

/// <summary>Independent observations remain attributable; only comparable 0-100 risk scores enter the median.</summary>
public sealed class IpQualityService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,DateTimeOffset> _backoff=new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,IpQualityResult> _cache=new();
    public async Task<IpQualityResult> QueryAsync(HttpClient http,IpQualitySettings settings,CancellationToken cancellation=default)
    {
        settings=IpQualitySettings.Normalize(settings);
        var sources=new List<IpQualitySource>();
        var own=await ReadAsync(http,new("https://api.ipquery.io/?format=json"),null,cancellation).ConfigureAwait(false);
        var ip=Text(own.Data,"ip");
        if(!PublicIp(ip))
        {
            var fallback=await ReadAsync(http,new("https://api.ipify.org?format=json"),null,cancellation).ConfigureAwait(false);
            ip=Text(fallback.Data,"ip");
        }
        if(!PublicIp(ip))return new("",[new("出口 IP",null,null,null,null,null,"Unavailable")],null,DateTimeOffset.UtcNow);
        ip=IPAddress.Parse(ip!).ToString();
        var fingerprint=ip+":"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(settings.ProxyCheckKey+"\0"+settings.IpApiKey)));
        if(_cache.TryGetValue(fingerprint,out var cached) && DateTimeOffset.UtcNow-cached.CheckedAt<TimeSpan.FromMinutes(cached.Scores.Length>=2 ? 10 : 1))return cached;
        sources.Add(ParseIpQuery(own.Data,ip,own.Status));
        var proxyTask=ReadAsync(http,new("https://proxycheck.io/v3/"+Uri.EscapeDataString(ip)+"?ver=24-June-2026&tag=0"),settings.ProxyCheckKey.Length>0 ? new Dictionary<string,string> { ["key"]=settings.ProxyCheckKey } : null,cancellation);
        var registryTask=ReadAsync(http,new("https://stat.ripe.net/data/rir-stats-country/data.json?resource="+Uri.EscapeDataString(ip)),null,cancellation);
        Task<Reply>? ipapiTask=settings.IpApiKey.Length>0 ? ReadAsync(http,new("https://api.ipapi.is/"),new Dictionary<string,string> { ["q"]=ip,["key"]=settings.IpApiKey },cancellation) : null;
        var proxy=await proxyTask.ConfigureAwait(false);sources.Add(ParseProxyCheck(proxy.Data,ip,proxy.Status));
        var registry=await registryTask.ConfigureAwait(false);var country=RegisteredCountry(registry.Data);
        if(ipapiTask is not null) { var extra=await ipapiTask.ConfigureAwait(false);sources.Add(ParseIpApi(extra.Data,ip,extra.Status)); }
        sources.Add(new("RIPEstat / RIR",null,null,null,null,null,country is null ? registry.Status=="OK" ? "Country unavailable" : registry.Status : "OK"));
        cancellation.ThrowIfCancellationRequested();var result=new IpQualityResult(ip,sources,country,DateTimeOffset.UtcNow);
        if(_cache.Count>=64 && _cache.MinBy(p=>p.Value.CheckedAt).Key is { } oldest)_cache.TryRemove(oldest,out _);
        _cache[fingerprint]=result;return result;
    }
    private sealed record Reply(JsonElement? Data,string Status);
    private async Task<Reply> ReadAsync(HttpClient http,Uri uri,Dictionary<string,string>? form,CancellationToken cancellation)
    {
        if(_backoff.TryGetValue(uri.Host,out var until) && until>DateTimeOffset.UtcNow)return new(null,"Rate limited");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromSeconds(7));
        try
        {
            using var request=new HttpRequestMessage(form is null ? HttpMethod.Get : HttpMethod.Post,uri);
            if(form is not null)request.Content=new FormUrlEncodedContent(form);
            request.Headers.UserAgent.ParseAdd("TIZI/0.1.26");request.Headers.Accept.ParseAdd("application/json");
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false);
            if(response.StatusCode==HttpStatusCode.TooManyRequests)
            {
                var retry=response.Headers.RetryAfter?.Date ?? (response.Headers.RetryAfter?.Delta is { } wait ? DateTimeOffset.UtcNow+wait : new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1),TimeSpan.Zero));
                _backoff[uri.Host]=retry;return new(null,"Rate limited");
            }
            if(!response.IsSuccessStatusCode)return new(null,"HTTP "+(int)response.StatusCode);
            using var stream=await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);using var bytes=new MemoryStream();
            var buffer=new byte[4096];int read;
            while((read=await stream.ReadAsync(buffer,deadline.Token).ConfigureAwait(false))>0)
            { if(bytes.Length+read>65536)return new(null,"Response too large");await bytes.WriteAsync(buffer.AsMemory(0,read),deadline.Token).ConfigureAwait(false); }
            using var json=JsonDocument.Parse(bytes.ToArray(),new JsonDocumentOptions { MaxDepth=32 });return new(json.RootElement.Clone(),"OK");
        }
        catch(OperationCanceledException) when(!cancellation.IsCancellationRequested) { return new(null,"Timed out"); }
        catch(Exception error) when(error is HttpRequestException or JsonException or IOException) { return new(null,"Unavailable"); }
    }
    private static IpQualitySource ParseIpQuery(JsonElement? data,string ip,string status)
    {
        if(Text(data,"ip")!=ip)return new("IPQuery",null,null,null,null,null,status=="OK" ? "Address mismatch" : status);
        var risk=At(data,"risk");var type=Boolean(risk,"is_datacenter")==true ? "Hosting" : Boolean(risk,"is_mobile")==true ? "Wireless" : null;
        return new("IPQuery",type,Country(Text(At(data,"location"),"country_code")),Text(At(data,"isp"),"asn"),Text(At(data,"isp"),"org"),Score(risk,"risk_score"),status);
    }
    private static IpQualitySource ParseProxyCheck(JsonElement? data,string ip,string status)
    {
        var answer=At(data,ip);if(answer is null)return new("Proxycheck",null,null,null,null,null,status=="OK" ? "No result" : status);
        var network=At(answer,"network");var type=Text(network,"type");
        if(type is not ("Residential" or "Business" or "Wireless" or "Hosting"))type=null;
        return new("Proxycheck",type,Country(Text(At(answer,"location"),"country_code")),Text(network,"asn"),Text(network,"provider"),Score(At(answer,"detections"),"risk"),status);
    }
    private static IpQualitySource ParseIpApi(JsonElement? data,string ip,string status)
    {
        if(Text(data,"ip")!=ip)return new("ipapi.is",null,null,null,null,null,status=="OK" ? "Address mismatch" : status);
        var type=Boolean(data,"is_datacenter")==true ? "Hosting" : Boolean(data,"is_mobile")==true ? "Wireless" : null;
        // A network abuse-share is not a per-address fraud score and must not be averaged into it.
        return new("ipapi.is",type,Country(Text(At(data,"location"),"country_code")),Text(At(data,"asn"),"asn"),Text(At(data,"asn"),"org"),null,status);
    }
    private static string? RegisteredCountry(JsonElement? data)
    {
        var resources=At(At(data,"data"),"located_resources");if(resources is not { ValueKind:JsonValueKind.Array } value)return null;
        var countries=value.EnumerateArray().Select(r=>Country(Text(r,"location"))).Where(c=>c is not null).Distinct().ToArray();
        return countries.Length==1 ? countries[0] : null;
    }
    private static JsonElement? At(JsonElement? data,string name)=>data is { ValueKind:JsonValueKind.Object } value && value.TryGetProperty(name,out var item) ? item : null;
    private static string? Text(JsonElement? data,string name)
    { var item=At(data,name);var text=item?.ValueKind switch { JsonValueKind.String=>item.Value.GetString(),JsonValueKind.Number=>item.Value.GetRawText(),_=>null };return text is { Length:>0 and <=512 } ? text : null; }
    private static bool? Boolean(JsonElement? data,string name)=>At(data,name)?.ValueKind switch { JsonValueKind.True=>true,JsonValueKind.False=>false,_=>null };
    private static double? Score(JsonElement? data,string name)=>double.TryParse(Text(data,name),NumberStyles.Float,CultureInfo.InvariantCulture,out var score) && double.IsFinite(score) && score is >=0 and <=100 ? score : null;
    private static string? Country(string? country)=>country is { Length:2 } && country.All(char.IsAsciiLetter) ? country.ToUpperInvariant() : null;
    private static bool PublicIp(string? text)
    {
        if(!IPAddress.TryParse(text,out var ip) || IPAddress.IsLoopback(ip))return false;
        if(ip.IsIPv4MappedToIPv6)ip=ip.MapToIPv4();var bytes=ip.GetAddressBytes();
        return bytes.Length==4 ? bytes[0] is >0 and <224 && bytes[0]!=10 && !(bytes[0]==172 && bytes[1] is >=16 and <=31) && !(bytes[0]==192 && bytes[1]==168) && !(bytes[0]==169 && bytes[1]==254) : !ip.IsIPv6LinkLocal && !ip.IsIPv6Multicast && !ip.IsIPv6SiteLocal && (bytes[0]&0xfe)!=0xfc && !ip.Equals(IPAddress.IPv6Any);
    }
}
