# Gate 3: original game loop

**Completed:** 2026-11-11

`Port.Launcher` validates and loads the locally prepared original game through FNA. The launcher reaches the logo, title, intro, and level using the original 1280×720 game loop. The first desktop port accepts controller input. No original game assembly, content, or prepared output is committed.

The bridge maps XNA 3 graphics calls to FNA. A manual visual check exposed white rectangles around transparent sprites; `SpriteBlendMode.AlphaBlend` now uses `BlendState.NonPremultiplied`. The user confirmed that graphics look correct in the updated stage. Native XACT/FAudio audio calls are temporarily bypassed; restoring music and sound is gate 4.

## Evidence

- `dotnet build PixelPort.sln -c Release -p:NuGetAudit=false -v:q`: zero warnings and errors.
- `dotnet private/gate3-blend/run/584E07D1/Port.Launcher.dll --check-load`: 551 methods JIT prepared, zero failures.
- A scripted `--run --frames 13000 --scripted-controller --play-frames 10800` run lasted 208.3 seconds, spent 10,800 level updates in GAME, paused/resumed, returned to TITLE, and exited cleanly. Ignored log: `private/gate3-final/play-soak.log`.
- A physical Xbox One controller started a new game, entered the level, paused/resumed, returned to title, and exited cleanly. Ignored log: `private/gate3-final/manual-play-user.log`.
- The corrected blend stage passed a scripted title → level → pause/resume → title → exit check. The user launched the same stage on their desktop, reached GAME, and confirmed the graphics look correct. Ignored logs: `private/gate3-blend/scripted-recheck.log` and `private/gate3-blend/manual-play-user.log`.
- Corrected stage manifest SHA-256: `3f88e2f852156c885c5036e1834a807811b1bf05f5ca87be82a65c7fd2ac0141`. Prepared game executable SHA-256: `ffa59623fbd806fb852aca3d6a5a52040ddf2c5e29d4143b79a5bcf638014edd`. Authored bridge SHA-256: `ea9ea39aa9f01daac9a1d393d0ca301023d9dcbf83a9cd7bbce35af75c3df1aa`.

Music and sound effects are absent in this stage. Gate 4 covers audio and full content-path checks; controller/save progression and the installer remain in later gates.
