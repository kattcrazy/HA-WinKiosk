using System.Diagnostics;

namespace HAWinKiosk.Mqtt.Commands;

/// <summary>
/// Executes a configured PowerShell command string.
/// Intended for trusted MQTT environments only.
/// </summary>
public static class PowerShellCommand
{
    public static void Execute(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process.Start(psi);
    }
}
