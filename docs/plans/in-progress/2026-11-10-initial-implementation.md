# Initial Windows implementation: controller, playable build, installer

**Status:** In progress. Gates 1–5 are complete with user-reported controller acceptance and an assisted save-progression fixture. Release packaging, setup, and final clean-account acceptance remain.

## Goal and boundaries

Produce a Windows x64 build that launches the player's own copy of **Arkedo Series - 03 PIXEL!**, reaches the title screen, can start and play a level with a controller, saves progress, plays sound, and exits cleanly. Then package it as one Inno Setup executable that asks for the supported LIVE package and prepares game files locally. Do not put original or prepared game code, assets, audio, or decompiled source in Git or setup. The first build supports controllers only; keyboard and mouse are outside the acceptance gate even though the original assembly contains keyboard branches.

After gate 5, the user requested the first enhancement: keyboard controls and mouse menus through a separate hook/patch layer, with no visual edits. This expands input support without changing the historical controller acceptance requirements. See the [enhancement record](../done/2026-11-18-enhancement-01-desktop-input.md).

Use the sibling `z0mb1es-rec0mp1led` top-level structure: `assets`, `config`, `docs`, `game`, `generated`, `installer`, `out`, `patches`, `private`, `scripts`, `src`, `tests`, and `tools`. `game` holds an ignored local source; `private` holds ignored extraction and transformations; `generated` holds ignored analysis reports; `out` holds ignored dependencies/builds. Keep authored runtime code in `src`, local preparation in `src/Port.Prepare`, format tools in `tools`, installer source/helper in `installer`, and version identities in `config`.

## Implementation gates

### 1. Lock the original version and API surface

**Done.** The STFS inspector handles this package's seven-block file table, validates every allocated block, checks the supported package identity, and rejects unsafe Windows names. The managed inspector emits every TypeRef and MemberRef, inheritance and virtual override chains, content API references, and every method's raw IL hash. A separate decoded-content inventory records all XNB type readers. `config/patch-anchors.json` pins 29 candidate methods, including `GameScale`, `CLASS_SAVE`, `CLASS_GAMEPAD`, trial/marketplace menu paths, and the custom content reader. The [gate record](../done/2026-11-10-01-version-api-surface.md) contains commands and results.

### 2. Build the local preparation engine

**Done.** `Port.Prepare` is callable from `prepare-cli` and from a future installer helper. It verifies the exact package and all STFS data blocks, extracts 364 originals, decodes 340 XNBs, and rewrites both managed assemblies with pinned Mono.Cecil. The local bridge supplies GamerServices and storage types while retaining original method bodies. All 29 candidate method signatures and IL hashes are checked before output. Two independent preparation runs produced identical manifests and prepared assembly hashes. Corrupt package and changed IL-anchor inputs failed without an output directory. See the [gate record](../done/2026-11-10-02-local-preparation.md). Runtime behavior remains for gates 3–5.

### 3. Bring up the original game loop

**Done.** `Port.Launcher` loads the prepared `Scale.GameScale` assembly through FNA and runs the original 1280×720 loop. Its 551-method JIT check has zero failures. A 208-second scripted run spent 10,800 updates in GAME, paused/resumed, returned to title, and exited cleanly. A physical Xbox One controller reached the level, paused/resumed, returned to title, and exited cleanly. The first desktop visual check revealed white rectangles around sprites; the XNA 3 alpha blend now maps to `BlendState.NonPremultiplied`, and the user confirmed the corrected graphics. XACT audio remains temporarily bypassed for gate 4. See the [gate record](../done/2026-11-11-03-original-game-loop.md).

### 4. Content and audio coverage

**Done.** All 340 XNB assets load through FNA, including 69 using the custom reader. Local preparation converts 19 XMA and 52 big-endian PCM entries into a desktop PCM wave bank, preserving sample counts, rates, channels, indices, and loops. Five static interactive music cues now select their original sound directly, resolving FAudio's initial silent cue state. All 63 cues and music transport/category volume checks pass. The user confirmed menu and level music/effects in the visible game. See the [gate record](../done/2026-11-18-04-content-and-audio.md) and [conversion/license notes](../../foundation/audio-conversion.md).

### 5. Controller and save behavior

Keep original gamepad semantics and verify controller selection, movement, jump/action, map/zoom, menus, and pause. Remove or disable the original keyboard branches only where they conflict with controller behavior; do not add desktop key bindings in this gate. Adapt GamerServices and storage to a local user save directory outside the installation folder. Preserve XML save format unless evidence requires a versioned migration. **Acceptance:** a controller can start and finish a level; unlocked progress survives exit/relaunch; disconnected-controller and missing/corrupt-save behavior are explicit.

**Done with the recorded validation limits.** The user reports controller validation complete. An assisted original-exit-event fixture saved tutorial/Grassland completion, loaded Cave, exited, and relaunched into Cave through Continue. Separate-process checks preserve all seven level flags and all eighteen treasure flags, verify New Game reset, preserve damaged saves before starting fresh, and retain the current save when a Windows sharing lock blocks replacement. Original XML version 1 is retained in `%LOCALAPPDATA%\Pixel Recompilation\Saves`; storage errors fail explicitly. The user's existing save was tested only through an isolated copy. Controller disconnection retains the original automatic pause behavior (IL inspection); full manual level completion on a clean account remains final acceptance. See the [gate record](../done/2026-11-18-05-controller-and-save.md) and [save behavior](../../foundation/save-behavior.md).

### 6. Build and release audit

Add `build-game` to `dev.ps1` only when it builds the actual launcher, bridge, preparation engine, and tools. Stage a local runnable directory from the user's package. Check the staged directory on a clean Windows x64 account, then audit release files and embedded resources for original game bytes and derived outputs. **Acceptance:** clean `build-game`, local launch, and an audit that setup contains authored code, permitted dependencies, notices, and metadata-only fingerprints.

### 7. One-file Windows installer

Use Inno Setup for Welcome, per-user Destination, LIVE-package picker, Ready, progress, and Finish. A bundled self-contained .NET helper must validate the package read-only before Next, stage extraction/rewrites/content into a temporary versioned directory, verify a launchable result, then atomically switch the current installation. Reinstall/update must preserve the previous working install on failure and reuse verified generated data when safe. Uninstall must leave saves and the source package alone. Setup must work offline without Python, .NET SDK, compiler, or developer tools. **Acceptance:** supported-package install and launch on a separate Windows account; bad/corrupt package rejected before install; cancellation/failure preserves the previous install; reinstall and uninstall preserve saves.

### 8. Final Windows acceptance

Run the installer end to end with a controller on a Windows account without development tools. Verify title, a complete playable level, save/relaunch, audio, and uninstall. Record exact commands, artifact hashes, observed results, and remaining limits in `docs/records/implementation-log.md`. Do not call the game or installer complete until this gate passes.

## Current measured results

- Package SHA-256: `64b5bb62e8cf16c44c04c1b9d2ac5f1c780db1cd28e031cd4315788ab3af4104`; 9,850 verified STFS data blocks; 364 files.
- Game assembly: 92 types, 581 methods, XNA 3.1 and .NET Framework 3.5 references. Companion content reader assembly: 5 types, 3 methods.
- `dotnet build PixelPort.sln -c Release` passes with zero warnings/errors.
- `Port.Smoke --frames 3 --content private/decoded-xnb` opens a Vulkan window, loads `TITLE/TITLE_Bg` at 1280×720, renders three frames, and exits successfully.
- `prepare-cli` produces 364 original files and 368 runnable-stage files (including FNA, its config, the bridge, and the desktop input hooks). The runnable-stage content has 340 decoded XNBs, 14 text levels, and three XACT files. A smoke load from this prepared content directory passes.
- A local prepared game runs through title, intro, level start, pause/resume, return to title, and clean exit under FNA using a virtual controller. Its `--check-load` JIT check passes 551 methods with zero failures. Original XACT calls, category fades, intro unloading, and the original GC measurement are restored after local audio preparation.
- The long play check, manual physical-controller input, corrected blend visuals, full XNB loading, and menu/level audio passed. The user accepted controller validation. Save progression passes the assisted original-completion/relaunch fixture and isolated persistence/recovery checks. Clean-account release, installer, and final manual acceptance remain unverified.
- The first enhancement adds keyboard controls and mouse menus through a separate guarded input hook with no visual edits. The keyboard/mouse fixture passes with no native controller connected; all 20 integration tests pass.

