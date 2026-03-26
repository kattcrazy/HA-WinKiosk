param(
    [switch]$ForceInstall
)

$ErrorActionPreference = "Stop"

function Test-DotNet8DesktopRuntime {
    try {
        $runtimes = & dotnet --list-runtimes 2>$null
        if (-not $runtimes) { return $false }
        return ($runtimes | Select-String -Pattern "^Microsoft\.WindowsDesktop\.App 8\.")
    } catch {
        return $false
    }
}

if (-not $ForceInstall -and (Test-DotNet8DesktopRuntime)) {
    Write-Host ".NET 8 Windows Desktop Runtime already installed."
    exit 0
}

$downloadUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
$tempPath = Join-Path $env:TEMP "dotnet8-windowsdesktop-runtime-win-x64.exe"

Write-Host "Downloading .NET 8 Windows Desktop Runtime..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $tempPath

Write-Host "Installing runtime (silent)..."
$proc = Start-Process -FilePath $tempPath -ArgumentList "/install", "/quiet", "/norestart" -Wait -PassThru
if ($proc.ExitCode -ne 0) {
    throw "Runtime installer failed with exit code $($proc.ExitCode)."
}

Write-Host ".NET 8 Windows Desktop Runtime install complete."
exit 0
