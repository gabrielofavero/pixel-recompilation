"""Local, game-package-dependent checks for the read-only evidence boundary."""

from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


REPO = Path(__file__).resolve().parent.parent
PACKAGE = REPO / "game" / "pixel-360"
ASSEMBLY = REPO / "private" / "pixel-360" / "584E07D1" / "Arkedo Series - 03 PIXEL!.exe"
INSPECTOR = REPO / "tools" / "inspect_360_assembly" / "bin" / "Release" / "net10.0" / "inspect_360_assembly.dll"
spec = importlib.util.spec_from_file_location("inspect_stfs", REPO / "tools" / "inspect_stfs.py")
assert spec and spec.loader
stfs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stfs)


class BytesPackage:
    name = "pixel-360"

    def __init__(self, data: bytes):
        self.data = data

    def read_bytes(self) -> bytes:
        return self.data


@unittest.skipUnless(PACKAGE.exists(), "local game package is required")
class GateOneChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.original = PACKAGE.read_bytes()
        cls.version = json.loads((REPO / "config" / "supported-versions.json").read_text())["packages"][0]

    def test_inventory_is_deterministic_and_pinned(self) -> None:
        first = stfs.inspect(BytesPackage(self.original), None, supported_version=self.version)
        second = stfs.inspect(BytesPackage(self.original), None, supported_version=self.version)
        self.assertEqual(json.dumps(first, sort_keys=True), json.dumps(second, sort_keys=True))
        self.assertEqual(first["input"]["sha256"], self.version["packageSha256"])
        self.assertEqual(first["counts"]["files"], 364)

    def test_corrupt_data_block_rejected(self) -> None:
        data = bytearray(self.original)
        header_size = stfs.integer(data, 0x340, 4)
        data[stfs.data_offset(7, header_size) + 10] ^= 1
        with self.assertRaisesRegex(ValueError, "fails its SHA-1"):
            stfs.inspect(BytesPackage(data), None)

    def test_different_layout_rejected(self) -> None:
        data = bytearray(self.original)
        data[0x379] = 0x23
        with self.assertRaisesRegex(ValueError, "unsupported STFS volume layout"):
            stfs.inspect(BytesPackage(data), None)

    def test_noncontiguous_file_rejected(self) -> None:
        data = bytearray(self.original)
        header_size = stfs.integer(data, 0x340, 4)
        table_offset = stfs.data_offset(0, header_size)
        data[table_offset + 40] &= ~0x40
        self._refresh_table_hash(data, header_size)
        with self.assertRaisesRegex(ValueError, "noncontiguous STFS entry"):
            stfs.inspect(BytesPackage(data), None)

    def test_unsafe_windows_path_rejected(self) -> None:
        data = bytearray(self.original)
        header_size = stfs.integer(data, 0x340, 4)
        table_offset = stfs.data_offset(0, header_size)
        data[table_offset:table_offset + 7] = b"CON.png"
        data[table_offset + 40] = (data[table_offset + 40] & 0xC0) | 7
        self._refresh_table_hash(data, header_size)
        with self.assertRaisesRegex(ValueError, "unsafe STFS name"):
            stfs.inspect(BytesPackage(data), None)

    def test_case_insensitive_duplicate_path_rejected(self) -> None:
        data = bytearray(self.original)
        header_size = stfs.integer(data, 0x340, 4)
        second_entry = stfs.data_offset(0, header_size) + 2 * 64
        name = b"dashboardicon.png"
        data[second_entry:second_entry + len(name)] = name
        data[second_entry + 40] = (data[second_entry + 40] & 0xC0) | len(name)
        self._refresh_table_hash(data, header_size)
        with self.assertRaisesRegex(ValueError, "duplicate STFS path"):
            stfs.inspect(BytesPackage(data), None)

    def test_unknown_package_rejected_before_output(self) -> None:
        data = bytearray(self.original)
        data[0x400] ^= 1
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "unknown-live"
            report = Path(temp) / "report.json"
            source.write_bytes(data)
            run = subprocess.run(
                [sys.executable, str(REPO / "tools" / "inspect_stfs.py"), str(source),
                 "--output", str(report)],
                capture_output=True, text=True, cwd=REPO, check=False,
            )
            self.assertNotEqual(run.returncode, 0)
            self.assertIn("unsupported package version", run.stderr)
            self.assertFalse(report.exists())

    @unittest.skipUnless(ASSEMBLY.exists() and INSPECTOR.exists(), "extracted assembly and built inspector are required")
    def test_wrong_mvid_rejected_before_output(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            manifest = json.loads((REPO / "config" / "supported-versions.json").read_text())
            manifest["packages"][0]["assemblyMvid"] = "00000000-0000-0000-0000-000000000000"
            path = Path(temp) / "manifest.json"
            report = Path(temp) / "report.json"
            path.write_text(json.dumps(manifest), encoding="utf-8")
            run = subprocess.run(
                ["dotnet", str(INSPECTOR), str(ASSEMBLY), "--manifest", str(path),
                 "--output", str(report)],
                capture_output=True, text=True, cwd=REPO, check=False,
            )
            self.assertNotEqual(run.returncode, 0)
            self.assertIn("MVID differs", run.stderr)
            self.assertFalse(report.exists())

    @staticmethod
    def _refresh_table_hash(data: bytearray, header_size: int) -> None:
        offset = stfs.data_offset(0, header_size)
        digest = hashlib.sha1(data[offset:offset + stfs.BLOCK]).digest()
        hash_offset = stfs.hash_offset(0, header_size)
        data[hash_offset:hash_offset + 20] = digest


if __name__ == "__main__":
    unittest.main()
