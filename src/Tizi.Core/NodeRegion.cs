using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tizi.Core;

public static class NodeRegion
{
    private static readonly HashSet<string> Codes = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => new RegionInfo(c.Name).TwoLetterISORegionName).Where(c => c.Length == 2).Append("TW").Append("HK").ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly (string Code, string[] Names)[] Names =
    [
        ("HK", ["香港", "Hong Kong", "Hongkong"]), ("TW", ["台湾", "台灣", "Taiwan", "Taipei"]),
        ("JP", ["日本", "东京", "東京", "大阪", "Japan", "Tokyo", "Osaka"]), ("SG", ["新加坡", "狮城", "Singapore"]),
        ("US", ["美国", "美國", "洛杉矶", "纽约", "United States", "Los Angeles", "New York", "Seattle"]),
        ("GB", ["英国", "英國", "伦敦", "United Kingdom", "London"]), ("KR", ["韩国", "韓國", "首尔", "Korea", "Seoul"]),
        ("DE", ["德国", "德國", "Germany", "Frankfurt"]), ("CA", ["加拿大", "Canada", "Toronto"]),
        ("AU", ["澳大利亚", "Australia", "Sydney"]), ("FR", ["法国", "France", "Paris"]), ("NL", ["荷兰", "Netherlands", "Amsterdam"]),
        ("CN", ["中国", "中國", "China"]), ("MO", ["澳门", "澳門", "Macau", "Macao"]), ("IN", ["印度", "India", "Mumbai"]),
        ("RU", ["俄罗斯", "Russia", "Moscow"]), ("TR", ["土耳其", "Turkey", "Türkiye"]), ("BR", ["巴西", "Brazil"]),
        ("MY", ["马来西亚", "Malaysia"]), ("TH", ["泰国", "Thailand"]), ("VN", ["越南", "Vietnam"]),
        ("PH", ["菲律宾", "Philippines"]), ("ID", ["印度尼西亚", "印尼", "Indonesia"]), ("AE", ["阿联酋", "Dubai", "UAE"]),
        ("CH", ["瑞士", "Switzerland"]), ("SE", ["瑞典", "Sweden"]), ("FI", ["芬兰", "Finland"]), ("IT", ["意大利", "Italy"]),
        ("ES", ["西班牙", "Spain"]), ("ZA", ["南非", "South Africa"]), ("NZ", ["新西兰", "New Zealand"]), ("IE", ["爱尔兰", "Ireland"]),
    ];
    public static string? Match(string name, string? explicitCode = null)
    {
        if (explicitCode is { Length: 2 } && Codes.Contains(explicitCode)) return explicitCode.ToUpperInvariant();
        var runes = name.EnumerateRunes().ToArray();
        for (var i = 0; i + 1 < runes.Length; i++)
        {
            if (runes[i].Value is >= 0x1F1E6 and <= 0x1F1FF && runes[i + 1].Value is >= 0x1F1E6 and <= 0x1F1FF)
            {
                var code = $"{(char)('A' + runes[i].Value - 0x1F1E6)}{(char)('A' + runes[i + 1].Value - 0x1F1E6)}";
                if (Codes.Contains(code)) return code;
            }
        }
        // Token boundaries avoid matching US in "Australia" or IN in "Singapore".
        foreach (Match match in Regex.Matches(name, @"(?<![A-Za-z])[A-Za-z]{2}(?![A-Za-z])"))
        { var code = match.ToString().ToUpperInvariant(); if (code == "UK") code = "GB"; if (Codes.Contains(code)) return code; }
        foreach (var entry in Names.OrderByDescending(e => e.Names.Max(n => n.Length)))
            if (entry.Names.Any(alias => alias.Any(c => c > 127) ? name.Contains(alias, StringComparison.OrdinalIgnoreCase) : Regex.IsMatch(name, @"(?<![A-Za-z])" + Regex.Escape(alias) + @"(?![A-Za-z])", RegexOptions.IgnoreCase))) return entry.Code;
        return null;
    }
    public static string StripFlag(string name) => string.Concat(name.EnumerateRunes().Where(r => r.Value is < 0x1F1E6 or > 0x1F1FF).Select(r => r.ToString())).Trim();
}
