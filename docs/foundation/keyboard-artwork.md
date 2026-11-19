# Keyboard prompt artwork

`assets/keyboard-prompts/keyboard.svg` is the editable, port-authored vector reference: uppercase alphabet, digits, the original-style A/B/X/Y construction, and a nine-cell stepped circle. It follows the original 128px canvas, 8px tile pitch, 7px white cells and soft glow. Keyboard badges fit one to three characters without enlarging the sprite. The renderer uses rectangle geometry from the SVG itself; it requires no system font, SVG library or PNG exports.

`layouts.json` maps 23 original textures to labels, placement, clear regions and white/blue/orange treatments. SPC means Space, SFT Shift, BSP Backspace, ENT Enter, PGU/Page Up and PGD/Page Down. The sheet includes those alternate bindings as well as WASD, E, Q, P, ESC and TAB. Existing arrow controls remain valid. Labels identify a primary key; alternative bindings still work as documented in [desktop input](desktop-input.md).

The swaps cover title confirmations and Exit, Continue Back/OK, Credits Back, Help and Options Back, the start caption, intro/outro/ending confirmation, sign interactions and dialogue dismissal, scan confirm, the scan trigger indicator, the special-action gauge in both atlas frames, zoom escape/goal, puzzle face/direction buttons, and both pause actions. Gameplay B actions display E; confirmation cancellation, Continue Back and Credits Back display BSP. Title Exit, Help and Options Back, and returning to title from pause retain the original controller Back/Select symbol for keyboard and mouse users too. ESC appears only on pause Resume. Backspace and the unlabelled Esc alternative both go back in menus; in gameplay Esc pauses/resumes and Backspace returns to title only while paused. The glyph-only source retained in the original build is replaced wherever that texture is shared, including sprite copies and static puzzle sprites. Artwork follows the current input bindings.

The shared presentation hook updates loaded textures before drawing. Original native pixels are retained in a weak cache, uploaded back exactly when controller input takes ownership, and released with their texture. Dimensions, atlas layout, animation frames, positions and click targets remain owned by the game. Opaque/semitransparent background patches interpolate clean neighboring scanlines to remove the old icon while retaining background opacity. Other regions remain bit-for-bit unchanged. The blue and orange variants use the same vector geometry.

The full special-action gauge alternates between a normal and expanded Q badge at the original Y prompt's animation speed. Both badges share a center within their 360px atlas frames; the empty/charging frame remains unchanged. The game still owns when the gauge starts and stops pulsing.

## Editing while playing

Both files are embedded in `Port.Hooks.dll`, so an ordinary prepared game has no extra deployment dependency. For live editing, set the artwork directory before launching:

```powershell
$env:PIXEL_PORT_PROMPT_ART_DIR = Join-Path $PWD 'assets/keyboard-prompts'
. ./dev.ps1
run-game
```

The game checks the two files once per second. A valid edit reloads keyboard textures on the next draw; controller artwork stays exact. Invalid/partially saved files retain the last valid artwork and log `PROMPT_ART_RELOAD_FAILED`. Removing the override files restores embedded defaults. The default external location is `assets/keyboard-prompts` beside the launcher. Remove the environment variable to return to normal embedded artwork.

Edit symbol rectangles and their zero-origin `viewBox` in the SVG; previews reuse the same symbols. Runtime symbol geometry supports rectangles only, so editor-added paths/transforms are not part of this format. Layouts support a `label`, `[x,y,width,height]` `bounds`, optional `clear` region, `badge` (default true), `background` and `style`. Live edits use supported asset names and dimensions. One-to-three-character badges are validated before accepting a reload. `tools/build_prompt_svg.py` regenerates the initial SVG and overwrites manual SVG edits; use it only intentionally.

## Texture audit and validation

All **268 textures** in the supported package's 340 XNB files were reviewed in seven contact sheets, including every atlas frame. `assets/keyboard-prompts/texture-audit.json` records every texture's name, size and disposition. The other files are three fonts and 69 custom data assets. Decorative lettering (including the zoom map's START tile), puzzle art, timer symbols, cursor arrows and the start-caption contour have no controller-specific binding to replace and retain original art. Sign text is translated independently by `TutorialHints`.

Regenerate private reference images and the metadata inventory from already decoded local content:

```powershell
python tools/audit_prompt_textures.py private/decoded-xnb private/prompt-audit --review-report assets/keyboard-prompts/texture-audit.json
```

Pillow is required for the diagnostic script. Original/reference PNGs stay in `private/`. After building and preparing a fresh stage, export comparisons and verify actual GPU uploads:

```powershell
dotnet private/prompt-art-verified/run/584E07D1/Port.Launcher.dll --probe-prompts private/prompt-art-review
```

The probe checks all 23 layouts, unchanged pixels outside authored regions, repeated swaps, exact native controller restoration, valid live edits, rejection of incomplete edits and recovery after correcting an edit. `Port.InputTests` also checks badge lettering, ring geometry and label validation. The desktop game-loop fixture covers menu and gameplay integration; the ultrawide fixture renders signs, scan, zoom, puzzles and pause in all seven levels.
