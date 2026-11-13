# @module: Foundation.Tools.SyncFoundationSdk
# @purpose: Publishes complete Foundation SDK snapshots (.dll, .xml, WebAssets, foundation_core, tools, sdk-manifest.json) to SharedLibs/Foundation/vX.Y.Z, SharedLibs/Foundation/Latest, and Builds/vX.Y.Z, and syncs consumer projects (--sync-consumer).
# @depends-on: argparse, datetime, hashlib, json, pathlib, shutil
# @exports: publish_sdk, sync_consumer, main

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

FOUNDATION_ROOT = Path(__file__).resolve().parents[2]
DEV_ROOT = FOUNDATION_ROOT / "Dev"
SHARED_LIBS_ROOT = Path(r"<PROJECTS_ROOT>\SharedLibs\Foundation")


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(65536), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _copy_tree_contents(src: Path, dst: Path) -> None:
    if not src.exists():
        return
    dst.mkdir(parents=True, exist_ok=True)
    for item in src.rglob("*"):
        if "__pycache__" in item.parts:
            continue
        rel = item.relative_to(src)
        target = dst / rel
        if item.is_dir():
            target.mkdir(parents=True, exist_ok=True)
        elif item.is_file() and item.resolve() != target.resolve():
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(item, target)


def sync_web_into_webassets() -> Path:
    ui_dir = DEV_ROOT / "src" / "Foundation.UI"
    web_src = ui_dir / "Web"
    web_assets = ui_dir / "WebAssets"
    _copy_tree_contents(web_src, web_assets)
    _copy_tree_contents(web_src, web_assets / "Web")
    return web_assets


def collect_compiled_binaries(configuration: str = "Release") -> list[Path]:
    src_root = DEV_ROOT / "src"
    selected: dict[str, Path] = {}
    configs = [configuration, "Release", "Debug"]
    for cfg in configs:
        for proj_dir in sorted(src_root.glob("Foundation.*")):
            if not proj_dir.is_dir():
                continue
            bin_cfg = proj_dir / "bin" / cfg
            if not bin_cfg.exists():
                continue
            for tfm_dir in sorted(bin_cfg.glob("net8.0*")):
                for ext in ("*.dll", "*.xml", "*.pdb", "*.deps.json"):
                    for f in tfm_dir.glob(ext):
                        if f.name.startswith("Foundation.") and f.name not in selected:
                            selected[f.name] = f
    return [selected[k] for k in sorted(selected.keys())]


def _populate_sdk_target(
    target_dir: Path,
    version: str,
    ui_version: str,
    binaries: list[Path],
    web_assets: Path,
    py_core: Path,
    tools_dir: Path,
) -> dict[str, object]:
    target_dir.mkdir(parents=True, exist_ok=True)
    for bin_file in binaries:
        dst = target_dir / bin_file.name
        if bin_file.resolve() != dst.resolve():
            shutil.copy2(bin_file, dst)

    _copy_tree_contents(web_assets, target_dir / "WebAssets")
    _copy_tree_contents(py_core, target_dir / "foundation_core")
    _copy_tree_contents(tools_dir, target_dir / "tools")

    files_inventory: list[dict[str, object]] = []
    for f in sorted(target_dir.rglob("*")):
        if f.is_file() and f.name != "sdk-manifest.json":
            files_inventory.append(
                {
                    "relativePath": f.relative_to(target_dir).as_posix(),
                    "sizeBytes": f.stat().st_size,
                    "sha256": _sha256_file(f),
                }
            )

    manifest = {
        "schemaVersion": 1,
        "sdkId": "Foundation",
        "version": version,
        "uiVersion": ui_version,
        "publishedAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "fileCount": len(files_inventory),
        "files": files_inventory,
    }
    (target_dir / "sdk-manifest.json").write_text(
        json.dumps(manifest, indent=2), encoding="utf-8"
    )
    return manifest


def publish_sdk(version: str = "0.3.1", ui_version: str = "0.2.0", configuration: str = "Release") -> dict[str, object]:
    clean_ver = version.lstrip("v")
    ver_tag = f"v{clean_ver}"
    web_assets = sync_web_into_webassets()
    binaries = collect_compiled_binaries(configuration)
    py_core = DEV_ROOT / "src" / "foundation_core"
    tools_dir = DEV_ROOT / "tools"

    targets = [
        SHARED_LIBS_ROOT / ver_tag,
        SHARED_LIBS_ROOT / "Latest",
        FOUNDATION_ROOT / "Builds" / ver_tag / "app",
    ]
    manifest: dict[str, object] = {}
    for target in targets:
        manifest = _populate_sdk_target(
            target, clean_ver, ui_version, binaries, web_assets, py_core, tools_dir
        )
    return manifest


def sync_consumer(
    consumer_root: Path,
    source_version: str = "Latest",
    web_dir: Path | None = None,
) -> Path:
    src_dir = SHARED_LIBS_ROOT / source_version
    if not src_dir.exists():
        raise FileNotFoundError(f"Foundation SharedLibs source not found: {src_dir}")

    dev_lib = consumer_root / "Dev" / "lib" if (consumer_root / "Dev").exists() else consumer_root / "lib"
    dev_lib.mkdir(parents=True, exist_ok=True)
    nested_foundation = dev_lib / "Foundation"
    has_flat_dlls = any(dev_lib.glob("Foundation.*.dll"))
    target_lib = dev_lib if has_flat_dlls or not nested_foundation.exists() else nested_foundation

    for item in src_dir.glob("Foundation.*"):
        if item.is_file():
            shutil.copy2(item, target_lib / item.name)
            if nested_foundation.exists() and target_lib != nested_foundation:
                shutil.copy2(item, nested_foundation / item.name)

    if (src_dir / "sdk-manifest.json").exists():
        shutil.copy2(src_dir / "sdk-manifest.json", target_lib / "sdk-manifest.json")

    _copy_tree_contents(src_dir / "WebAssets", target_lib / "WebAssets")
    _copy_tree_contents(src_dir / "foundation_core", target_lib / "foundation_core")
    if nested_foundation.exists() and target_lib != nested_foundation:
        _copy_tree_contents(src_dir / "WebAssets", nested_foundation / "WebAssets")
        _copy_tree_contents(src_dir / "foundation_core", nested_foundation / "foundation_core")

    if web_dir is not None:
        resolved_web = web_dir if web_dir.is_absolute() else (consumer_root / web_dir)
        _copy_tree_contents(src_dir / "WebAssets", resolved_web)

    return target_lib


def main() -> int:
    parser = argparse.ArgumentParser(description="Foundation SDK Publisher & Consumer Sync Tool")
    parser.add_argument("--version", default="0.3.1", help="Foundation SDK SemVer (default: 0.3.1)")
    parser.add_argument("--ui-version", default="0.2.0", help="Foundation.UI SemVer (default: 0.2.0)")
    parser.add_argument("--configuration", default="Release", help="Build configuration (Release/Debug)")
    parser.add_argument("--publish", action="store_true", help="Publish SDK to SharedLibs and Builds")
    parser.add_argument("--sync-consumer", type=Path, help="Consumer project root to sync from SharedLibs")
    parser.add_argument("--source-version", default="Latest", help="SharedLibs version folder (default: Latest)")
    parser.add_argument("--web-dir", type=Path, help="Optional consumer web assets target directory")
    args = parser.parse_args()

    if args.publish or args.sync_consumer is None:
        manifest = publish_sdk(args.version, args.ui_version, args.configuration)
        print(f"[SYNC-SDK] Published Foundation v{manifest['version']} ({manifest['fileCount']} files) to SharedLibs & Builds.")

    if args.sync_consumer is not None:
        synced = sync_consumer(args.sync_consumer.resolve(), args.source_version, args.web_dir)
        print(f"[SYNC-SDK] Synced consumer library at {synced}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
