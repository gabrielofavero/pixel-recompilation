# Windows installer

The installer follows `z0mb1es-rec0mp1led`: one offline, per-user Inno Setup executable and a self-contained .NET preparation helper. It uses PIXEL's description, `assets/installer/wizard-large-*.bmp`, and `assets/icon/pixel.ico` for setup, the uninstaller, shortcuts, and the game EXE. The wizard badge uses `pixel.png`.

Build from the repository root with .NET SDK 10.0.302, Inno Setup 6.7.1, and the bootstrapped dependencies:

```powershell
./installer/build.ps1
# With all pinned dependencies/runtime packs already cached:
./installer/build.ps1 -Offline
```

The result is `installer/out/PIXEL-1.0.0.exe`. The build checks the self-contained runtime packs, decoder hashes, and payload allow-list. It never copies the developer's game package or prepared game data into setup. The player needs only setup and their supported Xbox 360 LIVE package; no Python, .NET installation, SDK, compiler, or internet access is required.

The pages are Welcome, Folders, Game package, Shortcuts, Ready, Progress, and Finish. There are no flashing-light or optional texture-import pages. Folders selects installation and saves together; PIXEL has no pictures-import feature. Installation defaults to `%LOCALAPPDATA%\Programs\Arkedo Series - 03 PIXEL!`, and saves to the Windows Saved Games known folder under `Arkedo Series - 03 PIXEL!` (normally `%USERPROFILE%\Saved Games\Arkedo Series - 03 PIXEL!`). Inno's `{usersavedgames}` resolves the folder through Windows, including redirected locations. The folder picker and `/SAVEDIR` override remain available. Setup preserves the previously chosen save folder on reinstall, including an existing Documents location. Start Menu and Desktop shortcuts are independent; Start Menu starts checked. Finish offers an unchecked Launch option.

Next on the package page checks the exact supported package SHA-256. The helper validates its contents again, prepares game code/content/audio, hashes the installed files, and runs the launcher's compatibility check before committing. Progress reports each stage. Cancel preserves the previous installation; setup keeps `current.previous` until Inno finishes and rolls it back if a later setup step fails. Reinstall reuses verified content when the preparation version and runtime dependencies match; otherwise it prepares the supplied package again. The original package stays where the player selected it.

The installed layout is:

```text
{install}/current/run/584E07D1/PIXEL.exe
{install}/current/run/584E07D1/Content/
{install}/current/run/584E07D1/notices/
{install}/current/manifest.json
{install}/current/install-manifest.json
{install}/current/install-report.txt
{install}/locations.ini
{chosen saves folder}/Player.sav
```

The release EXE embeds the game icon and opens without a console. Opening it directly also honors the save folder in `locations.ini`. Normal launches log to `%LOCALAPPDATA%\Pixel Recompilation\Logs\run.log`. Developer builds keep their existing `Port.Launcher.dll` interface and diagnostics. Uninstall removes installed game files and shortcuts while preserving saves and the source package. Control Panel reports the measured installed size.

Silent installs accept Inno's standard switches plus `/PACKAGE=<LIVE-file>` and `/SAVEDIR=<folder>`:

```powershell
./installer/out/PIXEL-1.0.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /PACKAGE="C:\Games\pixel-360" /SAVEDIR="C:\Saves\PIXEL" /LOG="C:\Temp\pixel-setup.log"
```

The helper exposes `install`, `validate-package`, `verify-installed`, `rollback`, and `finalize`; run it without arguments for usage. Integration checks are in `tests/test_installer.py`.

The decoder is used only during setup. Its notices ship with the installation. For public distribution, build the accompanying source archives and release documents as well:

```powershell
./installer/release.ps1
# Package the existing installer with cached source archives:
./installer/release.ps1 -SkipInstallerBuild -Offline
```

Python 3.11 or newer is required for release packaging. The first run downloads the SHA-256-pinned codec sources in `installer/config/source-archives.json`; later runs can use `-Offline`. Output is `out/release/PIXEL-1.0.0/`:

- `PIXEL-1.0.0.exe`: the offline Windows x64 installer.
- `PIXEL-1.0.0-source.zip`: current port source, including local edits and build instructions, with a base commit reference and per-file hashes. Logs, local game data, saves, and build outputs are excluded.
- `PIXEL-1.0.0-third-party-sources.zip`: FNA and decoder/codec sources, upstream build instructions, the shipped FFmpeg configuration, licenses, download URLs/hashes, and [replacement instructions](codec-replacement.txt).
- `RELEASE-NOTES.txt`, `THIRD-PARTY-NOTICES.txt`, and `SHA256SUMS.txt`.

Upload all six files together to the release. The notes include an FFmpeg attribution and source location; retain it in the download-page description. Keep the corresponding source archives available beside every binary release. The packaging command verifies archive hashes and the decoder payload against the pinned upstream DLLs, and checks ZIP integrity. It packages only a built installer; rebuild setup first after changing port code or installer files. `-SkipInstallerBuild` is intended only for an already-current installer. Nothing is published by either script.

