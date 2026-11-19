param(
    [string]$IsccPath = 'ISCC.exe',
    [switch]$Offline,
    [switch]$SkipInstallerBuild
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $repo
try {
    if (-not $SkipInstallerBuild) {
        & (Join-Path $PSScriptRoot 'build.ps1') -IsccPath $IsccPath -Offline:$Offline
    }
    $arguments = @((Join-Path $repo 'scripts/package-release.py'))
    if ($Offline) { $arguments += '--offline' }
    & python @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Release packaging failed.' }
}
finally { Pop-Location }
