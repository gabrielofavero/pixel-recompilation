# Ultrawide presentation

Normal platforming, sign dialogue, scanning and zoom maps expand horizontally to the selected resolution's aspect ratio, up to 32:9. The virtual height remains 720; a 3440 x 1440 display uses a 1720 x 720 gameplay canvas, and 5120 x 1440 uses 2560 x 720. Character size and vertical visibility are preserved. Narrower screens retain the 1280 x 720 view with letterboxing as needed.

Menus, credits, level introductions and button puzzles retain 16:9 with black bars. Changing between the wider world and button puzzles crossfades the previous rendered frame over twelve draws so camera clamping changes at level edges do not produce a hard cut. Pausing preserves the current world width, camera, HUD and dialogue positions throughout the original pause fade. The pause artwork stays centered at its original scale; its dimmed border extends across the wider edges. Resuming does not resize the canvas or start an aspect-ratio crossfade. Hearts, scan and special-action meters keep their original left-edge margins; treasures keep their right-edge margins. Dialogue and saving/win/lose messages remain centered.

Display settings include common ultrawide resolutions and the current monitor's native ultrawide resolution, filtered by both desktop dimensions and a 2160-pixel height limit. They also retain the original 16:9 choices. Presentation compensates for FNA's backbuffer-to-window scaling, so a selected 1920 x 1080 resolution remains 16:9 with side bars on an ultrawide monitor. The physical window no longer forces a wider gameplay canvas.

The original simulation and collision coordinates stay at 1280 x 720. The guarded `GACM_Init` patch pins this logical size instead of letting level creation inherit the physical graphics viewport during Update. The authored `Ultrawide` hook temporarily widens and clamps the rendering camera, then restores both original camera matrices before the next update. Platforming simulation does not receive the wider camera. The guarded `ProcessScanMode` prefix temporarily supplies the same expanded camera to original scan hit testing, converts the screen-space cursor into the full canvas, and widens its original cursor limits and tile-search region. Its postfix restores the simulation camera and cursor coordinate convention. This lets the magnifier select objects in both added side regions, including at level boundaries and during zoom transitions.

The `ultrawide-platforming-v1` patch also replaces the tile drawing width and sprite clipping width with the current rendering width. Sprite submission anchors HUD sprites to their respective edges, centers dialogue and messages, extends full-screen fade/dimming quads, and reconstructs darkness contours around the player light across the whole wider canvas. Font submission centers dialogue. Scan prompts and zoom timers retain their left margins; zoom-map previews and Escape prompts retain their right margins. Zoom-map shading and scan zoom effects cover the whole canvas. Zoom maps preserve their original unclamped camera and magnification, translated to the wider canvas center. Level data, collision rules, puzzle rules and source assets remain unchanged. Hooks and patch sites are recorded in the preparation manifest and guarded by the original method signatures and IL hashes. Preparation version is 0.7.0.

## Plaque captures

The internal `--probe-signs <capture-directory>` launcher command enumerates sign tiles and their concealed `m_otherTile` alternatives in all seven levels. It teleports the player beside each plaque and invokes the original `StartSignMode` method. Each plaque gets eight native canvas PNGs: interaction prompt and opened dialogue, keyboard and controller hints, at exact 16:9 (1280 x 720) and 21:9 (1680 x 720). The probe freezes gameplay, settles the sign's dimming overlay and prompt bounce, and records coordinates, concealed status, original text and displayed text in `manifest.json`. It checks canvas size, hint selection, text translation and camera restoration. These are posed original-renderer captures, not automated traversal or physical monitor captures. Player saves are isolated beneath the capture directory.

```powershell
$env:FNA3D_FORCE_DRIVER = 'Vulkan'
$env:PIXEL_PORT_PROMPT_ART_DIR = Join-Path (Get-Location) 'assets/keyboard-prompts'
dotnet <stage>/run/584E07D1/Port.Launcher.dll --probe-signs private/plaque-review
python tools/build_sign_gallery.py private/plaque-review
```

Open `private/plaque-review/index.html` for side-by-side images, expandable prompt/controller variants and full-size PNG links. Captures, report and game-derived text stay in ignored private output.

An optional third argument filters plaques by a case-insensitive substring of their original text, for example `--probe-signs private/plaque-run-hint holding`. This captures just the run tutorial plaque in both aspect ratios and input modes while still searching all levels.

## Validation

`Port.InputTests` covers native ultrawide resolution choices, 16:9/21:9/32:9 view calculations, camera centering and both level boundaries, state selection, darkness coverage, and installation of the drawing hooks. `test_desktop_input.py` also rejects changed tile drawing, scan processing and camera anchors before output.

After preparing a stage and staging its launcher:

```powershell
$env:FNA3D_FORCE_DRIVER = 'Vulkan'
dotnet private/ultrawide-a/run/584E07D1/Port.Launcher.dll --probe-ultrawide private/ultrawide-a/captures
```

The probe uses simulated monitor shapes, the original game renderer, and isolated saves. It loads all seven levels and captures 37 fixture scenes per level: center, pause/resume, upper background, left edge, right edge, darkness, scan with pause/resume, zoom map, puzzle, dialogue at the left camera boundary with pause/resume, 32:9 with pause/resume, 1080p on an ultrawide monitor, and a full black fade, plus scan boundaries, targets and cursor clamps in both side regions, scan zoom transitions, original zoom-map movement, zoom-map pause/resume, 16:9 scan/zoom, and 32:9 scan/zoom with pause/resume. It checks canvas dimensions and camera restoration on every draw, including the first pause/resume draw, and rejects aspect-ratio crossfades during pause/resume. Additional first-frame PNGs expose transient errors that settled screenshots miss. It also asserts absence of a leaked drawing context and full fade coverage. It invokes original initialization methods and freezes/poses levels; it does not automatically traverse them. PNGs are ignored private artifacts.

The normal scripted desktop/controller runs separately check gameplay movement, input, focus and pause behavior. Set `PIXEL_PORT_SCRIPTED_ULTRAWIDE=1` for the existing seeded `--scripted-desktop` run to simulate a selected 3440 x 1440 resolution and matching output, and additionally assert that platforming, pause and resume all use a 1720-wide canvas. Pause mouse targets map through the actual canvas into the centered 1280-wide artwork, including in a narrower resized window. This environment variable is read only by the acceptance fixture. Physical ultrawide monitor behavior and complete level traversal remain manual validation.

The XNA3 graphics bridge maps legacy wrap/mirror/clamp enum values to FNA and supplies a mutable point sampler and a separate blend state for each deferred batch. This preserves the mirrored blue gradient above the level backgrounds, additive background decoration and the original pixel edges. The launcher also enables nearest filtering for the final backbuffer-to-window scale. Source title artwork is still 1280 x 720 and retains its baked glow; selecting a larger output does not add texture detail. The rendering probe checks exact GPU pixels for all address modes, point filtering and alpha/additive blending before capturing levels.
