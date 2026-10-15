using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Tizi.Core;

public sealed record WebEvidence(string Url, string Text);
public interface IWebResearch { Task<IReadOnlyList<WebEvidence>> SearchAsync(string query, CancellationToken cancellation); }

// Public pages only. No API keys, subscription credentials, cookies or browser session are sent.
public sealed class PublicWebResearch : IWebResearch, IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<string,CancellationToken,Task<IPAddress[]>> _resolve;
    private readonly string[] _candidates;
    public PublicWebResearch(HttpMessageHandler? handler=null,Func<string,CancellationToken,Task<IPAddress[]>>? resolve=null,IReadOnlyList<string>? candidates=null)
    {
        _http=new(handler??new SocketsHttpHandler { AllowAutoRedirect=false, UseCookies=false, ConnectTimeout=TimeSpan.FromSeconds(7), AutomaticDecompression=DecompressionMethods.All }){Timeout=TimeSpan.FromSeconds(12)};
        _resolve=resolve??((host,token)=>Dns.GetHostAddressesAsync(host,token));
        _candidates=(candidates??[]).Where(PublicUrl).Where(url=>new Uri(url).Query.Length==0&&new Uri(url).Fragment.Length==0&&!Regex.IsMatch(new Uri(url).AbsolutePath,@"(?i)/(?:auth|user|login|register|checkout|cart)(?:/|$)")).Distinct().Take(24).ToArray();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TIZI-public-research/1.0");
    }
    private static readonly Regex Relevant = new(@"Clash|Shadowrocket|Shadowsocks|VMess|VLESS|Hysteria|TUIC|WireGuard|\bVPN\b|科学上网|机场(?:推荐|订阅|评测)|代理订阅",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    public async Task<IReadOnlyList<WebEvidence>> SearchAsync(string query,CancellationToken cancellation)
    {
        var urls=new List<string>(_candidates);
        // Search engines need short topic queries, not the model's full instructions and JSON schema.
        var terms=new[]{"机场 Clash Shadowrocket 订阅 官网 价格","机场 Clash 订阅 便宜 月付 官网","机场 Clash 订阅 评价 排名","iKuuu Clash 订阅 官网","Just My Socks 官网 价格","奈云 Clash 订阅 官网","Nexitally Clash 订阅 官网","DlerCloud Clash 订阅 官网","WgetCloud Clash 订阅 官网"};
        foreach(var batch in terms.Chunk(3))
        {
            var found=await Task.WhenAll(batch.Select(async term=>
            {
              try {
                var feed=await GetAsync("https://www.bing.com/search?format=rss&mkt=zh-CN&setlang=zh-hans&q="+Uri.EscapeDataString(term),cancellation);
                using var reader=XmlReader.Create(new StringReader(feed.Text),new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,MaxCharactersInDocument=300000 });
                return XDocument.Load(reader).Descendants("item").Take(12).Where(i=>Relevant.IsMatch(WebUtility.HtmlDecode((i.Element("title")?.Value??"")+" "+(i.Element("description")?.Value??"")))).Select(i=>i.Element("link")?.Value??"").ToArray();
              }catch(Exception e) when(e is HttpRequestException or XmlException or IOException or FormatException or OperationCanceledException && !cancellation.IsCancellationRequested){return Array.Empty<string>();}
            }));
            urls.AddRange(found.SelectMany(list=>list));
        }
        var pages=new List<WebEvidence>(); var links=new List<string>();
        async Task ReadPages(IEnumerable<string> candidates,bool collectLinks)
        {
            foreach(var batch in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(PublicUrl).GroupBy(url=>new Uri(url).Host,StringComparer.OrdinalIgnoreCase).SelectMany(g=>g.Take(2)).Take(24).Chunk(4))
            {
                var found=await Task.WhenAll(batch.Select(async url=>
                {
                    try{return await GetAsync(url,cancellation);}catch(Exception e)when(e is HttpRequestException or IOException or FormatException or SocketException or OperationCanceledException && !cancellation.IsCancellationRequested){return null;}
                }));
                foreach(var page in found.OfType<WebEvidence>())
                {
                    if(collectLinks)
                        foreach(Match match in Regex.Matches(page.Text,@"(?i)href\s*=\s*[""']([^""'\s<>]+)[""']",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))
                        {
                            var value=WebUtility.HtmlDecode(match.Groups[1].Value);
                            if(value.StartsWith('#')||!Uri.TryCreate(new Uri(page.Url),value,out var target))continue;
                            var link=target.AbsoluteUri;
                            if(!PublicUrl(link)||target.AbsolutePath.Contains("/cdn-cgi/",StringComparison.OrdinalIgnoreCase))continue;
                            var privatePath=Regex.IsMatch(target.AbsolutePath,@"(?i)/(?:auth|user|login|register|checkout|cart)(?:/|$)");
                            if(target.Host!=new Uri(page.Url).Host&&(target.Query.Length>0||target.Fragment.Length>0||privatePath))link=target.GetLeftPart(UriPartial.Authority)+"/";
                            else if(target.Query.Length>0||target.Fragment.Length>0||privatePath)continue;
                            links.Add(link);
                        }
                    var text=Regex.Replace(page.Text,@"(?is)<(script|style|noscript)\b[^>]*>.*?</\1>"," ");
                    text=Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(text,@"<[^>]+>"," ")),@"\s+"," ").Trim();
                    if(text.Length>80&&Relevant.IsMatch(text))
                    {
                        var excerpts=new List<string>{text[..Math.Min(text.Length,700)]};
                        foreach(var pattern in new[]{Relevant,new Regex(@"价格|套餐|月付|年付|CNY|USD|pricing|plans|refund|退款|评价",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))})
                            foreach(Match match in pattern.Matches(text).Take(3))
                            {var start=Math.Max(0,match.Index-100);excerpts.Add(text.Substring(start,Math.Min(650,text.Length-start)));}
                        var excerpt=string.Join(" … ",excerpts.Distinct());pages.Add(new(page.Url,excerpt[..Math.Min(excerpt.Length,4000)]));
                    }
                }
            }
        }
        await ReadPages(urls,true);
        await ReadPages(links.Where(url=>!pages.Any(p=>p.Url==url)).OrderBy(url=>pages.Any(p=>new Uri(p.Url).Host==new Uri(url).Host)?1:0).ToArray(),true);
        // A comparison article often links to an official homepage whose client/price pages are relative links.
        await ReadPages(links.Where(url=>!pages.Any(p=>p.Url==url)).OrderBy(url=>pages.Any(p=>new Uri(p.Url).Host==new Uri(url).Host)?1:0).Take(12).ToArray(),false);
        if(pages.Count==0)throw new HttpRequestException("Public web research failed; previous results kept.");
        return pages.DistinctBy(p=>p.Url).ToArray();
    }
    private static bool PublicUrl(string url) => Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&!uri.IsLoopback&&uri.UserInfo.Length==0&&!IPAddress.TryParse(uri.Host,out _)
        &&!new[]{"google.com","bing.com","facebook.com","twitter.com","youtube.com","t.me"}.Any(h=>uri.Host==h||uri.Host.EndsWith("."+h,StringComparison.OrdinalIgnoreCase));
    private async Task<WebEvidence> GetAsync(string url,CancellationToken cancellation)
    {
        using var pageTimeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);pageTimeout.CancelAfter(TimeSpan.FromSeconds(8));var token=pageTimeout.Token;
        var uri=new Uri(url);
        for(var hop=0;hop<5;hop++)
        {
            if(uri.Scheme!="https"||uri.IsLoopback||uri.UserInfo.Length!=0)throw new FormatException("Expected a public HTTPS website.");
            var addresses=await _resolve(uri.Host,token);
            if(addresses.Length==0||addresses.Any(ip=>!IsPublicAddress(ip)))throw new FormatException("Expected a public HTTPS website.");
            using var response=await _http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
            if((int)response.StatusCode is >=300 and <400 && response.Headers.Location is { } location) {uri=location.IsAbsoluteUri?location:new Uri(uri,location);continue;}
            response.EnsureSuccessStatusCode();
            var media=response.Content.Headers.ContentType?.MediaType??"";
            if(media.Length>0&&!media.Contains("html")&&!media.Contains("xml")&&!media.StartsWith("text/"))throw new FormatException("Expected a public web page.");
            await using var stream=await response.Content.ReadAsStreamAsync(token);using var bytes=new MemoryStream();var buffer=new byte[8192];int read;
            while(bytes.Length<262144&&(read=await stream.ReadAsync(buffer,token))>0)bytes.Write(buffer,0,Math.Min(read,262144-(int)bytes.Length));
            return new(uri.AbsoluteUri,System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
        }
        throw new HttpRequestException("Too many website redirects.");
    }
    private static bool IsPublicAddress(IPAddress address)
    {
        if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();if(IPAddress.IsLoopback(address)||address.Equals(IPAddress.Any)||address.Equals(IPAddress.IPv6Any))return false;
        var b=address.GetAddressBytes();return b.Length==4 ? b[0] is not(0 or 10 or 127)&&b[0]<224&&!(b[0]==169&&b[1]==254)&&!(b[0]==172&&b[1]>=16&&b[1]<=31)&&!(b[0]==192&&b[1]==168)
            : !address.IsIPv6LinkLocal&&!address.IsIPv6SiteLocal&&!address.IsIPv6Multicast&&(b[0]&0xfe)!=0xfc;
    }
    public void Dispose()=>_http.Dispose();
}
