Run `bootstrap-fna.ps1` to verify and build pinned FNA dependencies. `installer/build.ps1` builds the offline Windows installer; `installer/release.ps1` also packages source archives, notices, release notes, and checksums.

Bootstrap also verifies and extracts the pinned vgmstream archive for local audio preparation; offline operation requires five cached archives. `stage-launcher.ps1` copies the built launcher and native runtime into a prepared private stage. Audio conversion and dependency notices are documented in [audio conversion](../docs/foundation/audio-conversion.md).

`package-release.py` implements the release packaging step using Python 3.11+ and the standard library. Run it through `installer/release.ps1`; use `-SkipInstallerBuild -Offline` to package an existing current installer with cached dependency sources. It snapshots tracked working-tree source plus the release tooling's explicit file list, excludes private/build data, verifies pinned archives and decoder DLLs, and creates deterministic ZIP entries and SHA-256 manifests under ignored `out/release/`. See [release packaging](../installer/README.md).

