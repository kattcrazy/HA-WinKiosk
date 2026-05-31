using Microsoft.Win32;
using System.Diagnostics;

namespace HAWinKiosk.Mqtt;

public static class AutoStartManager
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HA-WinKiosk";
    private const string LegacyTaskName = "HA-WinKiosk-AutoStart";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                return CheckLaunchOnUserLogin() || HasLegacyAutostart();
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
            if (enabled)
            {
                DeleteLegacyScheduledTask();
                EnableLaunchOnUserLogin();
            }
            else
            {
                DisableLaunchOnUserLogin();
                DeleteLegacyScheduledTask();
            }
        }
        catch
        {
            // Caller can handle/log
        }
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') + "\\HAWinKiosk.exe";
    }

    private static bool CheckLaunchOnUserLogin()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
        var value = key?.GetValue(ValueName) as string;
        if (string.IsNullOrWhiteSpace(value)) return false;

        return value.Contains(GetExecutablePath(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasLegacyAutostart()
    {
        return LegacyTaskExists() || HasLegacyRunEntry();
    }

    private static bool HasLegacyRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
        var value = key?.GetValue(ValueName) as string;
        return !string.IsNullOrEmpty(value)
               && value.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnableLaunchOnUserLogin()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true)
                        ?? Registry.CurrentUser.CreateSubKey(KeyPath, true);
        if (key == null) return;

        key.SetValue(ValueName, $"\"{GetExecutablePath()}\"", RegistryValueKind.String);
    }

    private static void DisableLaunchOnUserLogin()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
        key?.DeleteValue(ValueName, false);
    }

    private static bool LegacyTaskExists()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Query /TN \"{LegacyTaskName}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p == null) return false;
        p.WaitForExit(3000);
        return p.ExitCode == 0;
    }

    private static void DeleteLegacyScheduledTask()
    {
        if (!LegacyTaskExists()) return;

        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Delete /TN \"{LegacyTaskName}\" /F",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(3000);
    }
}
