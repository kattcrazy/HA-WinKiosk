namespace HAWinKiosk.Mqtt;

/// <summary>Display on/off as set by monitor sleep/wake commands (WM_SYSCOMMAND / SC_MONITORPOWER).</summary>
public static class MonitorPowerTracker
{
    private static bool _on = true;

    public static void SetOn(bool on) => _on = on;

    public static string GetState() => _on ? "on" : "off";
}
