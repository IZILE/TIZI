using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Tizi.Core;

public static class NetworkEnvironment
{
    private static bool HasGateway(NetworkInterface adapter) => adapter.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
    public static string[] OutboundInterfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211) && HasGateway(n))
        .Select(n => n.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    public static AppState ConnectionSettings(AppState state)
    {
        var choices = OutboundInterfaces();
        if (state.OutboundInterface.Length > 0)
        {
            if (!choices.Contains(state.OutboundInterface, StringComparer.Ordinal)) throw new IOException("The selected outbound interface is unavailable. Choose an active Ethernet or Wi-Fi interface in Settings.");
            return state;
        }
        // A single physical gateway is unambiguous. Never select another VPN's
        // default route as the upstream for this client.
        return choices.Length == 1 ? state with { OutboundInterface = choices[0] } : state;
    }
    public static string[] OtherTunnels() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.Name != "TIZI" && n.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Loopback) && HasGateway(n))
        .Select(n => n.Name).Distinct(StringComparer.Ordinal).ToArray();
}
