using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tizi.Core;

public static class ProviderDirectory
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static ProviderPlan? LowestPlan(ProviderSite site, string currency = "CNY") => (site.Profile?.Plans ?? [])
        .OrderBy(p => PriceCategory(p, currency)).ThenBy(p => p.Currency, StringComparer.OrdinalIgnoreCase)
        .ThenBy(p => ProviderDiscovery.MonthlyEquivalent(p) ?? p.Price ?? decimal.MaxValue).FirstOrDefault();
    public static List<ProviderSite> OrderByLowestPrice(IEnumerable<ProviderSite> sites, string currency = "CNY") => sites
        .Select(site => (Site: site, Plan: LowestPlan(site, currency)))
        .OrderBy(p => PriceCategory(p.Plan, currency)).ThenBy(p => p.Plan?.Currency, StringComparer.OrdinalIgnoreCase)
        .ThenBy(p => p.Plan is { } plan ? ProviderDiscovery.MonthlyEquivalent(plan) ?? plan.Price ?? decimal.MaxValue : decimal.MaxValue)
        .Select(p => p.Site).ToList();
    private static int PriceCategory(ProviderPlan? plan, string currency)
    {
        if (plan?.Price is null) return 5;
        var preferred = plan.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase);
        if (ProviderDiscovery.MonthlyEquivalent(plan) is not null) return preferred ? 0 : 2;
        if (plan.PriceUnit == "period" && plan.Period == "once") return preferred ? 1 : 3;
        return 4;
    }
    public static List<ProviderSite> Import(string json, string source, IReadOnlyList<ProviderSite> previous)
    {
        if (Encoding.UTF8.GetByteCount(json) > 1024 * 1024) throw new FormatException("Directory is too large (1 MB maximum).");
        var basic = SourceUpdates.ParseCatalog(json, source, previous); var root = JsonNode.Parse(json)!;
        var entries = ((JsonArray)root["providers"]!).OfType<JsonObject>().GroupBy(p => SourceUpdates.Normalize(p["url"]!.ToString())).ToDictionary(g => g.Key, g => g.First());
        return basic.Select(site =>
        {
            if (entries[SourceUpdates.Normalize(site.Url)]["profile"] is not JsonObject profileJson) return site;
            var profile = profileJson.Deserialize<ProviderProfile>(JsonOptions) ?? throw new FormatException("Invalid provider profile.");
            Bounded(profile.Description, 900); Bounded(profile.SecurityNotes, 1200); Bounded(profile.IpQualityNotes, 1200);
            if (profile.Plans is null || profile.Sources is null || profile.Regions is null || profile.SupportedProtocols is null || profile.SubscriptionSource is null || profile.Plans.Any(p => p is null) || profile.Plans.Count > 12 || profile.Sources.Count > 12 || profile.Regions.Count > 12 || profile.SupportedProtocols.Count > 100) throw new FormatException("Invalid provider profile.");
            foreach (var region in profile.Regions) Bounded(region, 40);
            Bounded(profile.ReviewSummary, 1200);
            if (profile.Reviews is null || profile.Reviews.Count > 8 || profile.Reviews.Any(r => r is null)) throw new FormatException("Invalid provider reviews.");
            foreach (var review in profile.Reviews) { Bounded(review.Title, 160); Bounded(review.Summary, 900); PublicUrl(review.Url); if (review.Rank is < 1 or > 999 || review.Date is { } date && !DateOnly.TryParseExact(date, "yyyy-MM-dd", out _)) throw new FormatException("Invalid provider review."); }
            foreach (var url in profile.Sources) PublicUrl(url);
            if (profile.SubscriptionSource.Length > 0) PublicUrl(profile.SubscriptionSource);
            foreach (var plan in profile.Plans)
            {
                Bounded(plan.Name, 120); Bounded(plan.Currency, 12); Bounded(plan.Conditions, 600); Bounded(plan.PriceQuote, 160); PublicUrl(plan.Source);
                if (plan.Price is < 0 or > 100000000 || plan.TrafficGb is < 0 or > 100000000 || plan.Period is not ("month" or "quarter" or "year" or "once" or "unknown") || plan.PriceUnit is not ("period" or "gb" or "ip" or "unknown")) throw new FormatException("Invalid provider plan.");
            }
            return site with { Profile = profile with { Verification = "Unverified", SupportedProtocols = profile.SupportedProtocols.Where(ProviderSearchModes.Protocols.Contains).Distinct().ToList() } };
        }).ToList();
    }
    public static string Export(IEnumerable<ProviderSite> sites) => JsonSerializer.Serialize(new { version = 1, providers = sites.Select(s => new { s.Name, s.Url, s.Region, s.Profile }) }, JsonOptions);
    public static List<ProviderSite> Merge(IReadOnlyList<ProviderSite> previous, IEnumerable<ProviderSite> incoming)
    {
        var result = previous.ToList();
        foreach (var site in incoming)
        {
            var index = result.FindIndex(p => SourceUpdates.Normalize(p.Url).Equals(SourceUpdates.Normalize(site.Url), StringComparison.OrdinalIgnoreCase));
            if (index < 0) result.Add(site);
            else result[index] = site with { Id = result[index].Id, CheckedAt = result[index].CheckedAt, StatusCode = result[index].StatusCode, LatencyMs = result[index].LatencyMs };
        }
        if (result.Count > 500) throw new FormatException("Maximum 500 websites."); return result;
    }
    public static AppState CacheSearch(AppState state, List<ProviderSite> found, ProviderSearchOptions criteria, DateTimeOffset time)
    {
        if (found.Count == 0) throw new FormatException("Search returned no verified providers. Previous results kept.");
        var old = state.Providers.GroupBy(p => SourceUpdates.Normalize(p.Url), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var results = OrderByLowestPrice(found.GroupBy(p => SourceUpdates.Normalize(p.Url), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Select(site => old.TryGetValue(SourceUpdates.Normalize(site.Url), out var saved) ? site with { Id = saved.Id, CheckedAt = saved.CheckedAt, StatusCode = saved.StatusCode, LatencyMs = saved.LatencyMs } : site), criteria.Currency);
        var keys = results.Select(p => SourceUpdates.Normalize(p.Url)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Newest search first; keep prior discoveries, evict oldest entries after 100.
        var visible = results.Concat(state.Providers.Where(p => !keys.Contains(SourceUpdates.Normalize(p.Url)))).GroupBy(p => SourceUpdates.Normalize(p.Url), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(100).ToList();
        return state with { Providers = visible, LastProviderSearch = new(time, criteria, results), ProvidersSearchedAt = time, ProviderSearchError = null };
    }
    private static void Bounded(string value, int maximum) { if (value is null || value.Length > maximum || value.Any(c => char.IsControl(c) && c is not '\n' and not '\t')) throw new FormatException("Invalid provider information."); }
    private static void PublicUrl(string value) { var uri = SourceUpdates.WebUri(value); if (uri.Scheme != "https" || uri.IsLoopback || System.Net.IPAddress.TryParse(uri.Host, out _)) throw new FormatException("Expected a public HTTPS website."); }
}
