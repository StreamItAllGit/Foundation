# Foundation (`v0.4.2`)

**Shared SDK & Zero-Duplication Infrastructure Core** (`Foundation.Core v0.4.2`, `Foundation.Retro v0.4.2`, `Foundation.Sidecars v0.4.2`, `Foundation.Server v0.4.2`, `Foundation.Launcher v0.4.2`, `Foundation.UI v0.4.2` + `@foundation/ui v0.4.2`, `Foundation.Installer v0.4.2`, Python `foundation_core v0.4.2`, and `Dev\tools\*.py`).

---

## 1. Agent Quick-Routing Matrix ("Find What You Need Without Digging")

| Task / Question | Go Directly Here | What You Get in 1 Read |
|---|---|---|
| Looking up a C# class, method, React prop, Python helper, CLI flag, or JSON schema | [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md) | 1-Stop API Dictionary + `@foundation/ui` barrel vs submodule import warning |
| Copy-paste integration recipes (Tier 1 Installer, Web UI, Python, C# Core, Stage 2 Sandbox) | [`Dev\docs\USER_GUIDE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/USER_GUIDE.md) | Minimal working quickstart snippets for all 8 subprojects + Stage 2 sandbox |
| Locating a file by path/SLOC (`250/400/600` code-only SLOC), assembly graph, SQLite pragmas, or ADRs | [`Dev\docs\ARCHITECTURE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/ARCHITECTURE.md) | Complete module registry, 4-drive sandbox pipelines, SLOC inventory & ADR notes |
| Building, running `Foundation.Tests`, publishing `SharedLibs`, or calling `Dev\tools\*.py` | [`Dev\docs\OPERATIONS.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/OPERATIONS.md) | Exact build/sync/release commands, 4-drive sandbox CLIs & installer exit codes |
| Checking implemented capabilities vs remaining upstream backlog | [`FEATURES.md`](file:///<PROJECTS_ROOT>/Foundation/FEATURES.md) & [`ROADMAP.md`](file:///<PROJECTS_ROOT>/Foundation/ROADMAP.md) | Verified module inventory + milestone roadmap |

> **Workspace vs. Pinned Binary Hub:** Consumer projects (`Housekeeping`, `ShadowLibrary`, `reStation`, `KingsFieldRecomp`, `MediaSync`) treat both `<PROJECTS_ROOT>\Foundation\` and `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\` (published via [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py)) as **strictly read-only**. Log consumer SDK gaps in [`Dev\docs\reports\`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/).

---

## 2. Subproject Inventory (`v0.4.2`)

- **C# Core & Runtime (`Dev\src\Foundation.Core\` — `24` files):** [`AppEnvironment`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AppEnvironment.cs) (`--data-dir`, `--config`, `--log-dir`, `--port`, `--dry-run`, `--tray-only`, `--managed-by-mother`, `--suite-tab`, `--window`/`--browser`, `UiPresentationMode.TrayOrBackground`, `Sandbox`, `AssertPathContained`, `NormalizeLongPath`), [`KnownFolderPaths`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/KnownFolderPaths.cs) (`GetFolderPath`, `GetHostFolderPath` via Win32 `SHGetKnownFolderPath` + `APPDATA` unmapping, `ResolveLocalProgramsDirectory`, `MapHostPathToSandbox`), [`SandboxEnvironmentSeam`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironmentSeam.cs) (`FOUNDATION_SANDBOX_ROOT`, `SANDBOX_*_DRIVE`, `APP_ISOLATED_TEST=1` precedence, `ExitCode = 3` containment guards), [`IPlatformRegistry` / `WindowsPlatformRegistry` / `JsonHivePlatformRegistry`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JsonHivePlatformRegistry.cs) (`PlatformRegistryFactory`, 1-file atomic `.tmp -> .bak` JSON pseudo-registry with `DWORD`/`QWORD`/`String`/`ExpandString`/`MultiString`/`Binary`, Windows `Reg*` 1223 semantics, PID-safe `.lock`), [`SandboxDriveController`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxDriveController.cs) (opt-in `.secrets\.env` / `Dev\.env` + `subst` resolver), [`HighResFramePacer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HighResFramePacer.cs), [`AudioRingBuffer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AudioRingBuffer.cs), [`WavCaptureWriter`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/WavCaptureWriter.cs), [`RgbaPngWriter`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/RgbaPngWriter.cs), [`PixelOverlayCanvas`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PixelOverlayCanvas.cs), [`CliArgumentHelper` / `BoundedTailStream` / `CappedSharedLogSink`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/CliAndProcessPrimitives.cs), [`GranularSettingsManager`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/GranularSettingsManager.cs), [`DpapiSecretStore`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/DpapiSecretStore.cs), [`SqliteWalPool`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SqliteWalPool.cs), [`PidSafeLockfile`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PidSafeLockfile.cs), [`HeartbeatLogger`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HeartbeatLogger.cs), [`CrashBundleService`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/CrashBundleService.cs), [`DualJobCancellationCoordinator` / `JobObjectGuard`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JobObjectGuard.cs), [`SafeNetworkTransfer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SafeNetworkTransfer.cs), [`ToolResolver`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/ToolResolver.cs), [`HybridModuleLoader` / `IFoundationModule`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HybridModuleLoader.cs).
- **Retro & Sidecar Engines (`Foundation.Retro v0.4.2` [`8` files] + `Foundation.Sidecars v0.4.2` [`5` files]):** 41-platform catalog (`IsVerifiedReferenceHash`), No-Intro/Redump/RA/CMP DAT parser, filename normalizer, headerless stripper + 20-magic sniffer, in-memory IPS/BPS/UPS patcher, BYO `.iso`/`.bin`+`.cue`/`.chd`/`.7z`/`.zip` [`MediaContainerExtractor`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/MediaContainerExtractor.cs), static recomp [`RecompContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompContracts.cs) + [`RecompTierAndBackendContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompTierAndBackendContracts.cs) (AOT-primary `<dataDir>\cache\`, JIT/IR bring-up, `.decomp` manifest), single-pass `ArrayPool<byte>` stream hasher, ZIP/7z `-slt` + LZMA/Deflate64 `7z.exe` fallback + nested `::` inspection + TorrentZip builder, `.hash` v1 sidecars, and `.hklock` / `.coexistence.json` v2 coordination.
- **Server, Launcher, UI & Installer (`Foundation.Server v0.4.2` [`3` files, `net8.0`], `Launcher v0.4.2` [`1` file], `UI v0.4.2` [`SdkVersion = "0.4.2"`], `Installer v0.4.2` [`17` files]):** Tiered Kestrel localhost/Tailscale/PBKDF2/HMAC auth, `/api/v1/suite/manifest`, `RootLauncherHost` (`AttachConsole(-1)`, `--wait`/`--open` blocking, relative arg rebase, Ctrl+C forwarding), `AppEnvironment`-aware `DisposableWebViewHostForm` + STA dialogs + tray badges, single-source `Web\` React components + Vanilla terminal drawer, and Tier 1 `StandardInstallerHost` (3-mode isolation `ShouldSuppressHostMutations` / `ShouldSuppressLiveHostMutations`, `KnownFolderPaths` shortcuts, `IPlatformRegistry` ARP uninstall metadata, `FileLockRetryHelper` allowing `<SandboxDDrive>\Programs` under `IsSandboxActive`, `WindowsShortcutWriter.SanitizeShortcutFileName` `:` -> ` -`, DPI-safe `_lblTitle`/`_lblSub` clamping preserving `650x460`/`650x570` `ClientSize`, `FOUNDATION_SUPPRESS_MODALS=1`, `Lightweight`/`Full` profiles, `SecondaryPathPicker`, `AllowAutoStart`, composite `CaptureWizardPreviewBitmap()`, bounded retry self-delete trampoline).
- **Python & Universal Tooling (`foundation_core v0.4.2` [`10` files] + `Dev\tools\` [`5` files]):** Pure-stdlib Python `foundation_core` (`0.4.2` in [`Dev\src\foundation_core\`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/)) adding `sandbox_seam.py` and `pseudo_registry.py` with 1:1 C# Stage 2 & hive parity, plus `5` universal scripts in [`Dev\tools\`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/) ([`sandbox_ctl.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sandbox_ctl.py), [`run_sandbox_e2e.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/run_sandbox_e2e.py) 12-point `S2-01`..`S2-12` runner, [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py) publishing `54` SDK files, [`build_release_universal.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/build_release_universal.py) `--allow-sdk-zip` strict 6-file validation, [`notion_task_helper.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/notion_task_helper.py)).

---

## 3. Quick Start & Build Verification

```powershell
# Run full C# unit test suite (40/40 passed across 7 files, 0 warnings)
dotnet test <PROJECTS_ROOT>\Foundation\Dev\Foundation.sln -c Release -p:TreatWarningsAsErrors=true

# Run Python foundation_core & tools test suite (8/8 passed)
C:\Python313\python.exe -m unittest discover -s <PROJECTS_ROOT>\Foundation\Dev\tests\foundation_core -v
pytest <PROJECTS_ROOT>\Foundation\Dev\tests\foundation_core -q

# Publish 54 SDK files (DLLs, @foundation/ui, foundation_core, tools) into SharedLibs\Foundation\v0.4.2 & Latest
python <PROJECTS_ROOT>\Foundation\Dev\tools\sync_foundation_sdk.py --publish --version 0.4.2 --ui-version 0.4.2 --configuration Release

# Sync pinned SDK into a consumer project (preserving sandbox_seam.py, sandbox_ctl.py, run_sandbox_e2e.py)
python <PROJECTS_ROOT>\Foundation\Dev\tools\sync_foundation_sdk.py --sync-consumer <PROJECTS_ROOT>\<Project>

# Initialize 4-drive sandbox and run 12-point Stage 2 E2E verification (S2-01..S2-12)
python <PROJECTS_ROOT>\Foundation\Dev\tools\sandbox_ctl.py init
python <PROJECTS_ROOT>\Foundation\Dev\tools\run_sandbox_e2e.py --app-id myapp --version 0.1.0 --installer <Setup.exe>
```

> ⚠️ **Post-Build Side Effect Note:** Building `Foundation.UI.csproj` with `-p:PublishFoundationSdk=true` updates `WebAssets\` and `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\`.

---

## 4. Governance & Lifecycle Status

- **Lifecycle State:** [`active.state`](file:///<PROJECTS_ROOT>/Foundation/active.state) (`v0.4.2`)
- **Project Anchor & Roadmap:** [`AGENTS.md`](file:///<PROJECTS_ROOT>/Foundation/AGENTS.md) · [`FEATURES.md`](file:///<PROJECTS_ROOT>/Foundation/FEATURES.md) · [`ROADMAP.md`](file:///<PROJECTS_ROOT>/Foundation/ROADMAP.md) · [`CHANGELOG.md`](file:///<PROJECTS_ROOT>/Foundation/CHANGELOG.md)
- **Dev Bible Compliance:** Strictly adheres to [`00_GLOSSARY.md`](file:///<PROJECTS_ROOT>/_DevBible/00_GLOSSARY.md) (v1.6.0; `250/400/600` code-only SLOC across `Dev\` sources; 14 sandbox/registry columns registered in Standard 18).

---

## 5. License & Legal

- **License:** Distributed under the [MIT License](file:///<PROJECTS_ROOT>/Foundation/LICENSE).
- **Clean-Room & Zero-PII Guarantee:** Contains zero copyrighted ROMs, BIOS dumps, proprietary keys, or workstation-specific credentials/paths.

---

## 6. 📘 Manuals

- [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md) — **1-Stop Agent API Dictionary** (all C# classes/signatures, `@foundation/ui` props + submodule import rules, Vanilla JS/CSS, `foundation_core`, `Dev\tools\*.py` CLIs, JSON schemas)
- [`Dev\docs\USER_GUIDE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/USER_GUIDE.md) — Consumer Integration Recipes, Tier 1 `StandardInstallerHost` & Stage 2 4-Drive Sandbox Quickstart
- [`Dev\docs\ARCHITECTURE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/ARCHITECTURE.md) — System Topology, Runtime Pipelines, Complete Module Registry, SQLite Pragmas, and ADR Notes
- [`Dev\docs\OPERATIONS.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/OPERATIONS.md) — Build, Hub Sync, 4-Drive Sandbox CLIs, Installer Flags, Exit Codes, and Troubleshooting
