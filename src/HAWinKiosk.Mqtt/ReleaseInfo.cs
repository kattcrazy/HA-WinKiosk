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
        "Monitor Sleep and Wake commands have been migrated to a single toggle.";

    public static string GetSensorValue()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        var label = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown";
        return $"Version: {label} - Breaking Changes: {BreakingChanges}";
    }
}
