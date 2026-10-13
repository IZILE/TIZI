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
    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect=false, UseCookies=false, ConnectTimeout=TimeSpan.FromSeconds(7), AutomaticDecompression=DecompressionMethods.All }) {Timeout=TimeSpan.FromSeconds(12)};
    public PublicWebResearch() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("TIZI-public-research/1.0");
    public async Task<IReadOnlyList<WebEvidence>> SearchAsync(string query,CancellationToken cancellation)
    {
        var urls=new List<string>();
        foreach(var term in new[]{"中文 机场 Clash 订阅 官网 价格 "+query[..Math.Min(query.Length,500)],"机场 Clash 订阅 便宜 月付 官网","机场 订阅 评价 排名"})
        {
            try
            {
                var feed=await GetAsync("https://www.bing.com/search?format=rss&q="+Uri.EscapeDataString(term),cancellation);
                using var reader=XmlReader.Create(new StringReader(feed.Text),new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,MaxCharactersInDocument=300000 });
                urls.AddRange(XDocument.Load(reader).Descendants("item").Take(12).Select(i=>i.Element("link")?.Value??""));
            }
            catch(Exception e) when(e is HttpRequestException or XmlException or IOException or FormatException or OperationCanceledException && !cancellation.IsCancellationRequested) { }
        }
        var pages=new List<WebEvidence>(); var links=new List<string>();
        async Task ReadPages(IEnumerable<string> candidates,bool collectLinks)
        {
            foreach(var batch in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(PublicUrl).Take(24).Chunk(4))
            {
                var found=await Task.WhenAll(batch.Select(async url=>
                {
                    try{return await GetAsync(url,cancellation);}catch(Exception e)when(e is HttpRequestException or IOException or FormatException or SocketException or OperationCanceledException && !cancellation.IsCancellationRequested){return null;}
                }));
                foreach(var page in found.OfType<WebEvidence>())
                {
                    if(collectLinks)
                        foreach(Match match in Regex.Matches(page.Text,@"(?i)href\s*=\s*[""'](https://[^""'\s<>]+)[""']"))
                        { var value=WebUtility.HtmlDecode(match.Groups[1].Value);if(PublicUrl(value)&&!value.Contains('?')&&!value.Contains("/cdn-cgi/",StringComparison.OrdinalIgnoreCase))links.Add(value); }
                    var text=Regex.Replace(page.Text,@"(?is)<(script|style|noscript)\b[^>]*>.*?</\1>"," ");
                    text=Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(text,@"<[^>]+>"," ")),@"\s+"," ").Trim();
                    if(text.Length>80)pages.Add(new(page.Url,text[..Math.Min(text.Length,2400)]));
                }
            }
        }
        await ReadPages(urls,true); await ReadPages(links.Where(url=>!pages.Any(p=>p.Url==url)),false);
        if(pages.Count==0)throw new HttpRequestException("Public web research failed; previous results kept.");
        return pages.DistinctBy(p=>p.Url).ToArray();
    }
    private static bool PublicUrl(string url) => Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&!uri.IsLoopback&&uri.UserInfo.Length==0&&!IPAddress.TryParse(uri.Host,out _)
        &&!new[]{"google.com","bing.com","facebook.com","twitter.com","youtube.com","t.me"}.Any(h=>uri.Host==h||uri.Host.EndsWith("."+h,StringComparison.OrdinalIgnoreCase));
    private async Task<WebEvidence> GetAsync(string url,CancellationToken cancellation)
    {
        var uri=new Uri(url);
        for(var hop=0;hop<5;hop++)
        {
            if(uri.Scheme!="https"||uri.IsLoopback||uri.UserInfo.Length!=0)throw new FormatException("Expected a public HTTPS website.");
            var addresses=await Dns.GetHostAddressesAsync(uri.Host,cancellation);
            if(addresses.Length==0||addresses.Any(ip=>!IsPublicAddress(ip)))throw new FormatException("Expected a public HTTPS website.");
            using var response=await _http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancellation);
            if((int)response.StatusCode is >=300 and <400 && response.Headers.Location is { } location) {uri=location.IsAbsoluteUri?location:new Uri(uri,location);continue;}
            response.EnsureSuccessStatusCode();
            var media=response.Content.Headers.ContentType?.MediaType??"";
            if(media.Length>0&&!media.Contains("html")&&!media.Contains("xml")&&!media.StartsWith("text/"))throw new FormatException("Expected a public web page.");
            await using var stream=await response.Content.ReadAsStreamAsync(cancellation);using var bytes=new MemoryStream();var buffer=new byte[8192];int read;
            while(bytes.Length<262144&&(read=await stream.ReadAsync(buffer,cancellation))>0)bytes.Write(buffer,0,Math.Min(read,262144-(int)bytes.Length));
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
