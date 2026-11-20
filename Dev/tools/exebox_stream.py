# @module: Foundation.Tools.exebox_stream
# @purpose: Shared Python desktop preflight and exebox-stream UDP 5004 live stream launcher/stopper for Stage 2 E2E suites.
# @depends-on: ctypes, os, shutil, subprocess, sys, pathlib
# @exports: get_thread_desktop_name, is_vlc_viewer_active, start_exebox_live_stream, stop_exebox_live_stream

from __future__ import annotations

import ctypes
import os
import shutil
import subprocess
import sys
from pathlib import Path


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


def is_vlc_viewer_active(udp_port: int = 5004) -> bool:
    if os.name != "nt":
        return False
    try:
        out = subprocess.check_output(["netstat", "-ano", "-p", "udp"], text=True, timeout=5)
        if f":{udp_port}" in out:
            return True
    except (OSError, subprocess.SubprocessError):
        pass
    try:
        out = subprocess.check_output(["tasklist", "/FI", "IMAGENAME eq vlc.exe", "/NH"], text=True, timeout=5)
        return "vlc.exe" in out.lower()
    except (OSError, subprocess.SubprocessError):
        return False


def _exebox_stream_script() -> Path:
    return Path.home() / ".gemini" / "config" / "skills" / "exebox-stream" / "scripts" / "start_exebox_vlc_stream.ps1"


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
) -> subprocess.Popen[str] | None:
    if os.name != "nt" or not shutil.which("ffmpeg"):
        return None
    script = _exebox_stream_script()
    if not script.is_file():
        return None

    desk = get_thread_desktop_name()
    is_exebox = "\\exebox-" in desk.lower()
    active = is_vlc_viewer_active(udp_port)
    print(f"[exebox-stream] Preflight: ThreadDesktop='{desk}' | ViewerActive(UDP:{udp_port})={active}", file=sys.stderr)
    if not active:
        lnk = Path(os.environ.get("APPDATA", "")) / "Microsoft" / "Windows" / "Start Menu" / "Programs" / "Test Viewer.lnk"
        if not is_exebox and lnk.is_file():
            os.startfile(str(lnk))  # type: ignore[attr-defined]
        elif is_exebox:
            print(f"[exebox-stream] Isolated exebox desktop; click 'Test Viewer' in Start Menu (udp://@127.0.0.1:{udp_port}) to watch live.", file=sys.stderr)

    cmd = [
        "powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script),
        "-UdpHost", "127.0.0.1", "-UdpPort", str(udp_port), "-Fps", str(fps),
    ]
    if automation_port > 0:
        cmd.extend(["-AutomationPort", str(automation_port)])
    if automation_token:
        cmd.extend(["-AutomationToken", automation_token])
    if target_pid > 0:
        cmd.extend(["-TargetPid", str(target_pid)])
    if window_title:
        cmd.extend(["-WindowTitle", window_title])
    if snapshot_dir:
        cmd.extend(["-SnapshotDir", str(snapshot_dir)])
    if log_path:
        cmd.extend(["-LogPath", str(log_path)])

    creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
    return subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, text=True, creationflags=creationflags)


def stop_exebox_live_stream(proc: subprocess.Popen[str] | None) -> None:
    if proc is None:
        return
    try:
        if proc.poll() is None:
            if os.name == "nt":
                subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=3, check=False)
            else:
                proc.terminate()
                proc.wait(timeout=2)
    except (OSError, subprocess.SubprocessError):
        pass
