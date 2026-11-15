# @module: Foundation.Tools.verify_sandbox_universal
# @purpose: Shared Standard 02 §6.4 Sandbox health & foreground-visible-by-default browser verification utility across sister apps.
# @depends-on: argparse, json, os, pathlib, shutil, subprocess, sys, time, urllib.request
# @exports: is_effective_headless, resolve_browser_executable, build_browser_launch_command, verify_rest_endpoints, main

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.request
from pathlib import Path
from typing import Any


def is_effective_headless(cli_headless: bool = False) -> bool:
    """Returns True only when --headless or HEADLESS=1/true/yes/on is explicitly set (Standard 02 visible-by-default)."""
    if cli_headless:
        return True
    raw = os.environ.get("HEADLESS", "").strip().lower()
    return raw in {"1", "true", "yes", "on"}


def resolve_browser_executable(preferred: str | None = None) -> str | None:
    """Locates Edge or Chrome desktop executable for foreground CDP verification."""
    if preferred:
        candidate = Path(preferred)
        if candidate.is_file():
            return str(candidate)
        found = shutil.which(preferred)
        if found:
            return found

    for name in ("msedge", "chrome", "google-chrome", "chromium"):
        found = shutil.which(name)
        if found:
            return found

    local_app_data = os.environ.get("LOCALAPPDATA", "")
    win_candidates = [
        Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
        Path(r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
        Path(r"C:\Program Files\Google\Chrome\Application\chrome.exe"),
        Path(r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"),
        Path(local_app_data) / "Microsoft" / "Edge" / "Application" / "msedge.exe" if local_app_data else None,
        Path(local_app_data) / "Google" / "Chrome" / "Application" / "chrome.exe" if local_app_data else None,
    ]
    for path in win_candidates:
        if path is not None and path.is_file():
            return str(path)
    return None


def build_browser_launch_command(
    browser_exe: str,
    url: str,
    user_data_dir: str | Path,
    headless: bool = False,
    width: int = 1920,
    height: int = 1080,
    remote_debugging_port: int = 0,
) -> list[str]:
    """Builds Edge/Chrome launch args: visible foreground --new-window by default, --headless=new only if headless=True."""
    cmd = [
        browser_exe,
        f"--user-data-dir={Path(user_data_dir).resolve()}",
        f"--window-size={width},{height}",
        f"--remote-debugging-port={remote_debugging_port}",
        "--new-window",
        "--no-first-run",
        "--no-default-browser-check",
    ]
    if headless:
        cmd.extend(["--headless=new", "--disable-gpu"])
    cmd.append(url)
    return cmd


def verify_rest_endpoints(base_url: str, endpoints: list[str], timeout_sec: float = 10.0) -> dict[str, Any]:
    """Probes required REST endpoints against a live Sandbox server and returns status summary."""
    results: dict[str, Any] = {}
    all_passed = True
    root = base_url.rstrip("/")

    for ep in endpoints:
        url = f"{root}{ep}"
        try:
            req = urllib.request.Request(url, headers={"Accept": "application/json"})
            with urllib.request.urlopen(req, timeout=timeout_sec) as resp:
                raw = resp.read().decode("utf-8", errors="replace")
                results[ep] = {
                    "status": resp.status,
                    "ok": 200 <= resp.status < 300,
                    "bytes": len(raw),
                }
                if not (200 <= resp.status < 300):
                    all_passed = False
        except Exception as exc:
            results[ep] = {"status": 0, "ok": False, "error": str(exc)}
            all_passed = False

    return {"baseUrl": root, "allPassed": all_passed, "endpoints": results}


def run_browser_smoke(
    url: str,
    headless: bool,
    browser_exe: str | None = None,
    profile_dir: Path | None = None,
    wait_seconds: float = 1.0,
) -> dict[str, Any]:
    resolved_exe = resolve_browser_executable(browser_exe)
    if not resolved_exe:
        return {"ok": False, "headless": headless, "error": "No Edge/Chrome executable found."}

    temp_ctx = tempfile.TemporaryDirectory(prefix="fn-browser-") if profile_dir is None else None
    try:
        effective_profile = profile_dir.resolve() if profile_dir is not None else Path(temp_ctx.name)
        effective_profile.mkdir(parents=True, exist_ok=True)
        cmd = build_browser_launch_command(resolved_exe, url, effective_profile, headless=headless)
        proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            time.sleep(max(0.2, wait_seconds))
            return {"ok": True, "headless": headless, "browserExe": resolved_exe, "command": cmd}
        finally:
            proc.terminate()
            try:
                proc.wait(timeout=3.0)
            except subprocess.TimeoutExpired:
                proc.kill()
    finally:
        if temp_ctx is not None:
            temp_ctx.cleanup()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Universal Sandbox 4-Gate Health & Foreground Browser Verifier")
    parser.add_argument("--base-url", default="http://127.0.0.1:5179", help="Sandbox server base URL")
    parser.add_argument("--endpoint", action="append", dest="endpoints", help="Endpoint path to verify")
    parser.add_argument("--output-json", type=Path, default=None, help="Optional JSON report path")
    parser.add_argument("--headless", action="store_true", help="Use --headless=new instead of visible foreground window")
    parser.add_argument("--visible-test", action="store_true", help="Explicit visible foreground browser verification")
    parser.add_argument("--launch-browser", action="store_true", help="Open browser against --base-url after endpoint probe")
    parser.add_argument("--browser-exe", default=None, help="Explicit msedge/chrome executable path")
    parser.add_argument("--browser-profile-dir", type=Path, default=None, help="Isolated browser user-data-dir")
    parser.add_argument("--wait-seconds", type=float, default=1.0, help="Seconds to hold browser open during smoke verify")
    args = parser.parse_args(argv)

    endpoints = args.endpoints or ["/api/v1/health", "/api/v1/system/dependencies", "/suite.manifest.json"]
    summary = verify_rest_endpoints(args.base_url, endpoints)
    headless = is_effective_headless(cli_headless=bool(args.headless) and not bool(args.visible_test))
    summary["effectiveVisibleBrowser"] = not headless

    if args.launch_browser:
        browser_res = run_browser_smoke(
            args.base_url,
            headless=headless,
            browser_exe=args.browser_exe,
            profile_dir=args.browser_profile_dir,
            wait_seconds=args.wait_seconds,
        )
        summary["browserSmoke"] = browser_res
        if not browser_res.get("ok"):
            summary["allPassed"] = False

    if args.output_json:
        args.output_json.parent.mkdir(parents=True, exist_ok=True)
        args.output_json.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")

    print(json.dumps(summary, indent=2))
    return 0 if summary["allPassed"] else 1


if __name__ == "__main__":
    sys.exit(main())
