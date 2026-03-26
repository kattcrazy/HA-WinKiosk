using System.Diagnostics;

namespace HAWinKiosk.Mqtt.Commands;

/// <summary>
/// Best-effort Windows Update run using WUA COM via PowerShell.
/// If the install result requires reboot, schedules a restart.
/// </summary>
public static class WindowsUpdateCommand
{
    public static void Execute()
    {
        const string script = """
$ErrorActionPreference = 'Stop'
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$result = $searcher.Search('IsInstalled=0 and IsHidden=0')
if ($result.Updates.Count -le 0) { return }

$downloader = $session.CreateUpdateDownloader()
$downloader.Updates = $result.Updates
[void]$downloader.Download()

$installer = $session.CreateUpdateInstaller()
$installer.Updates = $result.Updates
$installResult = $installer.Install()

if ($installResult.RebootRequired) {
  Start-Process -FilePath 'shutdown.exe' -ArgumentList '/r /t 30 /c "Restarting to complete Windows updates (HA WinKiosk command)."' -WindowStyle Hidden
}
""";

        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
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
