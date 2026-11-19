# Enhancement 1: mouse menus and keyboard controls

The user requested mouse menu hover/click and an authored keyboard scheme through a separate hook/patch structure, then explicitly required no visual changes. `Port.Hooks` implements the new input behavior; `Port.Prepare/Patches/DesktopInputPatch.cs` installs guarded calls into the prepared executable. No original source, artwork, labels, layout, or rendering methods are edited for this enhancement. Existing menu selection supplies the hover highlight, and the native pointer is visible where mouse actions are available.

Bindings are WASD/arrows for movement/navigation, Space/Enter for jump/confirm, Shift for run/X, E/Backspace for interact/back/B, held Tab for scan, Q for special/Y, Esc/P for pause/resume, and Backspace for title return while paused. See the [full controls and hook boundary](../../foundation/desktop-input.md).

## Implementation

- Retains every original `CLASS_GAMEPAD.Update` opcode and native controller processing. The pinned method receives a prefix, one redirected keyboard sampling call, and a postfix. No draw/display hooks are installed.
- Converts the chosen desktop scheme into the original reader's keyboard vocabulary so the original game owns pressed/held edges, input timers, analog values, menus, and gameplay actions.
- Uses original sprite positions/origins/scales for title, Continue, confirmation, and ready story/ending prompts. Native Exit and pause artwork have matching hit regions. Unavailable menu entries and locked levels cannot be clicked.
- Samples mouse clicks once per press and changes selection only on pointer motion/click, preventing a stationary pointer from overriding keyboard/controller navigation. Suppresses held inputs across context changes and after focus restoration.
- Supports keyboard-only play by treating active desktop input as the selected local input device. Native controller activity retains controller ownership/disconnect behavior. `PIXEL_PORT_DISABLE_DESKTOP_INPUT=1` bypasses the enhancement.
- Preparation 0.3.0 stages and hashes the hook assembly and records `desktop-input-v1` in the manifest.

## Validation

- Release solution build: zero warnings/errors. Prepared JIT check: 551 methods, zero failures. Original evidence: 46 verified IL anchors.
- All 20 Python integration tests passed: desktop input/guard tests plus the prior package, audio, repeated-preparation identity, and save-persistence/recovery tests. The C# runner performed 1,219 binding/target/opcode/manifest checks against the final stage, including the absence of visual hooks.
- Desktop game-loop checks use raw device samples injected at the hook's input sampling boundary, followed by the shipped hook and original game reader. They cover start-button hold, Credits, stationary-pointer arbitration, confirmation cancel/accept, locked and unlocked level selection, intro activation, movement/run/jump, scan/interact/special input slots, focus loss/restoration, pause/resume, title return, and clean exit. Reaching a frame limit fails acceptance.
- The final desktop run passed every step with `CONTROLLER connected=False`. Separate virtual-controller runs with the enhancement enabled and bypassed both passed gameplay, pause/resume, title return, exit input, and clean shutdown (`controller-enabled.log` and `controller-bypass.log`).
- Validation uses disposable saves under the ignored prepared stage. Original package hash remains `64b5bb62e8cf16c44c04c1b9d2ac5f1c780db1cd28e031cd4315788ab3af4104`. Physical desktop input feel and full-level traversal remain manual checks; no new visual treatment was introduced.

## Reproduction and evidence

```powershell
dotnet build PixelPort.sln -c Release -p:NuGetAudit=false -v:q
dotnet tools/prepare-cli/bin/Release/net10.0/prepare-cli.dll game/pixel-360 private/desktop-input-final private config/supported-versions.json config/patch-anchors.json out/deps/managed/FNA.dll
./scripts/stage-launcher.ps1 -Stage private/desktop-input-final
dotnet private/desktop-input-final/run/584E07D1/Port.Launcher.dll --check-load
dotnet tests/Port.InputTests/bin/Release/net10.0/Port.InputTests.dll private/desktop-input-final
$env:PIXEL_PORT_DESKTOP_STAGE = Join-Path $PWD 'private/desktop-input-final'
$env:PIXEL_PORT_GATE4_STAGE = $env:PIXEL_PORT_DESKTOP_STAGE
$env:PIXEL_PORT_GATE5_STAGE = $env:PIXEL_PORT_DESKTOP_STAGE
python -m unittest discover -s tests -p 'test*.py' -v
$env:FNA3D_FORCE_DRIVER = 'Vulkan'
$env:PIXEL_PORT_SAVE_DIR = Join-Path $PWD 'private/desktop-input-final/desktop-saves'
dotnet private/desktop-input-final/run/584E07D1/Port.Launcher.dll --probe-save write $env:PIXEL_PORT_SAVE_DIR
$env:SDL_GAMECONTROLLER_IGNORE_DEVICES = '0x045e/0x02ff'
dotnet private/desktop-input-final/run/584E07D1/Port.Launcher.dll --run --frames 5500 --scripted-desktop
Remove-Item Env:SDL_GAMECONTROLLER_IGNORE_DEVICES
$env:PIXEL_PORT_SAVE_DIR = Join-Path $PWD 'private/desktop-input-final/controller-saves'
dotnet private/desktop-input-final/run/584E07D1/Port.Launcher.dll --run --frames 3200 --scripted-controller
$env:PIXEL_PORT_DISABLE_DESKTOP_INPUT = '1'
$env:PIXEL_PORT_SAVE_DIR = Join-Path $PWD 'private/desktop-input-final/baseline-saves'
dotnet private/desktop-input-final/run/584E07D1/Port.Launcher.dll --run --frames 3200 --scripted-controller
Remove-Item Env:PIXEL_PORT_DISABLE_DESKTOP_INPUT
Remove-Item Env:PIXEL_PORT_SAVE_DIR
```

Preparation needs a new destination. The SDL ignore setting hides this machine's Xbox controller only within the validation process; it is not required for normal play. The desktop run reports `CONTROLLER connected=False`, exercising keyboard/mouse without a native controller. Hook bypass is exercised with the ordinary scripted virtual-controller driver and a separate isolated directory.

Evidence is ignored under `private/desktop-input-final/`: `manifest.json`, `check-load.log`, `tests.log`, `desktop-run.log`, controller regression logs, and isolated saves. The initial failed fixture logs remain local as development evidence; they are not acceptance runs.

- Manifest SHA-256: `153fd7b1583c9b930fc2c26e63d2b629e48de284f8505b1ca2dd82210a6fa95d`.
- Prepared executable SHA-256: `1f45e07a02f10e177742df54e246fd48fa6fde04eaa673c5d534ff7feac3bff5`.
- Hooks SHA-256: `f4b845487e05aa8f408a8bb21bdcbb4ff446894b854f9f0121c05102384c0f29`.

For normal play without test injection, run `dotnet private/desktop-input-final/run/584E07D1/Port.Launcher.dll --run` from the repository. Release packaging, setup, and clean-account final acceptance remain open.
