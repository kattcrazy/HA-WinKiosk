using System.Net;
using System.Net.Sockets;

namespace HAWinKiosk.Mqtt.Models;

/// <summary>
/// If the kiosk URL uses a literal IP host (IPv4 or IPv6), exposes it for suggesting Wyoming / wake service host when unset.
/// </summary>
public static class KioskUrlLiteralHost
{
    public static bool TryGetFromUrl(string? url, out string host)
    {
        host = "";
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var s = url.Trim();
        if (!s.Contains("://", StringComparison.Ordinal))
            s = "http://" + s;

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
            return false;

        var h = uri.IdnHost;
        if (h.Length == 0)
            return false;

        if (!IPAddress.TryParse(h, out var addr))
            return false;

        if (addr.AddressFamily != AddressFamily.InterNetwork && addr.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        host = addr.ToString();
        return true;
    }
}
