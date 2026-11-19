# Gate 2: local preparation engine

**Completed:** 2026-11-10

`Port.Prepare` now provides the common engine for the developer CLI and future installer helper. Given the supported LIVE package, it validates the exact package hash and STFS block table, extracts all 364 files to a new private stage, preserves originals, and creates a run tree with all 340 XNBs decoded through the pinned FNA LZX decoder. The 14 text levels, particle file, and XACT files retain their original relative paths. FNA, its config, and the authored bridge are staged beside the game executable.

The two original managed assemblies are hash and MVID guarded. All 29 candidate method signatures and raw IL hashes are checked before Mono.Cecil rewrites XNA 3.1 references to FNA and the authored GamerServices/storage bridge. The emitted assemblies retain their MVIDs and contain no legacy XNA assembly references. This gate makes no intentional method-body changes.

## Evidence

- `dotnet build PixelPort.sln -c Release -p:NuGetAudit=false -v:q`: zero warnings and errors.
- Two new stages, `private/gate2-final-a` and `private/gate2-final-b`, produced the same manifest SHA-256: `eec2065efb953a2c184bf8acf91ae6ee159740ef9432a79defe0c7ea6ec9e444`.
- Rewritten main assembly SHA-256 in both stages: `626cb6c04c8a09dd1eb7ca11b0175a41f1a7d2d886cbb4a01fc3f5b9c7d3dadb`.
- Rewritten companion assembly SHA-256 in both stages: `35aa84235f31743a2672ded49e5e993e903a8d62bb3ffa5039bbf15f69032896`.
- All 340 prepared XNB SHA-256 values matched the earlier standalone decoder output. The prepared content passed `Port.Smoke --frames 3` and loaded `TITLE/TITLE_Bg` at 1280×720.
- A one-byte-corrupt package and an altered method IL anchor failed with no output directory. `git check-ignore` confirmed the package, prepared assemblies, and generated reports remain ignored.

The original game has not launched. The bridge's gameplay behavior, save round trip, and installer are for later gates.
