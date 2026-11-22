# Builds Music Overlay, installs it to %LOCALAPPDATA%\Programs\MusicOverlay, registers it to start with
# Windows, and launches it. Re-run any time to update. Settings live in %APPDATA%\MusicOverlay and are kept.
#   .\install.ps1              install / update + start with Windows
#   .\install.ps1 -NoStartup   install / update without the startup entry
#   .\install.ps1 -Uninstall   remove the app and startup entry (settings are kept)
param([switch]$NoStartup, [switch]$Uninstall)
$ErrorActionPreference = 'Stop'

$dest   = Join-Path $env:LOCALAPPDATA 'Programs\MusicOverlay'
$exe    = Join-Path $dest 'MusicOverlay.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Get-Process MusicOverlay -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if ($Uninstall) {
    Remove-ItemProperty -Path $runKey -Name MusicOverlay -ErrorAction SilentlyContinue
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Write-Host "Uninstalled. Settings remain in $env:APPDATA\MusicOverlay."
    return
}

dotnet publish "$PSScriptRoot\MusicOverlay.csproj" -c Release -o $dest --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if ($NoStartup) {
    Remove-ItemProperty -Path $runKey -Name MusicOverlay -ErrorAction SilentlyContinue
} else {
    Set-ItemProperty -Path $runKey -Name MusicOverlay -Value "`"$exe`""
}

Start-Process $exe
Write-Host "Installed to $dest$(if (-not $NoStartup) { ' (starts with Windows)' })."
