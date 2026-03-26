using System.Diagnostics;

namespace HAWinKiosk.Mqtt.Commands;

public static class ShutdownCommand
{
    public static void Execute()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = "/s /t 0",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}
