# Foundation (`v0.5.6`) — Stage 2 E2E Coverage & Performance Ledger

> **Machine-Readable Ledgers (gitignored):** [`Dev\docs\tests\e2e_coverage.json`](tests/e2e_coverage.json) · [`Dev\docs\tests\e2e_perf_history.json`](tests/e2e_perf_history.json)
> **Last Run:** `e2e-20261128-0706` (`2026-11-28T07:06:34Z`) · **Seed:** `20261128` · **Host Load:** `CLEAN` (`CPU 49%`, `RAM 33.7%`, `GPU 2%`, `Free VRAM 6,131 MB`)

---

## 1. Coverage Summary

| Metric | Value | Notes |
|---|---:|---|
| **Total Inventoried Surfaces** | `18` | Screens, controls, CLI/sandbox entrypoints, and core SDK surfaces |
| **Verified (`VERIFIED`)** | `18` | `100%` of new/changed `v0.5.6` surfaces (`11/11`) + `7/7` sampled untouched surfaces |
| **Untested / Stale Diff / Failed** | `0 / 0 / 0` | Zero silent skips; `hostDiffCount == 0`; `0` orphan processes |
| **Seeded Random Regressions (`seed = 20261128`)** | `7 / 7` | `installer.ownership_guard`, `core.sandbox_seam`, `core.pseudo_registry`, `tools.sandbox_ctl`, `tools.build_release_universal`, `py.automation_loopback`, `py.granular_settings` |

---

## 2. Surface Inventory

| Surface ID | Category | Selector / Entry | Channel | Last Verified |
|---|---|---|---|---|
| `installer.nativeaot_win32_wizard` | Screen | `AotSmoke.exe` (`FoundationWin32InstallerWizard`) | Win32 HWND + `PrintWindow` PNG | `v0.5.6` (`2026-11-28`) |
| `installer.single_profile_collapse` | Control | `AotSmoke.exe --single-profile --details` (`Top == 34`) | Win32 HWND + `PrintWindow` PNG | `v0.5.6` (`2026-11-28`) |
| `installer.ownership_guard` | Control | Live `InstallDir` edit -> `FOREIGN APP DETECTED` | Win32 HWND + `PrintWindow` PNG | `v0.5.6` (`2026-11-28`) |
| `installer.startmenu_collision_guard` | Mode | `ResolveShortcutDirectories` (0-byte + non-empty) | `FoundationV056NativeAotAndOptTests` | `v0.5.6` (`2026-11-28`) |
| `installer.slim_uninstaller_extract` | Mode | `TryExtractEmbeddedUninstallerResource` | `FoundationV056NativeAotAndOptTests` | `v0.5.6` (`2026-11-28`) |
| `installer.online_payload_split` | Mode | `InstallerEngine.OnlinePayload.Extraction.cs` | `Foundation.Tests` | `v0.5.6` (`2026-11-28`) |
| `installer.cli_sandbox_12point` | Entrypoint | `Dev\tools\run_sandbox_e2e.py --no-subst` (`12/12`) | Stage 2 Sandbox CLI | `v0.5.6` (`2026-11-28`) |
| `core.sqlite_wal_pool_lockfree` | Feature | `SqliteWalPool.OpenConnectionAsync` / `ResetJournalModeCache` | `Foundation.Tests` (`74 ms`) | `v0.5.6` (`2026-11-28`) |
| `core.release_updater_client` | Feature | `ReleaseUpdaterClient` + `FoundationCoreJsonContext` | `Foundation.Tests` | `v0.5.6` (`2026-11-28`) |
| `py.secrets_dpapi` | Feature | `foundation_core.secrets.DpapiSecretStore` | Live Python + C# Interop (`11.49 ms`) | `v0.5.6` (`2026-11-28`) |
| `py.sidecars_hklock` | Feature | `foundation_core.sidecars` + `sidecars_locks.py` | Live Python + `unittest` (`72.95 ms`) | `v0.5.6` (`2026-11-28`) |
| `tools.exebox_stream_ws` | Feature | `ExeboxStreamHelpers.ps1` / `exebox_stream.py` (`:5005`) | Live Hub Probe + `unittest` | `v0.5.6` (`2026-11-28`) |
| `core.sandbox_seam` | Feature | `SandboxEnvironmentSeam` / `sandbox_seam.py` | Stage 2 Sandbox + `unittest` | `v0.5.6` (`2026-11-28`) |
| `core.pseudo_registry` | Feature | `JsonHivePlatformRegistry` / `pseudo_registry.py` | Stage 2 Sandbox (`S2-03`/`S2-10`) | `v0.5.6` (`2026-11-28`) |
| `tools.sandbox_ctl` | Entrypoint | `Dev\tools\sandbox_ctl.py` (`hostDiffCount == 0`) | Stage 2 Sandbox (`S2-01`/`S2-12`) | `v0.5.6` (`2026-11-28`) |
| `tools.build_release_universal` | Entrypoint | `Dev\tools\build_release_universal.py` | `unittest` (`8/8`) | `v0.5.6` (`2026-11-28`) |
| `py.automation_loopback` | Feature | `foundation_core.automation.AutomationLoopbackServer` | `unittest` | `v0.5.6` (`2026-11-28`) |
| `py.granular_settings` | Feature | `foundation_core.settings.GranularSettingsStore` | `unittest` | `v0.5.6` (`2026-11-28`) |

---

## 3. Performance Comparison vs. Base (`v0.5.5 -> v0.5.6`)

- **Base Run:** `e2e-20261122-v055-base` (`CLEAN`) | **Current Run:** `e2e-20261128-0706` (`CLEAN`)
- **Net Summary:** `1` Major Binary Footprint Gain (`-89.7%` un-compressed NativeAOT vs. WinForms reference), `0` Regressions, `Baseline Initialized` for live Stage 2 GUI/sandbox/WAL micro-timings (`Overall: NET GAIN`).

| Metric | Direction | Base (`0.5.5`) | Current (`0.5.6`) | Delta | % Change | Host Band | Verdict |
|---|---|---:|---:|---:|---:|---|---|
| Standalone Setup Binary Size (MB) | lower is better | `68.61` | `7.04` | `-61.57` | `-89.7%` | `CLEAN` | `GAIN` |
| NativeAOT Wizard Startup -> HWND (ms) | lower is better | `N/A` | `55.9` | `—` | `Init` | `CLEAN` | `BASELINE_INIT` |
| Sandbox 12-Point Matrix (`S2-01`..`S2-12`) (ms) | lower is better | `N/A` | `1157.0` | `—` | `Init` | `CLEAN` | `BASELINE_INIT` |
| `SqliteWalPool` Recreation + Concurrency (ms) | lower is better | `N/A` | `74.0` | `—` | `Init` | `CLEAN` | `BASELINE_INIT` |
| Python DPAPI Secret Round-Trip (ms) | lower is better | `N/A` | `11.49` | `—` | `Init` | `CLEAN` | `BASELINE_INIT` |
| Python Sidecar + `.hklock` Round-Trip (ms) | lower is better | `N/A` | `72.95` | `—` | `Init` | `CLEAN` | `BASELINE_INIT` |
| C# `Foundation.Tests` Count / Wall Time | coverage | `55` / `N/A` | `63` / `10726.9 ms` | `+8` | `+14.5%` | `CLEAN` | `GAIN` |
| Python `foundation_core` `unittest` Count / Time | coverage | `23` / `N/A` | `29` / `10053.0 ms` | `+6` | `+26.1%` | `CLEAN` | `GAIN` |
