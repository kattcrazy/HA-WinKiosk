param(
    [string]$SourceRoot = "",
    [string]$BuildRoot = "C:\HA-WinKiosk"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
} else {
    $SourceRoot = (Resolve-Path $SourceRoot).Path
}

$exeBuilder = Join-Path $SourceRoot "tools\Build-ExeFromGDrive.ps1"
$issFile = Join-Path $SourceRoot "installer\HAWinKiosk.iss"
$outputDir = Join-Path $SourceRoot "installer\output"
$csproj = Join-Path $SourceRoot "src\HAWinKiosk\HAWinKiosk.csproj"

if (-not (Test-Path $exeBuilder)) {
    throw "Missing EXE builder script: $exeBuilder"
}
if (-not (Test-Path $issFile)) {
    throw "Missing installer script: $issFile"
}
if (-not (Test-Path $csproj)) {
    throw "Missing project file: $csproj"
}

Write-Host "Building published single-file EXE first..."
& powershell -ExecutionPolicy Bypass -File $exeBuilder -SourceRoot $SourceRoot -BuildRoot $BuildRoot
if ($LASTEXITCODE -ne 0) {
    throw "EXE build step failed with exit code $LASTEXITCODE"
}

$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6, then run again."
}

Write-Host "Compiling installer with Inno Setup..."
if (Test-Path $outputDir) {
    Remove-Item -Path $outputDir -Recurse -Force
}

$appVersion = "1.0.0"
try {
    [xml]$projXml = Get-Content -Path $csproj -Raw
    $versionNode = $projXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ($versionNode) {
        $parsed = $versionNode.ToString().Trim()
        if (-not [string]::IsNullOrWhiteSpace($parsed)) {
            $appVersion = $parsed
        }
    }
} catch {
}

& $iscc "/DMyAppVersion=$appVersion" $issFile
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed with exit code $LASTEXITCODE"
}

$setupExe = Join-Path $outputDir "HAWinKiosk-Setup.exe"
if (-not (Test-Path $setupExe)) {
    throw "Expected installer output not found: $setupExe"
}

Write-Host ""
Write-Host "Done. Installer available at:"
Write-Host $setupExe
