using Microsoft.Win32;
using System.Diagnostics;
using System.Text;

namespace HAWinKiosk.Mqtt;

public static class AutoStartManager
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HA-WinKiosk";
    private const string TaskName = "HA-WinKiosk-AutoStart";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
                var value = key?.GetValue(ValueName) as string;
                return !string.IsNullOrEmpty(value) || TaskExists();
            }
            catch
            {
                return false;
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true) ?? Registry.CurrentUser.CreateSubKey(KeyPath, true);
            if (key == null) return;
            var exePath = Environment.ProcessPath ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') + "\\HAWinKiosk.exe";

            if (enabled)
            {
                EnsureScheduledTask(exePath);
                key.SetValue(ValueName, BuildFallbackRunValue(exePath));
            }
            else
            {
                DeleteScheduledTask();
                key.DeleteValue(ValueName, false);
            }
        }
        catch
        {
            // Caller can handle/log
        }
    }

    private static bool TaskExists()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Query /TN \"{TaskName}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p == null) return false;
        p.WaitForExit(3000);
        return p.ExitCode == 0;
    }

    private static void EnsureScheduledTask(string exePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Create /TN \"{TaskName}\" /SC ONLOGON /TR \"\\\"{exePath}\\\"\" /RL LIMITED /IT /F",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(5000);
    }

    private static void DeleteScheduledTask()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Delete /TN \"{TaskName}\" /F",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(3000);
    }

    private static string BuildFallbackRunValue(string exePath)
    {
        var script = $"$p = '{exePath.Replace("'", "''")}'; Start-Sleep -Seconds 45; try {{ Get-Process -Name 'HAWinKiosk' -ErrorAction Stop | Out-Null }} catch {{ Start-Process -FilePath $p | Out-Null }}";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return $"powershell.exe -WindowStyle Hidden -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";
    }
}
