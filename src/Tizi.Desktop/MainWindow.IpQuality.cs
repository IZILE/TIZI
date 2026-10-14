using Avalonia.Controls;

namespace Tizi.Desktop;

internal sealed partial class MainWindow
{
    private void ConfigureIpQuality()
    {
        var settings=_app.State.IpQuality;
        Dialog("IP detection services",[new("proxycheck","Proxycheck API key (optional)",settings.ProxyCheckKey,Secret:true),new("ipapi","ipapi.is API key (optional)",settings.IpApiKey,Secret:true)],async values=>
        {
            var proxy=values["proxycheck"].Trim();var ipapi=values["ipapi"].Trim();
            if(new[] { proxy,ipapi }.Any(key=>key.Length>512 || key.Any(char.IsControl)))throw new FormatException(UiText.Get("Invalid API key"));
            await _app.ApplyAsync(_app.State with { IpQuality=_app.State.IpQuality with { ProxyCheckKey=proxy,IpApiKey=ipapi } });
        });
    }
}
