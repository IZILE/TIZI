using System.Globalization;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class ExitCountryPresentation
{
    internal static string Text(IpQualityResult? result, bool connected, bool checking)
    {
        if (!connected) return UiText.Get("Exit: disconnected");
        if (result?.ExitCountry is not { } code) return UiText.Get(checking && result is null ? "Exit: checking…" : "Exit: unknown");
        string country;
        try
        {
            var region = new RegionInfo(code);
            country = UiText.Language == "en" ? region.EnglishName : region.DisplayName;
        }
        catch (ArgumentException) { return UiText.Get("Exit: unknown"); }
        return UiText.Format("Exit: {0}", country);
    }
    internal static string Details(IpQualityResult? result, bool connected) => !connected || result?.Ip is not { Length: > 0 } ip
        ? UiText.Get("Country is determined from the observed exit IP, not the node name.")
        : UiText.Get("Exit IP") + ": " + ip + "\n" + string.Join("\n", result.Sources.Where(s => s.Country is not null).Select(s => s.Name + " · " + s.Country)) + "\n" + result.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
