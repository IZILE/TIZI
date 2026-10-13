using System.Globalization;

namespace Tizi.Core;

public static class SubscriptionMetadata
{
    public static SubscriptionUsage? Parse(string? header)
    {
        if (string.IsNullOrWhiteSpace(header) || header.Length > 4096) return null;
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in header.Split(';'))
        {
            var pair = field.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && decimal.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= 0 && number <= long.MaxValue)
                values[pair[0]] = (long)number;
        }
        if (!values.Keys.Any(k => k.ToLowerInvariant() is "upload" or "download" or "total" or "expire")) return null;
        DateTimeOffset? expiry = null;
        if (values.TryGetValue("expire", out var unix) && unix > 0)
            try { expiry = DateTimeOffset.FromUnixTimeSeconds(unix); } catch (ArgumentOutOfRangeException) { }
        long? Get(string name) => values.TryGetValue(name, out var value) ? value : null;
        // Missing, unlimited and a real timestamp are three different states.
        return new(Get("upload"), Get("download"), Get("total"), expiry, values.GetValueOrDefault("expire", -1) == 0);
    }
}
