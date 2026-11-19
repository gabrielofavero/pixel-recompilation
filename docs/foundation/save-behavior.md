# Local saves

The desktop bridge uses `%LOCALAPPDATA%\Pixel Recompilation\Saves\Player.sav`, outside the installation directory. All four controller slots share this local player identity; this is one Windows-user save, not four Xbox profiles. `PIXEL_PORT_SAVE_DIR` overrides the directory for isolated validation. Scripted runs default to their prepared stage's `probe-saves` directory and honor an explicit override.

The original `CLASS_SAVEDATA` XML remains version 1: seven level-completion flags and six rows of three treasure flags. The original game owns progression, treasure updates, the New Game reset, and Continue selection. Local preparation replaces only the three hash-guarded storage methods (`DoLoadGame`, `DoSaveGame`, and `DoCreate`) with generic bridge calls using that original data type.

- A missing save creates fresh progress through the original initialization path.
- Malformed, truncated, oversized, unsupported-version, or structurally invalid XML is moved to `Player.sav.corrupt-<unique-id>` before fresh progress is created. The damaged bytes remain available for recovery. The loader prohibits DTDs and limits XML to 65,536 characters.
- Saving serializes to a unique temporary file in the same directory, flushes it to disk, then replaces the current save. `Player.sav.bak` retains the previous version. The first save uses a rename from the temporary file. Temporary files are removed after an ordinary write failure.
- Storage access and write failures propagate to the launcher's error log and fail the run. They do not silently report success or replace unreadable files with fresh progress. There is no in-game recovery dialog or automatic restoration of `.bak`; recovery of a backup is manual.
- New Game retains the original confirmation/reset behavior and writes cleared progress.

The original game pauses when the selected controller disconnects once its intro/fade permits pausing; reconnecting and pressing Start resumes. This is retained original `GAMEMain.HandleInput` behavior, established by private IL/navigation inspection. The save checks do not constitute a new physical-disconnect test.

## Validation

`Port.Launcher --probe-save <read|write|reset> <isolated-directory>` invokes the prepared game's original initialization/save/reset entry points in a fresh process. It prints the loaded version, levels, and all treasure flags as `SAVE_PROBE_RESULT` JSON. It refuses the normal user save directory. The write fixture creates a mixed pattern across every treasure row and multiple unlocks; the read operation leaves valid save bytes unchanged.

`--run --scripted-save complete` invokes the original level exit event in the tutorial and Grassland after each level intro. The original game then handles its victory timer, completion flag, save call, and next-level loading. `--run --scripted-save continue`, in a separate process with the same isolated directory, verifies that Continue unlocks/selects Cave and enters that level. Both runs require gameplay, pause/resume, return to title, and clean exit. Frame-limit termination without those observations fails.

This assisted exit-event fixture validates save integration; it does not demonstrate controller-only traversal of either level. Controller acceptance is supplied by the user. Full manual level completion and save/relaunch on a clean account remain part of final Windows acceptance.
