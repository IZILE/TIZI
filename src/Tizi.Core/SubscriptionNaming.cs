using System.Text.RegularExpressions;

namespace Tizi.Core;

public static class SubscriptionNaming
{
    public static string? Clean(string? name)
    {
        name = name?.Trim();
        return name is { Length: > 0 and <= 200 } && !name.Any(char.IsControl) ? name : null;
    }
    public static bool IsPlaceholder(Subscription subscription) => string.IsNullOrWhiteSpace(subscription.Name) ||
        Uri.TryCreate(subscription.Url, UriKind.Absolute, out var uri) && subscription.Name.Equals(uri.Host, StringComparison.OrdinalIgnoreCase);

    internal static string? ResponseTitle(string? profileTitle, string text, string? filename, string? website)
    {
        if (profileTitle?.StartsWith("base64:", StringComparison.OrdinalIgnoreCase) == true)
            try { profileTitle = NodeImport.DecodeBase64(profileTitle[7..]); } catch (FormatException) { profileTitle = null; }
        if (Clean(profileTitle) is { } explicitTitle) return explicitTitle;
        if (PreambleTitle(text) is { } remarks) return remarks;
        var brand = WebsiteName(website);
        if (filename is not null)
        {
            filename = filename.Trim('"');
            var stem = Path.GetFileNameWithoutExtension(filename.Replace('\\', '/'));
            // Version/format suffixes in download filenames are not part of the provider's name.
            if (brand is not null && Regex.Replace(stem, @"[_-]v\d+$", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)).Equals(brand, StringComparison.OrdinalIgnoreCase))
                stem = brand;
            if (Clean(stem) is { } title && title.ToLowerInvariant() is not ("node" or "nodes" or "config" or "clash" or "subscription" or "sub" or "download" or "proxy" or "proxies")) return title;
        }
        return brand;
    }
    private static string? PreambleTitle(string text)
    {
        text = text.Trim('\uFEFF', ' ', '\t', '\r', '\n');
        if (!text.Contains("://") && !text.StartsWith('{') && !text.StartsWith('[') && !text.Contains(':'))
            try { text = NodeImport.DecodeBase64(text); } catch (FormatException) { return null; }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("REMARKS=", StringComparison.OrdinalIgnoreCase)) return Clean(line[8..]);
            if (!line.StartsWith("STATUS=", StringComparison.OrdinalIgnoreCase)) break;
        }
        return null;
    }
    private static string? WebsiteName(string? website)
    {
        if (!Uri.TryCreate(website, UriKind.Absolute, out var uri) || uri.IsLoopback || System.Net.IPAddress.TryParse(uri.Host, out _)) return null;
        var labels = uri.IdnHost.Split('.');
        if (labels.Length < 2) return null;
        var index = labels.Length - 2;
        if (labels.Length >= 3 && labels[^1].Length == 2 && labels[index] is "co" or "com" or "net" or "org") index--;
        return Clean(labels[index].ToUpperInvariant());
    }
}
