# Pixel package and runtime evidence

Evidence labels here are **package** (verified bytes and metadata), **IL** (original managed assembly or its private decompiled navigation view), and **observed** (a local desktop run). The decompiled output is ignored private data, not a source project.

| Claim | Evidence | Status |
| --- | --- | --- |
| The local source is `Arkedo Series - 03 PIXEL!`, title ID `0x585503c9`, in a 40,632,320-byte LIVE/STFS package. | `GameInfo.xml` and package inventory; package SHA-256 `64b5bb62e8cf16c44c04c1b9d2ac5f1c780db1cd28e031cd4315788ab3af4104`. | Package |
| The STFS file table occupies seven blocks; all 9,850 allocated data blocks match their SHA-1 entries. The inventory lists 411 entries, including 364 files. | `tools/inspect_stfs.py` against this exact package. Its validation does not authenticate the package signature. | Package |
| The main executable is a 217,088-byte .NET assembly, MVID `04e2f29b-2da1-4501-aba1-9e19f732fa57`, with 92 metadata types and 581 methods. It references XNA 3.1, .NET Framework 3.5, and `Scale_Shared_ContentGame`. | Managed metadata inventory of `584E07D1/Arkedo Series - 03 PIXEL!.exe`. | Package |
| `Scale.GameScale` owns the XNA game loop, constructs a 1280×720 backbuffer, and starts at the logo state. | `GameScale` constructor, `Initialize`, `Update`, and `Draw` IL. | IL |
| `LowLevel.CLASS_GAMEPAD` polls `GamePad` for up to four players. Keyboard branches also exist in the original; the first desktop port will support controllers only. | `CLASS_GAMEPAD.Update` IL. | IL |
| The game has 340 `XNBx` version 4 assets, 14 text level files, a particle definition, and XACT XGS/XSB/XWB sound files. | STFS inventory. | Package |
| The 340 XNBs decode with FNA's LZX decoder. FNA 26.11 loads `TITLE/TITLE_Bg` as a 1280×720 texture, and a smoke window renders three frames. | Local `decode_360_xnb` and `Port.Smoke` runs. This checks one texture, not full game content or audio. | Observed |
| The main assembly has 129 TypeRefs, 316 MemberRefs, and 581 methods; the companion has 17 TypeRefs, 20 MemberRefs, and 3 methods. Reuse-slot virtual methods are reported with their base chain; no explicit MethodImpl overrides were found in the main assembly. | Managed report schema v2. | Package |
| The 340 locally decoded XNBs declare nine reader types. One is `Scale.ZoomMapContentReader` in the companion assembly; three generic reader strings contain assembly-qualified XNA 3.1 type names. | `tools/inspect_content.py` inventory. This is reader metadata, not a proof that every asset loads under FNA. | Package-derived |
| Twenty-nine candidate method boundaries have pinned signatures and raw IL SHA-256 values. | `config/patch-anchors.json` checked against both managed reports by `tools/gate1_evidence.py`. | Package-derived |
| The game uses XNA storage and XML serialized save data. `StorageContainer.Path`, synchronous `OpenContainer`, and GamerServices calls need desktop adaptation. | `CLASS_SAVE` IL and FNA 26.11 API inspection. | IL and implementation inference |
| `CLASS_SOUND` opens `PIXEL_Sounds.xgs`, `PIXEL_WaveBank.xwb`, and `PIXEL_SoundBank.xsb`. | `CLASS_SOUND.Initialize` IL. The desktop audio path has not been exercised. | IL |

The report files `generated/pixel-inventory.json`, `generated/pixel-assembly.json`, `generated/pixel-companion-assembly.json`, and `generated/pixel-content-readers.json` are reproducible local outputs. `config/supported-versions.json` pins the exact supplied package and assembly identities; it does not claim other copies are invalid.

