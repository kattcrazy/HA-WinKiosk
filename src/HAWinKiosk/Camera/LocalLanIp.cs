using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HAWinKiosk.Camera;

public static class LocalLanIp
{
    /// <summary>Best-effort private LAN IPv4 for the MJPEG stream URL preview.</summary>
    public static string Detect()
    {
        try
        {
            var candidates = new List<(IPAddress Address, bool HasGateway, bool IsWifiOrEthernet, int Score)>();

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                var props = ni.GetIPProperties();
                var hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address is { } ga
                    && ga.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(ga)
                    && !ga.Equals(IPAddress.Any));

                var isLanNic = ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                    or NetworkInterfaceType.Wireless80211
                    or NetworkInterfaceType.GigabitEthernet;

                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    if (IPAddress.IsLoopback(addr.Address))
                        continue;
                    if (IsLinkLocal(addr.Address))
                        continue;

                    var score = 0;
                    if (IsPrivateRfc1918(addr.Address)) score += 100;
                    if (hasGateway) score += 50;
                    if (isLanNic) score += 20;
                    // Prefer typical home LAN over CGNAT/VPN-ish ranges when both are private.
                    if (IsTypicalHomeLan(addr.Address)) score += 10;

                    candidates.Add((addr.Address, hasGateway, isLanNic, score));
                }
            }

            var best = candidates
                .Where(c => IsPrivateRfc1918(c.Address))
                .OrderByDescending(c => c.Score)
                .Select(c => c.Address.ToString())
                .FirstOrDefault();
            if (best != null)
                return best;
        }
        catch
        {
            // fall through
        }

        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 80);
            if (socket.LocalEndPoint is IPEndPoint ep
                && !IPAddress.IsLoopback(ep.Address)
                && !IsLinkLocal(ep.Address)
                && IsPrivateRfc1918(ep.Address))
            {
                return ep.Address.ToString();
            }
        }
        catch
        {
            // ignore
        }

        return "127.0.0.1";
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b.Length == 4 && b[0] == 169 && b[1] == 254;
    }

    private static bool IsPrivateRfc1918(IPAddress address)
    {
        var b = address.GetAddressBytes();
        if (b.Length != 4) return false;
        if (b[0] == 10) return true;
        if (b[0] == 192 && b[1] == 168) return true;
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
        return false;
    }

    private static bool IsTypicalHomeLan(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b.Length == 4 && b[0] == 192 && b[1] == 168;
    }
}
