param([Parameter(Mandatory=$true)][string]$Stage)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$privateRoot = [IO.Path]::GetFullPath((Join-Path $root 'private'))
$stagePath = [IO.Path]::GetFullPath($Stage)
if (-not $stagePath.StartsWith($privateRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The launch stage must be inside the repository private directory.'
}
$game = Join-Path $stagePath 'run/584E07D1'
if (-not (Test-Path -LiteralPath (Join-Path $game 'Arkedo Series - 03 PIXEL!.exe'))) {
    throw 'The prepared game executable is missing.'
}
$built = Join-Path $root 'src/Port.Launcher/bin/Release/net10.0'
foreach ($name in @('Port.Launcher.dll', 'Port.Launcher.deps.json', 'Port.Launcher.runtimeconfig.json', 'SDL3.dll', 'FNA3D.dll', 'FAudio.dll', 'libtheorafile.dll', 'Arkedo Series - 03 PIXEL!.png')) {
    Copy-Item -LiteralPath (Join-Path $built $name) -Destination (Join-Path $game $name) -Force
}
$d3d = Join-Path $game 'D3D12'
New-Item -ItemType Directory -Force -Path $d3d | Out-Null
Copy-Item -LiteralPath (Join-Path $built 'D3D12/D3D12Core.dll') -Destination (Join-Path $d3d 'D3D12Core.dll') -Force
Write-Output "LAUNCH_STAGE_OK $game"
