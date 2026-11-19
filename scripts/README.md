Run `bootstrap-fna.ps1` to verify and build pinned FNA dependencies. Build and staging scripts for the playable game and installer will be added at their implementation gates.

Bootstrap also verifies and extracts the pinned vgmstream archive for local audio preparation; offline operation requires five cached archives. `stage-launcher.ps1` copies the built launcher and native runtime into a prepared private stage. Audio conversion and dependency notices are documented in [audio conversion](../docs/foundation/audio-conversion.md).

