using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tizi.Core;

public sealed record AppRelease(Version Version, string Tag, string Notes, Uri Page, Uri Installer, Uri Checksums);

public sealed class ReleaseUpdates(HttpClient http)
{
    public const string Repository = "IZILE/TIZI";
    public const string LatestUrl = "https://api.github.com/repos/IZILE/TIZI/releases/latest";
    private const long MaximumBytes = 250 * 1024 * 1024;
    public async Task<AppRelease> LatestAsync(CancellationToken cancellation = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.UserAgent.ParseAdd("TIZI-updater/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await ReadBoundedAsync(response.Content, 1024 * 1024, cancellation)) as JsonObject ?? throw new FormatException("Invalid release metadata.");
        var tag = json["tag_name"]?.ToString() ?? "";
        if (json["draft"]?.GetValue<bool>() == true || json["prerelease"]?.GetValue<bool>() == true
            || !Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+(?:\.\d+)?$") || !Version.TryParse(tag.TrimStart('v'), out var version)) throw new FormatException("Invalid release version.");
        Uri Asset(string name)
        {
            var assets = (json["assets"] as JsonArray ?? []).Where(a => a?["name"]?.ToString() == name).ToArray();
            if (assets.Length != 1 || !Uri.TryCreate(assets[0]?["browser_download_url"]?.ToString(), UriKind.Absolute, out var url)
                || url.Scheme != "https" || url.Host != "github.com" || !url.AbsolutePath.StartsWith("/" + Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/", StringComparison.Ordinal)
                || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0) throw new FormatException("Invalid release asset.");
            return url;
        }
        return new(version, tag, json["body"]?.ToString() ?? "", new Uri("https://github.com/" + Repository + "/releases/tag/" + Uri.EscapeDataString(tag)), Asset("TIZI-Setup.exe"), Asset("SHA256SUMS.txt"));
    }
    public async Task<string> DownloadAsync(AppRelease release, string directory, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        using var sums = await http.GetAsync(release.Checksums, HttpCompletionOption.ResponseHeadersRead, cancellation);
        sums.EnsureSuccessStatusCode();
        var matches = Regex.Matches(await ReadBoundedAsync(sums.Content, 65536, cancellation), @"(?im)^([a-f0-9]{64})[ \t]+\*?TIZI-Setup\.exe[ \t]*\r?$");
        if (matches.Count != 1) throw new FormatException("No unique installer checksum.");
        var expected = matches[0].Groups[1].Value;
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "TIZI-Setup.exe"); var partial = target + ".partial";
        try
        {
            using var response = await http.GetAsync(release.Installer, HttpCompletionOption.ResponseHeadersRead, cancellation); response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            if (length is > MaximumBytes) throw new InvalidDataException("Installer is too large.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellation);
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
                { total += read; if (total > MaximumBytes) throw new InvalidDataException("Installer is too large."); await output.WriteAsync(buffer.AsMemory(0, read), cancellation); progress?.Report(length is > 0 ? total * 100d / length.Value : 0); }
            }
            var data = await File.ReadAllBytesAsync(partial, cancellation);
            if (data.Length < 2 || data[0] != 'M' || data[1] != 'Z' || !Convert.ToHexString(SHA256.HashData(data)).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installer checksum verification failed.");
            File.Move(partial, target, false); return target;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    private static async Task<string> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken cancellation)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellation); using var data = new MemoryStream(); var buffer = new byte[16384]; int read;
        while ((read = await stream.ReadAsync(buffer, cancellation)) > 0) { if (data.Length + read > maximum) throw new InvalidDataException("Response is too large."); data.Write(buffer, 0, read); }
        return System.Text.Encoding.UTF8.GetString(data.ToArray());
    }
}
