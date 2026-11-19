"""Exercise the published offline helper against a local, player-owned package."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parent.parent
PAYLOAD = REPO / "installer/out/payload"
HELPER = PAYLOAD / "helper/Port.SetupHelper.exe"
PACKAGE = Path(os.environ.get("PIXEL_PORT_PACKAGE", str(REPO / "game/pixel-360"))).resolve()


@unittest.skipUnless(os.name == "nt" and HELPER.exists() and PACKAGE.exists(),
                     "build the Windows installer and provide the supported local LIVE package")
class InstallerChecks(unittest.TestCase):
    def setUp(self):
        self.work = tempfile.TemporaryDirectory(prefix="installer-check-", dir=REPO / "private")
        self.addCleanup(self.work.cleanup)
        self.root = Path(self.work.name)
        self.target = self.root / "installation/current"

    def helper(self, *arguments, success=True):
        result = subprocess.run([str(HELPER), *map(str, arguments)], cwd=REPO,
                                capture_output=True, text=True, timeout=240)
        self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)
        return result

    def install(self, **kwargs):
        return self.helper("install", "--destination", self.target, "--payload", PAYLOAD,
                           "--package", PACKAGE, **kwargs)

    def verify(self, **kwargs):
        return self.helper("verify-installed", "--destination", self.target, **kwargs)

    def test_install_reuse_rollback_finalize_and_repair(self):
        source_hash = hashlib.sha256(PACKAGE.read_bytes()).hexdigest()
        saves = self.root / "saves/Player.sav"
        saves.parent.mkdir()
        saves.write_bytes(b"existing player progress")
        result = self.install()
        self.assertIn("CHECK_LOAD_RESULT", result.stdout)
        self.verify()
        self.assertFalse((self.target / "original").exists())
        self.assertFalse((self.target / "tools").exists())

        report = self.target / "install-report.txt"
        report.write_text("previous installation", encoding="utf-8")
        result = self.install()
        self.assertIn("STAGE reuse\n", result.stdout)
        self.assertTrue(self.target.with_name("current.previous").exists())
        self.helper("rollback", "--destination", self.target)
        self.verify()
        self.assertEqual(report.read_text(encoding="utf-8"), "previous installation")

        self.install()
        self.helper("finalize", "--destination", self.target)
        self.assertFalse(self.target.with_name("current.previous").exists())
        damaged = self.target / "run/584E07D1/Content/Sounds/PIXEL_WaveBank.xwb"
        with damaged.open("r+b") as stream:
            stream.write(b"BAD!")
        self.verify(success=False)
        result = self.install()
        self.assertNotIn("STAGE reuse\n", result.stdout)
        self.verify()
        self.helper("finalize", "--destination", self.target)

        # Manifest paths must not be allowed to escape the install directory.
        manifest_path = self.target / "install-manifest.json"
        manifest_bytes = manifest_path.read_bytes()
        manifest = json.loads(manifest_bytes)
        manifest["Files"][0]["Path"] = "../outside"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        self.verify(success=False)
        manifest_path.write_bytes(manifest_bytes)
        self.verify()
        self.assertEqual(saves.read_bytes(), b"existing player progress")
        self.assertEqual(hashlib.sha256(PACKAGE.read_bytes()).hexdigest(), source_hash)
        self.assertFalse(list(self.target.parent.glob(".port-work-*")))

    def test_invalid_package_and_cancel_preserve_existing_files(self):
        self.target.mkdir(parents=True)
        marker = self.target / "marker.txt"
        marker.write_text("preserve me", encoding="utf-8")
        invalid = self.root / "damaged.live"
        invalid.write_bytes(b"unsupported package")
        self.helper("install", "--destination", self.target, "--payload", PAYLOAD,
                    "--package", invalid, success=False)
        cancel = self.root / "cancel"
        cancel.touch()
        self.helper("install", "--destination", self.target, "--payload", PAYLOAD,
                    "--package", PACKAGE, "--cancel-file", cancel, success=False)
        self.assertEqual(marker.read_text(encoding="utf-8"), "preserve me")
        self.assertEqual(list(self.target.iterdir()), [marker])

    def test_cancel_during_preparation_cleans_stage(self):
        cancel = self.root / "cancel"
        process = subprocess.Popen([str(HELPER), "install", "--destination", str(self.target),
                                    "--payload", str(PAYLOAD), "--package", str(PACKAGE),
                                    "--cancel-file", str(cancel)], cwd=REPO,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            for line in process.stdout:
                if line.startswith("STAGE audio"):
                    cancel.touch()
                    break
            stdout, stderr = process.communicate(timeout=120)
            self.assertNotEqual(process.returncode, 0, stdout + stderr)
            self.assertIn("cancelled", stderr.lower())
            self.assertFalse(self.target.exists())
            self.assertFalse(list(self.target.parent.glob(".port-work-*")))
        finally:
            if process.poll() is None:
                process.kill()
                process.communicate()
            process.stdout.close()
            process.stderr.close()

    def test_reject_installation_directory_junction(self):
        outside = self.root / "outside"
        outside.mkdir()
        marker = outside / "keep.txt"
        marker.write_text("keep", encoding="utf-8")
        self.target.parent.mkdir()
        quote = lambda path: "'" + str(path).replace("'", "''") + "'"
        subprocess.run(["powershell", "-NoProfile", "-Command",
                        f"New-Item -ItemType Junction -Path {quote(self.target)} -Target {quote(outside)} | Out-Null"],
                       check=True, capture_output=True, timeout=30)
        try:
            self.install(success=False)
            self.assertEqual(marker.read_text(encoding="utf-8"), "keep")
        finally:
            # Remove only the junction; the outside directory stays intact.
            os.rmdir(self.target)


if __name__ == "__main__":
    unittest.main()
