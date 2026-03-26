using HAWinKiosk.Mqtt;

namespace HAWinKiosk.Mqtt.Commands;

/// <summary>System suspend (sleep).</summary>
public static class SystemSleepCommand
{
    public static void Execute()
    {
        NativeMethods.SetSuspendState(false, false, false);
    }
}
