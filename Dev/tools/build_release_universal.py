# @module: Foundation.Tools.build_release_universal
# @purpose: Shared 6-file Releases\ bundle validator and SHA256SUMS.txt generator for Tier A sister applications (Standard 01 & Standard 03).
# @depends-on: argparse, hashlib, pathlib, sys
# @exports: generate_sha256sums, validate_six_file_release_bundle, main

from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path

REQUIRED_RELEASE_FILES = (
    "README.md",
    "FEATURES.md",
    "RELEASE_NOTES.md",
    "CHANGELOG.md",
    "SHA256SUMS.txt",
)


def compute_sha256(file_path: Path) -> str:
    h = hashlib.sha256()
    with file_path.open("rb") as fh:
        while chunk := fh.read(1024 * 1024):
            h.update(chunk)
    return h.hexdigest().upper()


def generate_sha256sums(release_dir: str | Path) -> Path:
    target_dir = Path(release_dir).resolve()
    lines: list[str] = []
    for item in sorted(target_dir.iterdir(), key=lambda p: p.name.lower()):
        if item.is_file() and item.name != "SHA256SUMS.txt":
            digest = compute_sha256(item)
            lines.append(f"{digest}  {item.name}")
    sums_path = target_dir / "SHA256SUMS.txt"
    sums_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return sums_path


def validate_six_file_release_bundle(release_dir: str | Path) -> tuple[bool, list[str]]:
    target_dir = Path(release_dir).resolve()
    errors: list[str] = []
    if not target_dir.exists():
        return False, [f"Release directory does not exist: {target_dir}"]

    files = [p.name for p in target_dir.iterdir() if p.is_file()]
    setup_exes = [f for f in files if f.endswith(".exe") and "-Setup-v" in f]
    if len(setup_exes) != 1:
        errors.append(f"Expected exactly 1 Setup.exe installer, found: {setup_exes}")

    for req in REQUIRED_RELEASE_FILES:
        if req not in files:
            errors.append(f"Missing required release file: {req}")

    if len(files) != 6:
        errors.append(f"Standard 01 §1.5 requires strictly 6 files in release bundle, found {len(files)}: {files}")

    return len(errors) == 0, errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Universal 6-File Release Bundle Validator & SHA256SUMS Generator")
    parser.add_argument("release_dir", type=Path, help="Path to Releases\\v<Version> or Releases\\Latest")
    parser.add_argument("--regen-sums", action="store_true", help="Regenerate SHA256SUMS.txt before validating")
    args = parser.parse_args(argv)

    if args.regen_sums:
        generate_sha256sums(args.release_dir)

    ok, errs = validate_six_file_release_bundle(args.release_dir)
    if not ok:
        for e in errs:
            print(f"[FAIL] {e}", file=sys.stderr)
        return 1
    print(f"[PASS] 6-file release bundle verified: {args.release_dir.resolve()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
