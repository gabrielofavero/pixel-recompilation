`inspect_stfs.py` verifies the supported LIVE/STFS package and emits a metadata inventory. `inspect_360_assembly` emits version-checked managed API and IL metadata. `decode_360_xnb` expands local LZX-compressed XNBs into ignored `private/` output; `inspect_content.py` inventories their type readers. `gate1_evidence.py` compares all reports against the supported-version and patch-anchor manifests. `inspect_assembly` is a general assembly inspector carried over from the reference project for future API mapping.

`prepare-cli` invokes the reusable `Port.Prepare` engine. After `scripts/bootstrap-fna.ps1 -Offline` and a Release build, run:

```powershell
dotnet tools/prepare-cli/bin/Release/net10.0/prepare-cli.dll game/pixel-360 private/prepared-01 private config/supported-versions.json config/patch-anchors.json out/deps/managed/FNA.dll
```

The output directory must be new and beneath the private root. `original/` preserves package bytes; `run/584E07D1/` contains locally decoded content, rewritten assemblies, FNA, and the authored bridge. `manifest.json` records deterministic per-file hashes. The engine is also directly callable through `PreparationEngine.Prepare` by the future installer helper.

Preparation also converts XMA to PCM with hash-pinned vgmstream and normalizes five static interactive XACT cues. An optional seventh argument supplies the decoder directory; by default it uses `out/deps/vgmstream-r2168`. [Audio conversion](../docs/foundation/audio-conversion.md) documents the mapping and licenses. `measure-audio.ps1 -Seconds 10` reads Windows per-process output peaks and session volume/mute state for audible-output checks without recording audio.

