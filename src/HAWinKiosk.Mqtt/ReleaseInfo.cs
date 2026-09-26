using System.Reflection;

namespace HAWinKiosk.Mqtt;

/// <summary>
/// MQTT release info sensor text. Edit <see cref="BreakingChanges"/> before every release
/// (bump version in <c>HAWinKiosk.csproj</c> / <c>HAWinKiosk.iss</c> at the same time).
/// Version is read from the built app; only breaking changes are maintained here.
/// </summary>
public static class ReleaseInfo
{
    // UPDATE THIS BEFORE EACH RELEASE. Use "None" when there are no breaking changes.
    public const string BreakingChanges =
        "The HA MQTT camera entity is removed in favour of the MJPEG steam intergration in Home Assistant for less lag. If you were using it, the camera strea setting will be set to Off when updated. Set it to MJPEG stream in Settings and add a Home Assistant MJPEG camera pointing at http://<kiosk-ip>:<port>/stream.mjpg to get your cam back up and running!";

    public static bool HasBreakingChanges =>
        !string.IsNullOrWhiteSpace(BreakingChanges)
        && !BreakingChanges.Equals("None", StringComparison.OrdinalIgnoreCase);

    public static string GetVersionLabel()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown";
    }

    public static string GetSensorValue()
    {
        return $"Version: {GetVersionLabel()} - Breaking Changes: {BreakingChanges}";
    }
}
