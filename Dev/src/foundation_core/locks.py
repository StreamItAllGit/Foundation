# @module: foundation_core.locks
# @purpose: Standard 31 PID-reuse-safe JSON lockfiles (.hklock / .lock) with ±2s processStartTimeUtc verification, lease heartbeats, and shared OS process-state helpers.
# @depends-on: contextlib, ctypes, datetime, json, os, pathlib, socket, threading, time, typing
# @exports: utc_iso_ms, parse_iso_utc, get_process_state, get_process_start_time_utc, PidSentinelLock

from __future__ import annotations

from contextlib import suppress
import ctypes
from datetime import datetime, timedelta, timezone
import json
import os
from pathlib import Path
import socket
import threading
import time
from typing import Any


def utc_iso_ms(dt: datetime | None = None) -> str:
    """Formats a UTC datetime as ISO-8601 YYYY-MM-DDTHH:mm:ss.fffZ."""
    cur = (dt or datetime.now(timezone.utc)).astimezone(timezone.utc)
    return cur.strftime("%Y-%m-%dT%H:%M:%S.") + f"{cur.microsecond // 1000:03d}Z"


def parse_iso_utc(raw: Any) -> datetime | None:
    """Parses a UTC ISO-8601 timestamp string or returns None."""
    with suppress(ValueError):
        if isinstance(raw, str) and (s := raw.strip()):
            dt = datetime.fromisoformat((s[:-1] + "+00:00") if s.endswith("Z") else s)
            return dt if dt.tzinfo is not None else dt.replace(tzinfo=timezone.utc)
    return None


def _is_posix_pid_alive(pid: int) -> bool:
    if pid <= 0:
        return False
    try:
        os.kill(pid, 0)
        return True
    except PermissionError:
        return True
    except OSError:
        return False


def _read_posix_proc_start_utc(pid: int) -> datetime | None:
    try:
        stat_text = Path(f"/proc/{pid}/stat").read_text(encoding="utf-8", errors="ignore")
        rparen = stat_text.rfind(")")
        if rparen < 0:
            return None
        fields = stat_text[rparen + 2 :].split()
        if len(fields) <= 19:
            return None
        start_ticks = int(fields[19])
        clk_tck = os.sysconf("SC_CLK_TCK")
        if clk_tck <= 0:
            return None
        btime_sec: int | None = None
        for line in Path("/proc/stat").read_text(encoding="utf-8", errors="ignore").splitlines():
            if line.startswith("btime "):
                btime_sec = int(line.split()[1])
                break
        if btime_sec is None:
            return None
        return datetime.fromtimestamp(btime_sec + (start_ticks / float(clk_tck)), tz=timezone.utc)
    except Exception:
        return None


def get_process_state(pid: int) -> tuple[bool, datetime | None]:
    """Returns (is_alive, process_start_time_utc) using Win32 GetProcessTimes or POSIX /proc fallback."""
    if pid <= 0:
        return (False, None)
    if os.name != "nt":
        alive = _is_posix_pid_alive(pid)
        return (alive, _read_posix_proc_start_utc(pid) if alive else None)

    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    handle = k32.OpenProcess(0x1000, False, pid)
    if not handle:
        return (ctypes.get_last_error() == 5, None)
    try:
        code, ct, et, kt, ut = ctypes.c_uint32(), ctypes.c_uint64(), ctypes.c_uint64(), ctypes.c_uint64(), ctypes.c_uint64()
        if not k32.GetExitCodeProcess(handle, ctypes.byref(code)) or code.value != 259:
            return (False, None)
        ok = k32.GetProcessTimes(handle, ctypes.byref(ct), ctypes.byref(et), ctypes.byref(kt), ctypes.byref(ut))
        start_dt = (
            datetime(1970, 1, 1, tzinfo=timezone.utc) + timedelta(microseconds=max(0, (ct.value - 116444736000000000) // 10))
            if ok
            else None
        )
        return (True, start_dt)
    finally:
        k32.CloseHandle(handle)


def get_process_start_time_utc(pid: int | None = None) -> str | None:
    """Returns UTC ISO millisecond timestamp for a process start time, or current UTC for os.getpid() fallback."""
    target_pid = os.getpid() if pid is None else int(pid)
    alive, start_dt = get_process_state(target_pid)
    if start_dt is not None:
        return utc_iso_ms(start_dt)
    return utc_iso_ms() if alive and target_pid == os.getpid() else None


class PidSentinelLock:
    """Manages Standard 31 PID-reuse-safe .hklock/.lock sentinel files with lease heartbeats."""

    SCHEMA_URI = "https://schemas.local/lockfile.v1.json"

    def __init__(
        self,
        target_dir: str | Path,
        app_id: str,
        operation: str = "batch",
        *,
        lock_filename: str = ".hklock",
        lease_seconds: float = 300.0,
        lock_type: str = "directory-mutation",
        version: str = "0.1.0",
        suite_id: str | None = None,
        port: int | None = None,
    ) -> None:
        self.target_dir = Path(target_dir).resolve()
        self.lock_path = self.target_dir / lock_filename
        self.app_id = app_id
        self.operation = operation
        self.lease_seconds = max(1.0, float(lease_seconds))
        self.lock_type = lock_type
        self.version = version
        self.suite_id = suite_id
        self.port = port
        self._acquired = False
        self.envelope: dict[str, Any] | None = None

    @staticmethod
    def is_pid_alive(pid: int) -> bool:
        alive, _ = get_process_state(pid)
        return alive

    @classmethod
    def inspect_lock(cls, lock_path: str | Path) -> tuple[bool, dict[str, Any] | None]:
        """4-step stale-lock & ±2s PID-reuse verification returning (is_actively_held, envelope)."""
        path = Path(lock_path)
        if not path.exists():
            return (False, None)
        with suppress(OSError, ValueError, TypeError):
            raw = path.read_text(encoding="utf-8").strip()
            if not raw:
                return (False, None)
            env = json.loads(raw)
            if not isinstance(env, dict):
                return (False, None)
            host = str(env.get("hostName") or env.get("hostname") or env.get("host") or "").strip()
            if host and "hostName" not in env:
                env["hostName"] = host
            if not env.get("appId") and env.get("ownerAppId"):
                env["appId"] = env["ownerAppId"]
            pid = int(env.get("pid", -1))
            if pid <= 0:
                return (False, env)
            expires = parse_iso_utc(env.get("expiresAtUtc"))
            if expires is not None and expires < datetime.now(timezone.utc):
                return (False, env)
            if host and host.lower() != socket.gethostname().lower():
                return (True, env)
            alive, actual_start = get_process_state(pid)
            if not alive:
                return (False, env)
            expected_start = parse_iso_utc(env.get("processStartTimeUtc"))
            if expected_start is not None and actual_start is not None:
                if abs((actual_start - expected_start).total_seconds()) > 2.0:
                    return (False, env)
            return (True, env)
        return (False, None)

    def acquire(self) -> bool:
        self.target_dir.mkdir(parents=True, exist_ok=True)
        held, existing = self.inspect_lock(self.lock_path)
        if held and existing is not None:
            holder_pid = int(existing.get("pid", -1))
            if holder_pid != os.getpid() or not self._acquired:
                return False
        elif self.lock_path.exists():
            self.lock_path.unlink(missing_ok=True)

        now = datetime.now(timezone.utc)
        _, proc_start = get_process_state(os.getpid())
        envelope: dict[str, Any] = {
            "$schema": self.SCHEMA_URI,
            "schemaVersion": 1,
            "lockType": self.lock_type,
            "appId": self.app_id,
            "version": self.version,
            "pid": os.getpid(),
            "processStartTimeUtc": utc_iso_ms(proc_start) if proc_start is not None else utc_iso_ms(now),
            "hostName": socket.gethostname(),
            "operation": self.operation,
            "targetPath": str(self.target_dir),
            "acquiredAtUtc": utc_iso_ms(now),
            "heartbeatAtUtc": utc_iso_ms(now),
            "expiresAtUtc": utc_iso_ms(now + timedelta(seconds=self.lease_seconds)),
        }
        if self.suite_id is not None:
            envelope["suiteId"] = self.suite_id
        if self.port is not None:
            envelope["port"] = int(self.port)
        self._write_envelope_atomic(envelope)
        self.envelope = envelope
        self._acquired = True
        return True

    def refresh_heartbeat(self, lease_seconds: float | None = None) -> bool:
        if not self._acquired or self.envelope is None:
            return False
        ttl = max(1.0, float(self.lease_seconds if lease_seconds is None else lease_seconds))
        now = datetime.now(timezone.utc)
        updated = dict(self.envelope)
        updated["heartbeatAtUtc"] = utc_iso_ms(now)
        updated["expiresAtUtc"] = utc_iso_ms(now + timedelta(seconds=ttl))
        self._write_envelope_atomic(updated)
        self.envelope = updated
        return True

    def _write_envelope_atomic(self, envelope: dict[str, Any]) -> None:
        tmp = self.lock_path.with_name(f"{self.lock_path.name}.{os.getpid()}.{threading.get_ident()}.tmp")
        try:
            with tmp.open("w", encoding="utf-8", newline="\n") as fh:
                fh.write(json.dumps(envelope, indent=2) + "\n")
                fh.flush()
                os.fsync(fh.fileno())
            os.replace(tmp, self.lock_path)
        finally:
            tmp.unlink(missing_ok=True)

    def release(self) -> None:
        if self._acquired and self.lock_path.exists():
            with suppress(OSError):
                self.lock_path.unlink(missing_ok=True)
        self._acquired = False
        self.envelope = None

    def __enter__(self) -> PidSentinelLock:
        if not self.acquire():
            raise RuntimeError(f"Directory '{self.target_dir}' is locked by an active sister-app process.")
        return self

    def __exit__(self, exc_type: object, exc_val: object, exc_tb: object) -> None:
        self.release()
