param(
    [string]$IsccPath = 'ISCC.exe',
    [string]$RuntimePackSource = 'https://api.nuget.org/v3/index.json',
    [switch]$Offline
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$payload = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'out/payload'))
$expectedPayload = [IO.Path]::GetFullPath((Join-Path $repo 'installer/out/payload'))
$dependencies = Get-Content -LiteralPath (Join-Path $repo 'config/dependencies.json') -Raw | ConvertFrom-Json
$packs = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'config/runtime-packs.json') -Raw | ConvertFrom-Json
$cache = Join-Path $repo 'out/deps/cache'

# Fail before deleting the previous payload if required tooling is unavailable.
$null = Get-Command $IsccPath -ErrorAction Stop
if (-not (Test-Path -LiteralPath (Join-Path $repo 'out/deps/managed/FNA.dll'))) {
    & (Join-Path $repo 'scripts/bootstrap-fna.ps1') -Offline:$Offline
}
if ($payload -ne $expectedPayload) { throw 'Unexpected installer payload path.' }
if (Test-Path -LiteralPath $payload) {
    foreach ($entry in @(Get-Item -LiteralPath $payload -Force) + @(Get-ChildItem -LiteralPath $payload -Recurse -Force)) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Installer payload contains a reparse point.' }
    }
    Remove-Item -LiteralPath $payload -Recurse -Force
}

Push-Location -LiteralPath $repo
try {
    & dotnet build PixelPort.sln -c Release -p:NuGetAudit=false -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Game build failed.' }
    foreach ($item in @(
        @{ Project='installer/src/Port.SetupHelper'; Folder='helper' },
        @{ Project='src/Port.Launcher'; Folder='runtime' }
    )) {
        $project = Join-Path $repo $item.Project
        $target = Join-Path $payload $item.Folder
        $sources = @('--source', $cache)
        if (-not $Offline) { $sources += @('--source', $RuntimePackSource) }
        & dotnet restore $project -r win-x64 @sources -p:NuGetAudit=false "-p:RuntimeFrameworkVersion=$($packs.version)" -v:q
        if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
        $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
        foreach ($pin in $packs.packages.PSObject.Properties) {
            $file = Join-Path $nugetRoot "$($pin.Name)/$($packs.version)/$($pin.Name).$($packs.version).nupkg"
            if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $pin.Value) {
                throw "Runtime pack differs from installer/config/runtime-packs.json: $($pin.Name)"
            }
        }
        & dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore `
            -p:PublishSingleFile=true -p:PublishTrimmed=false "-p:RuntimeFrameworkVersion=$($packs.version)" -o $target -v:q
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
        Get-ChildItem -LiteralPath $target -Filter '*.pdb' -Recurse -File | Remove-Item -Force
    }
    $runtime = Join-Path $payload 'runtime'
    Move-Item -LiteralPath (Join-Path $runtime 'Port.Launcher.exe') -Destination (Join-Path $runtime 'PIXEL.exe')
    foreach ($name in @('FNA.dll','FNA.dll.config')) {
        Copy-Item -LiteralPath (Join-Path $repo "out/deps/managed/$name") -Destination $runtime -Force
    }
    foreach ($name in @('Port.Bridge','Port.Hooks')) {
        Copy-Item -LiteralPath (Join-Path $repo "src/$name/bin/Release/net10.0/$name.dll") -Destination $runtime -Force
    }
    $notices = Join-Path $runtime 'notices'
    New-Item -ItemType Directory -Force -Path $notices | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $notices 'Pixel-LICENSE')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'notices/Mono.Cecil-NOTICE.txt') -Destination $notices
    $fna = Join-Path $repo "out/deps/fna-$($dependencies.fna.version)/FNA"
    Copy-Item -Path (Join-Path $fna 'licenses/*') -Destination $notices
    foreach ($entry in @(
        @{ From='lib/FAudio/LICENSE'; To='FAudio-LICENSE' },
        @{ From='lib/FNA3D/LICENSE'; To='FNA3D-LICENSE' },
        @{ From='lib/FNA3D/MojoShader/LICENSE.txt'; To='MojoShader-LICENSE.txt' },
        @{ From='lib/SDL3-CS/LICENSE'; To='SDL3-CS-LICENSE' },
        @{ From='lib/Theorafile/licenses/LICENSE'; To='Theorafile-LICENSE' },
        @{ From='lib/Theorafile/licenses/libogg.LICENSE'; To='libogg-LICENSE' },
        @{ From='lib/Theorafile/licenses/libtheora.LICENSE'; To='libtheora-LICENSE' },
        @{ From='lib/Theorafile/licenses/libvorbis.LICENSE'; To='libvorbis-LICENSE' }
    )) {
        Copy-Item -LiteralPath (Join-Path $fna $entry.From) -Destination (Join-Path $notices $entry.To)
    }
    foreach ($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
        Copy-Item -LiteralPath (Join-Path $nugetRoot "microsoft.netcore.app.runtime.win-x64/$($packs.version)/$name") -Destination (Join-Path $notices "dotnet-$name")
    }
    Copy-Item -LiteralPath (Join-Path $repo "docs/notices/vgmstream-$($dependencies.audioDecoder.version)") -Destination $notices -Recurse
    $audio = Join-Path $payload 'tools/audio'
    New-Item -ItemType Directory -Force -Path $audio | Out-Null
    foreach ($file in $dependencies.audioDecoder.files) {
        $inputFile = Join-Path $repo "out/deps/vgmstream-$($dependencies.audioDecoder.version)/$($file.name)"
        if ((Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash -ine $file.sha256) { throw "Decoder file differs: $($file.name)" }
        Copy-Item -LiteralPath $inputFile -Destination $audio
    }
    $config = Join-Path $payload 'config'
    New-Item -ItemType Directory -Force -Path $config | Out-Null
    foreach ($name in @('dependencies.json','supported-versions.json','patch-anchors.json')) {
        Copy-Item -LiteralPath (Join-Path $repo "config/$name") -Destination $config
    }

    # Only explicitly selected port code, metadata and dependencies may ship.
    $allowedRuntime = @('PIXEL.exe','Arkedo Series - 03 PIXEL!.png','FNA.dll','FNA.dll.config','Port.Bridge.dll','Port.Hooks.dll') + @($dependencies.native.files)
    foreach ($folder in @('helper','runtime','tools','config')) {
        $folderPath = Join-Path $payload $folder
        foreach ($file in Get-ChildItem -LiteralPath $folderPath -Recurse -File) {
            $relative = [IO.Path]::GetRelativePath($folderPath, $file.FullName).Replace('\','/')
            $allowed = switch ($folder) {
                'helper' { $relative -eq 'Port.SetupHelper.exe' }
                'runtime' { $relative -in $allowedRuntime -or $relative -like 'notices/*' }
                'tools' { $relative -in @($dependencies.audioDecoder.files | ForEach-Object { "audio/$($_.name)" }) }
                'config' { $relative -in @('dependencies.json','supported-versions.json','patch-anchors.json') }
            }
            if (-not $allowed) { throw "Unexpected installer payload file: $folder/$relative" }
        }
    }
    foreach ($forbidden in @('Arkedo Series - 03 PIXEL!.exe','Scale_Shared_ContentGame.dll','*.xnb','*.xwb','*.xsb','*.wav','*.live','*.pdb')) {
        if (Get-ChildItem -LiteralPath $payload -Recurse -File -Filter $forbidden) { throw "Game data or symbols in installer payload: $forbidden" }
    }
    & $IsccPath (Join-Path $PSScriptRoot 'setup.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
}
finally { Pop-Location }
