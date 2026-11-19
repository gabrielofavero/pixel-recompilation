"""Authored desktop bindings and guarded enhancement preparation checks."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parent.parent
STAGE = Path(os.environ.get("PIXEL_PORT_DESKTOP_STAGE", str(REPO / "private/desktop-input-final"))).resolve()
RUNNER = REPO / "tests/Port.InputTests/bin/Release/net10.0/Port.InputTests.dll"
CLI = REPO / "tools/prepare-cli/bin/Release/net10.0/prepare-cli.dll"


@unittest.skipUnless(RUNNER.exists() and (STAGE / "manifest.json").exists(), "desktop input build and prepared stage required")
class DesktopInputChecks(unittest.TestCase):
    def test_bindings_menu_targets_and_retained_original_reader(self):
        result = subprocess.run(["dotnet", str(RUNNER), str(STAGE)], cwd=REPO,
                                capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("DESKTOP_INPUT_TESTS_OK", result.stdout)

    def test_changed_input_anchor_rejected_before_output(self):
        with tempfile.TemporaryDirectory(prefix="input-anchor-", dir=REPO / "private") as directory:
            directory = Path(directory)
            anchors = json.loads((REPO / "config/patch-anchors.json").read_text())
            method = next(method for assembly in anchors["assemblies"] for method in assembly["methods"]
                          if method["type"] == "LowLevel.CLASS_GAMEPAD" and method["name"] == "Update")
            method["ilSha256"] = "0" * 64
            path = directory / "anchors.json"
            path.write_text(json.dumps(anchors))
            output = directory / "stage"
            result = subprocess.run(["dotnet", str(CLI), "game/pixel-360", str(output), "private",
                                     "config/supported-versions.json", str(path), "out/deps/managed/FNA.dll"],
                                    cwd=REPO, capture_output=True, text=True, timeout=60)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("Method IL guard failed", result.stderr)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
