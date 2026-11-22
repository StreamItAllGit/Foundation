# @module: foundation_core.logging
# @purpose: Standard 35 structured UTC logging with 50-line in-memory ring buffer, byte-cap rotation, 30-day pruning, and <=15s TTY-aware \r\x1b[K heartbeats.
# @depends-on: collections, contextlib, os, pathlib, sys, threading, time
# @exports: RollingLogSink, emit_heartbeat

from __future__ import annotations

from collections import deque
from contextlib import suppress
import os
from pathlib import Path
import sys
import threading
import time

from .locks import utc_iso_ms


def emit_heartbeat(operation_name: str, completed: int, total: int, current_item: str = "working") -> str:
    """Formats and writes a <=15s heartbeat line with TTY-aware in-place carriage return."""
    pct = min(100.0, max(0.0, (completed / total * 100.0) if total > 0 else 0.0))
    line = f"[HEARTBEAT] {operation_name}: {completed}/{total} ({pct:.1f}%) | {current_item}"
    if sys.stdout.isatty():
        sys.stdout.write(f"\r\x1b[K{line}")
        sys.stdout.flush()
    return line


class RollingLogSink:
    """Thread-safe Standard 35 rolling text log writer with 50-line ring buffer and 30-day pruning."""

    DEFAULT_RING_BUFFER_SIZE = 50
    DEFAULT_RETENTION_DAYS = 30.0

    def __init__(
        self,
        log_dir: str | Path,
        base_name: str = "app.log",
        max_bytes: int = 10 * 1024 * 1024,
        max_files: int = 5,
        *,
        app_version: str = "0.1.0",
        default_slice: str = "core",
        ring_buffer_size: int = DEFAULT_RING_BUFFER_SIZE,
        retention_days: float = DEFAULT_RETENTION_DAYS,
    ) -> None:
        self.log_dir = Path(log_dir).resolve()
        self.base_name = base_name
        self.max_bytes = max(64 * 1024, int(max_bytes))
        self.max_files = max(1, int(max_files))
        self.app_version = (app_version or "0.1.0").lstrip("v")
        self.default_slice = (default_slice or "core").strip()
        self.retention_days = max(0.0, float(retention_days))
        self._ring: deque[str] = deque(maxlen=max(1, int(ring_buffer_size)))
        self._lock = threading.Lock()
        self.log_dir.mkdir(parents=True, exist_ok=True)

    @property
    def current_log_path(self) -> Path:
        return self.log_dir / self.base_name

    def write_line(self, level: str, message: str, slice_name: str | None = None) -> str:
        stamp = utc_iso_ms()
        tid = threading.get_native_id() if hasattr(threading, "get_native_id") else threading.get_ident()
        slice_tag = (slice_name or self.default_slice).strip() or "core"
        formatted = f"{stamp} [v{self.app_version}] [PID:{os.getpid()}/T:{tid:02d}] [{level.upper()}] [{slice_tag}] {message}"

        with self._lock:
            self.log_dir.mkdir(parents=True, exist_ok=True)
            path = self.current_log_path
            if path.exists() and path.stat().st_size >= self.max_bytes:
                self._rotate()
            with path.open("a", encoding="utf-8", newline="\n") as fh:
                fh.write(formatted + "\n")
            self._ring.append(formatted)
        return formatted

    def recent_lines(self, limit: int = DEFAULT_RING_BUFFER_SIZE) -> list[str]:
        with self._lock:
            if limit <= 0:
                return []
            items = list(self._ring)
            return items[-int(limit) :]

    def prune_old_logs(self, max_age_days: float | None = None) -> int:
        days = self.retention_days if max_age_days is None else max(0.0, float(max_age_days))
        cutoff = time.time() - (days * 86400.0)
        removed = 0
        if not self.log_dir.exists():
            return 0
        for candidate in self.log_dir.glob(f"{self.base_name}.*"):
            if not candidate.is_file():
                continue
            with suppress(OSError):
                if candidate.stat().st_mtime < cutoff:
                    candidate.unlink(missing_ok=True)
                    removed += 1
        return removed

    def _rotate(self) -> None:
        oldest = self.log_dir / f"{self.base_name}.{self.max_files}"
        if oldest.exists():
            oldest.unlink(missing_ok=True)

        for idx in range(self.max_files - 1, 0, -1):
            src = self.log_dir / f"{self.base_name}.{idx}"
            dst = self.log_dir / f"{self.base_name}.{idx + 1}"
            if src.exists():
                src.replace(dst)

        if self.current_log_path.exists():
            self.current_log_path.replace(self.log_dir / f"{self.base_name}.1")
        self.prune_old_logs()
