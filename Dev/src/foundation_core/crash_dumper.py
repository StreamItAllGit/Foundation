# @module: foundation_core.crash_dumper
# @purpose: Standard 43 unhandled exception hooks writing Zero-PII camelCase crash JSON bundles under logs/crashes/ with 20-bundle pruning.
# @depends-on: contextlib, datetime, json, os, pathlib, re, sys, threading, time, traceback, typing, .locks
# @exports: sanitize_pii_text, CrashDumper

from __future__ import annotations

from contextlib import suppress
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import sys
import threading
import time
import traceback
from types import TracebackType
from typing import Any, Callable, Iterable, Sequence

from .locks import utc_iso_ms

_SAFE_VER_RE = re.compile(r"[^A-Za-z0-9._-]+")
_WIN_HOME_RE = re.compile(r"[A-Za-z]:\\Users\\[^\\\s\"']+", re.IGNORECASE)
_WIN_SLASH_HOME_RE = re.compile(r"[A-Za-z]:/Users/[^/\s\"']+", re.IGNORECASE)
_POSIX_HOME_RE = re.compile(r"/(?:home|Users)/[^/\s\"']+")
_WEBHOOK_RE = re.compile(r"https://(?:discord|discordapp)\.com/api/webhooks/[^\s\"']+", re.IGNORECASE)
_BEARER_RE = re.compile(r"(Bearer\s+)[A-Za-z0-9._\-+/=]+", re.IGNORECASE)
_CLI_SECRET_RE = re.compile(r"(--(?:automation-token|token|api-key|secret|password))(?:=|\s+)(\"[^\"]*\"|'[^']*'|\S+)", re.IGNORECASE)
_KV_SECRET_RE = re.compile(r"(?i)\b(token|api_key|apikey|secret|password)\b(\s*[:=]\s*)(\"[^\"]*\"|'[^']*'|[^\s\",;]+)")


def sanitize_pii_text(text: str) -> str:
    """Scrubs user profile home directories ([USER_HOME]) and secrets/tokens ([REDACTED_TOKEN])."""
    if not text:
        return ""
    cleaned = text
    candidates = {str(Path.home()), os.environ.get("USERPROFILE", ""), os.environ.get("HOME", "")}
    for raw_home in sorted((c.strip() for c in candidates if c and c.strip()), key=len, reverse=True):
        norm_bs = raw_home.replace("/", "\\").rstrip("\\")
        norm_fs = raw_home.replace("\\", "/").rstrip("/")
        for variant in (norm_bs, norm_fs):
            if variant:
                cleaned = re.sub(re.escape(variant), "[USER_HOME]", cleaned, flags=re.IGNORECASE)
    cleaned = _WIN_HOME_RE.sub("[USER_HOME]", cleaned)
    cleaned = _WIN_SLASH_HOME_RE.sub("[USER_HOME]", cleaned)
    cleaned = _POSIX_HOME_RE.sub("[USER_HOME]", cleaned)
    cleaned = _WEBHOOK_RE.sub("[REDACTED_TOKEN]", cleaned)
    cleaned = _BEARER_RE.sub(r"\1[REDACTED_TOKEN]", cleaned)
    cleaned = _CLI_SECRET_RE.sub(r"\1 [REDACTED_TOKEN]", cleaned)
    cleaned = _KV_SECRET_RE.sub(r"\1\2[REDACTED_TOKEN]", cleaned)
    return cleaned


def _sanitize_command_line(argv: Sequence[str] | None = None) -> str:
    tokens = list(sys.argv if argv is None else argv)
    redacted: list[str] = []
    redact_next = False
    for tok in tokens:
        if redact_next:
            redacted.append("[REDACTED_TOKEN]")
            redact_next = False
            continue
        low = tok.lower()
        if low in {"--automation-token", "--token", "--api-key", "--secret", "--password"}:
            redacted.append(tok)
            redact_next = True
            continue
        redacted.append(sanitize_pii_text(tok))
    return sanitize_pii_text(" ".join(redacted))


class CrashDumper:
    """Writes Standard 43 Zero-PII camelCase JSON crash bundles into <log_dir>/crashes/."""

    DEFAULT_MAX_BUNDLES = 20

    def __init__(
        self,
        log_dir: str | Path,
        app_name: str,
        app_version: str,
        *,
        app_archetype: str = "STD",
        runtime_mode: str = "production",
        recent_logs_provider: Callable[[], Iterable[str]] | None = None,
        max_bundles: int = DEFAULT_MAX_BUNDLES,
        started_monotonic: float | None = None,
    ) -> None:
        self.log_dir = Path(log_dir).resolve()
        self.crashes_dir = self.log_dir / "crashes"
        self.app_name = app_name
        self.app_version = (app_version or "0.1.0").lstrip("v")
        self.app_archetype = app_archetype
        self.runtime_mode = runtime_mode
        self.recent_logs_provider = recent_logs_provider
        self.max_bundles = max(1, int(max_bundles))
        self.started_monotonic = time.monotonic() if started_monotonic is None else float(started_monotonic)

    def write_crash_report(
        self,
        exc_type: type[BaseException],
        exc_value: BaseException,
        exc_tb: TracebackType | None,
        context_tag: str = "unhandled",
        *,
        recent_logs: Iterable[str] | None = None,
        argv: Sequence[str] | None = None,
    ) -> Path:
        self.log_dir.mkdir(parents=True, exist_ok=True)
        self.crashes_dir.mkdir(parents=True, exist_ok=True)
        now = datetime.now(timezone.utc)
        stamp = now.strftime("%Y%m%d_%H%M%S")
        safe_ver = _SAFE_VER_RE.sub("_", self.app_version) or "0.1.0"
        crash_id = f"crash_{stamp}_v{safe_ver}"
        crash_path = self.crashes_dir / f"{crash_id}.json"
        seq = 1
        while crash_path.exists():
            crash_path = self.crashes_dir / f"{crash_id}_{seq:02d}.json"
            seq += 1

        frames = traceback.extract_tb(exc_tb) if exc_tb is not None else []
        last_frame = frames[-1] if frames else None
        tb_lines = [sanitize_pii_text(line.rstrip("\r\n")) for line in traceback.format_exception(exc_type, exc_value, exc_tb)]

        raw_logs = recent_logs if recent_logs is not None else (self.recent_logs_provider() if self.recent_logs_provider else ())
        ring_lines = [sanitize_pii_text(str(item)) for item in list(raw_logs)[-50:]]

        payload: dict[str, Any] = {
            "schemaVersion": 1,
            "crashId": crash_id,
            "createdAtUtc": utc_iso_ms(now),
            "appId": self.app_name,
            "version": self.app_version,
            "appArchetype": self.app_archetype,
            "runtimeMode": self.runtime_mode,
            "pid": os.getpid(),
            "uptimeSeconds": max(0, int(time.monotonic() - self.started_monotonic)),
            "commandLineSanitized": _sanitize_command_line(argv),
            "exception": {
                "type": f"{exc_type.__module__}.{exc_type.__qualname__}" if getattr(exc_type, "__module__", None) else exc_type.__name__,
                "message": sanitize_pii_text(str(exc_value)),
                "failingModule": sanitize_pii_text(last_frame.name) if last_frame else None,
                "failingFile": sanitize_pii_text(last_frame.filename) if last_frame else None,
                "failingLine": int(last_frame.lineno) if last_frame and last_frame.lineno is not None else None,
                "stackTrace": tb_lines,
            },
            "precedingLogRingBuffer": ring_lines,
        }

        tmp = crash_path.with_name(f"{crash_path.name}.{os.getpid()}.tmp")
        try:
            with tmp.open("w", encoding="utf-8", newline="\n") as fh:
                fh.write(json.dumps(payload, indent=2) + "\n")
                fh.flush()
                os.fsync(fh.fileno())
            os.replace(tmp, crash_path)
        finally:
            tmp.unlink(missing_ok=True)
        self.prune_old_bundles()
        return crash_path

    def prune_old_bundles(self) -> int:
        if not self.crashes_dir.exists():
            return 0
        bundles = sorted(
            self.crashes_dir.glob("crash_*.json"),
            key=lambda p: (p.stat().st_mtime, p.name),
            reverse=True,
        )
        removed = 0
        for stale in bundles[self.max_bundles :]:
            with suppress(OSError):
                stale.unlink(missing_ok=True)
                removed += 1
        return removed

    def install_global_hooks(self) -> None:
        orig_excepthook = sys.excepthook

        def _handle_sys(exc_type: type[BaseException], exc_value: BaseException, exc_tb: TracebackType | None) -> None:
            with suppress(Exception):
                self.write_crash_report(exc_type, exc_value, exc_tb, "sys.excepthook")
            orig_excepthook(exc_type, exc_value, exc_tb)

        def _handle_thread(args: threading.ExceptHookArgs) -> None:
            if args.exc_type and args.exc_value:
                with suppress(Exception):
                    self.write_crash_report(
                        args.exc_type,
                        args.exc_value,
                        args.exc_traceback,
                        f"thread:{getattr(args.thread, 'name', 'worker')}",
                    )

        sys.excepthook = _handle_sys
        threading.excepthook = _handle_thread
