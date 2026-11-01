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
    public const string BreakingChanges = "None";

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
