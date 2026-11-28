# @module: foundation_core.sidecars_locks
# @purpose: Pure-stdlib Standard 31/44 .hklock sentinel interop (canonical PidSentinelLock writes + legacy HkLockEnvelope read aliases) and partial-file filters.
# @depends-on: contextlib, datetime, json, os, pathlib, socket, typing, .locks
# @exports: is_temporary_or_partial_filename, is_active_foreign_hklock, has_active_foreign_lock_in_directory, try_acquire_directory_hklock, release_directory_hklock, DirectoryHkLock

from __future__ import annotations

from contextlib import suppress
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import socket
from typing import Any

from .locks import PidSentinelLock, get_process_state, parse_iso_utc


def is_temporary_or_partial_filename(file_name_or_path: str | Path | None) -> bool:
    if not file_name_or_path:
        return False
    name = Path(file_name_or_path).name
    low = name.lower()
    return (
        low.endswith((".hklock", ".part", ".tmp", ".crdownload"))
        or ".partial_" in low
        or name.startswith("~$")
    )


def _normalize_hklock_envelope(env: dict[str, Any]) -> dict[str, Any]:
    out = dict(env)
    if not out.get("hostName"):
        for alias in ("machineName", "MachineName", "hostname", "host"):
            if out.get(alias):
                out["hostName"] = str(out[alias]).strip()
                break
    if "pid" not in out:
        for alias in ("processId", "ProcessId"):
            if out.get(alias) is not None:
                out["pid"] = out[alias]
                break
    if not out.get("processStartTimeUtc") and out.get("ProcessStartTimeUtc"):
        out["processStartTimeUtc"] = out["ProcessStartTimeUtc"]
    if not out.get("heartbeatAtUtc") and out.get("HeartbeatAtUtc"):
        out["heartbeatAtUtc"] = out["HeartbeatAtUtc"]
    if not out.get("acquiredAtUtc"):
        for alias in ("startedAtUtc", "StartedAtUtc"):
            if out.get(alias):
                out["acquiredAtUtc"] = out[alias]
                break
    if not out.get("targetPath"):
        for alias in ("targetFilePath", "TargetFilePath"):
            if out.get(alias):
                out["targetPath"] = out[alias]
                break
    return out


def is_active_foreign_hklock(
    lock_path: str | Path, *, stale_max_age_seconds: float = 5400.0
) -> tuple[bool, str, dict[str, Any] | None]:
    path = Path(lock_path)
    try:
        st = path.stat()
    except OSError:
        return (False, "", None)

    now = datetime.now(timezone.utc)
    if (now.timestamp() - st.st_mtime) > stale_max_age_seconds:
        return (False, "", None)

    try:
        raw = path.read_text(encoding="utf-8-sig").strip()
    except OSError:
        return (True, f"Active exclusive .hklock file handle on '{path.name}'.", None)

    parsed: dict[str, Any] | None = None
    if raw.startswith("{"):
        with suppress(ValueError, TypeError):
            obj = json.loads(raw)
            if isinstance(obj, dict):
                parsed = _normalize_hklock_envelope(obj)

    if parsed is None or not str(parsed.get("hostName") or "").strip():
        return (True, f"Active .hklock sentinel '{path.name}' present.", parsed)

    with suppress(ValueError, TypeError):
        pid = int(parsed.get("pid", -1))
        if pid <= 0:
            return (True, f"Active .hklock sentinel '{path.name}' present.", parsed)
        expires = parse_iso_utc(parsed.get("expiresAtUtc"))
        if expires is not None and expires < now:
            return (False, "", parsed)
        hb = parse_iso_utc(parsed.get("heartbeatAtUtc"))
        if hb is not None and (now - hb).total_seconds() > stale_max_age_seconds:
            return (False, "", parsed)
        host = str(parsed["hostName"]).strip()
        if host.lower() == socket.gethostname().lower():
            if pid == os.getpid():
                return (False, "", parsed)
            alive, actual_start = get_process_state(pid)
            if not alive:
                return (False, "", parsed)
            expected_start = parse_iso_utc(parsed.get("processStartTimeUtc"))
            if expected_start is not None and actual_start is not None:
                if abs((actual_start - expected_start).total_seconds()) > 2.0:
                    return (False, "", parsed)
        return (True, f"Active directory lock held by {host} (PID {pid}).", parsed)
    return (False, "", parsed)


def _iter_lock_check_dirs(directory_path: Path) -> list[Path]:
    dirs = [directory_path]
    parent = directory_path.parent
    if parent != directory_path and parent.exists():
        dirs.append(parent)
    return dirs


def has_active_foreign_lock_in_directory(
    directory_path: str | Path | None, *, honor_hk_locks: bool = True
) -> tuple[bool, str, dict[str, Any] | None]:
    if not honor_hk_locks or not directory_path:
        return (False, "", None)
    root = Path(directory_path)
    if not root.is_dir():
        return (False, "", None)
    for search_dir in _iter_lock_check_dirs(root):
        with suppress(OSError):
            for lock_file in search_dir.glob("*.hklock"):
                active, reason, env = is_active_foreign_hklock(lock_file)
                if active:
                    return (True, reason, env)
    return (False, "", None)


def try_acquire_directory_hklock(
    directory_path: str | Path,
    app_slug: str,
    *,
    honor_hk_locks: bool = True,
    lease_seconds: float = 300.0,
    operation: str = "sidecar-mutation",
) -> tuple[bool, Path | None, str | None]:
    """Acquires a canonical Standard 18/31 .hklock sentinel via PidSentinelLock."""
    if not honor_hk_locks or not directory_path:
        return (True, None, None)
    root = Path(directory_path)
    if not root.is_dir():
        return (True, None, None)

    for search_dir in _iter_lock_check_dirs(root):
        with suppress(OSError):
            for existing in search_dir.glob("*.hklock"):
                active, reason, _ = is_active_foreign_hklock(existing)
                if active:
                    return (False, None, reason)
                with suppress(OSError):
                    existing.unlink(missing_ok=True)

    clean_slug = (app_slug or "app").strip().lower() or "app"
    lock_name = f".{clean_slug}_{os.getpid()}.hklock"
    sentinel = PidSentinelLock(
        root,
        app_id=clean_slug,
        operation=operation,
        lock_filename=lock_name,
        lease_seconds=lease_seconds,
        lock_type="directory-mutation",
    )
    if not sentinel.acquire():
        return (False, None, f"Contention acquiring '{lock_name}' in '{root}'.")
    return (True, sentinel.lock_path, None)


def release_directory_hklock(lock_path: str | Path | None) -> None:
    if lock_path is not None:
        with suppress(OSError):
            Path(lock_path).unlink(missing_ok=True)


class DirectoryHkLock:
    """Context manager wrapper around try_acquire_directory_hklock/release_directory_hklock."""

    def __init__(self, directory_path: str | Path, app_slug: str, *, honor_hk_locks: bool = True) -> None:
        self.directory_path = Path(directory_path)
        self.app_slug = app_slug
        self.honor_hk_locks = honor_hk_locks
        self.lock_path: Path | None = None

    def __enter__(self) -> DirectoryHkLock:
        acquired, path, reason = try_acquire_directory_hklock(
            self.directory_path, self.app_slug, honor_hk_locks=self.honor_hk_locks
        )
        if not acquired:
            raise RuntimeError(reason or f"Directory '{self.directory_path}' is locked.")
        self.lock_path = path
        return self

    def __exit__(self, exc_type: object, exc_val: object, exc_tb: object) -> None:
        release_directory_hklock(self.lock_path)
        self.lock_path = None
