# @module: foundation_core.tests.test_sandbox_and_registry
# @purpose: Pure-stdlib unittest/pytest suite for sandbox_seam.py, pseudo_registry.py, sandbox_ctl.py, and run_sandbox_e2e.py.
# @depends-on: ctypes, json, os, pathlib, string, subprocess, sys, tempfile, unittest, uuid
# @exports: TestSandboxSeam, TestPseudoRegistry, TestSandboxToolsCli

from __future__ import annotations

import ctypes
from datetime import datetime, timedelta, timezone
import json
import os
from pathlib import Path
import socket
import string
import subprocess
import sys
import tempfile
import unittest
import uuid

DEV_ROOT = Path(__file__).resolve().parents[2]
SRC_ROOT = DEV_ROOT / "src"
TOOLS_ROOT = DEV_ROOT / "tools"
for _p in (str(SRC_ROOT), str(TOOLS_ROOT)):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import foundation_core  # noqa: E402
from foundation_core.pseudo_registry import (  # noqa: E402
    REGISTRY_HIVE_SCHEMA_URI,
    REGISTRY_HIVE_SCHEMA_VERSION,
    JsonHivePlatformRegistry,
    PlatformRegistryHive,
    PlatformRegistryValueKind,
)
from foundation_core.sandbox_seam import (  # noqa: E402
    CONTAINMENT_EXIT_CODE,
    SandboxContainmentError,
    assert_sandbox_path_if_active,
    get_known_folder,
    is_containment_violation,
    is_path_within_sandbox,
    map_host_path_to_sandbox,
    resolve_known_folders,
    resolve_sandbox_environment,
)
import exebox_stream  # noqa: E402
import run_sandbox_e2e  # noqa: E402
import sandbox_ctl  # noqa: E402


def _find_unused_drive_letters(count: int = 4) -> list[str]:
    if os.name != "nt":
        return [f"Z{i}:" for i in range(count)]
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    mask = int(k32.GetLogicalDrives())
    subst_map = sandbox_ctl.get_subst_mappings()
    free: list[str] = []
    for idx in range(len(string.ascii_uppercase) - 1, 3, -1):
        letter = string.ascii_uppercase[idx]
        tag = f"{letter}:"
        if (mask & (1 << idx)) == 0 and tag not in subst_map and k32.GetDriveTypeW(f"{tag}\\") == 1:
            free.append(tag)
            if len(free) == count:
                return free
    raise RuntimeError(f"Unable to locate {count} unused drive letters for isolated CLI test.")


def _isolated_tool_env(fake_home: Path) -> dict[str, str]:
    (fake_home / "Desktop").mkdir(parents=True, exist_ok=True)
    (fake_home / "AppData" / "Local" / "Programs").mkdir(parents=True, exist_ok=True)
    (fake_home / "AppData" / "Roaming" / "Microsoft" / "Windows" / "Start Menu" / "Programs").mkdir(
        parents=True, exist_ok=True
    )
    env = dict(os.environ)
    for key in (
        "FOUNDATION_SANDBOX_ROOT",
        "FOUNDATION_SANDBOX_PROFILE_DIR",
        "SANDBOX_C_DRIVE",
        "SANDBOX_D_DRIVE",
        "SANDBOX_REMOVABLE_DRIVE",
        "SANDBOX_DISC_DRIVE",
        "APP_ISOLATED_TEST",
    ):
        env.pop(key, None)
    env["USERPROFILE"] = str(fake_home)
    env["HOME"] = str(fake_home)
    env["LOCALAPPDATA"] = str(fake_home / "AppData" / "Local")
    env["APPDATA"] = str(fake_home / "AppData" / "Roaming")
    env["PYTHONPATH"] = str(SRC_ROOT)
    return env


class TestSandboxSeam(unittest.TestCase):
    def test_sdk_version_and_inactive_production_safety(self) -> None:
        self.assertEqual(foundation_core.SDK_VERSION, "0.5.6")
        desktop_name = exebox_stream.get_thread_desktop_name()
        self.assertTrue(bool(desktop_name))
        res = resolve_sandbox_environment(env_override={})
        self.assertFalse(res.is_sandbox_active)
        self.assertIsNone(res.sandbox_root)

        host_env = {
            "USERPROFILE": str(Path.home()),
            "LOCALAPPDATA": os.environ.get("LOCALAPPDATA", str(Path.home() / "AppData" / "Local")),
            "APPDATA": os.environ.get("APPDATA", str(Path.home() / "AppData" / "Roaming")),
        }
        folders = resolve_known_folders(res, env_override=host_env)
        self.assertEqual(folders["UserProfile"], Path(os.path.abspath(host_env["USERPROFILE"])))
        self.assertEqual(folders["LocalApplicationData"], Path(os.path.abspath(host_env["LOCALAPPDATA"])))

    def test_app_isolated_test_beats_foundation_sandbox_root(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            sbx = Path(tmp) / "sbx"
            sbx.mkdir()
            for truthy in ("1", "true", "yes"):
                res = resolve_sandbox_environment(
                    env_override={
                        "APP_ISOLATED_TEST": truthy,
                        "FOUNDATION_SANDBOX_ROOT": str(sbx),
                    }
                )
                self.assertFalse(res.is_sandbox_active)
                self.assertIsNone(res.sandbox_root)

    def test_stage2_four_drive_resolution_known_folders_and_containment(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "sbx"
            c_dir, d_dir, e_dir, disc_dir = root / "C", root / "D", root / "E", root / "Disc"
            for folder in (c_dir, d_dir, e_dir, disc_dir):
                folder.mkdir(parents=True)

            env = {
                "FOUNDATION_SANDBOX_ROOT": str(root),
                "SANDBOX_C_DRIVE": str(c_dir),
                "SANDBOX_D_DRIVE": str(d_dir),
                "SANDBOX_REMOVABLE_DRIVE": str(e_dir),
                "SANDBOX_DISC_DRIVE": str(disc_dir),
            }
            res = resolve_sandbox_environment(env_override=env)
            self.assertTrue(res.is_sandbox_active)
            self.assertEqual(res.sandbox_root, Path(os.path.abspath(str(root))))
            self.assertEqual(res.system_drive_root, Path(os.path.abspath(str(c_dir))))
            self.assertEqual(res.storage_drive_root, Path(os.path.abspath(str(d_dir))))
            self.assertEqual(res.removable_drive_root, Path(os.path.abspath(str(e_dir))))
            self.assertEqual(res.disc_drive_root, Path(os.path.abspath(str(disc_dir))))
            self.assertEqual(res.sandbox_profile_dir, Path(os.path.abspath(str(c_dir / "Users" / "Sandbox"))))
            self.assertEqual(len(res.mounts), 4)
            self.assertTrue(all(m.is_enabled for m in res.mounts))

            folders = resolve_known_folders(res)
            self.assertEqual(folders["UserProfile"], res.sandbox_profile_dir)
            self.assertEqual(folders["LocalApplicationData"], res.sandbox_profile_dir / "AppData" / "Local")
            self.assertEqual(folders["DesktopDirectory"], res.sandbox_profile_dir / "Desktop")
            self.assertEqual(get_known_folder("ProgramFiles", res), res.system_drive_root / "Program Files")

            sandbox_target = d_dir / "Programs" / "MyApp"
            self.assertTrue(is_path_within_sandbox(sandbox_target, res))
            self.assertEqual(assert_sandbox_path_if_active(sandbox_target, res), Path(os.path.abspath(str(sandbox_target))))

            host_local = os.environ.get("LOCALAPPDATA") or str(Path.home() / "AppData" / "Local")
            mapped = map_host_path_to_sandbox(Path(host_local) / "Programs" / "DemoApp", res)
            self.assertEqual(mapped, res.sandbox_profile_dir / "AppData" / "Local" / "Programs" / "DemoApp")

            outside = Path(tmp) / "outside_escape"
            host_d_escape = Path(f"D:{os.sep}Programs{os.sep}EscapeApp")
            for escape in (outside, host_d_escape, Path(host_local) / "Programs" / "EscapeApp"):
                self.assertFalse(is_path_within_sandbox(escape, res))
                with self.assertRaises(SandboxContainmentError) as ctx:
                    assert_sandbox_path_if_active(escape, res)
                self.assertEqual(ctx.exception.exit_code, CONTAINMENT_EXIT_CODE)
                self.assertTrue(is_containment_violation(ctx.exception))

            with self.assertRaises(SandboxContainmentError) as col_ctx:
                resolve_sandbox_environment(env_override={"FOUNDATION_SANDBOX_ROOT": str(Path(host_local) / "Programs")})
            self.assertEqual(col_ctx.exception.exit_code, CONTAINMENT_EXIT_CODE)

    def test_redirected_userprofile_and_windows_short_8_3_path_do_not_false_collide(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            long_parent = Path(tmp) / "Runner Admin Home"
            sbx = long_parent / "sandbox"
            c_dir = sbx / "C"
            prof = c_dir / "Users" / "Sandbox"
            (prof / "AppData" / "Local" / "Temp").mkdir(parents=True, exist_ok=True)
            (prof / "Desktop").mkdir(parents=True, exist_ok=True)

            short_sbx = str(sbx)
            if os.name == "nt":
                k32 = ctypes.WinDLL("kernel32", use_last_error=True)
                buf = ctypes.create_unicode_buffer(1024)
                if k32.GetShortPathNameW(str(sbx), buf, len(buf)) > 0 and buf.value:
                    short_sbx = buf.value

            child_code = "\n".join([
                "import sys",
                f"sys.path.insert(0, {repr(str(SRC_ROOT))})",
                "from foundation_core.sandbox_seam import resolve_sandbox_environment, is_path_within_sandbox",
                "res = resolve_sandbox_environment()",
                "assert res.is_sandbox_active",
                "assert is_path_within_sandbox(str(res.sandbox_profile_dir / 'Desktop'), res)",
            ])
            child_env = dict(os.environ)
            child_env["FOUNDATION_SANDBOX_ROOT"] = short_sbx
            child_env["SANDBOX_C_DRIVE"] = str(c_dir)
            child_env["FOUNDATION_SANDBOX_PROFILE_DIR"] = str(prof)
            child_env["USERPROFILE"] = str(prof)
            child_env["LOCALAPPDATA"] = str(prof / "AppData" / "Local")
            child_env["APPDATA"] = str(prof / "AppData" / "Roaming")
            child_env["TEMP"] = str(prof / "AppData" / "Local" / "Temp")
            child_env["TMP"] = str(prof / "AppData" / "Local" / "Temp")
            proc = subprocess.run([sys.executable, "-B", "-c", child_code], env=child_env, capture_output=True, text=True, timeout=20, check=False)
            self.assertEqual(proc.returncode, 0, f"{proc.stderr}\n{proc.stdout}")


class TestPseudoRegistry(unittest.TestCase):
    def test_standard18_schema_all_six_kinds_and_bak_healing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            sbx_root = Path(tmp) / "sbx"
            hive_path = sbx_root / "registry" / "registry.hive.json"
            lock_path = sbx_root / "locks" / "registry.hive.lock"
            reg = JsonHivePlatformRegistry(hive_path, sbx_root, lock_path, app_id="py-test")

            reg.ensure_initialized()
            doc = json.loads(hive_path.read_text(encoding="utf-8"))
            self.assertEqual(doc["$schema"], REGISTRY_HIVE_SCHEMA_URI)
            self.assertEqual(doc["schemaVersion"], REGISTRY_HIVE_SCHEMA_VERSION)
            self.assertEqual(doc["sandboxRoot"], os.path.abspath(str(sbx_root)))
            self.assertTrue(doc["updatedAtUtc"].endswith("Z"))
            self.assertEqual(set(doc["registryHives"].keys()), {"HKCU", "HKLM", "HKCR"})

            subkey = r"HKEY_CURRENT_USER\Software\FoundationTest\AllKinds"
            norm_sub = r"Software\FoundationTest\AllKinds"
            raw_bin = b"\xde\xad\xbe\xef\x0a\xff"

            reg.set_value(PlatformRegistryHive.CURRENT_USER, subkey, "SzVal", "hello", PlatformRegistryValueKind.STRING)
            reg.set_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "ExpandVal", r"%USERPROFILE%\Docs", PlatformRegistryValueKind.EXPAND_STRING)
            reg.set_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "MultiVal", ["one", "two"], PlatformRegistryValueKind.MULTI_STRING)
            reg.set_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "DwordVal", 77, PlatformRegistryValueKind.DWORD)
            reg.set_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "QwordVal", 1 << 42, PlatformRegistryValueKind.QWORD)
            reg.set_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "BinVal", raw_bin, PlatformRegistryValueKind.BINARY)
            reg.set_value("HKLM", r"SOFTWARE\TestVendor\App", "Installed", 1, "REG_DWORD")
            reg.ensure_key(PlatformRegistryHive.CLASSES_ROOT, ".fndext")

            self.assertTrue(reg.key_exists(PlatformRegistryHive.CURRENT_USER, norm_sub))
            self.assertTrue(reg.key_exists(PlatformRegistryHive.CLASSES_ROOT, ".fndext"))
            self.assertEqual(reg.get_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "SzVal"), "hello")
            self.assertEqual(reg.get_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "ExpandVal"), r"%USERPROFILE%\Docs")
            self.assertEqual(reg.get_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "MultiVal"), ["one", "two"])
            self.assertEqual(reg.get_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "DwordVal"), 77)
            self.assertEqual(reg.get_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "QwordVal"), 1 << 42)
            self.assertEqual(reg.get_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "BinVal"), raw_bin)

            bin_entry = reg.get_value_entry(PlatformRegistryHive.CURRENT_USER, norm_sub, "BinVal")
            self.assertEqual(bin_entry, {"registryValueKind": "REG_BINARY", "registryValueData": "deadbeef0aff"})

            self.assertIn("AllKinds", reg.list_subkey_names(PlatformRegistryHive.CURRENT_USER, r"Software\FoundationTest"))
            self.assertIn("BinVal", reg.list_value_names(PlatformRegistryHive.CURRENT_USER, norm_sub))

            # Verify .bak healing after corruption
            reg.set_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "Step2", "latest", PlatformRegistryValueKind.STRING)
            bak_path = hive_path.with_name("registry.hive.json.bak")
            self.assertTrue(bak_path.is_file())
            hive_path.write_text("{corrupt json", encoding="utf-8")
            self.assertEqual(reg.get_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "BinVal"), raw_bin)
            self.assertIsNone(reg.get_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "Step2"))

            self.assertTrue(reg.delete_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "DwordVal"))
            self.assertFalse(reg.delete_value(PlatformRegistryHive.CURRENT_USER, norm_sub, "DwordVal"))
            self.assertTrue(reg.delete_key(PlatformRegistryHive.CURRENT_USER, r"Software\FoundationTest", recursive=True))
            self.assertFalse(reg.key_exists(PlatformRegistryHive.CURRENT_USER, norm_sub))

    def test_orphan_pid_lock_breaking_and_multiprocess_contention(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            sbx_root = Path(tmp) / "sbx"
            hive_path = sbx_root / "registry" / "registry.hive.json"
            lock_path = sbx_root / "locks" / "registry.hive.lock"
            lock_path.parent.mkdir(parents=True, exist_ok=True)
            reg = JsonHivePlatformRegistry(hive_path, sbx_root, lock_path, lock_timeout_seconds=20.0)

            future_utc = (datetime.now(timezone.utc) + timedelta(minutes=10)).strftime("%Y-%m-%dT%H:%M:%S.000Z")
            dead_lock = {
                "schemaVersion": 1,
                "appId": "crashed-worker",
                "pid": 999999,
                "processStartTimeUtc": "2020-01-01T00:00:00.000Z",
                "hostName": socket.gethostname(),
                "operation": "stale-write",
                "acquiredAtUtc": "2020-01-01T00:00:00.000Z",
                "heartbeatAtUtc": "2020-01-01T00:00:00.000Z",
                "expiresAtUtc": future_utc,
            }
            lock_path.write_text(json.dumps(dead_lock), encoding="utf-8")
            reg.set_value(PlatformRegistryHive.CURRENT_USER, r"Software\LockTest", "Recovered", 1, PlatformRegistryValueKind.DWORD)
            self.assertEqual(reg.get_value(PlatformRegistryHive.CURRENT_USER, r"Software\LockTest", "Recovered"), 1)
            self.assertFalse(lock_path.exists())

            # Expired lease even with a live external PID must be broken cleanly.
            sleeper = subprocess.Popen([sys.executable, "-B", "-c", "import time; time.sleep(15)"])
            try:
                expired_lock = dict(
                    dead_lock,
                    pid=sleeper.pid,
                    processStartTimeUtc="",
                    expiresAtUtc="2020-01-01T00:00:01.000Z",
                )
                lock_path.write_text(json.dumps(expired_lock), encoding="utf-8")
                reg.set_value(PlatformRegistryHive.CURRENT_USER, r"Software\LockTest", "RecoveredExpired", 2, PlatformRegistryValueKind.DWORD)
                self.assertEqual(reg.get_value(PlatformRegistryHive.CURRENT_USER, r"Software\LockTest", "RecoveredExpired"), 2)
            finally:
                sleeper.terminate()
                sleeper.wait(timeout=5)

            # Multi-process concurrent writes across 4 child processes
            worker_code = (
                "import sys; sys.path.insert(0, sys.argv[1]); "
                "from foundation_core.pseudo_registry import JsonHivePlatformRegistry, PlatformRegistryHive, PlatformRegistryValueKind; "
                "reg = JsonHivePlatformRegistry(sys.argv[2], sys.argv[3], sys.argv[4], lock_timeout_seconds=20.0); "
                "wid = sys.argv[5]; "
                "[reg.set_value(PlatformRegistryHive.CURRENT_USER, r'Software\\MultiProc', f'w{wid}_{i}', f'v{wid}_{i}', PlatformRegistryValueKind.STRING) for i in range(8)]"
            )
            procs = [
                subprocess.Popen(
                    [sys.executable, "-B", "-c", worker_code, str(SRC_ROOT), str(hive_path), str(sbx_root), str(lock_path), str(w)],
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    text=True,
                )
                for w in range(4)
            ]
            try:
                for proc in procs:
                    out, err = proc.communicate(timeout=30)
                    self.assertEqual(proc.returncode, 0, f"Worker failed: {err}\n{out}")
            finally:
                for proc in procs:
                    if proc.poll() is None:
                        proc.kill()
                    if proc.stdout is not None:
                        proc.stdout.close()
                    if proc.stderr is not None:
                        proc.stderr.close()
                    if proc.returncode is None:
                        proc.wait(timeout=5)

            for w in range(4):
                for i in range(8):
                    self.assertEqual(
                        reg.get_value(PlatformRegistryHive.CURRENT_USER, r"Software\MultiProc", f"w{w}_{i}"),
                        f"v{w}_{i}",
                    )
            self.assertFalse(lock_path.exists())


class TestSandboxToolsCli(unittest.TestCase):
    def test_sandbox_ctl_init_status_snapshot_diff_reset_and_collision(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            sbx_root = tmp_path / "sandbox"
            fake_home = tmp_path / "fake_home"
            env = _isolated_tool_env(fake_home)
            c_drv, d_drv, rem_drv, disc_drv = _find_unused_drive_letters(4)
            base_args = [
                "--sandbox-root", str(sbx_root),
                "--c-drive", c_drv,
                "--d-drive", d_drv,
                "--removable-drive", rem_drv,
                "--disc-drive", disc_drv,
            ]

            init_proc = subprocess.run(
                [sys.executable, "-B", str(TOOLS_ROOT / "sandbox_ctl.py"), *base_args, "init", "--no-subst", "--json"],
                env=env,
                capture_output=True,
                text=True,
                timeout=20,
                check=False,
            )
            self.assertEqual(init_proc.returncode, 0, init_proc.stderr)
            manifest = json.loads((sbx_root / "sandbox.manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(manifest["schemaVersion"], 2)
            self.assertEqual(manifest["appId"], "foundation-sandbox")
            self.assertEqual(manifest["version"], "0.5.6")
            self.assertNotIn("manifestType", manifest)
            self.assertEqual(len(manifest["sandboxMounts"]), 4)
            self.assertTrue((sbx_root / "registry" / "registry.baseline.json").is_file())
            self.assertTrue((sbx_root / "registry" / "registry.hive.json").is_file())

            status_proc = subprocess.run(
                [sys.executable, "-B", str(TOOLS_ROOT / "sandbox_ctl.py"), *base_args, "status", "--json"],
                env=env,
                capture_output=True,
                text=True,
                timeout=20,
                check=False,
            )
            self.assertEqual(status_proc.returncode, 0, status_proc.stderr)
            status = json.loads(status_proc.stdout)
            self.assertTrue(status["isManifestPresent"])
            self.assertTrue(status["isHivePresent"])

            snap_proc = subprocess.run(
                [sys.executable, "-B", str(TOOLS_ROOT / "sandbox_ctl.py"), *base_args, "snapshot", "--name", "cli-test"],
                env=env,
                capture_output=True,
                text=True,
                timeout=20,
                check=False,
            )
            self.assertEqual(snap_proc.returncode, 0, snap_proc.stderr)

            diff_ok = subprocess.run(
                [sys.executable, "-B", str(TOOLS_ROOT / "sandbox_ctl.py"), *base_args, "diff-host", "--name", "cli-test", "--fail-on-diff"],
                env=env,
                capture_output=True,
                text=True,
                timeout=20,
                check=False,
            )
            self.assertEqual(diff_ok.returncode, 0, diff_ok.stderr)
            self.assertEqual(json.loads(diff_ok.stdout)["hostDiffCount"], 0)

            (fake_home / "Desktop" / "simulated_host_leak.lnk").write_bytes(b"leak")
            diff_fail = subprocess.run(
                [sys.executable, "-B", str(TOOLS_ROOT / "sandbox_ctl.py"), *base_args, "diff-host", "--name", "cli-test", "--fail-on-diff"],
                env=env,
                capture_output=True,
                text=True,
                timeout=20,
                check=False,
            )
            self.assertEqual(diff_fail.returncode, 3)
            self.assertGreaterEqual(json.loads(diff_fail.stdout)["hostDiffCount"], 1)

            keep_run = sbx_root / "runs" / "kept-receipt.json"
            keep_run.write_text("{\"ok\":true}", encoding="utf-8")
            (sbx_root / "D" / "Programs" / "stale.txt").write_text("stale", encoding="utf-8")
            reset_proc = subprocess.run(
                [sys.executable, "-B", str(TOOLS_ROOT / "sandbox_ctl.py"), *base_args, "reset", "--keep-runs", "--no-subst"],
                env=env,
                capture_output=True,
                text=True,
                timeout=20,
                check=False,
            )
            self.assertEqual(reset_proc.returncode, 0, reset_proc.stderr)
            self.assertTrue(keep_run.is_file())
            self.assertFalse((sbx_root / "D" / "Programs" / "stale.txt").exists())

            bad_drive_proc = subprocess.run(
                [sys.executable, "-B", str(TOOLS_ROOT / "sandbox_ctl.py"), "--sandbox-root", str(sbx_root), "--c-drive", "C:", "status"],
                env=env,
                capture_output=True,
                text=True,
                timeout=20,
                check=False,
            )
            self.assertEqual(bad_drive_proc.returncode, 3)

    def test_run_sandbox_e2e_cli_12_point_mock_flow_and_containment_exit3(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            sbx_root = tmp_path / "sandbox"
            fake_home = tmp_path / "fake_home"
            env = _isolated_tool_env(fake_home)
            c_drv, d_drv, rem_drv, disc_drv = _find_unused_drive_letters(4)

            child_env = run_sandbox_e2e.build_sandbox_child_env(
                {
                    "sandboxRoot": sbx_root,
                    "sandboxProfileDir": sbx_root / "C" / "Users" / "Sandbox",
                    "drives": {
                        "SANDBOX_C_DRIVE": c_drv,
                        "SANDBOX_D_DRIVE": d_drv,
                        "SANDBOX_REMOVABLE_DRIVE": rem_drv,
                        "SANDBOX_DISC_DRIVE": disc_drv,
                    },
                }
            )
            self.assertEqual(child_env["FOUNDATION_SUPPRESS_MODALS"], "1")
            self.assertEqual(child_env["FOUNDATION_SANDBOX_ROOT"], str(sbx_root))
            self.assertNotIn("PYTHONPATH", child_env)
            for required_os_key in (
                "SystemDrive", "ProgramData", "ProgramFiles", "ProgramFiles(x86)",
                "CommonProgramFiles", "CommonProgramFiles(x86)", "ALLUSERSPROFILE", "PUBLIC",
            ):
                if any(k.lower() == required_os_key.lower() and v for k, v in os.environ.items()):
                    self.assertIn(required_os_key, child_env)

            prof_dir = sbx_root / "C" / "Users" / "Sandbox"
            mock_installer = tmp_path / "mock_installer.py"
            mock_app_code = (
                "import sys\n"
                "if '--version' in sys.argv:\n"
                "    print('0.4.2'); raise SystemExit(0)\n"
                "if any(a.startswith('--check-missing=') for a in sys.argv):\n"
                "    raise SystemExit(2)\n"
                "raise SystemExit(0)\n"
            )
            mock_uninst_code = (
                "import json, os, sys; from pathlib import Path\n"
                f"sys.path.insert(0, {repr(str(SRC_ROOT))})\n"
                "from foundation_core.pseudo_registry import JsonHivePlatformRegistry, PlatformRegistryHive\n"
                "inst = Path(sys.argv[sys.argv.index('--install-dir') + 1])\n"
                "exe = inst / 'app' / 'mockapp.py'\n"
                "if exe.exists(): exe.unlink()\n"
                "lnk = Path(os.environ['FOUNDATION_SANDBOX_PROFILE_DIR']) / 'Desktop' / 'Mock App.lnk'\n"
                "if lnk.exists(): lnk.unlink()\n"
                "JsonHivePlatformRegistry.from_sandbox().delete_key(PlatformRegistryHive.CURRENT_USER, r'Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\mockapp')\n"
            )
            mock_installer.write_text(
                "\n".join([
                    "import json, os, sys, uuid",
                    "from pathlib import Path",
                    f"sys.path.insert(0, {repr(str(SRC_ROOT))})",
                    "from foundation_core.pseudo_registry import JsonHivePlatformRegistry, PlatformRegistryHive, PlatformRegistryValueKind",
                    "inst = Path(sys.argv[sys.argv.index('--install-dir') + 1])",
                    "(inst / 'app').mkdir(parents=True, exist_ok=True)",
                    "(inst / 'data').mkdir(parents=True, exist_ok=True)",
                    f"(inst / 'app' / 'mockapp.py').write_text({repr(mock_app_code)}, encoding='utf-8')",
                    f"(inst / 'app' / 'mockapp-Uninstall.py').write_text({repr(mock_uninst_code)}, encoding='utf-8')",
                    "prof = Path(os.environ['FOUNDATION_SANDBOX_PROFILE_DIR'])",
                    "lnk = prof / 'Desktop' / 'Mock App.lnk'",
                    "lnk.parent.mkdir(parents=True, exist_ok=True)",
                    "lnk.write_bytes(b'\\x4c\\x00\\x00\\x00' + uuid.UUID('00021401-0000-0000-C000-000000000046').bytes_le + b'\\x00' * 56)",
                    "(inst / 'app' / 'install-manifest.json').write_text(json.dumps({'created_shortcuts': [str(lnk)]}), encoding='utf-8')",
                    "JsonHivePlatformRegistry.from_sandbox().set_value(PlatformRegistryHive.CURRENT_USER, r'Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\mockapp', 'DisplayName', 'Mock App', PlatformRegistryValueKind.STRING)",
                ]) + "\n",
                encoding="utf-8",
            )

            receipt_out = tmp_path / "receipt.json"
            e2e_proc = subprocess.run(
                [
                    sys.executable,
                    "-B",
                    str(TOOLS_ROOT / "run_sandbox_e2e.py"),
                    "--app-id", "mockapp",
                    "--version", "0.4.2",
                    "--installer", str(mock_installer),
                    "--missing-file-arg=--check-missing={missing}",
                    "--missing-file-expected-exit", "2",
                    "--no-subst",
                    "--sandbox-root", str(sbx_root),
                    "--profile-dir", str(prof_dir),
                    "--c-drive", c_drv,
                    "--d-drive", d_drv,
                    "--removable-drive", rem_drv,
                    "--disc-drive", disc_drv,
                    "--receipt-out", str(receipt_out),
                ],
                env=env,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            self.assertEqual(e2e_proc.returncode, 0, f"{e2e_proc.stderr}\n{e2e_proc.stdout}")
            receipt = json.loads(receipt_out.read_text(encoding="utf-8"))
            required_keys = {
                "$schema", "schemaVersion", "id", "appId", "version", "status",
                "isPassed", "isStage2SandboxPassed", "hostDiffCount", "sandboxRoot",
                "sandboxProfileDir", "registryHivePath", "sandboxMounts", "filePath",
                "directoryPath", "startedAtUtc", "completedAtUtc", "verifiedAtUtc",
                "durationMs", "exitCode", "errorCode", "errorMessage", "errorRemedy",
                "notes", "checklistResults",
            }
            self.assertTrue(required_keys.issubset(receipt.keys()))
            self.assertTrue(receipt["isPassed"])
            self.assertTrue(receipt["isStage2SandboxPassed"])
            self.assertEqual(receipt["hostDiffCount"], 0)
            self.assertEqual(receipt["exitCode"], 0)
            self.assertEqual(len(receipt["checklistResults"]), 12)
            self.assertTrue(all(item["isPassed"] for item in receipt["checklistResults"]), receipt["checklistResults"])

            escape_proc = subprocess.run(
                [
                    sys.executable,
                    "-B",
                    str(TOOLS_ROOT / "run_sandbox_e2e.py"),
                    "--app-id", "mockapp",
                    "--installer", str(mock_installer),
                    "--install-dir", str(tmp_path / "outside_escape"),
                    "--no-subst",
                    "--sandbox-root", str(sbx_root),
                    "--c-drive", c_drv,
                    "--d-drive", d_drv,
                    "--removable-drive", rem_drv,
                    "--disc-drive", disc_drv,
                ],
                env=env,
                capture_output=True,
                text=True,
                timeout=20,
                check=False,
            )
            self.assertEqual(escape_proc.returncode, 3)

    def test_scrubbed_child_env_interop_with_sandbox_seam_and_from_sandbox(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            sbx_root = tmp_path / "sandbox"
            c_dir = sbx_root / "C"
            d_dir = sbx_root / "D"
            e_dir = sbx_root / "E"
            disc_dir = sbx_root / "Disc"
            prof_dir = c_dir / "Users" / "Sandbox"
            for folder in (prof_dir / "AppData" / "Local" / "Temp", prof_dir / "AppData" / "Roaming", d_dir, e_dir, disc_dir):
                folder.mkdir(parents=True, exist_ok=True)

            child_env = run_sandbox_e2e.build_sandbox_child_env(
                {
                    "sandboxRoot": sbx_root,
                    "sandboxProfileDir": prof_dir,
                    "drives": {
                        "SANDBOX_C_DRIVE": str(c_dir),
                        "SANDBOX_D_DRIVE": str(d_dir),
                        "SANDBOX_REMOVABLE_DRIVE": str(e_dir),
                        "SANDBOX_DISC_DRIVE": str(disc_dir),
                    },
                }
            )
            self.assertEqual(child_env["USERPROFILE"], str(prof_dir))
            self.assertEqual(child_env["LOCALAPPDATA"], str(prof_dir / "AppData" / "Local"))
            self.assertEqual(child_env["APPDATA"], str(prof_dir / "AppData" / "Roaming"))

            child_code = "\n".join([
                "import os, sys",
                f"sys.path.insert(0, {repr(str(SRC_ROOT))})",
                "from foundation_core.sandbox_seam import resolve_sandbox_environment, SandboxContainmentError",
                "from foundation_core.pseudo_registry import JsonHivePlatformRegistry, PlatformRegistryHive, PlatformRegistryValueKind",
                "res = resolve_sandbox_environment()",
                "assert res.is_sandbox_active",
                "reg = JsonHivePlatformRegistry.from_sandbox(res)",
                "reg.set_value(PlatformRegistryHive.CURRENT_USER, r'Software\\ChildInterop\\Test', 'ChildWrite', 'ok-child', PlatformRegistryValueKind.STRING)",
                "assert reg.get_value(PlatformRegistryHive.CURRENT_USER, r'Software\\ChildInterop\\Test', 'ChildWrite') == 'ok-child'",
                "try:",
                "    resolve_sandbox_environment(env_override={'FOUNDATION_SANDBOX_ROOT': os.environ['SystemRoot']})",
                "    raise AssertionError('Expected SandboxContainmentError for SystemRoot')",
                "except SandboxContainmentError as exc:",
                "    assert exc.exit_code == 3",
            ])
            proc = subprocess.run(
                [sys.executable, "-B", "-c", child_code],
                env=child_env,
                capture_output=True,
                text=True,
                timeout=20,
                check=False,
            )
            self.assertEqual(proc.returncode, 0, f"{proc.stderr}\n{proc.stdout}")

            parent_reg = JsonHivePlatformRegistry(
                sbx_root / "registry" / "registry.hive.json",
                sbx_root,
                sbx_root / "locks" / "registry.hive.lock",
            )
            self.assertEqual(
                parent_reg.get_value(PlatformRegistryHive.CURRENT_USER, r"Software\ChildInterop\Test", "ChildWrite"),
                "ok-child",
            )


if __name__ == "__main__":
    unittest.main()
