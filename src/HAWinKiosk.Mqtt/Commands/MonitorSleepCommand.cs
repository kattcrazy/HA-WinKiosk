using HAWinKiosk.Mqtt;

namespace HAWinKiosk.Mqtt.Commands;

/// <summary>
/// Puts all monitors to sleep via WM_SYSCOMMAND/SC_MONITORPOWER.
/// Adapted from HASS.Agent MonitorSleepCommand.
/// </summary>
public static class MonitorSleepCommand
{
    public static void Execute()
    {
        NativeMethods.PostMessage(
            NativeMethods.HWND_BROADCAST,
            NativeMethods.WM_SYSCOMMAND,
            (IntPtr)NativeMethods.SC_MONITORPOWER,
            (IntPtr)2);
        MonitorPowerTracker.SetOn(false);
    }
}
