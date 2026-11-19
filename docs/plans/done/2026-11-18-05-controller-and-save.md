# Gate 5: controller and save progression

**Status:** Complete for the user-reported controller validation and the save checks below. The save integration run uses an assisted exit-event fixture; it is not a controller-only full-level traversal. Final clean-account acceptance remains open.

## Results

- The user reported the controller validated and requested save validation. Controller semantics remain unchanged. Original `GAMEMain.HandleInput` pauses when its selected controller disconnects, subject to intro/fade guards; this behavior was inspected, not physically retested here.
- Reproduced the original corrupt-save defect: a truncated XML file made `Ark_SavLoad` finish with `m_initOk=false`, `m_reset=true`. Existing corrupt files prevented `DoCreate` from recovering; original deserialization also left its stream undisposed on failure.
- Replaced three already hash-guarded storage method bodies with generic bridge calls. The original save type, XML format/version, loader initialization, New Game reset, completion flags, and Continue logic remain in the prepared game.
- Missing saves initialize cleanly. Twelve damaged/invalid XML cases are preserved byte-for-byte as unique `.corrupt-*` files before fresh progress is created. Validation covers syntax, required fields, version, array dimensions, DTD rejection, and size bounds.
- Saves use a flushed temporary file and atomic replacement with a previous-version `.bak`. A Windows sharing-lock test blocks replacement, observes explicit failure, verifies the old file unchanged, verifies temporary-file cleanup, and successfully reloads the old progress after unlocking.
- Separate processes round-trip seven level flags and all eighteen treasure flags with a mixed fixture, retaining valid XML bytes on read. Reset clears and persists every flag. All four controller slots resolve to the same `Player.sav` for this Windows user.
- The game-loop fixture invoked the original tutorial and Grassland exit events. The original victory timers and completion handlers saved `1100000` unlocks and loaded Cave. It then paused/resumed, returned to title, and exited. A separate process loaded the same save, exposed Grassland/Cave in Continue, selected Cave, entered level 2, paused/resumed, returned to title, and exited cleanly.
- An isolated copy of the user's existing XML loaded successfully. The normal user save was not modified; its before/after hashes match.
- Release build: zero warnings/errors. JIT check: 551 methods, zero failures. Original evidence guards: 46 anchors. All 18 gate-1, gate-4, and gate-5 integration tests passed, including deterministic repeated preparation and unchanged audio data.

## Reproduction

```powershell
dotnet build PixelPort.sln -c Release -p:NuGetAudit=false -v:q
dotnet tools/prepare-cli/bin/Release/net10.0/prepare-cli.dll game/pixel-360 private/gate5-final private config/supported-versions.json config/patch-anchors.json out/deps/managed/FNA.dll
./scripts/stage-launcher.ps1 -Stage private/gate5-final
dotnet private/gate5-final/run/584E07D1/Port.Launcher.dll --check-load
python tools/gate1_evidence.py
$env:PIXEL_PORT_GATE4_STAGE = Join-Path $PWD 'private/gate5-final'
$env:PIXEL_PORT_GATE5_STAGE = Join-Path $PWD 'private/gate5-final'
python -m unittest discover -s tests -p 'test_gate*.py' -v
$env:FNA3D_FORCE_DRIVER = 'Vulkan'
$env:PIXEL_PORT_SAVE_DIR = Join-Path $PWD 'private/gate5-final/progression-saves'
dotnet private/gate5-final/run/584E07D1/Port.Launcher.dll --run --frames 6000 --scripted-save complete
dotnet private/gate5-final/run/584E07D1/Port.Launcher.dll --run --frames 3500 --scripted-save continue
```

Preparation requires a new destination. The completion fixture requires an initially missing/fresh isolated save; the Continue fixture requires its output. Never point scripted New Game runs at personal saves. The headless `--probe-save <read|write|reset> <isolated-directory>` provides narrower persistence checks and refuses the normal user save directory.

Ignored evidence is in `private/gate5-final/`: `tests.log`, `check-load.log`, `save-completion.log`, `save-relaunch.log`, `existing-save.log`, and the isolated saves. The known-bad baseline is `private/gate5-baseline-corrupt/Player.sav`.

## Artifact identity and limits

- Manifest SHA-256: `649fc3f8aa8b62469c8ddc5915ca70addf02d2e143c90be27a32914d4bf937cb`.
- Prepared executable SHA-256: `50e4f127473f8ec3abada0598b7830e7a4458c7eced7f9b28bcaac70acd14438`.
- Bridge SHA-256: `47f05d08c943b4339da2c54367306c8750a43d55424879788c6ee4522571ed7b`.
- Progression save SHA-256 after completion/relaunch: `4d0f5f4b14bd4a171803ae31c96258f780f474036f9f972c7aa8524f3251a72c`.

Storage failure is explicit in the launcher log and terminates the run; there is no new in-game recovery dialog. Corrupt files and `.bak` files support manual recovery. Power-loss behavior beyond flushed writes and atomic replacement was not fault-injected. Full manual traversal, clean-account deployment, setup/uninstall save preservation, and installer acceptance remain gates 6–8.
