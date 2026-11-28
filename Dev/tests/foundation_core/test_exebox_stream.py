# @module: tests.foundation_core.test_exebox_stream
# @purpose: Isolated unit tests for Foundation.Tools.exebox_stream Web Test Viewer Hub health, event posting, and legacy shim contracts.
# @depends-on: http.server, json, pathlib, sys, threading, unittest, exebox_stream
# @exports: TestExeboxStreamHelpers

from __future__ import annotations

from http.server import BaseHTTPRequestHandler, HTTPServer
import json
from pathlib import Path
import sys
import threading
from typing import Any
import unittest

TOOLS_DIR = Path(__file__).resolve().parents[2] / "tools"
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import exebox_stream  # noqa: E402


class TestExeboxStreamHelpers(unittest.TestCase):
    def test_hub_health_and_event_post_roundtrip(self) -> None:
        received: list[dict[str, Any]] = []

        class _MockHubHandler(BaseHTTPRequestHandler):
            def log_message(self, format: str, *args: Any) -> None:
                return

            def do_GET(self) -> None:
                if self.path == "/api/v1/health":
                    body = json.dumps({"ok": True, "viewerCount": 2, "dvrCount": 12}).encode("utf-8")
                    self.send_response(200)
                    self.send_header("Content-Type", "application/json")
                    self.send_header("Content-Length", str(len(body)))
                    self.end_headers()
                    self.wfile.write(body)
                else:
                    self.send_response(404)
                    self.end_headers()

            def do_POST(self) -> None:
                cl = int(self.headers.get("Content-Length") or 0)
                raw = self.rfile.read(cl).decode("utf-8") if cl > 0 else "{}"
                received.append(json.loads(raw))
                body = b'{"ok":true}'
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

        server = HTTPServer(("127.0.0.1", 0), _MockHubHandler)
        port = int(server.server_address[1])
        thr = threading.Thread(target=server.serve_forever, daemon=True)
        thr.start()
        try:
            hub_url = f"http://127.0.0.1:{port}"
            health = exebox_stream.get_viewer_hub_health(hub_url)
            self.assertIsNotNone(health)
            assert health is not None
            self.assertEqual(health["viewerCount"], 2)
            self.assertTrue(exebox_stream.is_viewer_active(hub_url))
            self.assertTrue(exebox_stream.is_vlc_viewer_active(5004, hub_url=hub_url))

            ok = exebox_stream.send_exebox_stream_event(
                hub_url=hub_url,
                project="UnitDemo",
                step_id="S2-01",
                step_title="Install",
                status="running",
                message="Booting wizard",
            )
            self.assertTrue(ok)
            self.assertEqual(len(received), 1)
            self.assertEqual(received[0]["project"], "UnitDemo")
            self.assertEqual(received[0]["stepId"], "S2-01")
        finally:
            server.shutdown()
            server.server_close()
            thr.join(timeout=2.0)

    def test_unreachable_hub_fails_gracefully(self) -> None:
        self.assertIsNone(exebox_stream.get_viewer_hub_health("http://127.0.0.1:1", timeout_s=0.3))
        self.assertFalse(exebox_stream.is_viewer_active("http://127.0.0.1:1"))


if __name__ == "__main__":
    unittest.main()
