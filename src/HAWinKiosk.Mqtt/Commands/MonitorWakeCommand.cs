using HAWinKiosk.Mqtt;

namespace HAWinKiosk.Mqtt.Commands;

/// <summary>
/// Wakes the monitor by simulating a Shift key press (no visible key).
/// Adapted from HASS.Agent MonitorWakeCommand.
/// </summary>
public static class MonitorWakeCommand
{
    public static void Execute()
    {
        NativeMethods.keybd_event(NativeMethods.VK_Shift, 0, 0, IntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_Shift, 0, NativeMethods.KEYEVENTF_KEYUP, IntPtr.Zero);
        MonitorPowerTracker.SetOn(true);
    }
}
