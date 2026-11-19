"""Save integration checks against the locally prepared original game, in fresh processes."""
from __future__ import annotations

import ctypes
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parent.parent
STAGE = Path(os.environ.get("PIXEL_PORT_GATE5_STAGE", str(REPO / "private/gate5-final"))).resolve()
LAUNCHER = STAGE / "run/584E07D1/Port.Launcher.dll"


@unittest.skipUnless(LAUNCHER.exists(), "a staged gate-5 launcher is required")
class SaveChecks(unittest.TestCase):
    def setUp(self) -> None:
        self.work = tempfile.TemporaryDirectory(prefix="save-check-", dir=REPO / "private")
        self.addCleanup(self.work.cleanup)
        self.directory = Path(self.work.name)
        self.save = self.directory / "Player.sav"

    def run_probe(self, action="read", success=True):
        result = subprocess.run(["dotnet", str(LAUNCHER), "--probe-save", action, str(self.directory)],
                                cwd=REPO, capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)
        if not success:
            return result
        return json.loads(next(line.removeprefix("SAVE_PROBE_RESULT ") for line in result.stdout.splitlines()
                               if line.startswith("SAVE_PROBE_RESULT ")))

    def assert_fresh(self, result):
        self.assertTrue(result["initialized"])
        self.assertEqual(result["version"], 1)
        self.assertEqual(result["levels"], [False] * 7)
        self.assertEqual(result["treasures"], [[False] * 3 for _ in range(6)])

    def test_missing_save_creates_original_xml(self):
        self.assert_fresh(self.run_probe())
        self.assertEqual(ET.parse(self.save).getroot().tag, "CLASS_SAVEDATA")
        self.assertEqual(list(self.directory.iterdir()), [self.save])

    def test_all_progress_and_treasures_survive_process_restart(self):
        written = self.run_probe("write")
        before = self.save.read_bytes()
        loaded = self.run_probe()
        self.assertEqual(loaded["levels"], [True] * 3 + [False] * 4)
        self.assertEqual(loaded["treasures"], [[(i + j) % 2 == 0 for j in range(3)] for i in range(6)])
        self.assertEqual(written["levels"], loaded["levels"])
        self.assertEqual(written["treasures"], loaded["treasures"])
        self.assertEqual(self.save.read_bytes(), before)
        self.assertEqual(loaded["players"], ["Player"] * 4)
        self.assertTrue((self.directory / "Player.sav.bak").is_file())

    def test_new_game_reset_survives_restart(self):
        self.run_probe("write")
        self.assert_fresh(self.run_probe("reset"))
        self.assert_fresh(self.run_probe())

    def test_invalid_xml_preserved_and_replaced_with_fresh_save(self):
        self.run_probe()
        valid = self.save.read_bytes()
        examples = {"truncated": b"<CLASS_SAVEDATA><broken", "empty": b"",
                    "wrong-root": b"<Wrong/>",
                    "dtd": b'<!DOCTYPE x [<!ENTITY e SYSTEM "file:///missing">]><CLASS_SAVEDATA>&e;</CLASS_SAVEDATA>',
                    "oversize": b"<CLASS_SAVEDATA>" + b" " * 70000 + b"</CLASS_SAVEDATA>"}
        for field in ("m_savVersion", "m_savLevels", "m_savTresors"):
            changed = ET.fromstring(valid)
            changed.remove(changed.find(field))
            examples["missing-" + field] = ET.tostring(changed)
        changed = ET.fromstring(valid)
        changed.find("m_savVersion").text = "2"
        examples["unsupported-version"] = ET.tostring(changed)
        for field in ("m_savLevels", "m_savTresors", "m_savTresors/ArrayOfBoolean"):
            changed = ET.fromstring(valid)
            parent = changed.find(field)
            parent.remove(parent[0])
            examples["short-" + field] = ET.tostring(changed)
        for label, damaged in examples.items():
            with self.subTest(label=label):
                self.save.write_bytes(damaged)
                before = set(self.directory.glob("*.corrupt-*"))
                self.assert_fresh(self.run_probe())
                created = set(self.directory.glob("*.corrupt-*")) - before
                self.assertEqual(len(created), 1)
                self.assertEqual(created.pop().read_bytes(), damaged)
                self.assert_fresh(self.run_probe())

    @unittest.skipUnless(os.name == "nt", "Windows file-sharing semantics")
    def test_failed_replacement_preserves_previous_save_and_cleans_temporary(self):
        self.run_probe("write")
        before = self.save.read_bytes()
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p,
                                      ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
        kernel.CreateFileW.restype = ctypes.c_void_p
        kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        # Permit reads while denying the delete sharing required by atomic replacement.
        handle = kernel.CreateFileW(str(self.save), 0x80000000, 1, None, 3, 0, None)
        self.assertNotEqual(handle, ctypes.c_void_p(-1).value)
        try:
            result = self.run_probe("reset", success=False)
            self.assertIn("SAVE_WRITE_FAILED", result.stderr)
            self.assertEqual(self.save.read_bytes(), before)
            self.assertEqual(list(self.directory.glob("*.tmp-*")), [])
        finally:
            kernel.CloseHandle(handle)
        self.assertEqual(self.run_probe()["levels"], [True] * 3 + [False] * 4)

    def test_unwritable_storage_path_fails_explicitly(self):
        self.directory = self.directory / "not-a-directory"
        self.directory.write_bytes(b"preserve this file")
        result = self.run_probe(success=False)
        self.assertIn("LAUNCH_FAIL", result.stderr)
        self.assertEqual(self.directory.read_bytes(), b"preserve this file")


if __name__ == "__main__":
    unittest.main()
