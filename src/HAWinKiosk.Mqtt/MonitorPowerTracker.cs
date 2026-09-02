namespace HAWinKiosk.Mqtt;

/// <summary>
/// Display on/off for the MQTT monitor_on sensor. Updated from Windows session display power
/// notifications and from monitor sleep/wake MQTT commands.
/// </summary>
public static class MonitorPowerTracker
{
    private static bool _on = true;

    public static void SetOn(bool on) => _on = on;

    public static string GetState() => _on ? "on" : "off";
}
