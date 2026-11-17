# Foundation (`v0.5.0`)

**Shared SDK & Zero-Duplication Infrastructure Core** (`Foundation.Core v0.5.0`, `Foundation.Retro v0.5.0`, `Foundation.Sidecars v0.5.0`, `Foundation.Server v0.5.0`, `Foundation.Launcher v0.5.0`, `Foundation.UI v0.5.0` + `@foundation/ui v0.5.0`, `Foundation.Installer v0.5.0`, `Foundation.Automation v0.5.0`, Python `foundation_core v0.5.0`, and `Dev\tools\*.py`).

---

## 1. Agent Quick-Routing Matrix ("Find What You Need Without Digging")

| Task / Question | Go Directly Here | What You Get in 1 Read |
|---|---|---|
| Looking up a C# class, method, React prop, Python helper, CLI flag, or JSON schema | [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md) | 1-Stop API Dictionary + `@foundation/ui` barrel vs submodule import warning |
| Copy-paste integration recipes (Markup Installer, `Foundation.Automation`, Web UI, Python, C# Core, Stage 2 Sandbox) | [`Dev\docs\USER_GUIDE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/USER_GUIDE.md) | Minimal working quickstart snippets for all 9 subprojects + Stage 2 sandbox |
| Locating a file by path/SLOC (`250/400/600` code-only SLOC), assembly graph, SQLite pragmas, or ADRs | [`Dev\docs\ARCHITECTURE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/ARCHITECTURE.md) | Complete module registry, 4-drive sandbox pipelines, SLOC inventory & ADR notes |
| Building, running `Foundation.Tests`, publishing `SharedLibs`, calling `Dev\tools\*.py`, or pre-public git-filter-repo checks | [`Dev\docs\OPERATIONS.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/OPERATIONS.md) | Exact build/sync/release commands, 4-drive sandbox CLIs, installer exit codes & pre-public checklist |
| Security invariants & vulnerability disclosure | [`SECURITY.md`](file:///<PROJECTS_ROOT>/Foundation/SECURITY.md) | DPAPI, loopback bearer/`Host`/`Origin` enforcement, `ExitCode = 3` containment & disclosure rules |
| Checking implemented capabilities vs remaining upstream backlog | [`FEATURES.md`](file:///<PROJECTS_ROOT>/Foundation/FEATURES.md) & [`ROADMAP.md`](file:///<PROJECTS_ROOT>/Foundation/ROADMAP.md) | Verified module inventory + milestone roadmap |

> **Workspace vs. Pinned Binary Hub:** Consumer projects (`Housekeeping`, `ShadowLibrary`, `reStation`, `KingsFieldRecomp`, `MediaSync`) treat both `<PROJECTS_ROOT>\Foundation\` and `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\` (published via [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py)) as **strictly read-only**. Log consumer SDK gaps in [`Dev\docs\reports\`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/).

---

## 2. Subproject Inventory & Architecture Overview (`v0.5.0`)

- **C# Core & Runtime (`Dev\src\Foundation.Core\` — `24` files, `net8.0`):** [`AppEnvironment`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AppEnvironment.cs) (`--data-dir`, `--config`, `--log-dir`, `--port`, `--dry-run`, `--tray-only`, `--managed-by-mother`, `--suite-tab`, `--window`/`--browser`, `UiPresentationMode.TrayOrBackground`, `Sandbox`, `AssertPathContained`, `NormalizeLongPath`), [`KnownFolderPaths`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/KnownFolderPaths.cs) (`GetFolderPath`, `GetHostFolderPath`, `ResolveLocalProgramsDirectory`, `MapHostPathToSandbox`), [`SandboxEnvironmentSeam`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironmentSeam.cs) (`FOUNDATION_SANDBOX_ROOT`, `SANDBOX_*_DRIVE`, `APP_ISOLATED_TEST=1` precedence, `ExitCode = 3` containment guards), [`IPlatformRegistry` / `WindowsPlatformRegistry` / `JsonHivePlatformRegistry`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JsonHivePlatformRegistry.cs), [`SandboxDriveController`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxDriveController.cs), [`HighResFramePacer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HighResFramePacer.cs), [`AudioRingBuffer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AudioRingBuffer.cs), [`WavCaptureWriter`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/WavCaptureWriter.cs), [`RgbaPngWriter`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/RgbaPngWriter.cs), [`PixelOverlayCanvas`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PixelOverlayCanvas.cs), [`CliArgumentHelper` / `BoundedTailStream` / `CappedSharedLogSink`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/CliAndProcessPrimitives.cs), [`GranularSettingsManager`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/GranularSettingsManager.cs), [`DpapiSecretStore`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/DpapiSecretStore.cs), [`SqliteWalPool`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SqliteWalPool.cs), [`PidSafeLockfile`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PidSafeLockfile.cs), [`HeartbeatLogger`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HeartbeatLogger.cs), [`CrashBundleService`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/CrashBundleService.cs), [`DualJobCancellationCoordinator` / `JobObjectGuard`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JobObjectGuard.cs), [`SafeNetworkTransfer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SafeNetworkTransfer.cs), [`ToolResolver`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/ToolResolver.cs), [`HybridModuleLoader` / `IFoundationModule`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HybridModuleLoader.cs).
- **Retro & Sidecar Engines (`Foundation.Retro v0.5.0` [`8` files] + `Foundation.Sidecars v0.5.0` [`5` files]):** 41-platform catalog (`IsVerifiedReferenceHash`), No-Intro/Redump/RA/CMP DAT parser, filename normalizer, headerless stripper + 20-magic sniffer, in-memory IPS/BPS/UPS patcher, BYO `.iso`/`.bin`+`.cue`/`.chd`/`.7z`/`.zip` [`MediaContainerExtractor`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/MediaContainerExtractor.cs), static recomp [`RecompContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompContracts.cs) + [`RecompTierAndBackendContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompTierAndBackendContracts.cs), single-pass `ArrayPool<byte>` stream hasher, ZIP/7z `-slt` + LZMA/Deflate64 `7z.exe` fallback + nested `::` inspection + TorrentZip builder, `.hash` v1 sidecars, and `.hklock` / `.coexistence.json` v2 coordination.
- **Server, Launcher, UI, Installer & Automation (`Foundation.Server v0.5.0` [`3` files, `net8.0`], `Launcher v0.5.0` [`1` file], `UI v0.5.0` [`SdkVersion = "0.5.0"`], `Installer v0.5.0` [`19` files], `Automation v0.5.0` [`4` files]):**
  - Tiered Kestrel localhost/Tailscale/PBKDF2/HMAC auth, `/api/v1/suite/manifest`, `RootLauncherHost` (`AttachConsole(-1)`, `--wait`/`--open` blocking, relative arg rebase, Ctrl+C forwarding).
  - `AppEnvironment`-aware `DisposableWebViewHostForm` + STA dialogs + tray badges, single-source `Web\` React components + Vanilla terminal drawer.
  - Tier 1 `StandardInstallerHost` + declarative [`InstallerManifestStore`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerManifestStore.cs) (`installer.manifest.json` Standard 40 `schemaVersion: 2`, `single-line` vs `multi-line`, `lightweight`/`full`/`custom`, 2-line tooltips, theme tokens, `logoPath`/`iconPath`) + smart `HKCU\Software\StreamItAll\LastInstallBaseDir` fresh-install suggestion (post-success persistence, `APP_ISOLATED_TEST=1`/temp/reparse guarded).
  - Opt-in [`Foundation.Automation`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Automation/AutomationLoopbackServer.cs) (`--automation-port`, `127.0.0.1` HTTP/JSON loopback with `Bearer` `401` + `Host`/`Origin` `403` validation, focus-free window-scoped virtual pointer/keyboard & `inspectTree`, offscreen WinForms/WebView2/`IRenderSurfaceSnapshotProvider` PNG snapshots via `RgbaPngWriter`).
- **Python & Universal Tooling (`foundation_core v0.5.0` [`10` files] + `Dev\tools\` [`5` files]):** Pure-stdlib Python `foundation_core` (`0.5.0` in [`Dev\src\foundation_core\`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/)) with `sandbox_seam.py` and `pseudo_registry.py` 1:1 C# Stage 2 & hive parity, plus `5` universal scripts in [`Dev\tools\`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/).

---

## 3. Public Quickstart & Build Verification

### Prerequisites
- **.NET 8 SDK** (`net8.0` / `net8.0-windows` on Windows for WinForms/Installer/Automation)
- **Python 3.12+** (`pytest` optional; pure-stdlib `unittest` works with zero external packages)

```powershell
# 1. Clone and run full C# test suite (0 warnings)
git clone <repo-url> Foundation
dotnet test Foundation\Dev\Foundation.sln -c Release -p:TreatWarningsAsErrors=true

# 2. Run Python foundation_core & tools test suite (unittest & pytest)
python -m unittest discover -s Foundation\Dev\tests\foundation_core -v
pytest Foundation\Dev\tests\foundation_core -q

# 3. Publish compiled SDK (DLLs, @foundation/ui, foundation_core, tools) into SharedLibs\Foundation\v0.5.0 & Latest
python Foundation\Dev\tools\sync_foundation_sdk.py --publish --version 0.5.0 --ui-version 0.5.0 --configuration Release

# 4. Sync pinned SDK into a consumer project
python Foundation\Dev\tools\sync_foundation_sdk.py --sync-consumer <PROJECTS_ROOT>\<Project>

# 5. Initialize 4-drive sandbox and run 12-point Stage 2 E2E verification (S2-01..S2-12)
python Foundation\Dev\tools\sandbox_ctl.py init
python Foundation\Dev\tools\run_sandbox_e2e.py --app-id myapp --version 0.1.0 --installer <Setup.exe>
```

> ⚠️ **Post-Build Side Effect Note:** Building `Foundation.UI.csproj` with `-p:PublishFoundationSdk=true` updates `WebAssets\` and `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\`.

---

## 4. Governance & Lifecycle Status

- **Lifecycle State:** [`active.state`](file:///<PROJECTS_ROOT>/Foundation/active.state) (`v0.5.0`)
- **Project Anchor & Roadmap:** [`AGENTS.md`](file:///<PROJECTS_ROOT>/Foundation/AGENTS.md) · [`FEATURES.md`](file:///<PROJECTS_ROOT>/Foundation/FEATURES.md) · [`ROADMAP.md`](file:///<PROJECTS_ROOT>/Foundation/ROADMAP.md) · [`CHANGELOG.md`](file:///<PROJECTS_ROOT>/Foundation/CHANGELOG.md)
- **Dev Bible Compliance:** Strictly adheres to [`00_GLOSSARY.md`](file:///<PROJECTS_ROOT>/_DevBible/00_GLOSSARY.md) (`250/400/600` code-only SLOC across `Dev\` sources; Standard 18 & Standard 40 compliant).

---

## 5. License & Legal

- **License:** Distributed under the [MIT License](file:///<PROJECTS_ROOT>/Foundation/LICENSE).
- **Security Policy:** See [`SECURITY.md`](file:///<PROJECTS_ROOT>/Foundation/SECURITY.md).
- **Clean-Room & Zero-PII Guarantee:** Contains zero copyrighted ROMs, BIOS dumps, proprietary keys, or workstation-specific credentials/paths.

---

## 6. 📘 Manuals

- [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md) — **1-Stop Agent API Dictionary** (all C# classes/signatures including `Foundation.Automation` & `InstallerManifestStore`, `@foundation/ui` props, Vanilla JS/CSS, `foundation_core`, `Dev\tools\*.py` CLIs, JSON schemas)
- [`Dev\docs\USER_GUIDE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/USER_GUIDE.md) — Consumer Integration Recipes, Declarative Markup Installer, `Foundation.Automation` Loopback & Stage 2 4-Drive Sandbox Quickstart
- [`Dev\docs\ARCHITECTURE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/ARCHITECTURE.md) — System Topology, Runtime Pipelines, Complete Module Registry, SQLite Pragmas, and ADR Notes
- [`Dev\docs\OPERATIONS.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/OPERATIONS.md) — Build, Hub Sync, 4-Drive Sandbox CLIs, Installer Flags, Exit Codes, Pre-Public History Scrub & Public-Repo Gate
