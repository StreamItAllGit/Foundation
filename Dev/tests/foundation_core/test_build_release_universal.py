# @module: Foundation.Tests.BuildReleaseUniversal
# @purpose: Verifies 6-file SDK, 6-file classic app, and 7-file app+portable bundle validation in build_release_universal.py (Standard 28 parity).
# @depends-on: pathlib, sys, tempfile, unittest, zipfile
# @exports: TestBuildReleaseUniversal

from __future__ import annotations

import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

DEV_ROOT = Path(__file__).resolve().parents[2]
TOOLS_ROOT = DEV_ROOT / "tools"
if str(TOOLS_ROOT) not in sys.path:
    sys.path.insert(0, str(TOOLS_ROOT))

import build_release_universal as bru  # noqa: E402


def _write_common_docs(release_dir: Path) -> None:
    for name in ("README.md", "FEATURES.md", "RELEASE_NOTES.md", "CHANGELOG.md"):
        (release_dir / name).write_text(f"# {name}\n", encoding="utf-8")


def _write_zip(zip_path: Path, entries: dict[str, bytes]) -> None:
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for entry_name, payload in entries.items():
            zf.writestr(entry_name, payload)


class TestBuildReleaseUniversal(unittest.TestCase):
    def test_six_file_sdk_bundle_passes_and_loose_license_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            rel = Path(tmp) / "v0.5.2"
            rel.mkdir()
            _write_common_docs(rel)
            _write_zip(rel / "Foundation-v0.5.2.zip", {"Foundation.Core.dll": b"dll"})
            bru.generate_sha256sums(rel)

            ok, errs = bru.validate_six_file_release_bundle(rel, allow_sdk_zip=True)
            self.assertTrue(ok, errs)

            (rel / "LICENSE").write_text("MIT\n", encoding="utf-8")
            ok_bad, errs_bad = bru.validate_six_file_release_bundle(rel, allow_sdk_zip=True)
            self.assertFalse(ok_bad)
            self.assertTrue(any("Unexpected files" in e for e in errs_bad))

    def test_six_file_classic_app_bundle_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            rel = Path(tmp) / "v0.3.0"
            rel.mkdir()
            _write_common_docs(rel)
            (rel / "Mother-Setup-v0.3.0-win-x64.exe").write_bytes(b"MZ")
            bru.generate_sha256sums(rel)

            ok, errs = bru.validate_six_file_release_bundle(rel)
            self.assertTrue(ok, errs)

    def test_seven_file_portable_app_bundle_passes_explicit_and_autodetect(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            rel = Path(tmp) / "v0.3.0"
            rel.mkdir()
            _write_common_docs(rel)
            (rel / "Mother-Setup-v0.3.0-win-x64.exe").write_bytes(b"MZ")
            _write_zip(rel / "Mother-v0.3.0-win-x64-Portable.zip", {"app/Mother.exe": b"MZ", "app/LICENSE": b"MIT"})
            bru.generate_sha256sums(rel)

            ok_flag, errs_flag = bru.validate_six_file_release_bundle(rel, allow_portable_zip=True)
            self.assertTrue(ok_flag, errs_flag)
            ok_auto, errs_auto = bru.validate_six_file_release_bundle(rel)
            self.assertTrue(ok_auto, errs_auto)
            self.assertEqual(bru.main([str(rel), "--allow-portable-zip"]), 0)

    def test_missing_portable_zip_or_leaked_setup_exe_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            rel = Path(tmp) / "v0.3.0"
            rel.mkdir()
            _write_common_docs(rel)
            (rel / "Mother-Setup-v0.3.0-win-x64.exe").write_bytes(b"MZ")
            bru.generate_sha256sums(rel)

            ok_missing, errs_missing = bru.validate_six_file_release_bundle(rel, allow_portable_zip=True)
            self.assertFalse(ok_missing)
            self.assertTrue(any("-Portable.zip" in e for e in errs_missing))
            self.assertEqual(bru.main([str(rel), "--allow-portable-zip"]), 1)

            _write_zip(rel / "Mother-v0.3.0-Portable.zip", {"Mother-Setup-v0.3.0.exe": b"MZ"})
            bru.generate_sha256sums(rel)
            ok_leak1, errs_leak1 = bru.validate_six_file_release_bundle(rel, allow_portable_zip=True)
            self.assertFalse(ok_leak1)
            self.assertTrue(any("Portable Release Violation" in e for e in errs_leak1))

            _write_zip(rel / "Mother-v0.3.0-Portable.zip", {"bin/Setup.exe": b"MZ"})
            bru.generate_sha256sums(rel)
            ok_leak2, errs_leak2 = bru.validate_six_file_release_bundle(rel, allow_portable_zip=True)
            self.assertFalse(ok_leak2)
            self.assertTrue(any("Portable Release Violation" in e for e in errs_leak2))

    def test_corrupt_empty_subdir_extra_and_flag_conflict_fail(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            rel = Path(tmp) / "v0.3.0"
            rel.mkdir()
            _write_common_docs(rel)
            (rel / "Mother-Setup-v0.3.0.exe").write_bytes(b"MZ")
            portable = rel / "Mother-v0.3.0-Portable.zip"
            portable.write_bytes(b"not-a-zip")
            bru.generate_sha256sums(rel)

            ok_corrupt, _ = bru.validate_six_file_release_bundle(rel, allow_portable_zip=True)
            self.assertFalse(ok_corrupt)

            _write_zip(portable, {})
            bru.generate_sha256sums(rel)
            ok_empty, _ = bru.validate_six_file_release_bundle(rel, allow_portable_zip=True)
            self.assertFalse(ok_empty)

            _write_zip(portable, {"Mother.exe": b"MZ"})
            (rel / "extra.txt").write_text("oops", encoding="utf-8")
            (rel / "sidecar_dir").mkdir()
            ok_extra, errs_extra = bru.validate_six_file_release_bundle(rel, allow_portable_zip=True)
            self.assertFalse(ok_extra)
            self.assertTrue(any("Loose sidecar directories" in e for e in errs_extra))
            self.assertTrue(any("Unexpected files" in e for e in errs_extra))

            ok_conflict, _ = bru.validate_six_file_release_bundle(rel, allow_sdk_zip=True, allow_portable_zip=True)
            self.assertFalse(ok_conflict)


if __name__ == "__main__":
    unittest.main()
