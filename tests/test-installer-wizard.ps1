$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$setup = Join-Path $repo 'installer/out/PIXEL-1.0.0.exe'
$privateRoot = [IO.Path]::GetFullPath((Join-Path $repo 'private'))
$testRoot = [IO.Path]::GetFullPath((Join-Path $privateRoot ('wizard-check-' + [Guid]::NewGuid().ToString('N'))))
$install = Join-Path $testRoot 'installation'
$saves = Join-Path $testRoot 'saves'
$package = Join-Path $repo 'game/pixel-360'
$registry = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{3F56AE2E-A958-47D3-A1B8-5D12664D39E8}_is1'
if (Test-Path $registry) { throw 'An existing PIXEL installation is registered; refusing to change it during the test.' }
if (-not $testRoot.StartsWith($privateRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected test directory.' }
New-Item -ItemType Directory -Path $saves -Force | Out-Null
$sentinel = Join-Path $saves 'preserve.txt'
Set-Content -LiteralPath $sentinel -Value 'existing player data'
$initialHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
$uninstaller = Join-Path $install 'unins000.exe'
try {
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/MERGETASKS="!startmenu,!desktop"',
        "/DIR=`"$install`"", "/PACKAGE=`"$package`"", "/SAVEDIR=`"$saves`"", "/LOG=`"$(Join-Path $testRoot 'install.log')`"")
    $process = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Silent setup failed: $($process.ExitCode). See $testRoot" }
    if (-not (Test-Path -LiteralPath (Join-Path $install 'current/run/584E07D1/PIXEL.exe'))) { throw 'Installed EXE missing.' }
    $registration = Get-ItemProperty $registry
    if ($registration.EstimatedSize -lt 100000) { throw 'Measured installed size missing.' }
    if ((Get-Content -LiteralPath (Join-Path $install 'locations.ini') -Raw) -notlike "*Saves=$saves*") { throw 'Custom saves folder not persisted.' }
    Write-Output 'SILENT_INSTALL_OK'
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/MERGETASKS="!startmenu,!desktop"',
        "/DIR=`"$install`"", "/PACKAGE=`"$package`"", "/LOG=`"$(Join-Path $testRoot 'reinstall.log')`"")
    $process = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Silent reinstall failed: $($process.ExitCode)." }
    if ((Get-Content -LiteralPath (Join-Path $install 'locations.ini') -Raw) -notlike "*Saves=$saves*") { throw 'Reinstall changed custom saves folder.' }
    if ((Get-Content -LiteralPath (Join-Path $install 'current/install-report.txt') -Raw) -notlike '*Reused content: True*') { throw 'Reinstall did not reuse prepared content.' }
    Write-Output 'SILENT_REINSTALL_OK'
}
finally {
    if (Test-Path -LiteralPath $uninstaller) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$(Join-Path $testRoot 'uninstall.log')`"") -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Uninstall failed: $($process.ExitCode)." }
    }
}
if (Test-Path -LiteralPath (Join-Path $install 'current')) { throw 'Uninstall left game data behind.' }
if (Test-Path $registry) { throw 'Uninstall left its registry entry behind.' }
if ((Get-Content -LiteralPath $sentinel -Raw).Trim() -ne 'existing player data') { throw 'Uninstall changed player data.' }
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $initialHash) { throw 'Setup changed the source package.' }
Write-Output "SILENT_UNINSTALL_OK saves=preserved source=unchanged logs=$testRoot"
