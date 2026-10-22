using Tizi.Core;

namespace Tizi.Desktop;

internal static class IpQualityPresentation
{
    internal static IpQualityDisplay? From(IpQualityResult? result)
    {
        if(result is null)return null;
        string Type(string? value)=>value switch { "Residential"=>UiText.Get("Home broadband"),"Business"=>UiText.Get("Business network"),"Wireless"=>UiText.Get("Mobile network"),"Hosting"=>UiText.Get("Datacenter"),_=>"" };
        var unknown=UiText.Get("IP data unavailable");var stamp=result.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        var typeDetails=string.Join("\n",new[] { UiText.Get("Exit IP")+" · "+(result.Ip.Length>0 ? result.Ip : unknown) }.Concat(result.Sources.Select(s=>s.Name+" · "+(Type(s.Type) is {Length:>0} type ? type : unknown)+" · "+(s.Asn??"—")+" · "+(s.Owner??"—")+" · "+UiText.Get(s.Status))).Append(stamp));
        var countries=string.Join("\n",result.Sources.Where(s=>s.Country is not null).Select(s=>s.Name+" · "+s.Country));
        var nativeDetails=UiText.Get("Native IP is a reference based on registration and geolocation countries, not a physical-location guarantee")+"\nRIPEstat / RIR · "+(result.RegisteredCountry??unknown)+"\n"+countries+"\n"+stamp;
        return new(Type(result.Type),result.NativeReference == true ? UiText.Get("Native IP") : "",typeDetails,nativeDetails,result.NativeReference);
    }
}
