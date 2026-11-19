# Arkedo Series - 03 PIXEL! (recompilation)

Hello! This is a quick recomp port of **Arkedo Series - 03 PIXEL!**. This game was also available on PS3 and RPCS3 can run it perfectly, so this is more a novelty project and a possibility if people wish to expand this game to other platforms.

I went pretty light on enhancements: keyboard support, custom resolutions (ultrawide included) and display modes (window, borderless and fullscreen)

Keyboard bindings and mouse menu support are documented in [Desktop input](docs/foundation/desktop-input.md).

For local development, open PowerShell in this repository:

```powershell
. .\dev.ps1
build-game
run-game
```

`build-game` builds the Release solution, prepares your `game/pixel-360` package into a fresh ignored private stage, and checks that it loads. Missing FNA dependencies are bootstrapped automatically; use `build-game -Offline` with cached dependencies or `build-game -Package <path>` for another package location. Each build keeps previous stages intact.

`run-game` opens the latest dev build from your interactive terminal and waits until you close the game, keeping startup errors visible. It builds automatically if needed; `run-game -Build` rebuilds before playing. The functions work from other directories after dot-sourcing. Rebuilding preserves normal player saves.

How to install

Just run the installer and provide your game package or folder. It's a straightfoward process

About assets
The only assets that we have in the project are photoshop adaptations of the cover and an AI recriation of the game icon. No in game code or assets on project.

AI Usage

AI assisted.
