# Foundation — Roadmap & Milestones

> Maintained by `@tessa` per Dev Bible [Standard 16](file:///<PROJECTS_ROOT>/_DevBible/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md). Tracks the North Star goal, active in-dev milestone, upcoming release targets, completed milestones, and cutting-room floor ideas.

---

## 1. North Star Goal
Provide a 100% backward-compatible, version-pinned, and fully customizable shared SDK (`Foundation.Core.dll`, `Foundation.Retro.dll`, `Foundation.Sidecars.dll`, `Foundation.Server.dll`, `Foundation.Launcher.dll`, `Foundation.UI.dll`, `Foundation.Installer.dll`, `@foundation/ui`, and `foundation_core`) so every sister app, standalone utility, and game shares rock-solid Dev Bible infrastructure while keeping every individual file `<= 400 LOC` (`250 LOC` target) and zero-touch extensible.

---

## 2. Active In-Dev Milestone (`v0.3.0` Shipped -> `v0.4.0` / `v1.0.0` Full Ecosystem Rollout)
- **Active Baseline Version:** `v0.3.0` (`2026-11-07` — Introducing `Foundation.UI v0.2.0` / `@foundation/ui v0.2.0` Canonical ShadowLibrary Strict Standard Docked Terminal Drawer across React 19 + Vanilla ES Module `foundation-terminal.js` + `foundation-terminal.css`, `sync_foundation_sdk.py` Auto-Sync Pipeline, `Foundation.Launcher.dll v0.1.1`, and `v0.1.0` of `Foundation.Retro.dll`, `Foundation.Sidecars.dll`, `Foundation.Server.dll`, and `foundation_core`; Full SemVer `MAJOR.MINOR.PATCH` per [Standard 03 §1](file:///<PROJECTS_ROOT>/_DevBible/03_VERSIONING_AND_RELEASES.md))
- **Next In-Dev Target:** `v0.4.0` (Pinning `Foundation v0.3.0` / `@foundation/ui v0.2.0` / `foundation-terminal.js` across `Housekeeping`, `uStation`, `Mother`, `Greenroom`, `Redroom`, and `StreamEngine` via `sync_foundation_sdk.py --sync-consumer`) -> `v1.0.0` GA
- **Completed `v0.3.0` (`Foundation.UI v0.2.0` / `@foundation/ui v0.2.0`) Deliverables (`2026-11-07` — [`v0.3.0.2026-11-07.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/plans/v0.3.0.2026-11-07.md)):**
  - [x] **T1 — Canonical Terminal Drawer Core & React 19 Components (`ShadowLibrary Strict Standard` — Standard 09 §4.5):** Added [`terminalDrawerCore.ts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Web/components/terminalDrawerCore.ts) (`163 LOC`) and upgraded [`TerminalToolbarAndFilters.tsx`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Web/components/TerminalToolbarAndFilters.tsx) (`263 LOC`) and [`GlobalTerminalDrawer.tsx`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Web/components/GlobalTerminalDrawer.tsx) (`292 LOC`) to the full docked resizable ShadowLibrary standard (`S`/`M`/`L` presets, 6px pointer-capture resize handle, `ResizeObserver` bottom latch, `Verbosity`/`History`/`Search`/`Wrap`/`Auto-scroll` filters, replaceable progress-line coalescing, and `ConfirmActionModal` dual job cancellation).
  - [x] **T2 — Non-Breaking Barrel Export (`@foundation/ui v0.2.0` — Standard 17 §1.6):** Updated [`Web/index.ts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Web/index.ts) (`52 LOC`) to export `GlobalTerminalDrawer`, `GlobalTerminalDrawerProps`, `TerminalToolbarAndFilters`, `TerminalToolbarAndFiltersProps`, and all `terminalDrawerCore.ts` helpers/types while preserving backward-compatible `filterTerminalLines` and `TerminalLogFilter` exports.
  - [x] **T3 — Dual-Target WebAssets (`foundation-terminal.css` & `foundation-terminal.js` Vanilla ES Module):** Authored [`foundation-terminal.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.css) (`112 LOC`) and [`foundation-terminal.js`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.js) (`306 LOC`) exporting `mountFoundationTerminalDrawer(options)` + `window.FoundationTerminalDrawer` with `⚙ Flt` mobile filter label, preset height recomputation, `window` `resize`/`keydown` (`Ctrl+\``) listeners cleaned up in `destroy()`, and `@media (max-width: 820px), (max-height: 480px)` `overflow: hidden` + `min-height: 44px` landscape fit.
  - [x] **T4 — Auto-Sync SDK Pipeline (`sync_foundation_sdk.py` + MSBuild Post-Build Target):** Authored [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py) (`199 LOC`) and wired MSBuild `SyncFoundationWebAndSharedLibs` in [`Foundation.UI.csproj`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Foundation.UI.csproj) (`Version` `0.2.0`) publishing complete SDK snapshots to `<PROJECTS_ROOT>\SharedLibs\Foundation\v0.3.0\` and `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\` plus `--sync-consumer <ProjectRoot>`.
  - [x] **T5 — Comprehensive Unit Test Suite (`FoundationTerminalDrawerTests.cs`) & Documentation Sync:** Added [`FoundationTerminalDrawerTests.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/tests/Foundation.Tests/FoundationTerminalDrawerTests.cs) (`201 LOC`, `17/17` total passing tests in `Foundation.Tests`, `0` warnings) and synchronized the 4-Document Manual Shelf.
- **Completed `v0.2.1` Deliverables (`2026-11-07`):**
  - [x] **T1 — `Foundation.Launcher.dll` (`v0.1.1`):** `RootLauncherHost.ResolveTargetExecutable` clean-root `app\<exe>` auto-resolution and fail-closed self-spawn guard against `Environment.ProcessPath` (`16/16` passing tests in `Foundation.Tests`).
- **Completed `v0.2.0` (`v0.1.0` New Shared Libraries) Deliverables (`2026-11-07`):**
  - [x] **T1 — `Foundation.Retro.dll` (`v0.1.0`):** `ConsoleSystemCatalog` (64 platforms, 50+ BIOS definitions, copier header rules, provenance tiers), `NoIntroRedumpDatParser`, `DatTitleNormalizer`, `HeaderlessRomStripper`, and `SoftPatchEngine` (`IPS`/`BPS1`/`UPS1`).
  - [x] **T2 — `Foundation.Sidecars.dll` (`v0.1.0`):** `SinglePassStreamHasher`, `SevenZipAndZipInspector`, `HashSidecarService` (`.hash` v1 + wrapper-path normalizer + `COMPOSITE_SHA1` + archive `CRC32` index validator), and `HkLockSentinel` / `SisterAppCoexistenceClient`.
  - [x] **T3 — `Foundation.Server.dll` (`v0.1.0`):** `TieredAuthEngine`, `FoundationTieredAuthMiddleware`, and `SisterAppStandardEndpoints` (`/suite.manifest.json`, `/api/v1/suite/manifest`).
  - [x] **T4 — `Foundation.Launcher.dll` (`v0.1.0`), `Foundation.UI.Desktop` (`v0.1.0`) & `Foundation.Core.Cli`:** `RootLauncherHost`, `StaNativeDialogBridgeBase`, `DisposableWebViewHostForm`, `TrayIconBadgeHelper`, `CliSchema`, `BoundedTailStream`, and `CappedSharedLogSink`.
  - [x] **T5 — `Foundation.Installer.dll` (`v0.2.0`):** Fast `<1.5s` `NoCompression` `CreatePreUpgradeBackupAsync` (`_Quarantine\`/`drops\`/`webview2_cache\` exclusion).
  - [x] **T6 — `@foundation/ui` (`v0.1.0`):** 10 shared React 19 TypeScript components (`StyledTooltip`, `ModalDialog`, `ResponsiveTable`, `UniversalDataTable`, `SchemaSettingsRenderer`, `HybridPathPicker`, `CoexistenceWarningBanner`, `TerminalToolbarAndFilters`, `GlobalTerminalDrawer`, `SidebarNavRail`).
  - [x] **T7 — `foundation_core` (`v0.1.0` Modular Parity) & `Dev\tools\`:** Multi-module Python 3.12+ SDK (`settings.py`, `logging.py`, `crash_dumper.py`, `locks.py`, `db.py`, `cli.py`) + `verify_sandbox_universal.py`, `build_release_universal.py`, and `notion_task_helper.py` (`14/14` passing tests in `Foundation.Tests`, pinned in `ShadowLibrary v1.0.0-rc.20`).
- **Completed `v0.1.2` Deliverables (`2026-11-05`):**
  - [x] Configure `PRAGMA cache_size = -65536;` (`64 MB` page cache) in `SqliteWalPool.ApplyStandardPragmas` and `foundation_core.open_wal_sqlite` to eliminate random 4KB B-tree page thrashing during multi-index batch transactions (`10/10` tests passing).
- **Completed `v0.1.1` Deliverables (`2026-11-04`):**
  - [x] Switch `SqliteWalPool` from `SqliteCacheMode.Shared` to `SqliteCacheMode.Default` (`busyTimeoutMs = 15000`) to restore lock-free WAL MVCC reader/writer snapshot isolation and prevent `SQLITE_LOCKED` (Error 6) table lock collisions (`10/10` tests passing).
- **Completed `v0.1.0` Deliverables (`2026-11-04`):**
  - [x] Scaffold Tier A 6-folder layout (`<PROJECTS_ROOT>\Foundation\`) and shared distribution root (`<PROJECTS_ROOT>\SharedLibs\Foundation\v0.1.0\`).
  - [x] Author core customization and modularity contracts (`IFoundationModule.cs`, `InstallerThemeOptions.cs`, `ShellBrandingOptions.cs`) with 4-line `@module` headers.
  - [x] Implement and verify `Foundation.Core.dll`, `Foundation.Installer.dll`, `Foundation.UI.dll`, and `Foundation.Tests` (`9/9` passing tests, `0` warnings), pinned into `ShadowLibrary v1.0.0-rc.14` and `Housekeeping v1.0.0-rc.12`.

---

## 3. Upcoming Milestones: Building Toward `v1.0.0` GA

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│ v0.1.0 – v0.2.1 (SHIPPED): .NET 8 Shared Core, Retro, Sidecars, Server,         │
│                            Launcher v0.1.1, Installer v0.2.0 & foundation_core  │
└───────────────────────────────────────┬─────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│ v0.3.0 (SHIPPED): @foundation/ui v0.2.0 Canonical Terminal Drawer, Vanilla ESM  │
│                   (foundation-terminal.js/css) & sync_foundation_sdk.py         │
└───────────────────────────────────────┬─────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│ v0.4.0: Sister-App Fleet Migration (Housekeeping, uStation, Mother, Greenroom)  │
└───────────────────────────────────────┬─────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│ 🌟 v1.0.0 SUMMIT: Full Ecosystem Going Gold Across All 7 Sister Apps            │
└─────────────────────────────────────────────────────────────────────────────────┘
```

### `v0.4.0` — Sister-App Fleet Migration (`Housekeeping`, `uStation`, `Mother`, `Greenroom`, `Redroom`, `StreamEngine`)
- **Cornerstone Goal:** Sync `Foundation v0.3.0` (`@foundation/ui v0.2.0`, `foundation-terminal.js` + `foundation-terminal.css`, `Foundation.Retro.dll v0.1.0`, `Foundation.Sidecars.dll v0.1.0`, `Foundation.Server.dll v0.1.0`, `Foundation.Launcher.dll v0.1.1`, and `foundation_core v0.3.0`) across the remaining 6 sister applications via `python sync_foundation_sdk.py --sync-consumer <ProjectRoot>`.
- **Planned Deliverables:**
  - [ ] Sync `Foundation.Sidecars.dll`, `Foundation.Server.dll`, `Foundation.Launcher.dll`, `Foundation.Installer.dll`, and `@foundation/ui v0.2.0` (`GlobalTerminalDrawer`) into `Housekeeping`, `uStation`, and `Mother`.
  - [ ] Sync `foundation_core` and `foundation-terminal.js` + `foundation-terminal.css` into `Greenroom`, `Redroom`, and `StreamEngine`.

### `v1.0.0` (Going Gold — Full Release)
- **Cornerstone Goal:** Verified across all Sister Apps (`Mother`, `Housekeeping`, `ShadowLibrary`, `Redroom`, `Greenroom`, `StreamEngine`, `uStation`) prior to their `v1.0.0` Going Gold releases.

---

## 4. Completed Milestones

| Version | Date | Headline Achievement | Plan Reference |
| :--- | :--- | :--- | :--- |
| **`v0.3.0`** | `2026-11-07` | `Foundation.UI v0.2.0` / `@foundation/ui v0.2.0` Canonical ShadowLibrary Strict Standard Docked Terminal Drawer (`terminalDrawerCore.ts`, `TerminalToolbarAndFilters.tsx`, `GlobalTerminalDrawer.tsx`), Dual-Target Vanilla ES Module (`foundation-terminal.js` + `foundation-terminal.css` with `⚙ Flt` mobile toggle, `resize`/`keydown` listeners & `844x390` landscape fit), and `sync_foundation_sdk.py` auto-sync publisher/consumer pipeline (`17/17` passing tests in `Foundation.Tests`), published to `<PROJECTS_ROOT>\SharedLibs\Foundation\v0.3.0\` & `Latest\`. | [`v0.3.0.2026-11-07.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/plans/v0.3.0.2026-11-07.md) |
| **`v0.2.1`** | `2026-11-07` | `Foundation.Launcher.dll` (`v0.1.1`) `RootLauncherHost.ResolveTargetExecutable` clean-root `app\<exe>` auto-resolution & fail-closed self-spawn guard against `Environment.ProcessPath` (`16/16` passing tests), pinned in `ShadowLibrary v1.0.0-rc.20-hf.1` & `v1.0.0-rc.20-hf.2`. | [`ShadowLibrary v1.0.0-rc.20-hf.1.2026-11-07.md`](file:///<PROJECTS_ROOT>/ShadowLibrary/Dev/docs/plans/v1.0.0-rc.20-hf.1.2026-11-07.md) |
| **`v0.2.0`** | `2026-11-07` | Full Ecosystem Decoupling: `Foundation.Retro.dll` (`v0.1.0`), `Foundation.Sidecars.dll` (`v0.1.0`), `Foundation.Server.dll` (`v0.1.0`), `Foundation.Launcher.dll` (`v0.1.0`), `Foundation.UI.Desktop` (`v0.1.0`), `@foundation/ui` (`v0.1.0` — 10 React 19 components), `Foundation.Installer.dll` (`v0.2.0` `<1.5s` pre-upgrade backup), and Python `foundation_core` (`v0.1.0` modular parity) + `Dev\tools\` (`14/14` passing tests), published to `<PROJECTS_ROOT>\SharedLibs\Foundation\v0.1.0\` & `v0.2.0\` and pinned in `ShadowLibrary v1.0.0-rc.20`. | [`ShadowLibrary v1.0.0-rc.20.2026-11-07.md`](file:///<PROJECTS_ROOT>/ShadowLibrary/Dev/docs/plans/v1.0.0-rc.20.2026-11-07.md) |
| **`v0.1.2`** | `2026-11-05` | `SqliteWalPool` & `foundation_core.open_wal_sqlite` configured with `PRAGMA cache_size = -65536` (`64 MB` page cache) eliminating multi-index B-tree thrashing (`10/10` passing tests), published to `<PROJECTS_ROOT>\SharedLibs\Foundation\v0.1.2\` and pinned in `ShadowLibrary v1.0.0-rc.15-hf.1`. | [`ShadowLibrary v1.0.0-rc.15-hf.1.2026-11-05.md`](file:///<PROJECTS_ROOT>/ShadowLibrary/Dev/docs/plans/v1.0.0-rc.15-hf.1.2026-11-05.md) |
| **`v0.1.1`** | `2026-11-04` | `SqliteWalPool` switched to `SqliteCacheMode.Default` (`busyTimeoutMs = 15000`) for lock-free WAL MVCC concurrent reader/writer isolation (`10/10` passing tests), published to `<PROJECTS_ROOT>\SharedLibs\Foundation\v0.1.1\` and pinned in `ShadowLibrary v1.0.0-rc.15`. | [`ShadowLibrary v1.0.0-rc.15.2026-11-04.md`](file:///<PROJECTS_ROOT>/ShadowLibrary/Dev/docs/plans/v1.0.0-rc.15.2026-11-04.md) |
| **`v0.1.0`** | `2026-11-04` | Full `.NET 8` Shared SDK (`Foundation.Core.dll`, `Foundation.Installer.dll`, `Foundation.UI.dll`, `Foundation.Tests` `9/9` passing), published to `<PROJECTS_ROOT>\SharedLibs\Foundation\v0.1.0\` and pinned in `ShadowLibrary v1.0.0-rc.14` & `Housekeeping v1.0.0-rc.12`. | [`ShadowLibrary v1.0.0-rc.14.2026-11-04.md`](file:///<PROJECTS_ROOT>/ShadowLibrary/Dev/docs/plans/v1.0.0-rc.14.2026-11-04.md) |

---

## 5. Icebox & Cutting-Room Floor
- **NuGet / PyPI Local Server Daemon:** Optional local feed server; currently unnecessary because direct file pinning from `<PROJECTS_ROOT>\SharedLibs\Foundation\v<Version>\` into `Dev\lib\` requires zero background server overhead.
