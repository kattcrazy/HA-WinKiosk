using System.Diagnostics;

namespace HAWinKiosk.Mqtt.Commands;

/// <summary>
/// Best-effort Windows Update run using WUA COM via PowerShell.
/// If the install result requires reboot, schedules a restart.
/// </summary>
public static class WindowsUpdateCommand
{
    public static void Execute(bool respectActiveHours)
    {
        var respectLiteral = respectActiveHours ? "$true" : "$false";
        var script = $$"""
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
  $delaySeconds = 30
  if ({{respectLiteral}}) {
    try {
      $uxPath = 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'
      $start = [int](Get-ItemProperty -Path $uxPath -Name ActiveHoursStart -ErrorAction Stop).ActiveHoursStart
      $end = [int](Get-ItemProperty -Path $uxPath -Name ActiveHoursEnd -ErrorAction Stop).ActiveHoursEnd
      $now = Get-Date
      $hour = $now.Hour

      $inActiveHours = if ($start -lt $end) {
        ($hour -ge $start) -and ($hour -lt $end)
      } elseif ($start -gt $end) {
        ($hour -ge $start) -or ($hour -lt $end)
      } else {
        $true
      }

      if ($inActiveHours) {
        $restartAt = Get-Date -Hour $end -Minute 5 -Second 0
        if ($start -lt $end -and $hour -ge $end) {
          $restartAt = $restartAt.AddDays(1)
        } elseif ($start -gt $end -and $hour -ge $start) {
          $restartAt = $restartAt.AddDays(1)
        }

        $candidateDelay = [int][Math]::Ceiling(($restartAt - (Get-Date)).TotalSeconds)
        if ($candidateDelay -gt $delaySeconds) {
          $delaySeconds = [Math]::Min($candidateDelay, 315360000)
        }
      }
    } catch {
      # Fallback to immediate-ish reboot when active-hours lookup fails.
    }
  }

  & shutdown.exe /r /t $delaySeconds /c "Restarting to complete Windows updates (HA WinKiosk command)."
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
