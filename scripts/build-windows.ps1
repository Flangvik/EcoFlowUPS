<#
.SYNOPSIS
  Build EcoFlow UPS Monitor for Windows.

.DESCRIPTION
  Produces a self-contained single-file executable and a zip archive.
  Run from an elevated shell is NOT required.

.PARAMETER Version
  Version string to stamp into the assembly (default: 0.0.0-local).

.PARAMETER Rid
  Target runtime identifier (default: win-x64). Use win-arm64 for ARM devices.

.EXAMPLE
  .\scripts\build-windows.ps1
  # dev build, win-x64, version 0.0.0-local

.EXAMPLE
  .\scripts\build-windows.ps1 -Version 1.2.3
  # versioned release build

.EXAMPLE
  .\scripts\build-windows.ps1 -Version 1.2.3 -Rid win-arm64
  # cross-build for ARM64 Windows

.NOTES
  Requires .NET 10 SDK on PATH.
  Output:
    dist\EcoFlowMonitor-v<Version>-<Rid>.zip
    publish\<Rid>\EcoFlowMonitor.App.exe   (self-contained single file)
#>

param(
    [string]$Version = "0.0.0-local",
    [string]$Rid     = "win-x64"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $RepoRoot

Write-Host "==> Building EcoFlow UPS Monitor $Version for $Rid"
Write-Host "    repo: $RepoRoot"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "'dotnet' not found on PATH. Install the .NET 10 SDK first."
    exit 1
}

$Tfm        = "net10.0"
$AppCsproj  = "src\EcoFlowMonitor.App\EcoFlowMonitor.App.csproj"
$OutDir     = "publish\$Rid"

Write-Host "==> Publishing self-contained single-file..."
& dotnet publish $AppCsproj `
    -c Release `
    -f $Tfm `
    -r $Rid `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    -o $OutDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path "dist" | Out-Null
$Out = "dist\EcoFlowMonitor-v$Version-$Rid.zip"
if (Test-Path $Out) { Remove-Item $Out }

Write-Host "==> Packaging $Out..."
Compress-Archive -Path "$OutDir\*" -DestinationPath $Out -CompressionLevel Optimal

$Size = "{0:N1} MB" -f ((Get-Item $Out).Length / 1MB)

Write-Host ""
Write-Host "==> Done."
Write-Host "    Publish dir : $OutDir\"
Write-Host "    Archive     : $Out ($Size)"
Write-Host ""
Write-Host "Run it with:"
Write-Host "    .\$OutDir\EcoFlowMonitor.App.exe"
Write-Host ""
Write-Host "If Windows SmartScreen warns: click 'More info' -> 'Run anyway'."
