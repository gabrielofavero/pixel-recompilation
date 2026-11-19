`test_gate1.py` runs local-package-dependent rejection and reproducibility checks for the STFS and managed inspectors. It skips when the ignored source package is absent.

`Port.Launcher --probe-ultrawide <private-capture-directory>` renders 259 fixture scenes across all seven original levels using simulated 16:9/21:9/32:9 outputs. It checks deferred point sampling, wrap/mirror/clamp and blend compatibility, camera restoration, full-width scan targeting/cursor clamps, unclamped zoom-map cameras and movement, inspection aspect ratios, stable pause/resume widths without crossfades (including the first draw, open dialogue and 32:9), 1080p on an ultrawide monitor, upper backgrounds, and full-screen fade coverage, and saves PNGs for visual review. This is separate from gameplay traversal; see [ultrawide presentation](../docs/foundation/ultrawide.md).

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

`test_desktop_input.py` runs the authored `Port.InputTests` executable against `PIXEL_PORT_DESKTOP_STAGE` (default `private/desktop-input-final`) and rejects changed gamepad-update, title-display, tile-drawing, camera-initialization and scan-processing anchors before output. Use a freshly prepared stage after hook changes. The C# checks cover keyboard mappings, visible/unlocked menu targets, timer gates, original input-reader preservation, hook DLL hashing, presentation hooks, resolution limits, letterboxing and settings persistence/fallback. Build `PixelPort.sln` first. `--run --frames 6200 --scripted-desktop` runs the raw-device-state game-loop fixture in an isolated seeded save, including Help and Options, all display modes, resolution cycling, mouse/keyboard Back and Credits; see [desktop input validation](../docs/foundation/desktop-input.md). Set `PIXEL_PORT_CAPTURE_OPTIONS` to a private directory to capture the menu screens during this fixture.

`--probe-prompts <private-capture-directory>` checks all 23 keyboard artwork layouts through the GPU, exports keyboard/controller comparisons, verifies exact controller restoration and untouched regions, and exercises valid/invalid live edits. See [keyboard artwork validation](../docs/foundation/keyboard-artwork.md). `Port.InputTests` validates the embedded SVG alphabet, ring and one-to-three-character labels without a graphics device.

