# Foundation (`v0.5.1`) — Strategic Roadmap

> **Active Version:** `v0.5.1` (`Beta` — Unified SemVer across all 8 C# assemblies, `@foundation/ui v0.5.1`, and `foundation_core v0.5.1`; Pre-1.0 Beta until `v1.0.0` Going Gold)
> **Pinned Binary Hub:** `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\`

---

## Completed Milestones

### ✅ `v0.1.0` — Core & Web/Python Substrate
- **C# Core & UI (`Foundation.Core`, `Foundation.UI`):** `AppEnvironment`, `GranularSettingsManager`, `DpapiSecretStore`, `SqliteWalPool`, `PidSafeLockfile`, `HeartbeatLogger`, `CrashBundleService`, `JobObjectGuard` + `DualJobCancellationCoordinator`, `SafeNetworkTransfer`, `ToolResolver`, `HybridModuleLoader`, `IFoundationModule`, WinForms WebView2/STA/Tray helpers, and `foundation-terminal.js`/`css`.
- **Installer & Hub:** Tier 2 `InstallerEngineCore` + `InstallerThemeOptions` (`v0.2.0`), `foundation_core` (`v0.1.0`–`v0.1.2` in [`Dev\src\foundation_core\`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/)), and [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py).

### ✅ `v0.2.0` — Universal Tooling & Component Expansion
- **Root Dev Tooling (`Dev\tools\`):** [`build_release_universal.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/build_release_universal.py), [`notion_task_helper.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/notion_task_helper.py).
- **UI & Primitives (`@foundation/ui v0.2.0`):** `CliArgumentHelper`, `BoundedTailStream`, `CappedSharedLogSink`, `UniversalDataTable`, `ResponsiveTable`, `SchemaSettingsRenderer`, `ModalDialog`, `ConfirmActionModal`, `StyledTooltip`, `GlobalTerminalDrawer`, `TerminalToolbarAndFilters`.

### ✅ `v0.3.0` — Consolidation & Tier 1 Installer Wizard (`Installer v0.2.1`)
- **New C# Assemblies:**
  - [`Foundation.Retro`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/) — 41-platform `ConsoleSystemCatalog`, `DatTitleNormalizer`, `NoIntroRedumpDatParser`, `HeaderlessRomStripper`, `MagicBytePlatformDetector`, `SoftPatchEngine`.
  - [`Foundation.Sidecars`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Sidecars/) — `SinglePassStreamHasher`, `SevenZipAndZipInspector`, `HashSidecarService` (`.hash` v1 / `COMPOSITE_SHA1`), `HkLockSentinel` (`.hklock` / `.coexistence.json` v2).
  - [`Foundation.Server`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Server/) — `TieredAuthEngine`, `FoundationTieredAuthMiddleware`, `SisterAppStandardEndpoints`.
  - [`Foundation.Launcher`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Launcher/) — `RootLauncherHost` (`AttachConsole(-1)` + fail-closed self-spawn guard).
- **UI Additions (`Dev\src\Foundation.UI\Web\components\`):** `CoexistenceWarningBanner`, `HybridPathPicker`, `SidebarNavRail` (available via direct `@foundation/ui/components/<Name>` imports).
- **Installer Upgrade (`v0.2.1`):** Added Tier 1 [`StandardInstallerHost`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/StandardInstallerHost.cs), [`InstallerAppDescriptor`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerContracts.cs), `InstallerWizardForm`, and `WindowsShortcutWriter`.
- **Verification & Compliance:** `18` `[Fact]` unit tests across 4 files in [`Dev\tests\Foundation.Tests\`](file:///<PROJECTS_ROOT>/Foundation/Dev/tests/Foundation.Tests/); `100%` of files in `Dev\` `<= 400 LOC` (max `383` lines); published to `<PROJECTS_ROOT>\SharedLibs\Foundation\v0.3.0`.

### ✅ `v0.3.1` — Recomp Timing/Audio/PNG Primitives, BYO Media Extractor & Installer `v0.2.2`
- **Core Primitives (`Foundation.Core` `net8.0`, +5 files):** [`HighResFramePacer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HighResFramePacer.cs), [`AudioRingBuffer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AudioRingBuffer.cs), [`WavCaptureWriter`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/WavCaptureWriter.cs), [`RgbaPngWriter`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/RgbaPngWriter.cs), [`PixelOverlayCanvas`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PixelOverlayCanvas.cs).
- **Retro Media & Recomp Contracts (`Foundation.Retro v0.1.1`):** [`MediaContainerExtractor`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/MediaContainerExtractor.cs) (`.iso`/`.bin`+`.cue`/`.chd`/`.7z`/`.zip`, SHA-256 + `AssertPathContained`) and [`RecompContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompContracts.cs).
- **Installer Hardening (`Foundation.Installer v0.2.2`, +1 file):** Optional `AllowAutoStart = true` opt-out (`false` hides Startup checkbox & clamps `--autostart`) and [`FileLockRetryHelper`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/FileLockRetryHelper.cs).
- **Verification & Compliance:** Implemented per [`v0.3.1.2026-11-13.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/plans/v0.3.1.2026-11-13.md) (`@dean` Gate 1/2 `PASS`; unit tests `0` warnings); published to `SharedLibs\Foundation\v0.3.1` & `Latest` and synced to `reStation` & `KingsFieldRecomp`.

### ✅ `v0.4.0` — Wave 1: Studio/Suite Installer, AOT-Primary `.decomp` Contracts & Public-Repo Readiness
- **Installer & Runtime Environment (`Foundation.Installer`, `Foundation.Core`):** Studio/Suite default install root (`%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\`), opt-in `--migrate-legacy`, bounded retry self-delete trampoline, `InstallerProfileOption` (`Lightweight` / `Full`), `SecondaryPathPicker` (`--reengine-dir`, `--suite-dir`), and `AssertPathContained` containment.
- **Retro & Recomp Contracts (`Foundation.Retro`):** Added [`RecompTierAndBackendContracts.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompTierAndBackendContracts.cs) (`RecompExecutionTierMode` AOT-primary + `JitBringup` / `ThreadedIr`, `RecompRenderBackendIds`, `IDecompPackageManifest`, `AotCacheLayoutHelper`), replaced synthetic BIOS hashes with `IsVerifiedReferenceHash` metadata, and added `.ConfigureAwait(false)` in `MediaContainerExtractor.cs`.
- **Network, Sidecars & Launcher (`SafeNetworkTransfer`, `SevenZipAndZipInspector`, `RootLauncherHost`):** Sanitized `JobId` (`:` ADS fix), recovered HTTP `416`, excluded `400/401/403/404/410` from transient retries, enforced `ResponseHeadersTimeout`, added LZMA/Deflate64 `7z.exe` fallback + nested `::` inspection, and fixed `RootLauncherHost` `--wait`/`--open` blocking, relative arg rebasing, and Ctrl+C forwarding.
- **Unified `0.4.0` SemVer & Public-Repo Readiness:** Aligned all 7 `.csproj` assemblies, `@foundation/ui`, and `foundation_core` on `0.4.0`; deduplicated tripled `Foundation.UI` TS/TSX source copies into `Web\`; externalized Notion IDs (`Dev\.env.example`, `Dev\settings.example.json`); purged `14` stale `.pdb`s; archived `12` diagnostic reports.

### ✅ `v0.4.1` — Wave 2: Modular Installer Wizard, Preview Z-Order & Profile/Secondary Forwarding
- **Modularized Installer & Preview Capture (`Foundation.Installer`):** Extracted `InstallerContracts.cs` and modular partials (`<= 400 LOC`), fixed composite back-to-front `CaptureWizardPreviewBitmap()` overdraw, decoupled `showLegacyMigration` visibility from `AllowLegacyMigration`, and forwarded `SelectedProfileId` / `SecondaryInstallDir` / `UmbrellaSubpath` / `MigrateLegacyChecked` into `InstallManifest`.
- **Packaging & Verification Tooling:** Added `--allow-sdk-zip` / `Foundation-v0.4.1.zip` release validation in `build_release_universal.py` & `sync_foundation_sdk.py` and composite `CapturePreviewBitmap` in [`DisposableWebViewHostForm.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/DisposableWebViewHostForm.cs).

### ✅ `v0.4.1-hf.1` — Ecosystem Unit-Test-Only Runtime Hygiene Purge & Tier A 4-Folder Normalization
- **Runtime Harness & Flag Purge:** Removed retired isolated runtime test directories, flags, and harnesses; standardized [`AppEnvironment`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AppEnvironment.cs) (`UiPresentationMode.TrayOrBackground`), [`InstallerRuntimeContext`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerRuntimeContext.cs), and [`InstallerEngine.CliAndRegistry.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngine.CliAndRegistry.cs); split [`SevenZipAndZipInspector`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Sidecars/SevenZipAndZipInspector.cs) & [`InstallerEngineCore`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngineCore.cs) cleanly under `<= 400 LOC`; added stale consumer test-harness cleanup in [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py); verified `33/33` `[Fact]` unit tests with `0` warnings.

### ✅ `v0.4.2` — Shared 4-Drive Sandbox Seams, Atomic Pseudo-Registry & Stage 2 E2E Runner *(2026-11-16)*
- **Blueprint:** [`Dev\docs\plans\0.4.2.2026-11-15.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/plans/0.4.2.2026-11-15.md).
- **`Foundation.Core` (`24` files):** Added [`KnownFolderPaths`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/KnownFolderPaths.cs) (`GetFolderPath`, `GetHostFolderPath` via Win32 `SHGetKnownFolderPath` + `APPDATA` unmapping, `ResolveLocalProgramsDirectory`, `MapHostPathToSandbox`), [`SandboxEnvironmentSeam`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironmentSeam.cs) (`FOUNDATION_SANDBOX_ROOT`, `SANDBOX_*_DRIVE`, `APP_ISOLATED_TEST=1` beats `FOUNDATION_SANDBOX_ROOT`, `ExitCode = 3` containment guards), [`IPlatformRegistry`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/IPlatformRegistry.cs) / [`WindowsPlatformRegistry`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/WindowsPlatformRegistry.cs) / [`JsonHivePlatformRegistry`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JsonHivePlatformRegistry.cs) (1-file atomic `.tmp -> .bak` JSON pseudo-registry implementing `DWORD`, `QWORD`, `String`, `ExpandString`, `MultiString`, `Binary` + Windows `Reg*` 1223 semantics + PID-safe `.lock`), [`SandboxDriveController`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxDriveController.cs) (opt-in `.secrets\.env` / `Dev\.env` + `subst` resolver), and `AppEnvironment.Sandbox` + `PlatformRegistryFactory`.
- **`Foundation.Installer` (`17` files):** Wired 3-mode isolation (`ShouldSuppressHostMutations` = Stage 1, `ShouldSuppressLiveHostMutations` = Stage 1 || Stage 2), `KnownFolderPaths` shortcuts, `IPlatformRegistry` ARP uninstall metadata, `FileLockRetryHelper` (`<SandboxDDrive>\Programs` allowed under `IsSandboxActive`), `WindowsShortcutWriter.SanitizeShortcutFileName` (`:` -> ` -`), DPI-safe `_lblTitle`/`_lblSub` clamping preserving `650x460`/`650x570` `ClientSize`, and `FOUNDATION_SUPPRESS_MODALS=1` modal suppression (resolving the archived `v0.4.1` shortcut `:` & header badge report).
- **Python `foundation_core` (`0.4.2`), Dev Bible v1.6.0 & Packaging:** Added pure-stdlib [`sandbox_seam.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/sandbox_seam.py) and [`pseudo_registry.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/pseudo_registry.py) (`1:1` C# Stage 2 & hive parity), [`sandbox_ctl.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sandbox_ctl.py), [`run_sandbox_e2e.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/run_sandbox_e2e.py) (12-point `S2-01`..`S2-12`), narrowed [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py) (`54` SDK files published to `SharedLibs\Foundation\v0.4.2\` & `Latest\`), aligned `build_release_universal.py --allow-sdk-zip` strict 6-file validation (no loose `LICENSE` in `Releases\`), and registered 14 sandbox/registry columns in Standard 18 (`18_DATA_DICTIONARY.md`).
- **Verification & Compliance:** `dotnet test Dev\Foundation.sln -c Release -p:TreatWarningsAsErrors=true` (`40/40` passed, `0` warnings), Python `unittest` & `pytest` (`8/8` passed), `audit_scanner.py` (`P0: 0, P1: 0, P2: 45, P3: 0`), live Stage 2 4-drive E2E dogfood (`run_sandbox_e2e.py` with `DogfoodSample-Setup.exe`: `12/12` passed, `S2-01`..`S2-12`, `hostDiffCount=0`, `ExitCode=0`), `@dean` Gate 2 + Delta Gate 2 `PASS`, and `@inspecter` Phase 3 + 2-tier log sweep `PASS`.

### ✅ `v0.5.0` — Declarative Markup Installer, `Foundation.Automation` Loopback & Pre-Public Readiness *(2026-11-16)*
- **Cornerstone Theme:** Eliminated per-app `InstallerWizardForm.cs` duplication via declarative `installer.manifest.json` (`schemaVersion: 2`), added shared `HKCU\Software\StreamItAll\LastInstallBaseDir` precedence, added non-focus-stealing `Foundation.Automation` loopback + PNG snapshot capture, and staged pre-public OSS hygiene (`SECURITY.md`, public `README.md` Quickstart, human-review gate report).
- **Deliverables:**
  - `InstallerManifestStore.cs` (`"single-line"`/`"multi-line"`, `"lightweight"`/`"full"`/`"custom"`, 2-line tooltips, theme/button/asset overrides, `secondaryPickers`, `allowAutoStart`) + `LastInstallBaseDir` registry precedence (`IPlatformRegistry` / `APP_ISOLATED_TEST=1` / reparse-point safe).
  - `Foundation.Automation` (`net8.0-windows`): `AutomationLoopbackServer` (`127.0.0.1`, Host/Origin `403`, constant-time Bearer `401`, UI-thread dispatch), `AutomationCliHelper`, `Win32VirtualInputDriver` (`WM_*` pointer/keyboard, zero `SendInput`), and `WindowSnapshotService` (`IRenderSurfaceSnapshotProvider` + `RgbaPngWriter` + `CapturePreviewBitmap`).
- **Verification & Compliance:** `dotnet test Dev\Foundation.sln -c Release -p:TreatWarningsAsErrors=true` (`45/45` passed, `0` warnings), Python `unittest` (`8/8` OK), `audit_scanner.py` (`P0: 0, P1: 0, P2: 45, P3: 0`), `57` SDK files published + strict 6-file `Releases\v0.5.0\` / `Latest\` bundle validated (`build_release_universal.py --allow-sdk-zip`), `@dean` Gate 2 `PASS`, `@inspecter` Phase 3 `PASS`.

### ✅ `v0.5.1` — ISO/IEC 18004 QR Code Suite (`Foundation.Core.QrCodeEncoder`) *(2026-11-19)*
- **Added:** Zero-dependency `Foundation.Core.QrCodeEncoder` (`QrCodeEncoder.cs`, `<= 350` SLOC, byte-mode Versions 1–10 ECC Level M up to 213 UTF-8 bytes, GF(256) Reed-Solomon, BCH format/version bits, 8-mask ISO penalty scoring, 4-module ISO quiet zone, high-contrast `RenderSvg`) + `FoundationQrCodeTests` and unified SDK `v0.5.1` bump (`Mother v0.3.0` prerequisite).

---

## Upcoming Horizons (`v0.6.0` $\rightarrow$ `v1.0.0` Going Gold)

```
v0.5.1 (Live) ─▶ v0.6.0 (TrayDaemonHost + Python foundation_core.server + Self-Update)
                   │
                   ▼
                v0.7.0 (JobQueue/Scheduler + Doctor/Backup + NativeAOT Core Subset)
                   │
                   ▼
                v1.0.0 (Going Gold: Local NuGet/PyPI Feeds, Full pytest Parity & ABI Freeze)
```

### 🎯 `v0.6.0` — Shared Tray Daemon Host, Python Server/SSE Parity & Self-Update Check
- **Cornerstone Theme:** Unify daemon tray lifecycles (`Mother`, `ShadowLibrary`, `Housekeeping`, `uStation`, `Greenroom`, `Redroom`), Python web/SSE auth, and launcher update checks.
- **Deliverables:**
  - `TrayDaemonHost` (`Foundation.UI` + `foundation_core`): shared tray icon, menu builder, and `PidSafeLockfile` zombie-handle cleanup.
  - `foundation_core.server`: Python parity for `TieredAuthEngine`, `suite.manifest.json` v2 endpoints, and bounded SSE log-tailing (`Greenroom`, `Redroom`, `_Goals`).
  - `Foundation.Launcher` self-update check: GitHub Release check + SHA-256/signature verification + staged handoff (never auto-overwriting running `D:\Programs\`).
- **Gate 0 & Verification:** Register `latestVersion`, `releaseUrl`, `isUpdateAvailable` via `/define`. Verify clean tray handle teardown on kill/restart, `401`/disconnect SSE tests, and tampered-hash rejection.

### 🎯 `v0.7.0` — Shared Job Queue, Doctor/Backup & NativeAOT-Compatible Core Subset
- **Cornerstone Theme:** Unify background job/cron scheduling and SQLite Doctor/Backup across sister apps, and make `Foundation.Core` + `Foundation.Automation` usable in `Snowfall` (`.NET 10 NativeAOT`) and `Project26` (Unity 6).
- **Deliverables:**
  - `JobQueue`, `IntervalCronScheduler`, and `@foundation/ui` job queue drawer (`Mother`, `ShadowLibrary`, `Housekeeping`, `uStation`, `Greenroom`, `Redroom`).
  - `SystemDoctorAndBackupService`: SQLite integrity check, `VACUUM INTO`, and `10`-snapshot config rotation.
  - `FoundationCoreJsonContext` (`JsonSerializerContext`) + `<IsAotCompatible>true</IsAotCompatible>` reflection-free `Foundation.Core` & `Foundation.Automation` subset (separating reflection-heavy `HybridModuleLoader`).
- **Gate 0 & Verification:** Register `cronExpression`, `jobType`, `maxRetries`, `backupPath`, `isIntegrityCheckPassed`, `retentionCount` via `/define`. Verify `dotnet publish -p:PublishAot=true` with `0` `IL2026`/`IL3050` warnings.

### 🏆 `v1.0.0` — Going Gold: Local NuGet/PyPI Feeds, Full `pytest` Parity & ABI Freeze
- **Cornerstone Theme:** Graduate `Foundation` from `0.x` to production-locked `v1.0.0` across C#, Python, and TypeScript.
- **Deliverables:** Versioned `.nupkg` + Python wheel artifacts in `<PROJECTS_ROOT>\SharedLibs\Foundation\` local feeds, `100%` `pytest` parity suite mirroring all C# `[Fact]` invariants (`0` warnings), and automated public ABI diff gate.
- **Scope Non-Goals:** `Foundation` never owns SDL3/OpenGL console emulation loops (`reEngine`), ECS/physics/`PakCooker` (`Snowfall`), or app-specific domain schemas.
