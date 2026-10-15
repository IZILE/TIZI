using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tizi.Core;

public sealed class SourceUpdates(HttpClient http)
{
    private static readonly Regex StatusEntry = new(@"^(?:(?:剩余流量|已用流量|总流量|Remaining\s+Traffic|Traffic\s+Remaining)\s*[:：]\s*[\d.,\s%]+(?:[KMGT]i?B)?(?:\s+[\d.,]+\s*[KMGT]i?B)?|(?:过期时间|到期时间|套餐到期|Expires?(?:\s+at)?|Expiry(?:\s+Date)?)\s*[:：]\s*\d{4}[-/]\d{1,2}[-/]\d{1,2}(?:\s+\d{1,2}:\d{2}(?::\d{2})?)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    public sealed record Download(string Text, SubscriptionUsage? Usage, string? Title, string? Website);
    public sealed record SubscriptionRefresh(List<ProxyNode> Nodes, Subscription Subscription, List<RoutingRule> Rules, List<string> Warnings);
    public static Uri WebUri(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0 || uri.Host.Length == 0) throw new FormatException("Enter a valid HTTP or HTTPS address without a fragment or embedded login.");
        return uri;
    }
    public async Task<string> DownloadAsync(string url, int maximum, CancellationToken cancellation = default)
        => (await DownloadDetailsAsync(url, maximum, cancellation).ConfigureAwait(false)).Text;
    public async Task<Download> DownloadDetailsAsync(string url, int maximum, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var request = new HttpRequestMessage(HttpMethod.Get, WebUri(url)); request.Headers.UserAgent.ParseAdd("TIZI/0.1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Download is too large.");
        using var bytes = new MemoryStream();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var buffer = new byte[16384]; int count;
        while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
        { if (bytes.Length + count > maximum) throw new InvalidDataException("Download is too large."); bytes.Write(buffer, 0, count); }
        string? Header(string name) => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
        var website = Header("profile-web-page-url");
        if (website is not null) try { website = WebUri(website).AbsoluteUri; } catch (FormatException) { website = null; }
        var text = new UTF8Encoding(false, true).GetString(bytes.ToArray());
        var disposition = response.Content.Headers.ContentDisposition;
        var title = SubscriptionNaming.ResponseTitle(Header("profile-title"), text, disposition?.FileNameStar ?? disposition?.FileName, website);
        return new(text, SubscriptionMetadata.Parse(Header("subscription-userinfo")), title, website);
    }
    public async Task<List<ProxyNode>> SubscriptionAsync(Subscription subscription, IReadOnlyList<ProxyNode> previous, CancellationToken cancellation = default)
        => (await RefreshSubscriptionAsync(subscription, previous, cancellation).ConfigureAwait(false)).Nodes;
    public async Task<SubscriptionRefresh> RefreshSubscriptionAsync(Subscription subscription, IReadOnlyList<ProxyNode> previous, CancellationToken cancellation = default)
    {
        var downloaded = await DownloadDetailsAsync(subscription.Url, NodeImport.MaxBytes, cancellation).ConfigureAwait(false);
        var document = ImportDocument.Parse(downloaded.Text, subscription.Id);
        // Clash feeds commonly include routing rules and groups alongside nodes.
        // Keep convertible rules for an explicit import; background refresh only replaces nodes.
        // A partial node/rule parse still rejects the update so a working selection is retained.
        if (document.Subscriptions.Count > 0 || document.Nodes.Count == 0 || document.Warnings.Any(w => w is not ("Proxy groups use their default action; proxy traffic follows the selected TIZI node." or "Custom proxy groups require manual conversion." or "Remote providers and scripts were not imported."))) throw new FormatException("Subscription contains unsupported settings. Import the file manually to review it.");
        // Some feeds encode quota/expiry notices as dummy proxy entries. Their
        // numeric status labels belong in subscription details, not the node list.
        var imported = document.Nodes.Where(n => !StatusEntry.IsMatch(n.Name)).ToList();
        if (imported.Count == 0) throw new FormatException("Subscription contains no connection nodes.");
        var prior = previous.Where(n => n.SubscriptionId == subscription.Id).ToList();
        var old = prior.GroupBy(Fingerprint).ToDictionary(g => g.Key, g => new Queue<ProxyNode>(g));
        static string Label(ProxyNode n) => n.Name + "\n" + n.Type;
        var uniqueOld = prior.GroupBy(Label).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var uniqueNew = imported.GroupBy(Label).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();
        var used = new HashSet<Guid>();
        var nodes = imported.Select(node =>
        {
            ProxyNode? match = null;
            if (old.TryGetValue(Fingerprint(node), out var queue))
                while (queue.TryDequeue(out var exact)) if (!used.Contains(exact.Id)) { match = exact; break; }
            // Providers can rotate credentials/hostnames on each download. A
            // unique label and protocol still denote the same selectable node.
            if (match is null && uniqueNew.Contains(Label(node)) && uniqueOld.TryGetValue(Label(node), out var logical) && !used.Contains(logical.Id)) match = logical;
            if (match is null) return node;
            used.Add(match.Id);
            return node with { Id = match.Id, CountryCode = match.CountryCode ?? node.CountryCode };
        }).ToList();
        var name = downloaded.Title is { Length: > 0 } title && SubscriptionNaming.IsPlaceholder(subscription) ? title : subscription.Name;
        return new(nodes, subscription with { Name = name, UpdatedAt = DateTimeOffset.Now, LastError = null, Usage = downloaded.Usage, Website = downloaded.Website }, document.Rules, document.Warnings);
    }
    private static string Fingerprint(ProxyNode node) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.Name + "\n" + node.Options.ToJsonString())));
    public async Task<List<ProviderSite>> CatalogAsync(string url, IReadOnlyList<ProviderSite> previous, CancellationToken cancellation = default)
    {
        var text = await DownloadAsync(url, 1024 * 1024, cancellation).ConfigureAwait(false);
        return ParseCatalog(text, url, previous);
    }
    public static List<ProviderSite> ParseCatalog(string json, string source, IReadOnlyList<ProviderSite> previous)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || version.GetInt32() != 1 || !root.TryGetProperty("providers", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 500) throw new FormatException("Expected a TIZI directory (version 1, maximum 500 websites).");
        var old = previous.GroupBy(p => Normalize(p.Url)).ToDictionary(g => g.Key, g => g.First());
        var result = new List<ProviderSite>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in entries.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()?.Trim() ?? "";
            var uri = WebUri(item.GetProperty("url").GetString() ?? "");
            if (uri.Scheme != "https" || uri.IsLoopback || (System.Net.IPAddress.TryParse(uri.Host, out _))) throw new FormatException("A remote directory must contain HTTPS website names, not local or IP addresses.");
            if (name.Length is 0 or > 120 || name.Any(char.IsControl)) throw new FormatException("Invalid website name.");
            var region = item.TryGetProperty("region", out var r) ? r.GetString() ?? "" : "";
            if (region.Length > 120) throw new FormatException("Region is too long.");
            var key = Normalize(uri.AbsoluteUri); if (!seen.Add(key)) continue;
            result.Add(old.TryGetValue(key, out var saved) ? saved with { Name = name, Region = region, Source = source } : new ProviderSite { Name = name, Url = uri.AbsoluteUri, Region = region, Source = source });
        }
        return result;
    }
    public async Task<ProviderSite> CheckAsync(ProviderSite site, CancellationToken cancellation = default)
    {
        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, WebUri(site.Url)); request.Headers.UserAgent.ParseAdd("TIZI/0.1.0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            // A 403 is an observed site response, not proof that a provider or its nodes are unavailable.
            return site with { CheckedAt = DateTimeOffset.Now, StatusCode = (int)response.StatusCode, LatencyMs = (int)watch.ElapsedMilliseconds, Error = null };
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return site with { CheckedAt = DateTimeOffset.Now, StatusCode = null, LatencyMs = null, Error = "Timeout" }; }
        catch (HttpRequestException) { return site with { CheckedAt = DateTimeOffset.Now, StatusCode = null, LatencyMs = null, Error = "Connection failed" }; }
    }
    public static string Normalize(string url) => WebUri(url).GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped).TrimEnd('/');
}
