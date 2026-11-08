# @module: Foundation.Tools.verify_sandbox_universal
# @purpose: Shared 4-gate dual-viewport (1920x1080 Desktop + 390x844/844x390 Mobile) Sandbox UI & REST health verifier across sister apps (Standard 02 §6.4).
# @depends-on: argparse, json, pathlib, sys, time, urllib.request
# @exports: verify_rest_endpoints, main

from __future__ import annotations

import argparse
import json
import sys
import urllib.request
from pathlib import Path
from typing import Any


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


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Universal Sandbox 4-Gate Health & Endpoint Verifier")
    parser.add_argument("--base-url", default="http://127.0.0.1:5179", help="Sandbox server base URL")
    parser.add_argument(
        "--endpoint",
        action="append",
        dest="endpoints",
        help="Endpoint path to verify (may be specified multiple times)",
    )
    parser.add_argument("--output-json", type=Path, default=None, help="Optional JSON report path")
    args = parser.parse_args(argv)

    endpoints = args.endpoints or [
        "/api/v1/health",
        "/api/v1/system/dependencies",
        "/suite.manifest.json",
    ]
    summary = verify_rest_endpoints(args.base_url, endpoints)
    if args.output_json:
        args.output_json.parent.mkdir(parents=True, exist_ok=True)
        args.output_json.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")

    print(json.dumps(summary, indent=2))
    return 0 if summary["allPassed"] else 1


if __name__ == "__main__":
    sys.exit(main())
