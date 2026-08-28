param(
    [string]$SourceRoot = "",
    [string]$BuildRoot = "C:\dev\HA-WinKiosk"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
} else {
    $SourceRoot = (Resolve-Path $SourceRoot).Path
}

$buildProject = Join-Path $BuildRoot "src\HAWinKiosk\HAWinKiosk.csproj"
$tfm = "net8.0-windows10.0.19041.0"
$buildPublishDir = Join-Path $BuildRoot "src\HAWinKiosk\bin\Release\$tfm\win-x64\publish"
$sourcePublishDir = Join-Path $SourceRoot "src\HAWinKiosk\bin\Release\$tfm\win-x64\publish"
# Camera capture (MSMF/OpenCV) fails when the EXE is launched from a Google Drive path.
$localRunDir = "C:\dev\HA-WinKiosk-run"

Write-Host "SourceRoot: $SourceRoot"
Write-Host "BuildRoot:  $BuildRoot"

if ($BuildRoot -eq $SourceRoot) {
    throw "BuildRoot and SourceRoot must be different."
}

if (Test-Path $BuildRoot) {
    Write-Host "Removing existing build folder..."
    Remove-Item -Path $BuildRoot -Recurse -Force
}

try {
    New-Item -Path $BuildRoot -ItemType Directory | Out-Null

    Write-Host "Copying source to local build folder..."
    $copyArgs = @(
        $SourceRoot, $BuildRoot, "/MIR",
        "/XD", ".git", ".vs", "bin", "obj",
        "/R:2", "/W:1", "/NFL", "/NDL", "/NP"
    )
    & robocopy @copyArgs
    $copyExitCode = $LASTEXITCODE
    if ($copyExitCode -ge 8) {
        throw "robocopy source->build failed with exit code $copyExitCode"
    }

    Write-Host "Publishing self-contained win-x64 EXE..."
    # OpenCvSharp native DLLs do not load reliably from a single-file bundle.
    $publishArgs = @(
        "publish",
        $buildProject,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "/p:BundleAsExe=true",
        "/p:PublishSingleFile=false"
    )

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path $buildPublishDir)) {
        throw "Publish output folder not found: $buildPublishDir"
    }

    New-Item -Path $sourcePublishDir -ItemType Directory -Force | Out-Null

    Write-Host "Copying publish output back to source..."
    $copyBackArgs = @(
        $buildPublishDir, $sourcePublishDir, "/MIR",
        "/R:2", "/W:1", "/NFL", "/NDL", "/NP"
    )
    & robocopy @copyBackArgs
    $copyBackExitCode = $LASTEXITCODE
    if ($copyBackExitCode -ge 8) {
        throw "robocopy build->source failed with exit code $copyBackExitCode"
    }

    $sourceExe = Join-Path $sourcePublishDir "HAWinKiosk.exe"
    if (-not (Test-Path $sourceExe)) {
        throw "Expected EXE not found after copy-back: $sourceExe"
    }

    if (Test-Path $localRunDir) {
        Remove-Item -Path $localRunDir -Recurse -Force
    }
    New-Item -Path $localRunDir -ItemType Directory -Force | Out-Null
    Write-Host "Copying runnable build to local disk (required for camera)..."
    $copyRunArgs = @(
        $buildPublishDir, $localRunDir, "/MIR",
        "/R:2", "/W:1", "/NFL", "/NDL", "/NP"
    )
    & robocopy @copyRunArgs
    $copyRunExit = $LASTEXITCODE
    if ($copyRunExit -ge 8) {
        throw "robocopy build->local run failed with exit code $copyRunExit"
    }

    $localExe = Join-Path $localRunDir "HAWinKiosk.exe"
    Write-Host ""
    Write-Host "Done. Run from local disk (camera does not work from Google Drive path):"
    Write-Host $localExe
    Write-Host "Source tree publish copy:"
    Write-Host $sourceExe
}
finally {
    if (Test-Path $BuildRoot) {
        Write-Host "Cleaning up local build folder..."
        Remove-Item -Path $BuildRoot -Recurse -Force
    }
}
