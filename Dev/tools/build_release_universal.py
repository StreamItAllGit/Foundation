# @module: Foundation.Tools.build_release_universal
# @purpose: Shared 6/7/8-file Dual-Installer Releases\ validator, hardlink-safe atomic SHA256SUMS.txt generator, and Foundation SDK ZIP stager (Standards 01, 03, 27 & 28).
# @depends-on: argparse, fnmatch, hashlib, os, pathlib, stat, sys, zipfile
# @exports: compute_sha256, generate_sha256sums, stage_sdk_release_zip, validate_six_file_release_bundle, validate_release_bundle, main

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import os
import stat
import sys
import zipfile
from pathlib import Path, PurePosixPath

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
    return h.hexdigest().lower()


def _atomic_write_text(path: Path, content: str) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp_path = path.with_name(f"{path.name}.tmp_{os.getpid()}")
    with tmp_path.open("w", encoding="utf-8", newline="\n") as fh:
        fh.write(content)
        fh.flush()
        os.fsync(fh.fileno())
    # os.replace unlinks the destination entry rather than mutating hardlinked sibling inodes.
    os.replace(tmp_path, path)
    return path


def generate_sha256sums(release_dir: str | Path) -> Path:
    target_dir = Path(release_dir).resolve()
    lines: list[str] = []
    for item in sorted(target_dir.iterdir(), key=lambda p: p.name.lower()):
        if item.is_file() and item.name != "SHA256SUMS.txt":
            digest = compute_sha256(item)
            lines.append(f"{digest}  {item.name}")
    sums_path = target_dir / "SHA256SUMS.txt"
    return _atomic_write_text(sums_path, "\n".join(lines) + "\n")


def stage_sdk_release_zip(sdk_dir: str | Path, release_dir: str | Path, version: str) -> Path:
    clean_ver = version.lstrip("v")
    src_root = Path(sdk_dir).resolve()
    out_dir = Path(release_dir).resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    if not src_root.exists():
        raise FileNotFoundError(f"SDK snapshot directory does not exist: {src_root}")

    for old_zip in out_dir.glob("Foundation-v*.zip"):
        os.chmod(old_zip, stat.S_IWRITE)
        old_zip.unlink()

    zip_path = out_dir / f"Foundation-v{clean_ver}.zip"
    if zip_path.exists():
        os.chmod(zip_path, stat.S_IWRITE)
        zip_path.unlink()

    tmp_zip = out_dir / f".tmp_{os.getpid()}_Foundation-v{clean_ver}.zip"
    try:
        with zipfile.ZipFile(tmp_zip, "w", compression=zipfile.ZIP_DEFLATED) as zf:
            for item in sorted(src_root.rglob("*")):
                if item.is_file() and "__pycache__" not in item.parts:
                    zf.write(item, item.relative_to(src_root).as_posix())
        os.replace(tmp_zip, zip_path)
    finally:
        if tmp_zip.exists():
            tmp_zip.unlink(missing_ok=True)

    generate_sha256sums(out_dir)
    return zip_path


def _inspect_portable_zip(zip_path: Path) -> list[str]:
    errors: list[str] = []
    try:
        with zipfile.ZipFile(zip_path, "r") as zf:
            infos = zf.infolist()
            if not infos:
                errors.append(f"Portable archive '{zip_path.name}' is empty.")
                return errors
            leaks: list[str] = []
            for info in infos:
                norm = info.filename.replace("\\", "/").lstrip("/")
                if not norm or norm.endswith("/"):
                    continue
                full_lower = norm.lower()
                base_lower = PurePosixPath(full_lower).name
                if (
                    fnmatch.fnmatchcase(full_lower, "*-setup-*.exe")
                    or fnmatch.fnmatchcase(full_lower, "*-offline-setup-*.exe")
                    or fnmatch.fnmatchcase(base_lower, "*setup*.exe")
                ):
                    leaks.append(info.filename)
            if leaks:
                errors.append(
                    f"Portable Release Violation: '{zip_path.name}' must not contain installer executables (found: {', '.join(leaks)})."
                )
    except (zipfile.BadZipFile, OSError) as exc:
        errors.append(f"Invalid portable ZIP archive '{zip_path.name}': {exc}")
    return errors


def validate_six_file_release_bundle(
    release_dir: str | Path,
    allow_sdk_zip: bool = False,
    allow_portable_zip: bool = False,
    allow_dual_installer: bool = False,
) -> tuple[bool, list[str]]:
    if sum(1 for flag in (allow_sdk_zip, allow_portable_zip, allow_dual_installer) if flag) > 1:
        return False, ["Cannot combine --allow-sdk-zip, --allow-portable-zip, and --allow-dual-installer."]

    target_dir = Path(release_dir).resolve()
    errors: list[str] = []
    if not target_dir.exists():
        return False, [f"Release directory does not exist: {target_dir}"]

    subdirs = sorted(p.name for p in target_dir.iterdir() if p.is_dir())
    if subdirs:
        errors.append(f"Single-EXE Violation: Loose sidecar directories found in release folder: {', '.join(subdirs)}")

    files = sorted(p.name for p in target_dir.iterdir() if p.is_file())
    offline_setups = [f for f in files if f.lower().endswith(".exe") and "-offline-setup-v" in f.lower()]
    online_setups = [
        f for f in files
        if f.lower().endswith(".exe") and "-setup-v" in f.lower() and "-offline-setup-v" not in f.lower()
    ]
    sdk_zips = [f for f in files if f.startswith("Foundation-v") and f.lower().endswith(".zip")]
    portable_zips = [f for f in files if f.lower().endswith("-portable.zip")]

    for req in REQUIRED_RELEASE_FILES:
        if req not in files:
            errors.append(f"Missing required release file: {req}")

    if allow_sdk_zip:
        if len(sdk_zips) != 1:
            errors.append(f"Expected exactly 1 Foundation-v*.zip archive, found: {sdk_zips}")
        if online_setups or offline_setups:
            errors.append(f"Unexpected app setup executable in SDK release bundle: {online_setups + offline_setups}")
        allowed = set(REQUIRED_RELEASE_FILES) | set(sdk_zips)
        extras = sorted(set(files) - allowed)
        if extras:
            errors.append(f"Unexpected files in SDK release bundle: {extras}")
        if len(files) != 6:
            errors.append(f"Standard 28 §2 requires strictly 6 files in SDK release bundle, found {len(files)}: {files}")
    elif allow_dual_installer or (not allow_portable_zip and len(offline_setups) > 0):
        if len(online_setups) != 1:
            errors.append(f"Expected exactly 1 compact Online Setup (*-Setup-v*.exe), found: {online_setups}")
        if len(offline_setups) != 1:
            errors.append(f"Expected exactly 1 full Offline Setup (*-Offline-Setup-v*.exe), found: {offline_setups}")
        if len(portable_zips) != 1:
            errors.append(f"Expected exactly 1 *-Portable.zip archive, found: {portable_zips}")
        else:
            errors.extend(_inspect_portable_zip(target_dir / portable_zips[0]))
        allowed = set(REQUIRED_RELEASE_FILES) | set(online_setups) | set(offline_setups) | set(portable_zips)
        extras = sorted(set(files) - allowed)
        if extras:
            errors.append(f"Unexpected files in 8-file dual-installer release bundle: {extras}")
        if len(files) != 8:
            errors.append(f"Standard 28 §2 requires strictly 8 files in dual-installer release bundle, found {len(files)}: {files}")
    elif allow_portable_zip or len(portable_zips) > 0:
        if len(online_setups) != 1:
            errors.append(f"Expected exactly 1 Setup.exe installer, found: {online_setups}")
        if len(portable_zips) != 1:
            errors.append(f"Expected exactly 1 *-Portable.zip archive, found: {portable_zips}")
        else:
            errors.extend(_inspect_portable_zip(target_dir / portable_zips[0]))
        allowed = set(REQUIRED_RELEASE_FILES) | set(online_setups) | set(portable_zips)
        extras = sorted(set(files) - allowed)
        if extras:
            errors.append(f"Unexpected files in 7-file app release bundle: {extras}")
        if len(files) != 7:
            errors.append(f"Standard 28 §2 requires strictly 7 files in app+portable release bundle, found {len(files)}: {files}")
    else:
        if len(online_setups) != 1:
            errors.append(f"Expected exactly 1 Setup.exe installer, found: {online_setups}")
        allowed = set(REQUIRED_RELEASE_FILES) | set(online_setups)
        extras = sorted(set(files) - allowed)
        if extras:
            errors.append(f"Unexpected files in app release bundle: {extras}")
        if len(files) != 6:
            errors.append(f"Standard 01 §1.5 requires strictly 6 files in release bundle, found {len(files)}: {files}")

    return len(errors) == 0, errors


validate_release_bundle = validate_six_file_release_bundle


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Universal 6/7/8-File Release Bundle Validator & SHA256SUMS Generator")
    parser.add_argument("release_dir", type=Path, help="Path to Releases\\v<Version> or Releases\\Latest")
    parser.add_argument("--regen-sums", action="store_true", help="Regenerate SHA256SUMS.txt before validating")
    parser.add_argument("--allow-sdk-zip", action="store_true", help="Validate 6-file Foundation-v*.zip SDK bundle")
    parser.add_argument("--allow-portable-zip", action="store_true", help="Validate 7-file app bundle (*-Setup-v*.exe + *-Portable.zip)")
    parser.add_argument("--allow-dual-installer", action="store_true", help="Validate 8-file Dual-Installer bundle (*-Setup-v*.exe + *-Offline-Setup-v*.exe + *-Portable.zip)")
    parser.add_argument("--stage-sdk-zip", type=Path, help="Stage Foundation-v<version>.zip from this SDK directory into release_dir before validating")
    parser.add_argument("--version", default="", help="Version string used with --stage-sdk-zip (e.g. 0.5.4)")
    args = parser.parse_args(argv)

    if args.stage_sdk_zip is not None:
        if not args.version.strip():
            print("[FAIL] --stage-sdk-zip requires --version <X.Y.Z>.", file=sys.stderr)
            return 1
        stage_sdk_release_zip(args.stage_sdk_zip, args.release_dir, args.version.strip())
    elif args.regen_sums:
        generate_sha256sums(args.release_dir)

    ok, errs = validate_six_file_release_bundle(
        args.release_dir,
        allow_sdk_zip=args.allow_sdk_zip,
        allow_portable_zip=args.allow_portable_zip,
        allow_dual_installer=args.allow_dual_installer,
    )
    if not ok:
        for e in errs:
            print(f"[FAIL] {e}", file=sys.stderr)
        return 1
    print(f"[PASS] Release bundle verified: {args.release_dir.resolve()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
