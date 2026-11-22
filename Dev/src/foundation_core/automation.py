# @module: foundation_core.automation
# @purpose: Pure-stdlib opt-in 127.0.0.1 HTTP/1.1 JSON automation loopback server mirroring Foundation.Automation contracts (Host/Origin 403, Bearer 401, ExitCode=2 fail-closed gate, /v1/health, /v1/state, /v1/snapshot, /v1/snapshot.png, /v1/shutdown).
# @depends-on: dataclasses, hashlib, hmac, http.server, json, os, pathlib, re, secrets, struct, threading, typing, urllib.parse, zlib, .cli, .locks, .sandbox_seam
# @exports: AUTOMATION_DISABLED_EXIT_CODE, AutomationDisabledError, AutomationServerOptions, encode_rgba_png, parse_automation_cli_args, AutomationLoopbackServer

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import hmac
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import re
import secrets
import struct
import threading
from typing import Any, Callable, Mapping, Sequence
from urllib.parse import urlparse
import zlib

from .cli import assert_path_contained
from .locks import utc_iso_ms
from .sandbox_seam import SandboxResolution, is_path_within_sandbox, resolve_sandbox_environment

AUTOMATION_DISABLED_EXIT_CODE: int = 2
_TRUTHY: frozenset[str] = frozenset({"1", "true", "yes", "on"})
_PNG_SIG: bytes = b"\x89PNG\r\n\x1a\n"
_LOOPBACK_HOSTS: frozenset[str] = frozenset({"127.0.0.1", "localhost", "::1", "[::1]"})


class AutomationDisabledError(RuntimeError):
    """Fail-closed exception (exit_code=2) raised when --automation-port is passed without <APP>_TEST_AUTOMATION=1."""

    def __init__(self, message: str, exit_code: int = AUTOMATION_DISABLED_EXIT_CODE) -> None:
        super().__init__(message)
        self.exit_code = exit_code


@dataclass(frozen=True, slots=True)
class AutomationServerOptions:
    automation_port: int
    bearer_token: str
    app_id: str = "app"
    allowed_snapshot_root: Path | None = None


def _png_chunk(chunk_type: bytes, data: bytes) -> bytes:
    crc = zlib.crc32(chunk_type + data) & 0xFFFFFFFF
    return struct.pack(">I", len(data)) + chunk_type + data + struct.pack(">I", crc)


def encode_rgba_png(width: int, height: int, rgba_bytes: bytes | bytearray | memoryview) -> bytes:
    """Encodes a raw top-down RGBA8888 buffer into a valid PNG byte stream using Python stdlib zlib."""
    if width <= 0 or height <= 0:
        raise ValueError(f"Invalid PNG dimensions {width}x{height}.")
    raw_bytes = bytes(rgba_bytes)
    stride = width * 4
    if len(raw_bytes) != stride * height:
        raise ValueError(f"Expected {stride * height} RGBA bytes for {width}x{height}, got {len(raw_bytes)}.")
    scanlines = b"".join(b"\x00" + raw_bytes[y * stride : (y + 1) * stride] for y in range(height))
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return _PNG_SIG + _png_chunk(b"IHDR", ihdr) + _png_chunk(b"IDAT", zlib.compress(scanlines, level=6)) + _png_chunk(b"IEND", b"")


def _parse_png_dimensions(png_bytes: bytes) -> tuple[int, int]:
    if len(png_bytes) < 24 or not png_bytes.startswith(_PNG_SIG):
        raise ValueError("Snapshot bytes are not a valid PNG stream.")
    width, height = struct.unpack(">II", png_bytes[16:24])
    return int(width), int(height)


def parse_automation_cli_args(
    app_id: str,
    argv: Sequence[str] | None = None,
    *,
    env: Mapping[str, str | None] | None = None,
    allow_automation: bool | None = None,
    allowed_snapshot_root: str | Path | None = None,
) -> AutomationServerOptions | None:
    """Parses --automation-port / --automation-token flags and enforces ExitCode=2 fail-closed release gating."""
    args = list(argv or ())
    port: int | None = None
    token: str | None = None
    i = 0
    while i < len(args):
        arg = str(args[i]).strip()
        if arg.lower() == "--automation-port" and i + 1 < len(args):
            port = int(str(args[i + 1]).strip())
            i += 2
        elif arg.lower() == "--automation-token" and i + 1 < len(args):
            token = str(args[i + 1]).strip()
            i += 2
        else:
            i += 1
    if port is None:
        return None
    if port < 0 or port > 65535:
        raise ValueError(f"Automation port must be between 0 and 65535, got {port}.")

    env_map: Mapping[str, str | None] = os.environ if env is None else env
    prefix = re.sub(r"[^A-Z0-9]+", "_", (app_id or "APP").strip().upper()).strip("_") or "APP"
    gate_val = next((str(v).strip().lower() for k, v in env_map.items() if k.upper() in (f"{prefix}_TEST_AUTOMATION", "FOUNDATION_TEST_AUTOMATION") and v), "")
    is_enabled = allow_automation if allow_automation is not None else (gate_val in _TRUTHY)
    if not is_enabled:
        raise AutomationDisabledError(f"--automation-port is disabled unless {prefix}_TEST_AUTOMATION=1 or explicit test build authorization is active.")

    resolved_tok = token or next((str(v).strip() for k, v in env_map.items() if k.upper() == "FOUNDATION_AUTOMATION_TOKEN" and v), "") or secrets.token_hex(16)
    snap_root = Path(allowed_snapshot_root).resolve() if allowed_snapshot_root is not None else None
    return AutomationServerOptions(automation_port=port, bearer_token=resolved_tok, app_id=app_id.strip() or "app", allowed_snapshot_root=snap_root)


class AutomationLoopbackServer:
    """Opt-in 127.0.0.1 HTTP/1.1 automation loopback server enforcing Standard 02/18/25/41 security invariants."""

    def __init__(
        self,
        options: AutomationServerOptions,
        *,
        state_provider: Callable[[], dict[str, Any]] | None = None,
        snapshot_provider: Callable[[str | None], bytes | tuple[int, int, bytes]] | None = None,
        shutdown_callback: Callable[[], None] | None = None,
        sandbox: SandboxResolution | None = None,
    ) -> None:
        if not options.bearer_token.strip():
            raise ValueError("bearer_token must be non-empty.")
        self.options = options
        self._expected_hash = hashlib.sha256(options.bearer_token.strip().encode("utf-8")).digest()
        self._state_provider = state_provider
        self._snapshot_provider = snapshot_provider
        self._shutdown_callback = shutdown_callback
        self._sandbox = sandbox if sandbox is not None else resolve_sandbox_environment()
        self._httpd: ThreadingHTTPServer | None = None
        self._thread: threading.Thread | None = None

    @property
    def automation_port(self) -> int:
        return int(self._httpd.server_address[1]) if self._httpd is not None else int(self.options.automation_port)

    @property
    def bearer_token(self) -> str:
        return self.options.bearer_token

    @property
    def is_listening(self) -> bool:
        return self._httpd is not None

    def start(self) -> int:
        if self._httpd is not None:
            return self.automation_port
        outer = self

        class _Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"
            def log_message(self, format: str, *args: Any) -> None: return
            def do_GET(self) -> None: outer._dispatch(self, "GET")
            def do_POST(self) -> None: outer._dispatch(self, "POST")

        class _Server(ThreadingHTTPServer):
            allow_reuse_address = True
            daemon_threads = True

        self._httpd = _Server(("127.0.0.1", int(self.options.automation_port)), _Handler)
        self._thread = threading.Thread(target=self._httpd.serve_forever, name=f"foundation-auto-{self.options.app_id}", daemon=True)
        self._thread.start()
        return self.automation_port

    def stop(self) -> None:
        if (srv := self._httpd) is not None:
            self._httpd = None
            srv.shutdown()
            srv.server_close()
        if (thr := self._thread) is not None:
            self._thread = None
            thr.join(timeout=2.0)

    def __enter__(self) -> AutomationLoopbackServer:
        self.start()
        return self

    def __exit__(self, exc_type: object, exc_val: object, exc_tb: object) -> None:
        self.stop()

    def _capture_png_bytes(self, target_id: str | None) -> tuple[bytes, int, int]:
        if self._snapshot_provider is None:
            raise RuntimeError("No snapshot_provider registered for /v1/snapshot.")
        captured = self._snapshot_provider(target_id)
        if isinstance(captured, (bytes, bytearray, memoryview)):
            png_bytes = bytes(captured)
            return png_bytes, *_parse_png_dimensions(png_bytes)
        w, h, rgba = captured
        return encode_rgba_png(int(w), int(h), rgba), int(w), int(h)

    def _dispatch(self, h: BaseHTTPRequestHandler, method: str) -> None:
        host_raw = (h.headers.get("Host") or "").strip()
        if host_raw.startswith("[") and "]" in host_raw:
            host_only = host_raw[1:].split("]", 1)[0]
        else:
            host_only = host_raw.rsplit(":", 1)[0]
        if not host_only or host_only.lower() not in _LOOPBACK_HOSTS:
            return self._send_json(h, 403, {"isPassed": False, "status": "failed", "errorMessage": "Forbidden: Host must be 127.0.0.1 or localhost.", "data": None})
        if (origin := (h.headers.get("Origin") or "").strip()):
            parsed_o = urlparse(origin)
            if not parsed_o.hostname or parsed_o.hostname.lower() not in {"127.0.0.1", "localhost", "::1"}:
                return self._send_json(h, 403, {"isPassed": False, "status": "failed", "errorMessage": "Forbidden: external Origin header rejected.", "data": None})
        auth = (h.headers.get("Authorization") or "").strip()
        if not auth.lower().startswith("bearer "):
            return self._send_json(h, 401, {"isPassed": False, "status": "failed", "errorMessage": "Unauthorized: invalid or missing bearer token.", "data": None})
        if not hmac.compare_digest(hashlib.sha256(auth[7:].strip().encode("utf-8")).digest(), self._expected_hash):
            return self._send_json(h, 401, {"isPassed": False, "status": "failed", "errorMessage": "Unauthorized: invalid or missing bearer token.", "data": None})

        route = h.path.split("?", 1)[0]
        try:
            if method == "GET" and route == "/v1/health":
                return self._send_json(h, 200, {"isPassed": True, "status": "completed", "errorMessage": None, "data": {"appId": self.options.app_id, "automationPort": self.automation_port, "status": "completed", "isPassed": True, "generatedAtUtc": utc_iso_ms()}})
            if method == "GET" and route == "/v1/state":
                return self._send_json(h, 200, {"isPassed": True, "status": "completed", "errorMessage": None, "data": dict(self._state_provider() if self._state_provider else {})})
            if method == "GET" and route == "/v1/snapshot.png":
                png_bytes, _, _ = self._capture_png_bytes(None)
                return self._send_bytes(h, 200, png_bytes, "image/png", {"Cache-Control": "no-store, no-cache, must-revalidate"})
            if method == "POST" and route == "/v1/snapshot":
                cl_raw = int(h.headers.get("Content-Length") or 0)
                if cl_raw < 0 or cl_raw > 1_048_576:
                    return self._send_json(h, 400, {"isPassed": False, "status": "failed", "errorMessage": "Invalid or oversized Content-Length.", "data": None})
                req = json.loads((h.rfile.read(cl_raw).decode("utf-8") or "{}"))
                fmt = str(req.get("snapshotFormat") or "png").strip().lower()
                if fmt != "png":
                    return self._send_json(h, 400, {"isPassed": False, "status": "failed", "errorMessage": f"Unsupported snapshotFormat '{fmt}'. Only 'png' is supported.", "data": None})
                raw_path = str(req.get("filePath") or "").strip()
                if not raw_path:
                    return self._send_json(h, 400, {"isPassed": False, "status": "failed", "errorMessage": "filePath is required.", "data": None})
                try:
                    dest = assert_path_contained(raw_path, self.options.allowed_snapshot_root) if self.options.allowed_snapshot_root is not None else Path(os.path.abspath(raw_path))
                    if self._sandbox.is_sandbox_active and not is_path_within_sandbox(dest, self._sandbox):
                        raise PermissionError(f"Snapshot destination '{dest}' escapes active sandbox.")
                except (ValueError, PermissionError) as ex:
                    return self._send_json(h, 403, {"isPassed": False, "status": "failed", "errorMessage": str(ex), "data": None})
                png_bytes, width, height = self._capture_png_bytes(req.get("targetId"))
                dest.parent.mkdir(parents=True, exist_ok=True)
                tmp = dest.with_name(f"{dest.name}.{os.getpid()}.tmp")
                tmp.write_bytes(png_bytes)
                os.replace(tmp, dest)
                return self._send_json(h, 200, {"isPassed": True, "status": "completed", "errorMessage": None, "data": {"filePath": str(dest), "width": width, "height": height, "fileSizeBytes": len(png_bytes), "snapshotFormat": "png"}})
            if method == "POST" and route == "/v1/shutdown":
                self._send_json(h, 200, {"isPassed": True, "status": "completed", "errorMessage": None, "data": {"isShuttingDown": True, "appId": self.options.app_id, "generatedAtUtc": utc_iso_ms()}})
                cb = self._shutdown_callback
                threading.Timer(0.04, lambda: (cb() if cb else None, self.stop())).start()
                return
            self._send_json(h, 404, {"isPassed": False, "status": "failed", "errorMessage": f"Route not found: {method} {route}", "data": None})
        except Exception as ex:
            self._send_json(h, 400, {"isPassed": False, "status": "failed", "errorMessage": str(ex), "data": None})

    def _send_json(self, h: BaseHTTPRequestHandler, code: int, obj: Any) -> None:
        self._send_bytes(h, code, json.dumps(obj, separators=(",", ":")).encode("utf-8"), "application/json; charset=utf-8")

    def _send_bytes(self, h: BaseHTTPRequestHandler, code: int, body: bytes, ctype: str, extra_headers: Mapping[str, str] | None = None) -> None:
        h.send_response(code)
        h.send_header("Content-Type", ctype)
        h.send_header("Content-Length", str(len(body)))
        h.send_header("Connection", "close")
        for k, v in (extra_headers or {}).items():
            h.send_header(k, v)
        h.end_headers()
        h.wfile.write(body)
