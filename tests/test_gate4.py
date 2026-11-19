"""Local-package integration checks for the prepared desktop wave bank."""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parent.parent
STAGE = Path(os.environ.get("PIXEL_PORT_GATE4_STAGE", str(REPO / "private" / "local-run"))).resolve()
BANK = "584E07D1/Content/Sounds/PIXEL_WaveBank.xwb"
CLI = REPO / "tools/prepare-cli/bin/Release/net10.0/prepare-cli.dll"


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


@unittest.skipUnless((STAGE / "manifest.json").exists() and CLI.exists(), "a gate-4 prepared stage and build are required")
class AudioPreparationChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest = json.loads((STAGE / "manifest.json").read_text())
        cls.original = (STAGE / "original" / BANK).read_bytes()
        cls.prepared = (STAGE / "run" / BANK).read_bytes()

    def prepare(self, destination: Path, decoder: Path | None = None) -> subprocess.CompletedProcess:
        args = ["dotnet", str(CLI), "game/pixel-360", str(destination), "private",
                "config/supported-versions.json", "config/patch-anchors.json", "out/deps/managed/FNA.dll"]
        if decoder is not None:
            args.append(str(decoder))
        env = dict(os.environ, DOTNET_CLI_HOME=str(REPO / "out/dotnet-home"), DOTNET_CLI_TELEMETRY_OPTOUT="1")
        return subprocess.run(args, cwd=REPO, env=env, capture_output=True, text=True, timeout=300)

    def test_wave_identity_timing_loops_and_pcm_samples_preserved(self) -> None:
        old, new = self.original, self.prepared
        self.assertEqual(old[:4], b"DNBW")
        self.assertEqual(new[:4], b"WBND")
        ou = lambda at: struct.unpack_from(">I", old, at)[0]
        nu = lambda at: struct.unpack_from("<I", new, at)[0]
        self.assertEqual(nu(nu(12) + 4), 71)
        self.assertEqual(old[60:124], new[60:124])
        codecs = {0: 0, 1: 0}
        for index in range(71):
            with self.subTest(wave=index):
                original_entry = struct.unpack_from(">6I", old, ou(20) + index * 24)
                prepared_entry = struct.unpack_from("<6I", new, nu(20) + index * 24)
                flags, fmt, start, size, loop_start, loop_size = original_entry
                new_flags, new_fmt, new_start, new_size, new_loop_start, new_loop_size = prepared_entry
                codec = fmt & 3
                codecs[codec] += 1
                self.assertEqual(new_fmt & 3, 0)  # Every entry is desktop PCM.
                self.assertEqual(new_flags, flags)
                self.assertEqual((new_fmt >> 2) & 7, (fmt >> 2) & 7)
                self.assertEqual((new_fmt >> 5) & 0x3ffff, (fmt >> 5) & 0x3ffff)
                self.assertEqual((new_loop_start, new_loop_size), (loop_start, loop_size))
                self.assertEqual(new_size, (flags >> 4) * ((fmt >> 2) & 7) * 2)
                pcm = new[nu(44) + new_start:nu(44) + new_start + new_size]
                self.assertEqual(len(pcm), new_size)
                self.assertTrue(any(pcm), "wave unexpectedly contains only silence")
                if codec == 0:
                    source_pcm = old[ou(44) + start:ou(44) + start + size]
                    self.assertEqual(pcm[::2], source_pcm[1::2])
                    self.assertEqual(pcm[1::2], source_pcm[::2])
        self.assertEqual(codecs, {0: 52, 1: 19})

    def test_corrupt_decoder_rejected_without_output(self) -> None:
        config = json.loads((REPO / "config/dependencies.json").read_text())["audioDecoder"]
        with tempfile.TemporaryDirectory(prefix="gate4-reject-", dir=REPO / "private") as work:
            root = Path(work)
            decoder = root / "decoder"
            decoder.mkdir()
            (decoder / config["files"][0]["name"]).write_bytes(b"incorrect dependency")
            output = root / "stage"
            result = self.prepare(output, decoder)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("Audio decoder dependency hash differs", result.stderr)
            self.assertFalse(output.exists())

    def test_static_interactive_cues_keep_the_selected_sound_and_transport(self) -> None:
        relative = "584E07D1/Content/Sounds/PIXEL_SoundBank.xsb"
        old, new = (STAGE / "original" / relative).read_bytes(), (STAGE / "run" / relative).read_bytes()
        u = lambda data, at: struct.unpack_from(">I", data, at)[0]
        # The actual sound definitions, wave indices, loop counts and events stay intact.
        self.assertEqual(old[202:1695], new[202:1695])
        simple, complex_count = struct.unpack_from(">2H", old, 19)
        self.assertEqual((simple, complex_count), (54, 9))
        self.assertEqual(old[u(old, 34):u(old, 38)], new[u(old, 34):u(old, 38)])
        converted, random = 0, 0
        for index in range(complex_count):
            at = u(old, 38) + index * 15
            original_code, prepared_code = u(old, at + 1), u(new, at + 1)
            header = u(old, original_code)
            count, kind = header & 0xffff, (header >> 19) & 7
            self.assertEqual(new[at + 5:at + 15], old[at + 5:at + 15])  # fades, limits and transitions
            if kind == 3:
                self.assertEqual(count, 1)
                self.assertEqual(new[at], old[at] | 4)
                self.assertEqual(prepared_code, u(old, original_code + 8))
                lo, hi = struct.unpack_from(">2f", old, original_code + 12)
                self.assertLessEqual(lo, 0)
                self.assertGreaterEqual(hi, 0)
                converted += 1
            else:
                size = 8 + count * 6
                self.assertEqual(new[at], old[at])
                self.assertGreaterEqual(prepared_code, len(old))
                self.assertEqual(new[prepared_code:prepared_code + size], old[original_code:original_code + size])
                random += 1
        self.assertEqual((converted, random), (5, 4))

    def test_second_preparation_is_byte_identical(self) -> None:
        with tempfile.TemporaryDirectory(prefix="gate4-repeat-", dir=REPO / "private") as work:
            output = Path(work) / "stage"
            result = self.prepare(output)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual((output / "manifest.json").read_bytes(), (STAGE / "manifest.json").read_bytes())
            for entry in self.manifest["files"]:
                path = entry["path"]
                self.assertEqual(sha((output / "run" / path).read_bytes()), entry["preparedSha256"], path)


if __name__ == "__main__":
    unittest.main()
