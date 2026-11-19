# Gate 4: content and audio coverage

**Completed:** 2026-11-18

All 340 XNB assets load through FNA, including 69 assets using the prepared `Scale_Shared_ContentGame` custom reader. The original game now uses its XACT audio calls, categories, fades, cue transport, and cleanup. The user confirmed music and effects in both the visible title menu and level.

Preparation converts 19 XMA2 and 52 big-endian PCM16 entries to a desktop PCM wave bank. Entry order, bank name, rates, channels, sample counts, and loops are preserved. Hash-pinned vgmstream r2168 decodes XMA locally. The five static interactive sound-bank cues now select their original sound directly: FAudio otherwise leaves them silent until their control variable changes, which PIXEL never does. Sound definitions, event loops, cue fades/limits/transitions, and four random variation tables remain intact. See [conversion and license notes](../../foundation/audio-conversion.md).

## Evidence

Commands were run from the repository root with the pinned .NET 10.0.302 SDK:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD 'out/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build PixelPort.sln -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false -v:q
python tools/gate1_evidence.py
python -m unittest discover -s tests -p test_gate1.py -v
dotnet tools/prepare-cli/bin/Release/net10.0/prepare-cli.dll game/pixel-360 private/gate4-final private config/supported-versions.json config/patch-anchors.json out/deps/managed/FNA.dll
./scripts/stage-launcher.ps1 -Stage private/gate4-final
$env:PIXEL_PORT_GATE4_STAGE = Join-Path $PWD 'private/gate4-final'
python -m unittest discover -s tests -p test_gate4.py -v
dotnet private/gate4-final/run/584E07D1/Port.Launcher.dll --check-load
dotnet private/gate4-final/run/584E07D1/Port.Launcher.dll --probe-content
dotnet private/gate4-final/run/584E07D1/Port.Launcher.dll --probe-audio
$env:PIXEL_PORT_TRACE_CONTENT = '1'
dotnet private/gate4-final/run/584E07D1/Port.Launcher.dll --run --frames 3000 --scripted-controller --play-frames 900
```

- Build: zero warnings/errors. Evidence check: 46 anchors. Existing rejection tests: eight passed. Gate-4 integration tests: four passed, including exact PCM sample/timing/loop preservation, cue-selection/transport preservation, corrupt-decoder rejection without output, and byte-identical repeated preparation. Temporary repeat stages are removed by the tests.
- JIT check: 551 methods, zero failures. Content probe: 340 assets, 69 custom objects, zero load failures. Ignored log: `private/gate4-final/content.log`.
- Audio probe: 63 cues, title and level cue pause/resume/stop, category pause/resume, and volume values 0, 0.25, 1. Ignored log: `private/gate4-final/audio.log`.
- Scripted run: gameplay, pause/resume, return to title, final exit input, and clean exit must all be observed for success. Ignored log: `private/gate4-final/scripted-play.log`. Content-load instrumentation reports the actual launch/intro/level/title paths.
- The first converted stage revealed that `MUS_TITLE` reported Playing while silent. Windows per-process peaks were zero. Playing the same PCM track directly produced output and was audible to the user. After the static-cue conversion, the actual visible title menu's session peaks ranged from `0.055619` to `0.265885`, at session volume 1 and unmuted. Ignored measurement: `private/gate4-final/title-output-peaks.log` (PID 20764).
- The user then confirmed **menu and level audio work** in the corrected visible game. The physical-controller run reached `START → INTRO → GAME` and exited cleanly. Ignored logs: `private/gate4-final/manual-play-user.log` and `.err.log`.

## Artifact identities

| Local artifact | SHA-256 |
| --- | --- |
| Preparation manifest | `62ef1343c540a31c387df8a49d3391560f0e52f597b367ea5ec8d9ce1b523e67` |
| Prepared executable | `af70ee93747da9911761d1af069b7d4e9adb75681a65a48779c0535eabcc5b1f` |
| PCM wave bank (113,826,656 bytes) | `a58ec50ae8e74b185daba164ff323b3a8f6e6f56020eeeffa52ba24bc8cd8b79` |
| Adapted sound bank (4,438 bytes) | `71fe1eb58fa19f3a7b6e92bef6783d8fdbecb26bd54e156a58290d6f91b10993` |

Original and prepared game bytes remain ignored. This gate does not establish full-level completion, save progression, a clean-account release, or installer acceptance. Those remain gates 5–8. Decoder redistribution notices and matching-source obligations must be included in the release audit before packaging.
