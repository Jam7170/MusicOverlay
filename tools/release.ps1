# Builds the GitHub release assets into .\publish\assets:
#   MusicOverlay-<ver>-win-x64.zip                    standalone (no .NET needed). The exe is uncompressed inside
#                                                     the zip, so memory use stays as low as the small build.
#   MusicOverlay-<ver>-win-x64-requires-dotnet10.exe  small single-file exe, needs the .NET 10 Desktop Runtime
#
#   powershell -File tools\release.ps1
#   gh release create v<ver> publish\assets\* --title "v<ver>" --notes "..."
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$proj = Join-Path $root 'MusicOverlay.csproj'
$ver = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$out = Join-Path $root 'publish'
$assets = Join-Path $out 'assets'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $assets | Out-Null

function Publish($dir, [string[]]$extra) {
    # Separate intermediate folders: the two RID-specific builds otherwise contaminate each other.
    Remove-Item (Join-Path $root 'obj\Release\net10.0-windows10.0.19041.0\win-x64') -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish $proj -c Release -r win-x64 -p:PublishSingleFile=true -p:DebugType=none -o $dir --nologo -v quiet @extra
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $dir" }
}

Publish (Join-Path $out 'standalone') @('--self-contained', 'true', '-p:IncludeNativeLibrariesForSelfExtract=true')
Publish (Join-Path $out 'small') @('-p:SelfContained=false')

Compress-Archive -Path (Join-Path $out 'standalone\MusicOverlay.exe') -DestinationPath (Join-Path $assets "MusicOverlay-$ver-win-x64.zip") -CompressionLevel Optimal
Copy-Item (Join-Path $out 'small\MusicOverlay.exe') (Join-Path $assets "MusicOverlay-$ver-win-x64-requires-dotnet10.exe")

Get-ChildItem $assets | ForEach-Object { '{0}  {1:N1} MB  sha256 {2}' -f $_.Name, ($_.Length / 1MB), (Get-FileHash $_.FullName).Hash }
