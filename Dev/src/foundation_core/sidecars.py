# @module: foundation_core.sidecars
# @purpose: Pure-stdlib Standard 44 streaming CRC32/MD5/SHA1/SHA256 hashing, .hash v1 sidecars, .archive_hashes.json atomic store, and .hklock re-exports.
# @depends-on: contextlib, dataclasses, datetime, enum, hashlib, json, os, pathlib, threading, typing, zlib, .sidecars_locks
# @exports: HashFilePlacement, FileHashDigest, HashSidecarEntry, HashSidecarDocument, compute_crc32_hex, compute_file_hashes, get_hash_sidecar_path, normalize_relative_paths_stripping_single_wrapper, compute_composite_folder_sha1, format_hash_sidecar, parse_hash_sidecar, read_hash_sidecar, write_hash_sidecar_atomic, read_archive_hashes_json, write_archive_hashes_json_atomic, is_temporary_or_partial_filename, is_active_foreign_hklock, has_active_foreign_lock_in_directory, try_acquire_directory_hklock, release_directory_hklock, DirectoryHkLock

from __future__ import annotations

from contextlib import suppress
from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import IntEnum
import hashlib
import json
import os
from pathlib import Path
import threading
from typing import Any, Iterable, Mapping, Sequence
import zlib

from .sidecars_locks import (
    DirectoryHkLock,
    has_active_foreign_lock_in_directory,
    is_active_foreign_hklock,
    is_temporary_or_partial_filename,
    release_directory_hklock,
    try_acquire_directory_hklock,
)

RESERVED_CONSOLE_WRAPPER_FOLDERS: frozenset[str] = frozenset(
    {"PS3_GAME", "sce_sys", "code", "content", "meta", "romfs", "exefs", "BDMV", "VIDEO_TS"}
)
_ARCHIVE_EXTENSIONS: frozenset[str] = frozenset({".zip", ".7z", ".rar", ".tar", ".gz", ".xz", ".bz2"})


class HashFilePlacement(IntEnum):
    DatabaseOnly = 0
    SidecarBeside = 1
    PackageInsideArchive = 2
    SidecarAndArchive = 3


@dataclass(frozen=True, slots=True)
class FileHashDigest:
    size_bytes: int
    crc32: str
    md5: str
    sha1: str
    sha256: str


@dataclass(slots=True)
class HashSidecarEntry:
    relative_path: str
    size: int
    crc32: str
    md5: str = ""
    sha1: str = ""


@dataclass(slots=True)
class HashSidecarDocument:
    title: str = ""
    is_composite_folder: bool = False
    composite_sha1: str = ""
    total_files_header: int = 0
    total_size_header: int = 0
    container_file_name: str = ""
    container_size: int = 0
    container_crc32: str = ""
    container_md5: str = ""
    container_sha1: str = ""
    is_legacy_outer_archive_hash: bool = False
    files: list[HashSidecarEntry] = field(default_factory=list)


def compute_crc32_hex(file_path: str | Path, chunk_size: int = 65536) -> str:
    """Computes uppercase 8-character CRC32 hex digest for a file without loading it fully into RAM."""
    crc = 0
    with Path(file_path).open("rb") as fh:
        while chunk := fh.read(chunk_size):
            crc = zlib.crc32(chunk, crc)
    return f"{crc & 0xFFFFFFFF:08X}"


def compute_file_hashes(file_path: str | Path, chunk_size: int = 65536) -> FileHashDigest:
    """Computes size, CRC32, MD5, SHA1, and SHA256 in a single streaming read pass."""
    crc = 0
    md5_h = hashlib.md5()
    sha1_h = hashlib.sha1()
    sha256_h = hashlib.sha256()
    total = 0
    with Path(file_path).open("rb") as fh:
        while chunk := fh.read(chunk_size):
            total += len(chunk)
            crc = zlib.crc32(chunk, crc)
            md5_h.update(chunk)
            sha1_h.update(chunk)
            sha256_h.update(chunk)
    return FileHashDigest(
        size_bytes=total,
        crc32=f"{crc & 0xFFFFFFFF:08X}",
        md5=md5_h.hexdigest().upper(),
        sha1=sha1_h.hexdigest().upper(),
        sha256=sha256_h.hexdigest().upper(),
    )


def get_hash_sidecar_path(target_path: str | Path) -> Path:
    return Path(f"{os.fspath(target_path)}.hash")


def normalize_relative_paths_stripping_single_wrapper(entries: Sequence[HashSidecarEntry]) -> list[HashSidecarEntry]:
    if not entries:
        return []
    normalized = [
        HashSidecarEntry(e.relative_path.replace("\\", "/").lstrip("/"), e.size, e.crc32, e.md5, e.sha1)
        for e in entries
    ]
    reserved_lower = {r.lower() for r in RESERVED_CONSOLE_WRAPPER_FOLDERS}
    shared_wrapper: str | None = None
    for item in normalized:
        slash_idx = item.relative_path.find("/")
        if slash_idx <= 0 or slash_idx == len(item.relative_path) - 1:
            return normalized
        seg0 = item.relative_path[:slash_idx]
        low = seg0.lower()
        if low in reserved_lower or low.startswith(("disc ", "disk ", "cd")):
            return normalized
        if shared_wrapper is None:
            shared_wrapper = seg0
        elif shared_wrapper.lower() != low:
            return normalized
    if not shared_wrapper:
        return normalized
    prefix_len = len(shared_wrapper) + 1
    return [
        HashSidecarEntry(item.relative_path[prefix_len:], item.size, item.crc32, item.md5, item.sha1)
        for item in normalized
    ]


def compute_composite_folder_sha1(entries: Iterable[HashSidecarEntry]) -> str:
    sorted_items = sorted(entries, key=lambda x: x.relative_path.replace("\\", "/").lower())
    lines = "".join(
        f"{item.relative_path.replace('\\', '/')}|{item.size}|{item.crc32.upper()}\n"
        for item in sorted_items
    )
    return hashlib.sha1(lines.encode("utf-8")).hexdigest().upper()


def format_hash_sidecar(
    title: str,
    entries: Sequence[HashSidecarEntry],
    *,
    composite_sha1: str | None = None,
    container: tuple[str, int, str, str, str] | None = None,
) -> str:
    generated = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    if composite_sha1 and composite_sha1.strip():
        total_size = sum(e.size for e in entries)
        out = [
            "# Housekeeping Folder Composite Checksum",
            "# SchemaVersion: 1",
            f"# Folder: {title}",
            f"# TotalFiles: {len(entries)}",
            f"# Generated: {generated}",
            "",
            f"TOTAL_FILES: {len(entries)}",
            f"TOTAL_SIZE: {total_size}",
            f"COMPOSITE_SHA1: {composite_sha1.strip().upper()}",
        ]
        for e in sorted(entries, key=lambda x: x.relative_path.replace("\\", "/").lower()):
            rel = e.relative_path.replace("\\", "/")
            out.append(f"ENTRY: {rel} | SIZE: {e.size} | CRC32: {e.crc32.upper()}")
        return "\n".join(out) + "\n"

    norm = normalize_relative_paths_stripping_single_wrapper(entries) if len(entries) > 1 else list(entries)
    out = [
        "# Housekeeping Checksum Sidecar",
        "# SchemaVersion: 1",
        f"# Title: {title}",
    ]
    if len(norm) > 1:
        out.append(f"# TotalFiles: {len(norm)}")
    if container is not None and container[4].strip():
        cname, csz, ccrc, cmd5, csha1 = container
        out.append(
            f"# Container: {cname} | SIZE: {csz} | CRC32: {ccrc.upper()} | MD5: {cmd5.upper()} | SHA1: {csha1.upper()}"
        )
    out.extend([f"# Generated: {generated}", ""])
    for f in norm:
        rel = f.relative_path.replace("\\", "/")
        out.append(
            f"FILE: {rel} | SIZE: {f.size} | CRC32: {f.crc32.upper()} | MD5: {f.md5.upper()} | SHA1: {f.sha1.upper()}"
        )
    return "\n".join(out) + "\n"


def parse_hash_sidecar(text: str, outer_name: str = "") -> HashSidecarDocument | None:
    if not text or not text.strip():
        return None
    doc = HashSidecarDocument()
    outer_is_archive = Path(outer_name).suffix.lower() in _ARCHIVE_EXTENSIONS if outer_name else False
    for raw in text.splitlines():
        line = raw.strip()
        if not line:
            continue
        low = line.lower()
        if low.startswith("# title:"):
            doc.title = line[len("# Title:") :].strip()
        elif low.startswith("# folder:"):
            doc.title = line[len("# Folder:") :].strip()
            doc.is_composite_folder = True
        elif low.startswith("# totalfiles:") and doc.total_files_header <= 0:
            with suppress(ValueError):
                doc.total_files_header = int(line[len("# TotalFiles:") :].strip())
        elif low.startswith("total_files:"):
            with suppress(ValueError):
                doc.total_files_header = int(line[len("TOTAL_FILES:") :].strip())
                doc.is_composite_folder = True
        elif low.startswith("total_size:"):
            with suppress(ValueError):
                doc.total_size_header = int(line[len("TOTAL_SIZE:") :].strip())
                doc.is_composite_folder = True
        elif low.startswith("# container:"):
            for seg in (p.strip() for p in line.split("|")):
                sl = seg.lower()
                if sl.startswith("# container:"):
                    doc.container_file_name = seg[len("# Container:") :].strip()
                elif sl.startswith("size:"):
                    with suppress(ValueError):
                        doc.container_size = int(seg[len("SIZE:") :].strip())
                elif sl.startswith("crc32:"):
                    doc.container_crc32 = seg[len("CRC32:") :].strip().upper()
                elif sl.startswith("md5:"):
                    doc.container_md5 = seg[len("MD5:") :].strip().upper()
                elif sl.startswith("sha1:"):
                    doc.container_sha1 = seg[len("SHA1:") :].strip().upper()
        elif low.startswith("composite_sha1:"):
            doc.is_composite_folder = True
            doc.composite_sha1 = line[len("COMPOSITE_SHA1:") :].strip().upper()
        elif low.startswith("file:") or low.startswith("entry:"):
            if low.startswith("entry:"):
                doc.is_composite_folder = True
            rel, crc, md5_h, sha1_h, sz = "", "", "", "", 0
            for seg in (p.strip() for p in line.split("|")):
                sl = seg.lower()
                if sl.startswith("file:"):
                    rel = seg[len("FILE:") :].strip()
                elif sl.startswith("entry:"):
                    rel = seg[len("ENTRY:") :].strip()
                elif sl.startswith("size:"):
                    with suppress(ValueError):
                        sz = int(seg[len("SIZE:") :].strip())
                elif sl.startswith("crc32:"):
                    crc = seg[len("CRC32:") :].strip().upper()
                elif sl.startswith("md5:"):
                    md5_h = seg[len("MD5:") :].strip().upper()
                elif sl.startswith("sha1:"):
                    sha1_h = seg[len("SHA1:") :].strip().upper()
            if rel:
                doc.files.append(HashSidecarEntry(rel, sz, crc, md5_h, sha1_h))

    if outer_is_archive and not doc.is_composite_folder and doc.files:
        if any(
            Path(f.relative_path).name.lower() == outer_name.lower()
            or Path(f.relative_path).suffix.lower() in _ARCHIVE_EXTENSIONS
            for f in doc.files
        ):
            doc.is_legacy_outer_archive_hash = True
            if not doc.container_sha1 and len(doc.files) == 1:
                f0 = doc.files[0]
                doc.container_file_name = f0.relative_path
                doc.container_size = f0.size
                doc.container_crc32 = f0.crc32
                doc.container_md5 = f0.md5
                doc.container_sha1 = f0.sha1
            doc.files.clear()

    return doc if (doc.files or doc.composite_sha1 or doc.container_sha1) else None


def read_hash_sidecar(sidecar_path: str | Path, outer_name: str = "") -> HashSidecarDocument | None:
    path = Path(sidecar_path)
    if not path.is_file():
        return None
    target_outer = outer_name or (path.stem if path.suffix.lower() == ".hash" else path.name)
    return parse_hash_sidecar(path.read_text(encoding="utf-8-sig"), target_outer)


def _write_text_atomic(path: Path, text: str) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(f"{path.name}.{os.getpid()}.{threading.get_ident()}.tmp")
    try:
        with tmp.open("w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
            fh.flush()
            os.fsync(fh.fileno())
        os.replace(tmp, path)
        return path
    finally:
        with suppress(OSError):
            tmp.unlink(missing_ok=True)


def write_hash_sidecar_atomic(sidecar_path: str | Path, content: str) -> Path:
    return _write_text_atomic(Path(sidecar_path), content)


def read_archive_hashes_json(json_path: str | Path) -> dict[str, Any] | None:
    path = Path(json_path)
    if not path.is_file():
        return None
    with suppress(OSError, ValueError, TypeError):
        loaded = json.loads(path.read_text(encoding="utf-8-sig"))
        if isinstance(loaded, dict):
            return loaded
    return None


def write_archive_hashes_json_atomic(json_path: str | Path, payload: Mapping[str, Any]) -> Path:
    return _write_text_atomic(Path(json_path), json.dumps(dict(payload), indent=2, ensure_ascii=False) + "\n")


__all__ = [
    "DirectoryHkLock",
    "FileHashDigest",
    "HashFilePlacement",
    "HashSidecarDocument",
    "HashSidecarEntry",
    "compute_composite_folder_sha1",
    "compute_crc32_hex",
    "compute_file_hashes",
    "format_hash_sidecar",
    "get_hash_sidecar_path",
    "has_active_foreign_lock_in_directory",
    "is_active_foreign_hklock",
    "is_temporary_or_partial_filename",
    "normalize_relative_paths_stripping_single_wrapper",
    "parse_hash_sidecar",
    "read_archive_hashes_json",
    "read_hash_sidecar",
    "release_directory_hklock",
    "try_acquire_directory_hklock",
    "write_archive_hashes_json_atomic",
    "write_hash_sidecar_atomic",
]
