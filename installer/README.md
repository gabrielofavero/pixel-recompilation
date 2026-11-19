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

The decoder is used only during setup. Its notices ship with the installation. Public distribution still needs the matching codec source/build material and replacement/relinking provisions described in [audio conversion](../docs/foundation/audio-conversion.md); a local installer build does not close that release gate. Nothing is published by the build script.

