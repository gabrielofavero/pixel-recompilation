# Local XACT preparation

The supported package has 71 wave-bank entries: 52 big-endian PCM16 and 19 XMA2, with 66 mono and five stereo entries. The pinned Windows runtime stalled on the original XMA paths. `WaveBankConverter` changes the bank to little-endian PCM16 during local preparation. The 52 PCM entries retain their exact sample values; vgmstream decodes the 19 XMA entries. Entry indices, bank name, sample rates, channel counts, durations, loop regions, and cue references are preserved. Decoding uses `-i -s N` to decode one subsong once, without added loops or fades. Every decoded RIFF format and sample count is checked before writing a stage.

The sound bank also needs a compatibility conversion. FAudio's `FACTSoundBank_Prepare` defers sound creation for interactive cues, and `FACT_INTERNAL_UpdateCue` starts their sound only when the control variable changes. PIXEL never calls `Cue.SetVariable`, `AudioEngine.SetGlobalVariable`, or `Apply3D`. Five interactive tables each contain exactly one sound whose range includes the default control value zero. In particular, `MUS_TITLE` otherwise reports Playing while producing silence. This was confirmed with Windows session output peaks, and comparison with direct playback of the same decoded PCM samples.

`SoundBankConverter` makes these five cues refer directly to their selected sound. It keeps their cue instance limits, fades, transitions, sound definitions, events, categories, and loop counts. It appends byte-identical copies of the four remaining random variation tables and updates their references. It leaves the global settings bank unchanged. The conversion accepts only the supported layout after package hash verification; it is not a general interactive-cue converter. Source behavior: [FAudio cue preparation](https://github.com/FNA-XNA/FAudio/blob/master/src/FACT.c) and [interactive cue update](https://github.com/FNA-XNA/FAudio/blob/master/src/FACT_internal.c).

## Decoder pin and redistribution

`config/dependencies.json` pins vgmstream **r2168**, release commit `d9875b1fff307e4759dfb85e49e3d5e6fb196f7c`, and its Windows x64 archive SHA-256 `692d45070852a35c1fa42726aa3c9de528e44eec925d37d99b1074256b99a229`. Bootstrap verifies the archive, then preparation checks the executable and every bundled DLL against individual SHA-256 pins before starting it. A changed dependency fails before an output stage is created. Decoder subprocesses have a two-minute limit per entry, and temporary game-derived data stays under ignored `private/` and is removed on success or failure.

- [Pinned release](https://github.com/vgmstream/vgmstream/releases/tag/r2168), [source tree](https://github.com/vgmstream/vgmstream/tree/d9875b1fff307e4759dfb85e49e3d5e6fb196f7c), and [build files for bundled codecs](https://github.com/vgmstream/vgmstream/blob/d9875b1fff307e4759dfb85e49e3d5e6fb196f7c/ext_libs/Makefile).
- vgmstream uses the ISC permission notice. Its Windows archive also contains FFmpeg 9.0.2 libraries, CELT, mpg123, Speex, and Vorbis; the upstream license collection includes Ogg and Opus notices as well. The original notices are retained in [docs/notices/vgmstream-r2168](../notices/vgmstream-r2168/).
- FFmpeg and mpg123 include LGPL requirements. Before distributing the decoder in setup, gate 6 must include the applicable licenses, matching codec source/build material, and required replacement/relinking provisions. The copied notices alone are not a completed release audit.

The decoder belongs to preparation, not the runtime game directory. Original and converted banks, decoded WAVs, and game assemblies remain local and must not be included in Git or installer payloads.

## Diagnostics

From a prepared stage, `Port.Launcher --probe-content` loads all 340 XNB assets through FNA and the prepared custom reader. `--probe-audio` exercises all 63 cues and music transport/category volume. `--probe-menu-audio` plays only the actual XACT title cue for ten seconds. Cue state is insufficient evidence of audible output: `tools/measure-audio.ps1` reads Windows session peaks, volume, and mute state without recording audio or accessing a microphone. A user listening to the visible game confirms the final audible result.

Set `PIXEL_PORT_TRACE_CONTENT=1` to log the actual game's four generic content-load sites. All are guarded by original IL hashes. Scripted controller runs now fail if they exit without observing gameplay, pause, resume, return to title, and the final exit input.
