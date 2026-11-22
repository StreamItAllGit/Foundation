# @module: foundation_core.tests.test_std19_31_35_43_and_automation
# @purpose: Pure-stdlib unittest suite verifying NeuroPlay v0.5.4 parity across cli.py, GranularSettingsStore (Std 19), SqliteSchemaMigrationRunner (Std 10), PidSentinelLock (Std 31), RollingLogSink/CrashDumper (Std 35/43), and AutomationLoopbackServer (Std 02/18/41).
# @depends-on: hashlib, json, os, pathlib, sqlite3, subprocess, sys, tempfile, time, unittest, urllib.error, urllib.request
# @exports: TestFoundationCoreNeuroPlayParity

from __future__ import annotations

import json
import os
from pathlib import Path
import socket
import sqlite3
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.error
import urllib.request

DEV_ROOT = Path(__file__).resolve().parents[2]
SRC_ROOT = DEV_ROOT / "src"
if str(SRC_ROOT) not in sys.path:
    sys.path.insert(0, str(SRC_ROOT))

import foundation_core  # noqa: E402
from foundation_core import (  # noqa: E402
    AUTOMATION_DISABLED_EXIT_CODE,
    AutomationDisabledError,
    AutomationLoopbackServer,
    AutomationServerOptions,
    CrashDumper,
    GranularSettingsStore,
    PidSentinelLock,
    RollingLogSink,
    SqliteSchemaMigrationRunner,
    build_standard_cli_parser,
    open_wal_sqlite,
    parse_automation_cli_args,
    parse_standard_cli_flags,
    sanitize_pii_text,
    vacuum_backup_sqlite,
    verify_snapshot_integrity,
)


class TestFoundationCoreNeuroPlayParity(unittest.TestCase):
    def test_cli_subcommand_help_passthrough_and_std19_default_config(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp).resolve()
            opts = parse_standard_cli_flags(["train", "--help", "--data-dir", str(root / "data")], default_root=root, app_name="NeuroPlay", app_version="0.1.1")
            self.assertEqual(opts.data_dir, (root / "data").resolve())
            self.assertEqual(opts.config_path, (root / "data" / "config" / "settings.json").resolve())
            self.assertIn("train", opts.extra_args)
            self.assertIn("--help", opts.extra_args)

            parent = build_standard_cli_parser(app_name="NeuroPlay", app_version="0.1.1", default_root=root)
            import argparse
            sub = argparse.ArgumentParser(parents=[parent])
            sub.add_argument("subcmd")
            ns = sub.parse_args(["sync", "--log-dir", str(root / "custom_logs")])
            self.assertEqual(ns.subcmd, "sync")
            self.assertEqual(ns.log_dir, root / "custom_logs")

    def test_granular_settings_store_std19_migration_quarantine_splits_and_extra(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            data_dir = (Path(tmp) / "data").resolve()
            store = GranularSettingsStore(data_dir, max_snapshots=20)
            cfg_path = store.resolve_settings_path("settings.json")
            cfg_path.parent.mkdir(parents=True, exist_ok=True)
            cfg_path.write_text(
                json.dumps({"schemaVersion": 1, "legacyLimit": 5, "futurePluginToggle": True, "nestedUnknown": {"x": 42}}, indent=2),
                encoding="utf-8",
            )

            def _migrate_v1_v2(doc: dict[str, object]) -> dict[str, object]:
                val = doc.pop("legacyLimit", 10)
                doc["behavior"] = {"maxWorkers": val}
                return doc

            res = store.load_or_self_heal(
                "settings.json",
                default_factory=lambda: {"schemaVersion": 2, "behavior": {"maxWorkers": 2}},
                current_schema_version=2,
                migrations={1: _migrate_v1_v2},
                known_keys=("behavior",),
            )
            self.assertTrue(res.loaded_from_primary)
            self.assertEqual(res.migrated_from_version, 1)
            self.assertEqual(res.value["behavior"]["maxWorkers"], 5)
            self.assertEqual(res.extra.get("futurePluginToggle"), True)
            self.assertEqual(res.extra.get("nestedUnknown"), {"x": 42})

            # Re-saving preserves unknown keys at top level without serializing a literal "_extra" wrapper.
            store.save_atomic("settings.json", res.value)
            raw_saved = json.loads(cfg_path.read_text(encoding="utf-8"))
            self.assertNotIn("_extra", raw_saved)
            self.assertTrue(raw_saved["futurePluginToggle"])
            self.assertEqual(raw_saved["nestedUnknown"], {"x": 42})

            # Corrupt primary -> quarantine + .bak heal; then corrupt primary + .bak -> config_history heal
            cfg_path.write_text("{corrupt_json:", encoding="utf-8")
            healed_bak = store.load_or_self_heal("settings.json")
            self.assertTrue(healed_bak.healed_from_backup)
            self.assertIsNotNone(healed_bak.quarantined_path)
            self.assertTrue(healed_bak.quarantined_path.is_file())
            self.assertEqual(healed_bak.quarantined_path.parent, data_dir / "quarantine")

            cfg_path.write_text("{corrupt_again", encoding="utf-8")
            cfg_path.with_name("settings.json.bak").write_text("{corrupt_bak", encoding="utf-8")
            healed_hist = store.load_or_self_heal("settings.json")
            self.assertTrue(healed_hist.healed_from_backup)

            # Domain split + 20-snapshot retention cap
            split_path = store.save_atomic("settings.notifications.json", {"schemaVersion": 2, "isEnabled": True})
            self.assertEqual(split_path, data_dir / "config" / "settings.notifications.json")
            for idx in range(25):
                store.save_atomic("settings.json", {"schemaVersion": 2, "iter": idx})
            self.assertEqual(len(store.list_history_snapshots("settings.json")), 20)

    def test_sqlite_user_version_migrations_rollback_readonly_and_vacuum_into(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp).resolve()
            db_path = root / "db" / "app.db"
            mig_dir = root / "migrations"
            mig_dir.mkdir(parents=True)
            (mig_dir / "0001_init.sql").write_text("CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT NOT NULL);\n", encoding="utf-8")
            (mig_dir / "0002_seed.sql").write_text("INSERT INTO items (id, name) VALUES (1, 'alpha');\n-- trailing comment after semicolon\n", encoding="utf-8")
            (mig_dir / "0003_bad.sql").write_text("CREATE TABLE broken (id INTEGER PRIMARY KEY);\nINSERT INTO missing_table VALUES (1);\n", encoding="utf-8")

            # Bridge legacy _foundation_schema_migrations DB with user_version == 0
            legacy_conn = open_wal_sqlite(db_path)
            with legacy_conn:
                legacy_conn.execute("CREATE TABLE _foundation_schema_migrations (version INTEGER PRIMARY KEY, description TEXT NOT NULL, applied_at_utc TEXT NOT NULL);")
                legacy_conn.execute("INSERT INTO _foundation_schema_migrations VALUES (1, 'legacy', '2026-11-20T00:00:00Z');")
                legacy_conn.execute("CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT NOT NULL);")
            runner = SqliteSchemaMigrationRunner(legacy_conn)
            self.assertEqual(runner.current_version(), 1)
            self.assertEqual(legacy_conn.execute("PRAGMA user_version;").fetchone()[0], 1)

            with self.assertRaises(sqlite3.OperationalError):
                runner.apply_sql_migrations(mig_dir)
            # Migration 2 committed atomically; failing migration 3 rolled back DDL + PRAGMA user_version
            self.assertEqual(legacy_conn.execute("PRAGMA user_version;").fetchone()[0], 2)
            self.assertIsNone(legacy_conn.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='broken';").fetchone())
            self.assertEqual(legacy_conn.execute("SELECT name FROM items WHERE id=1;").fetchone()[0], "alpha")

            snap = vacuum_backup_sqlite(legacy_conn, root / "backup" / "snapshot.db")
            verify_snapshot_integrity(snap)
            legacy_conn.close()

            ro_conn = open_wal_sqlite(db_path, read_only=True)
            try:
                self.assertEqual(ro_conn.execute("PRAGMA query_only;").fetchone()[0], 1)
                with self.assertRaises(sqlite3.OperationalError):
                    ro_conn.execute("INSERT INTO items (id, name) VALUES (2, 'blocked');")
            finally:
                ro_conn.close()

    def test_pid_sentinel_lock_reuse_guard_heartbeat_and_legacy_read_aliases(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            target = (Path(tmp) / "lock_dir").resolve()
            lock = PidSentinelLock(target, "neuroplay", "daemon", lock_filename="neuroplay.lock", lease_seconds=60.0)
            self.assertTrue(lock.acquire())
            try:
                env = json.loads(lock.lock_path.read_text(encoding="utf-8"))
                self.assertEqual(env["appId"], "neuroplay")
                self.assertEqual(env["hostName"], socket.gethostname())
                self.assertTrue(env["processStartTimeUtc"].endswith("Z"))
                old_exp = env["expiresAtUtc"]
                time.sleep(0.02)
                self.assertTrue(lock.refresh_heartbeat(120.0))
                env2 = json.loads(lock.lock_path.read_text(encoding="utf-8"))
                self.assertNotEqual(env2["expiresAtUtc"], old_exp)
                held, _ = PidSentinelLock.inspect_lock(lock.lock_path)
                self.assertTrue(held)
            finally:
                lock.release()

            # Legacy Standard 31 v1 aliases ("hostname", "ownerAppId") + bogus PID-reuse startTime mismatch -> auto-evict
            lock.lock_path.parent.mkdir(parents=True, exist_ok=True)
            lock.lock_path.write_text(json.dumps({
                "schemaVersion": 1,
                "ownerAppId": "legacy-app",
                "pid": os.getpid(),
                "processStartTimeUtc": "1999-01-01T00:00:00.000Z",
                "hostname": socket.gethostname().upper(),
                "expiresAtUtc": "2099-01-01T00:00:00.000Z",
            }), encoding="utf-8")
            held_stale, stale_env = PidSentinelLock.inspect_lock(lock.lock_path)
            self.assertFalse(held_stale)
            self.assertIsNotNone(stale_env)
            self.assertEqual(stale_env["appId"], "legacy-app")
            self.assertEqual(stale_env["hostName"], socket.gethostname().upper())
            self.assertTrue(lock.acquire())
            lock.release()

    def test_logging_std35_ring_buffer_and_crash_dumper_std43_pii_scrub_retention(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            log_dir = (Path(tmp) / "logs").resolve()
            sink = RollingLogSink(log_dir, app_version="0.5.4", default_slice="core.worker", ring_buffer_size=50, retention_days=30)
            for idx in range(60):
                line = sink.write_line("info", f"step {idx}")
            self.assertIn("[v0.5.4]", line)
            self.assertIn("[core.worker]", line)
            self.assertEqual(len(sink.recent_lines()), 50)
            self.assertTrue(sink.recent_lines()[0].endswith("step 10"))

            old_rot = log_dir / "app.log.1"
            old_rot.write_text("old\n", encoding="utf-8")
            stale_ts = time.time() - (45 * 86400)
            os.utime(old_rot, (stale_ts, stale_ts))
            self.assertEqual(sink.prune_old_logs(), 1)
            self.assertFalse(old_rot.exists())

            secret_msg = rf"Failed reading {Path.home()}\secret.json with Bearer supersecrettoken123 and https://discord.com/api/webhooks/1234/abcd"
            scrubbed = sanitize_pii_text(secret_msg)
            self.assertIn("[USER_HOME]", scrubbed)
            self.assertNotIn("supersecrettoken123", scrubbed)
            self.assertNotIn("1234/abcd", scrubbed)

            dumper = CrashDumper(log_dir, "neuroplay", "0.5.4", app_archetype="SIS", runtime_mode="development", recent_logs_provider=sink.recent_lines, max_bundles=20)
            for idx in range(23):
                try:
                    raise RuntimeError(f"boom {idx} at {Path.home()}\\save token=my_secret_{idx}")
                except RuntimeError as exc:
                    path = dumper.write_crash_report(type(exc), exc, exc.__traceback__, argv=["app.py", "--automation-token", "tok_xyz"])
            bundles = list((log_dir / "crashes").glob("crash_*.json"))
            self.assertEqual(len(bundles), 20)
            doc = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual(doc["appId"], "neuroplay")
            for forbidden in ("platform", "contextTag", "pythonVersion"):
                self.assertNotIn(forbidden, doc)
            self.assertIn("--automation-token [REDACTED_TOKEN]", doc["commandLineSanitized"])
            self.assertIn("[USER_HOME]", doc["exception"]["message"])
            self.assertIn("[REDACTED_TOKEN]", doc["exception"]["message"])
            self.assertEqual(len(doc["precedingLogRingBuffer"]), 50)

    def test_automation_loopback_server_security_exit2_and_std18_endpoints(self) -> None:
        with self.assertRaises(AutomationDisabledError) as ctx:
            parse_automation_cli_args("neuroplay", ["--automation-port", "0"], env={})
        self.assertEqual(ctx.exception.exit_code, AUTOMATION_DISABLED_EXIT_CODE)

        opts = parse_automation_cli_args("neuroplay", ["--automation-port", "0", "--automation-token", "np-secret-054"], env={"NEUROPLAY_TEST_AUTOMATION": "1"})
        self.assertIsNotNone(opts)
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp).resolve()
            shot_root = root / "snaps"
            shot_root.mkdir()
            rgba = bytes([14, 165, 233, 255] * 16)
            shutdown_flag = {"called": False}
            srv = AutomationLoopbackServer(
                AutomationServerOptions(0, opts.bearer_token, "neuroplay", shot_root),
                state_provider=lambda: {"activeView": "dashboard", "isRunning": True},
                snapshot_provider=lambda _tid: (4, 4, rgba),
                shutdown_callback=lambda: shutdown_flag.__setitem__("called", True),
            )
            srv.start()
            try:
                base = f"http://127.0.0.1:{srv.automation_port}"
                # Unauthorized (401)
                with self.assertRaises(urllib.error.HTTPError) as e401:
                    urllib.request.urlopen(urllib.request.Request(f"{base}/v1/health"))
                self.assertEqual(e401.exception.code, 401)
                # External Origin (403)
                with self.assertRaises(urllib.error.HTTPError) as e403:
                    urllib.request.urlopen(urllib.request.Request(f"{base}/v1/health", headers={"Authorization": f"Bearer {opts.bearer_token}", "Origin": "https://evil.example"}))
                self.assertEqual(e403.exception.code, 403)
                # Non-loopback Host header via raw TCP (403) + bracketed IPv6 loopback Host ([::1]:port -> 200)
                with socket.create_connection(("127.0.0.1", srv.automation_port), timeout=3) as sock:
                    sock.sendall(f"GET /v1/health HTTP/1.1\r\nHost: evil.example.com\r\nAuthorization: Bearer {opts.bearer_token}\r\nConnection: close\r\n\r\n".encode("ascii"))
                    raw_resp = sock.recv(4096)
                self.assertTrue(raw_resp.startswith(b"HTTP/1.1 403") or raw_resp.startswith(b"HTTP/1.0 403"), raw_resp)
                with socket.create_connection(("127.0.0.1", srv.automation_port), timeout=3) as sock:
                    sock.sendall(f"GET /v1/health HTTP/1.1\r\nHost: [::1]:{srv.automation_port}\r\nAuthorization: Bearer {opts.bearer_token}\r\nConnection: close\r\n\r\n".encode("ascii"))
                    v6_resp = sock.recv(4096)
                self.assertTrue(v6_resp.startswith(b"HTTP/1.1 200") or v6_resp.startswith(b"HTTP/1.0 200"), v6_resp)

                headers = {"Authorization": f"Bearer {opts.bearer_token}", "Content-Type": "application/json"}
                health = json.loads(urllib.request.urlopen(urllib.request.Request(f"{base}/v1/health", headers=headers)).read())
                self.assertTrue(health["isPassed"])
                self.assertEqual(health["data"]["automationPort"], srv.automation_port)
                self.assertTrue(health["data"]["generatedAtUtc"].endswith("Z"))

                state = json.loads(urllib.request.urlopen(urllib.request.Request(f"{base}/v1/state", headers=headers)).read())
                self.assertEqual(state["data"]["activeView"], "dashboard")

                # Unsupported snapshotFormat (400)
                bad_fmt_body = json.dumps({"filePath": str(shot_root / "bad.jpg"), "snapshotFormat": "jpg"}).encode("utf-8")
                with self.assertRaises(urllib.error.HTTPError) as bad_fmt:
                    urllib.request.urlopen(urllib.request.Request(f"{base}/v1/snapshot", data=bad_fmt_body, headers=headers, method="POST"))
                self.assertEqual(bad_fmt.exception.code, 400)

                # Escape snapshot root (403)
                esc_body = json.dumps({"filePath": str(root / "outside.png")}).encode("utf-8")
                with self.assertRaises(urllib.error.HTTPError) as esc:
                    urllib.request.urlopen(urllib.request.Request(f"{base}/v1/snapshot", data=esc_body, headers=headers, method="POST"))
                self.assertEqual(esc.exception.code, 403)

                dest_png = shot_root / "cap.png"
                snap_res = json.loads(urllib.request.urlopen(urllib.request.Request(f"{base}/v1/snapshot", data=json.dumps({"filePath": str(dest_png)}).encode("utf-8"), headers=headers, method="POST")).read())
                self.assertEqual(snap_res["data"]["width"], 4)
                self.assertEqual(snap_res["data"]["height"], 4)
                self.assertEqual(snap_res["data"]["snapshotFormat"], "png")
                self.assertTrue(dest_png.read_bytes().startswith(b"\x89PNG"))

                live_png = urllib.request.urlopen(urllib.request.Request(f"{base}/v1/snapshot.png", headers={"Authorization": f"Bearer {opts.bearer_token}"})).read()
                self.assertTrue(live_png.startswith(b"\x89PNG"))

                sd_res = json.loads(urllib.request.urlopen(urllib.request.Request(f"{base}/v1/shutdown", data=b"{}", headers=headers, method="POST")).read())
                self.assertTrue(sd_res["data"]["isShuttingDown"])
                time.sleep(0.15)
                self.assertTrue(shutdown_flag["called"])
            finally:
                srv.stop()


if __name__ == "__main__":
    unittest.main()
