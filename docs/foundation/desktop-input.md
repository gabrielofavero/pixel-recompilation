# Desktop input enhancement

The `desktop-input-v1` enhancement adds keyboard input and mouse menu hit testing through `Port.Hooks.dll`. It changes input behavior only. Original artwork, labels, layout, button glyphs, and rendering stay unchanged. Mouse movement uses the original title/Continue selection highlight; confirmation buttons retain their existing appearance.

## Controls

| Action | Keyboard / mouse |
| --- | --- |
| Move, climb, crouch; navigate menus/puzzles | WASD or arrow keys |
| Jump / confirm / original A button | Space or Enter |
| Run / original X button | Left or right Shift |
| Interact, read signs, cancel / original B button | E or Backspace; left mouse button during gameplay |
| Scan mode / original left trigger | Hold Tab |
| Zoom/confirm a scan selection | Space or Enter |
| Special action / original Y button | Q |
| Pause/resume gameplay | Esc or P |
| Return to title while paused | Backspace |
| Back in menus; exit from the title | Esc |
| Original left/right shoulder buttons | Page Up / Page Down |

At the start prompt, Space or Enter selects the local player. Keyboard and the selected controller feed the same original pressed/held input reader. For puzzles displaying Xbox button glyphs, use **A → Space, B → E, X → Shift, Y → Q**. The glyphs are retained as requested.

Mouse movement selects available title menu entries and unlocked Continue levels. Left click activates the pointed option, the original New Game confirmation buttons, the start/intro/outro/ending prompts when ready, the title's Exit prompt, or the existing pause actions (Main Menu on the left, Continue on the right). Hidden Continue/Purchase options and locked levels have no mouse target. Mouse clicks during gameplay do not move or jump the player.

A stationary pointer does not reselect an option after keyboard/controller navigation. Menu clicks are edge-triggered. During gameplay, the left mouse button feeds the original B pressed/held input, just like E or Backspace. Held keys and the left mouse button are suppressed across menu/context transitions until released. Desktop input is ignored while the window is inactive, and focus restoration requires releasing held keys/the mouse button before accepting another action. When desktop input owns the session, losing focus uses the original disconnected-input pause path. Controller activity restores controller ownership and its original disconnect behavior.

Mouse coordinates come from FNA's backbuffer-scaled `Mouse.GetState`; the hook does not scale them a second time. Menu row bounds derive from the original sprites, origins, and scales, with nonoverlapping row heights. Pause/Exit hit regions correspond to existing artwork; no additional UI is drawn.

## Hook boundary

- `src/Port.Hooks/` contains authored keyboard translation, input sampling/focus handling, reflective access to the supported game's fields, and menu hit targets. It contains no decompiled game source and has no drawing hook.
- `src/Port.Prepare/Patches/DesktopInputPatch.cs` is the enhancement's installation patch, separate from framework compatibility fixes. It verifies the already pinned `CLASS_GAMEPAD.Update` anchor, inserts a prefix/postfix, and redirects its one `Keyboard.GetState` call. Every original reader opcode is retained, including controller polling, input timers, and native button processing.
- The original package/extracted source bytes remain unchanged. Only the ignored prepared executable receives the guarded hook calls. No game source project is edited.
- Preparation 0.3.0 stages `Port.Hooks.dll`, records its SHA-256, and lists `desktop-input-v1` in the manifest. Future enhancements belong in their own hook and installation patch rather than in the original game logic.

After changing hooks or installation patches, prepare a new stage to keep the hook DLL and its manifest hash consistent. `stage-launcher.ps1` refreshes the launcher/native runtime; preparation owns the hook DLL.

To bypass the enhancement for a baseline comparison:

```powershell
$env:PIXEL_PORT_DISABLE_DESKTOP_INPUT = '1'
dotnet private/desktop-input-final/run/584E07D1/Port.Launcher.dll --run
Remove-Item Env:PIXEL_PORT_DISABLE_DESKTOP_INPUT
```

The DLL must remain present because the prepared executable references its entry points. Bypass calls the prior keyboard reader and leaves controller state unchanged. `PIXEL_PORT_TRACE_INPUT=1` logs accepted mouse targets without changing behavior.

## Validation

Build the solution and prepare a new stage using the normal preparation command, then stage the launcher. `tests/Port.InputTests` checks bindings, hidden/locked targets, timer gates, original input-reader opcode preservation, absence of visual hooks, and hook manifest identity. `test_desktop_input.py` runs these checks and verifies changed input anchors are rejected before creating an output stage.

```powershell
$env:PIXEL_PORT_DESKTOP_STAGE = Join-Path $PWD 'private/desktop-input-final'
$env:PIXEL_PORT_GATE4_STAGE = $env:PIXEL_PORT_DESKTOP_STAGE
$env:PIXEL_PORT_GATE5_STAGE = $env:PIXEL_PORT_DESKTOP_STAGE
python -m unittest discover -s tests -p 'test*.py' -v
```

The game-loop desktop fixture requires a disposable seeded save and invokes the same hook with raw keyboard/mouse state samples, rather than sending input to other desktop applications:

```powershell
$env:FNA3D_FORCE_DRIVER = 'Vulkan'
$env:PIXEL_PORT_SAVE_DIR = Join-Path $PWD 'private/desktop-input-final/desktop-saves'
dotnet private/desktop-input-final/run/584E07D1/Port.Launcher.dll --probe-save write $env:PIXEL_PORT_SAVE_DIR
dotnet private/desktop-input-final/run/584E07D1/Port.Launcher.dll --run --frames 5500 --scripted-desktop
```

This fixture tests held Enter, Credits, keyboard navigation under a stationary pointer, New Game confirmation/cancel, locked/unlocked level selection, new-game reset, movement/run/jump, scan/interact/special input slots, focus loss/restoration, pause/resume, title return, and exit. It must finish all steps; reaching the frame limit fails. Physical mouse/keyboard feel and complete gameplay traversal remain manual validation.
