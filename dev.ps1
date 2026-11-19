# Dot-source this file: . .\dev.ps1
$script:PixelDevRoot = $PSScriptRoot

function build-game {
    <# .SYNOPSIS
    Build the port and prepare a fresh playable game from your local package.
    #>
    [CmdletBinding()]
    param(
        [string]$Package = (Join-Path $script:PixelDevRoot 'game/pixel-360'),
        [switch]$Offline
    )

    $ErrorActionPreference = 'Stop'
    $packagePath = (Resolve-Path -LiteralPath $Package).ProviderPath
    Push-Location -LiteralPath $script:PixelDevRoot
    try {
        if (-not (Test-Path -LiteralPath 'out/deps/managed/FNA.dll')) {
            & ./scripts/bootstrap-fna.ps1 -Offline:$Offline
        }

        & dotnet build PixelPort.sln -c Release -p:NuGetAudit=false -v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Game build failed ($LASTEXITCODE)." }

        # Preparation requires a new directory. Preserve previous builds and saves.
        $stage = Join-Path $script:PixelDevRoot ('private/dev-' + [Guid]::NewGuid().ToString('N'))
        & dotnet tools/prepare-cli/bin/Release/net10.0/prepare-cli.dll `
            $packagePath $stage (Join-Path $script:PixelDevRoot 'private') `
            config/supported-versions.json config/patch-anchors.json out/deps/managed/FNA.dll
        if ($LASTEXITCODE -ne 0) { throw "Game preparation failed ($LASTEXITCODE)." }

        & ./scripts/stage-launcher.ps1 -Stage $stage
        $launcher = Join-Path $stage 'run/584E07D1/Port.Launcher.dll'
        & dotnet $launcher --check-load
        if ($LASTEXITCODE -ne 0) { throw "Prepared game load check failed ($LASTEXITCODE)." }

        # Only select this build after all steps succeed.
        Set-Content -LiteralPath 'private/dev-stage.txt' -Value $stage -Encoding UTF8
        Write-Host "Game ready. Run run-game to play. Stage: $stage"
    }
    finally {
        Pop-Location
    }
}

function run-game {
    <# .SYNOPSIS
    Run the latest dev build interactively; build it first if none exists.
    #>
    [CmdletBinding()]
    param([switch]$Build)

    $ErrorActionPreference = 'Stop'
    $stageFile = Join-Path $script:PixelDevRoot 'private/dev-stage.txt'
    if ($Build -or -not (Test-Path -LiteralPath $stageFile)) {
        build-game
    }
    $stage = (Get-Content -LiteralPath $stageFile -Raw).Trim()
    $gameDirectory = Join-Path $stage 'run/584E07D1'
    $launcher = Join-Path $gameDirectory 'Port.Launcher.dll'
    if (-not (Test-Path -LiteralPath $launcher)) {
        throw 'The dev build is missing. Run build-game to prepare it again.'
    }

    Push-Location -LiteralPath $gameDirectory
    try {
        # Stay attached to the user's interactive terminal so the game opens on
        # their desktop and startup errors remain visible. Wait until it closes.
        & dotnet $launcher --run
        if ($LASTEXITCODE -ne 0) { throw "Game exited with code $LASTEXITCODE." }
    }
    finally {
        Pop-Location
    }
}
