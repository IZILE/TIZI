using System.Globalization;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class IpQualityPresentation
{
    internal static IpQualityDisplay? From(IpQualityResult? result)
    {
        if(result is null)return null;
        string Type(string? value)=>value is null ? UiText.Get("Unknown") : UiText.Get(value switch { "Residential"=>"Home broadband","Business"=>"Business network","Wireless"=>"Mobile network","Hosting"=>"Datacenter",_=>"Unknown" });
        var unknown=UiText.Get("Unknown");var stamp=result.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        var typeDetails=string.Join("\n",new[] { UiText.Get("Exit IP")+" · "+(result.Ip.Length>0 ? result.Ip : unknown) }.Concat(result.Sources.Select(s=>s.Name+" · "+Type(s.Type)+" · "+(s.Asn??"—")+" · "+(s.Owner??"—")+" · "+UiText.Get(s.Status))).Append(stamp));
        var raw=result.Sources.Where(s=>s.Risk is not null).Select(s=>s.Name+" · "+s.Risk!.Value.ToString("0.#",CultureInfo.InvariantCulture)+" / 100").ToList();
        var score=result.Median?.ToString("0.#",CultureInfo.InvariantCulture)+"/100";
        var scoreDetails=string.Join("\n",raw.Append(UiText.Format("Median of {0} available sources",result.Scores.Length)).Append(UiText.Get(result.ScoreConflict ? "Sources disagree substantially" : "Risk scores are references, not a guarantee of access")).Append(stamp));
        var countries=string.Join("\n",result.Sources.Where(s=>s.Country is not null).Select(s=>s.Name+" · "+s.Country));
        var nativeDetails=UiText.Get("Native IP is a reference based on registration and geolocation countries, not a physical-location guarantee")+"\nRIPEstat / RIR · "+(result.RegisteredCountry??unknown)+"\n"+countries+"\n"+stamp;
        return new(Type(result.Type),result.Median is null ? "—" : score,result.NativeReference is { } native ? UiText.Get(native ? "Native IP" : "Registration differs") : UiText.Get("Undetermined"),typeDetails,scoreDetails,nativeDetails,result.Median is { } risk ? (int)Math.Ceiling(risk) : null,result.NativeReference);
    }
}
