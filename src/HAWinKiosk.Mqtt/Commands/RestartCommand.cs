using System.Diagnostics;

namespace HAWinKiosk.Mqtt.Commands;

public static class RestartCommand
{
    public static void Execute()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = "/r /t 0",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}
