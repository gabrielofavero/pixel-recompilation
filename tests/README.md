`test_gate1.py` runs local-package-dependent rejection and reproducibility checks for the STFS and managed inspectors. It skips when the ignored source package is absent.

`test_gate4.py` checks the prepared wave bank's entry identities, timing, channels, loop regions, exact PCM sample preservation, static interactive-cue conversion, rejection of a changed decoder before output, and byte-identical repeated preparation. It needs a Release build, the pinned decoder cache, the local package, and a prepared stage:

```powershell
$env:PIXEL_PORT_GATE4_STAGE = Join-Path $PWD 'private/local-run'
python -m unittest discover -s tests -p 'test_gate*.py' -v
```

`test_gate5.py` runs the prepared game's original save entry points in separate processes with isolated directories. It checks missing-save creation, mixed unlock/treasure persistence, New Game reset, preservation/recovery of twelve malformed or invalid XML examples, explicit storage failure, and a Windows sharing-lock failure that preserves the current save and removes temporary files:

```powershell
$env:PIXEL_PORT_GATE5_STAGE = Join-Path $PWD 'private/gate5-final'
python -m unittest discover -s tests -p 'test_gate5.py' -v
```

Runtime probes are `Port.Launcher --probe-content`, `--probe-audio`, `--probe-menu-audio`, and `--probe-save <read|write|reset> <isolated-directory>`. A scripted run must observe gameplay, pause/resume, return to title, and its exit input to pass. The save-completion and relaunch scripts additionally verify the original game completion and Continue paths; see [save behavior](../docs/foundation/save-behavior.md). Windows output peaks and manual listening supplement cue-state checks; see [audio conversion](../docs/foundation/audio-conversion.md). Installer checks remain for later gates.

`test_desktop_input.py` runs the authored `Port.InputTests` executable against `PIXEL_PORT_DESKTOP_STAGE` (default `private/desktop-input-final`) and rejects a changed gamepad-update anchor before output. The C# checks cover keyboard mappings, visible/unlocked menu targets, timer gates, original input-reader preservation, hook DLL hashing, and absence of visual hook calls. Build `PixelPort.sln` first. `--run --frames 5500 --scripted-desktop` runs the raw-device-state game-loop fixture in an isolated seeded save; see [desktop input validation](../docs/foundation/desktop-input.md).

