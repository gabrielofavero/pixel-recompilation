# Gate 1 complete: supported version and API surface

The exact supplied LIVE package is pinned in `config/supported-versions.json`. `tools/inspect_stfs.py` rejects a different package hash before producing a report or extracting files. For the supported package, it verifies the STFS file table, each allocated data block, safe Windows paths, title metadata, both assembly hashes, and expected counts. It does not authenticate the package signature.

`tools/inspect_360_assembly` checks each extracted managed assembly hash, name, version, and MVID before producing a report. Its schema v2 includes all AssemblyRef, TypeRef, and MemberRef entries with tokens and signatures; type bases, interfaces, explicit overrides, virtual reuse-slot methods and base chains; every method's signature and raw IL SHA-256; and content API references. `tools/inspect_content.py` inventories the reader type names in all 340 locally decoded XNBs. The reports remain under ignored `generated/`.

`config/patch-anchors.json` pins 29 original-method signatures and IL hashes in the game and content-reader assemblies. They are candidate patch boundaries, not patches. `tools/gate1_evidence.py` compares the manifest, package inventory, both assembly reports, decoded-content report, and anchors.

## Reproduce

```powershell
python tools/inspect_stfs.py game/pixel-360 --output generated/pixel-inventory.json
dotnet build PixelPort.sln -c Release -p:NuGetAudit=false -v:q
dotnet tools/inspect_360_assembly/bin/Release/net10.0/inspect_360_assembly.dll "private/pixel-360/584E07D1/Arkedo Series - 03 PIXEL!.exe" --output generated/pixel-assembly.json
dotnet tools/inspect_360_assembly/bin/Release/net10.0/inspect_360_assembly.dll private/pixel-360/584E07D1/Scale_Shared_ContentGame.dll --output generated/pixel-companion-assembly.json
python tools/inspect_content.py private/decoded-xnb --inventory generated/pixel-inventory.json --output generated/pixel-content-readers.json
python tools/gate1_evidence.py
python -m unittest discover -s tests -p test_gate1.py -v
```

Observed: build passed with zero warnings/errors; package inspection reported 364 files and 9,850 verified blocks; the main assembly reported 92 types, 129 TypeRefs, 316 MemberRefs, and 581 methods; the companion reported 5 types, 17 TypeRefs, 20 MemberRefs, and 3 methods. The XNB inventory found nine reader types, including `Scale.ZoomMapContentReader`. Evidence verification found 29 matching patch anchors. All eight local tests passed. They cover deterministic inventory, corrupted data, unsupported volume layout, noncontiguous file entries, unsafe Windows names, case-insensitive path collision, an unknown package hash, and an incorrect assembly MVID. Rejected CLI inputs produced no report.

The first game runtime and installer gates remain open. Type names and IL alone do not prove FNA API compatibility, content-load coverage, audio, saves, or playability.

