# @module: Foundation.Tools.exebox_stream
# @purpose: Shared Python desktop preflight and Always-On Web Test Viewer Hub (http://127.0.0.1:5005) Virtual Desktop Compositor launcher/controller (+ backward-compatible legacy shims) for Stage 2 E2E suites.
# @depends-on: ctypes, dataclasses, json, os, pathlib, subprocess, sys, tempfile, time, typing, urllib.error, urllib.request, uuid
# @exports: DEFAULT_HUB_URL, ExeboxStreamHandle, get_thread_desktop_name, get_viewer_hub_health, is_viewer_active, is_vlc_viewer_active, start_exebox_desktop_stream, attach_exebox_automation_window, detach_exebox_automation_window, send_exebox_stream_event, stop_exebox_desktop_stream, start_exebox_live_stream, stop_exebox_live_stream

from __future__ import annotations

import ctypes
from dataclasses import dataclass
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
from typing import Any
import urllib.error
import urllib.request
import uuid

DEFAULT_HUB_URL: str = "http://127.0.0.1:5005"


@dataclass(slots=True)
class ExeboxStreamHandle:
    process: subprocess.Popen[str]
    control_port: int
    control_port_file: Path
    hub_url: str
    session_id: str
    project: str
    log_path: Path


def get_thread_desktop_name() -> str:
    if os.name != "nt":
        return "non-windows"
    k32, u32 = ctypes.windll.kernel32, ctypes.windll.user32
    ws_buf = ctypes.create_unicode_buffer(256)
    dk_buf = ctypes.create_unicode_buffer(256)
    needed = ctypes.c_ulong(0)
    u32.GetUserObjectInformationW(u32.GetProcessWindowStation(), 2, ws_buf, ctypes.sizeof(ws_buf), ctypes.byref(needed))
    u32.GetUserObjectInformationW(u32.GetThreadDesktop(k32.GetCurrentThreadId()), 2, dk_buf, ctypes.sizeof(dk_buf), ctypes.byref(needed))
    return f"{ws_buf.value or 'UnknownWinSta'}\\{dk_buf.value or 'UnknownDesktop'}"


def _capture_exe_path() -> Path:
    return Path.home() / ".gemini" / "config" / "skills" / "exebox-stream" / "bin" / "DesktopCapture" / "ExeboxDesktopCapture.exe"


def _post_json(url: str, payload: dict[str, Any], timeout_s: float = 2.0) -> bool:
    data = json.dumps(payload, separators=(",", ":")).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=timeout_s) as resp:
            return 200 <= int(resp.status) < 300
    except (OSError, urllib.error.URLError, ValueError):
        return False


def get_viewer_hub_health(hub_url: str = DEFAULT_HUB_URL, timeout_s: float = 1.5) -> dict[str, Any] | None:
    url = f"{hub_url.rstrip('/')}/api/v1/health"
    try:
        with urllib.request.urlopen(url, timeout=timeout_s) as resp:
            if resp.status == 200:
                parsed = json.loads(resp.read().decode("utf-8"))
                if isinstance(parsed, dict) and parsed.get("ok"):
                    return parsed
    except (OSError, urllib.error.URLError, ValueError):
        pass
    return None


def is_viewer_active(hub_url: str = DEFAULT_HUB_URL) -> bool:
    return get_viewer_hub_health(hub_url) is not None


def is_vlc_viewer_active(udp_port: int = 5004, hub_url: str = DEFAULT_HUB_URL) -> bool:
    """Backward-compatible alias checking the persistent Test Viewer Hub."""
    _ = udp_port
    return is_viewer_active(hub_url)


def _preflight_viewer(hub_url: str) -> dict[str, Any]:
    desk = get_thread_desktop_name()
    is_exebox = "\\exebox-" in desk.lower()
    health = get_viewer_hub_health(hub_url)
    hub_active = health is not None
    viewers = int(health.get("viewerCount", 0)) if health else 0
    print(f"[exebox-stream] Preflight: ThreadDesktop='{desk}' | HubActive({hub_url})={hub_active} | Viewers={viewers}", file=sys.stderr)
    if not hub_active or viewers <= 0:
        lnk = Path(os.environ.get("APPDATA", "")) / "Microsoft" / "Windows" / "Start Menu" / "Programs" / "Test Viewer.lnk"
        if not is_exebox and os.name == "nt" and lnk.is_file():
            os.startfile(str(lnk))  # type: ignore[attr-defined]
            time.sleep(0.6)
        elif is_exebox and viewers <= 0:
            print(f"[exebox-stream] Isolated exebox desktop; click 'Test Viewer' in Start Menu ({hub_url}) to watch live.", file=sys.stderr)
    return {"desktopName": desk, "isExebox": is_exebox, "hubActive": hub_active, "viewerCount": viewers}


def start_exebox_desktop_stream(
    *,
    project: str = "Sandbox E2E",
    hub_url: str = DEFAULT_HUB_URL,
    fps: int = 20,
    width: int = 1280,
    height: int = 720,
    automation_port: int = 0,
    automation_token: str = "",
    target_pid: int = 0,
    window_title: str = "",
    snapshot_dir: str | Path | None = None,
    log_path: str | Path | None = None,
    stop_on_target_exit: bool = False,
    compat_ffmpeg_marker: bool = False,
) -> ExeboxStreamHandle | None:
    if os.name != "nt":
        return None
    cap_exe = _capture_exe_path()
    if not cap_exe.is_file():
        return None

    clean_hub = hub_url.rstrip("/")
    _preflight_viewer(clean_hub)
    session_id = f"s2-{uuid.uuid4().hex[:8]}"
    ctrl_file = Path(tempfile.gettempdir()) / f"exebox_ctrl_{session_id}.txt"
    resolved_log = Path(log_path) if log_path else (Path(tempfile.gettempdir()) / "exebox_stream_5005.log")
    clamped_fps = max(5, min(30, int(fps)))

    cmd = [
        str(cap_exe),
        "--hub", clean_hub,
        "--project", project or "Sandbox E2E",
        "--session-id", session_id,
        "--fps", str(clamped_fps),
        "--width", str(width),
        "--height", str(height),
        "--control-port", "0",
        "--control-port-file", str(ctrl_file),
        "--log-path", str(resolved_log),
    ]
    if automation_port > 0:
        cmd.extend(["--automation-port", str(automation_port)])
    if automation_token:
        cmd.extend(["--automation-token", automation_token])
    if target_pid > 0:
        cmd.extend(["--target-pid", str(target_pid)])
    if window_title:
        cmd.extend(["--window-title", window_title])
    if snapshot_dir:
        cmd.extend(["--snapshot-dir", str(snapshot_dir)])
    if stop_on_target_exit:
        cmd.append("--stop-on-target-exit")
    if compat_ffmpeg_marker:
        cmd.append("--compat-ffmpeg-marker")

    creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
    proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, text=True, creationflags=creationflags)

    ctrl_port = 0
    for _ in range(40):
        if proc.poll() is not None:
            break
        if ctrl_file.is_file():
            try:
                raw = ctrl_file.read_text(encoding="utf-8").strip()
                if raw.isdigit() and int(raw) > 0:
                    ctrl_port = int(raw)
                    break
            except OSError:
                pass
        time.sleep(0.05)

    return ExeboxStreamHandle(
        process=proc,
        control_port=ctrl_port,
        control_port_file=ctrl_file,
        hub_url=clean_hub,
        session_id=session_id,
        project=project or "Sandbox E2E",
        log_path=resolved_log,
    )


def attach_exebox_automation_window(
    handle: ExeboxStreamHandle | None,
    *,
    automation_port: int,
    automation_token: str = "",
    target_pid: int = 0,
    label: str = "",
    snapshot_dir: str | Path | None = None,
) -> bool:
    if handle is None or handle.control_port <= 0 or automation_port <= 0:
        return False
    payload: dict[str, Any] = {
        "automationPort": int(automation_port),
        "automationToken": automation_token,
        "targetPid": int(target_pid),
        "label": label,
    }
    if snapshot_dir:
        payload["snapshotDir"] = str(snapshot_dir)
    return _post_json(f"http://127.0.0.1:{handle.control_port}/attach", payload)


def detach_exebox_automation_window(
    handle: ExeboxStreamHandle | None,
    *,
    automation_port: int = 0,
    target_pid: int = 0,
) -> bool:
    if handle is None or handle.control_port <= 0:
        return False
    return _post_json(
        f"http://127.0.0.1:{handle.control_port}/detach",
        {"automationPort": int(automation_port), "targetPid": int(target_pid)},
    )


def send_exebox_stream_event(
    handle: ExeboxStreamHandle | None = None,
    *,
    hub_url: str = DEFAULT_HUB_URL,
    project: str = "",
    event_type: str = "step",
    step_id: str = "",
    step_title: str = "",
    status: str = "info",
    message: str = "",
    input_kind: str | None = None,
    norm_x: float | None = None,
    norm_y: float | None = None,
    input_label: str | None = None,
) -> bool:
    resolved_hub = handle.hub_url if handle is not None else hub_url.rstrip("/")
    payload: dict[str, Any] = {
        "type": event_type,
        "sessionId": handle.session_id if handle is not None else "",
        "project": project or (handle.project if handle is not None else "Sandbox E2E"),
        "stepId": step_id,
        "stepTitle": step_title,
        "status": status,
        "message": message,
    }
    if input_kind:
        payload["inputKind"] = input_kind
        payload["normX"] = 0.5 if norm_x is None else float(norm_x)
        payload["normY"] = 0.5 if norm_y is None else float(norm_y)
        payload["inputLabel"] = input_label or message
    ok = _post_json(f"{resolved_hub}/api/v1/event", payload)
    if handle is not None and handle.control_port > 0 and event_type != "input" and message:
        note = f"[{step_id}] {message}" if step_id else message
        _post_json(f"http://127.0.0.1:{handle.control_port}/status", {"message": note}, timeout_s=1.0)
    return ok


def stop_exebox_desktop_stream(handle: ExeboxStreamHandle | subprocess.Popen[str] | None) -> None:
    if handle is None:
        return
    proc = handle.process if isinstance(handle, ExeboxStreamHandle) else handle
    try:
        if proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=2.0)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait(timeout=2.0)
    except (OSError, subprocess.SubprocessError):
        pass
    if isinstance(handle, ExeboxStreamHandle):
        try:
            handle.control_port_file.unlink(missing_ok=True)
        except OSError:
            pass


def start_exebox_live_stream(
    *,
    automation_port: int = 0,
    automation_token: str = "",
    target_pid: int = 0,
    window_title: str = "",
    snapshot_dir: str | Path | None = None,
    udp_port: int = 5004,
    fps: int = 20,
    log_path: str | Path | None = None,
    hub_url: str = DEFAULT_HUB_URL,
    project: str = "",
) -> subprocess.Popen[str] | None:
    """Backward-compatible wrapper returning the compositor Popen handle targeting Test Viewer Hub."""
    _ = udp_port
    handle = start_exebox_desktop_stream(
        project=project or window_title or "Sandbox E2E",
        hub_url=hub_url,
        fps=fps,
        automation_port=automation_port,
        automation_token=automation_token,
        target_pid=target_pid,
        window_title=window_title,
        snapshot_dir=snapshot_dir,
        log_path=log_path,
        stop_on_target_exit=target_pid > 0,
        compat_ffmpeg_marker=True,
    )
    return handle.process if handle is not None else None


def stop_exebox_live_stream(proc: ExeboxStreamHandle | subprocess.Popen[str] | None) -> None:
    stop_exebox_desktop_stream(proc)
