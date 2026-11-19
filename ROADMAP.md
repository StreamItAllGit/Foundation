# Foundation (`v0.5.2`) — Strategic Roadmap

> **Active Version:** `v0.5.2` (`Beta` — Unified SemVer across all 8 C# assemblies, `@foundation/ui v0.5.2`, and `foundation_core v0.5.2`; Pre-1.0 Beta until `v1.0.0` Going Gold)
> **Pinned Binary Hub:** `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\`

---

## Completed Milestones

### ✅ `v0.1.0` — Core & Web/Python Substrate
- **C# Core & UI (`Foundation.Core`, `Foundation.UI`):** `AppEnvironment`, `GranularSettingsManager`, `DpapiSecretStore`, `SqliteWalPool`, `PidSafeLockfile`, `HeartbeatLogger`, `CrashBundleService`, `JobObjectGuard` + `DualJobCancellationCoordinator`, `SafeNetworkTransfer`, `ToolResolver`, `HybridModuleLoader`, `IFoundationModule`, WinForms WebView2/STA/Tray helpers, and `foundation-terminal.js`/`css`.
- **Installer & Hub:** Tier 2 `InstallerEngineCore` + `InstallerThemeOptions` (`v0.2.0`), `foundation_core` (`v0.1.0`–`v0.1.2` in [`Dev/src/foundation_core/`](Dev/src/foundation_core/)), and [`sync_foundation_sdk.py`](Dev/tools/sync_foundation_sdk.py).

### ✅ `v0.2.0` — Universal Tooling & Component Expansion
- **Root Dev Tooling (`Dev\tools\`):** [`build_release_universal.py`](Dev/tools/build_release_universal.py), [`notion_task_helper.py`](Dev/tools/notion_task_helper.py).
- **UI & Primitives (`@foundation/ui v0.2.0`):** `CliArgumentHelper`, `BoundedTailStream`, `CappedSharedLogSink`, `UniversalDataTable`, `ResponsiveTable`, `SchemaSettingsRenderer`, `ModalDialog`, `ConfirmActionModal`, `StyledTooltip`, `GlobalTerminalDrawer`, `TerminalToolbarAndFilters`.

### ✅ `v0.3.0` — Consolidation & Tier 1 Installer Wizard (`Installer v0.2.1`)
- **New C# Assemblies:**
  - [`Foundation.Retro`](Dev/src/Foundation.Retro/) — 41-platform `ConsoleSystemCatalog`, `DatTitleNormalizer`, `NoIntroRedumpDatParser`, `HeaderlessRomStripper`, `MagicBytePlatformDetector`, `SoftPatchEngine`.
  - [`Foundation.Sidecars`](Dev/src/Foundation.Sidecars/) — `SinglePassStreamHasher`, `SevenZipAndZipInspector`, `HashSidecarService` (`.hash` v1 / `COMPOSITE_SHA1`), `HkLockSentinel` (`.hklock` / `.coexistence.json` v2).
  - [`Foundation.Server`](Dev/src/Foundation.Server/) — `TieredAuthEngine`, `FoundationTieredAuthMiddleware`, `SisterAppStandardEndpoints`.
  - [`Foundation.Launcher`](Dev/src/Foundation.Launcher/) — `RootLauncherHost` (`AttachConsole(-1)` + fail-closed self-spawn guard).
- **UI Additions (`Dev\src\Foundation.UI\Web\components\`):** `CoexistenceWarningBanner`, `HybridPathPicker`, `SidebarNavRail` (available via direct `@foundation/ui/components/<Name>` imports).
- **Installer Upgrade (`v0.2.1`):** Added Tier 1 [`StandardInstallerHost`](Dev/src/Foundation.Installer/StandardInstallerHost.cs), [`InstallerAppDescriptor`](Dev/src/Foundation.Installer/InstallerContracts.cs), `InstallerWizardForm`, and `WindowsShortcutWriter`.
- **Verification & Compliance:** `18` `[Fact]` unit tests across 4 files in [`Dev/tests/Foundation.Tests/`](Dev/tests/Foundation.Tests/); `100%` of files in `Dev\` `<= 400 LOC` (max `383` lines); published to `<PROJECTS_ROOT>\SharedLibs\Foundation\v0.3.0`.

### ✅ `v0.3.1` — Recomp Timing/Audio/PNG Primitives, BYO Media Extractor & Installer `v0.2.2`
- **Core Primitives (`Foundation.Core` `net8.0`, +5 files):** [`HighResFramePacer`](Dev/src/Foundation.Core/HighResFramePacer.cs), [`AudioRingBuffer`](Dev/src/Foundation.Core/AudioRingBuffer.cs), [`WavCaptureWriter`](Dev/src/Foundation.Core/WavCaptureWriter.cs), [`RgbaPngWriter`](Dev/src/Foundation.Core/RgbaPngWriter.cs), [`PixelOverlayCanvas`](Dev/src/Foundation.Core/PixelOverlayCanvas.cs).
- **Retro Media & Recomp Contracts (`Foundation.Retro v0.1.1`):** [`MediaContainerExtractor`](Dev/src/Foundation.Retro/MediaContainerExtractor.cs) (`.iso`/`.bin`+`.cue`/`.chd`/`.7z`/`.zip`, SHA-256 + `AssertPathContained`) and [`RecompContracts`](Dev/src/Foundation.Retro/RecompContracts.cs).
- **Installer Hardening (`Foundation.Installer v0.2.2`, +1 file):** Optional `AllowAutoStart = true` opt-out (`false` hides Startup checkbox & clamps `--autostart`) and [`FileLockRetryHelper`](Dev/src/Foundation.Installer/FileLockRetryHelper.cs).
- **Verification & Compliance:** Implemented per `Dev\docs\plans\v0.3.1.2026-11-13.md` (gate checks verified; unit tests `0` warnings); published to `SharedLibs\Foundation\v0.3.1` & `Latest` and synced to `reStation` & `KingsFieldRecomp`.

### ✅ `v0.4.0` — Wave 1: Studio/Suite Installer, AOT-Primary `.decomp` Contracts & Public-Repo Readiness
- **Installer & Runtime Environment (`Foundation.Installer`, `Foundation.Core`):** Studio/Suite default install root (`%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\`), opt-in `--migrate-legacy`, bounded retry self-delete trampoline, `InstallerProfileOption` (`Lightweight` / `Full`), `SecondaryPathPicker` (`--reengine-dir`, `--suite-dir`), and `AssertPathContained` containment.
- **Retro & Recomp Contracts (`Foundation.Retro`):** Added [`RecompTierAndBackendContracts.cs`](Dev/src/Foundation.Retro/RecompTierAndBackendContracts.cs) (`RecompExecutionTierMode` AOT-primary + `JitBringup` / `ThreadedIr`, `RecompRenderBackendIds`, `IDecompPackageManifest`, `AotCacheLayoutHelper`), replaced synthetic BIOS hashes with `IsVerifiedReferenceHash` metadata, and added `.ConfigureAwait(false)` in `MediaContainerExtractor.cs`.
- **Network, Sidecars & Launcher (`SafeNetworkTransfer`, `SevenZipAndZipInspector`, `RootLauncherHost`):** Sanitized `JobId` (`:` ADS fix), recovered HTTP `416`, excluded `400/401/403/404/410` from transient retries, enforced `ResponseHeadersTimeout`, added LZMA/Deflate64 `7z.exe` fallback + nested `::` inspection, and fixed `RootLauncherHost` `--wait`/`--open` blocking, relative arg rebasing, and Ctrl+C forwarding.
- **Unified `0.4.0` SemVer & Public-Repo Readiness:** Aligned all 7 `.csproj` assemblies, `@foundation/ui`, and `foundation_core` on `0.4.0`; deduplicated tripled `Foundation.UI` TS/TSX source copies into `Web\`; externalized Notion IDs (`Dev\.env.example`, `Dev\settings.example.json`); purged `14` stale `.pdb`s; archived `12` diagnostic reports.

### ✅ `v0.4.1` — Wave 2: Modular Installer Wizard, Preview Z-Order & Profile/Secondary Forwarding
- **Modularized Installer & Preview Capture (`Foundation.Installer`):** Extracted `InstallerContracts.cs` and modular partials (`<= 400 LOC`), fixed composite back-to-front `CaptureWizardPreviewBitmap()` overdraw, decoupled `showLegacyMigration` visibility from `AllowLegacyMigration`, and forwarded `SelectedProfileId` / `SecondaryInstallDir` / `UmbrellaSubpath` / `MigrateLegacyChecked` into `InstallManifest`.
- **Packaging & Verification Tooling:** Added `--allow-sdk-zip` / `Foundation-v0.4.1.zip` release validation in `build_release_universal.py` & `sync_foundation_sdk.py` and composite `CapturePreviewBitmap` in [`DisposableWebViewHostForm.cs`](Dev/src/Foundation.UI/Desktop/DisposableWebViewHostForm.cs).

### ✅ `v0.4.1-hf.1` — Ecosystem Unit-Test-Only Runtime Hygiene Purge & Tier A 4-Folder Normalization
- **Runtime Harness & Flag Purge:** Removed retired isolated runtime test directories, flags, and harnesses; standardized [`AppEnvironment`](Dev/src/Foundation.Core/AppEnvironment.cs) (`UiPresentationMode.TrayOrBackground`), [`InstallerRuntimeContext`](Dev/src/Foundation.Installer/InstallerRuntimeContext.cs), and [`InstallerEngine.CliAndRegistry.cs`](Dev/src/Foundation.Installer/InstallerEngine.CliAndRegistry.cs); split [`SevenZipAndZipInspector`](Dev/src/Foundation.Sidecars/SevenZipAndZipInspector.cs) & [`InstallerEngineCore`](Dev/src/Foundation.Installer/InstallerEngineCore.cs) cleanly under `<= 400 LOC`; added stale consumer test-harness cleanup in [`sync_foundation_sdk.py`](Dev/tools/sync_foundation_sdk.py); verified `33/33` `[Fact]` unit tests with `0` warnings.

### ✅ `v0.4.2` — Shared 4-Drive Sandbox Seams, Atomic Pseudo-Registry & Stage 2 E2E Runner *(2026-11-16)*
- **Blueprint:** `Dev\docs\plans\0.4.2.2026-11-15.md`.
- **`Foundation.Core` (`24` files):** Added [`KnownFolderPaths`](Dev/src/Foundation.Core/KnownFolderPaths.cs) (`GetFolderPath`, `GetHostFolderPath` via Win32 `SHGetKnownFolderPath` + `APPDATA` unmapping, `ResolveLocalProgramsDirectory`, `MapHostPathToSandbox`), [`SandboxEnvironmentSeam`](Dev/src/Foundation.Core/SandboxEnvironmentSeam.cs) (`FOUNDATION_SANDBOX_ROOT`, `SANDBOX_*_DRIVE`, `APP_ISOLATED_TEST=1` beats `FOUNDATION_SANDBOX_ROOT`, `ExitCode = 3` containment guards), [`IPlatformRegistry`](Dev/src/Foundation.Core/IPlatformRegistry.cs) / [`WindowsPlatformRegistry`](Dev/src/Foundation.Core/WindowsPlatformRegistry.cs) / [`JsonHivePlatformRegistry`](Dev/src/Foundation.Core/JsonHivePlatformRegistry.cs) (1-file atomic `.tmp -> .bak` JSON pseudo-registry implementing `DWORD`, `QWORD`, `String`, `ExpandString`, `MultiString`, `Binary` + Windows `Reg*` 1223 semantics + PID-safe `.lock`), [`SandboxDriveController`](Dev/src/Foundation.Core/SandboxDriveController.cs) (opt-in `.secrets\.env` / `Dev\.env` + `subst` resolver), and `AppEnvironment.Sandbox` + `PlatformRegistryFactory`.
- **`Foundation.Installer` (`17` files):** Wired 3-mode isolation (`ShouldSuppressHostMutations` = Stage 1, `ShouldSuppressLiveHostMutations` = Stage 1 || Stage 2), `KnownFolderPaths` shortcuts, `IPlatformRegistry` ARP uninstall metadata, `FileLockRetryHelper` (`<SandboxDDrive>\Programs` allowed under `IsSandboxActive`), `WindowsShortcutWriter.SanitizeShortcutFileName` (`:` -> ` -`), DPI-safe `_lblTitle`/`_lblSub` clamping preserving `650x460`/`650x570` `ClientSize`, and `FOUNDATION_SUPPRESS_MODALS=1` modal suppression (resolving the archived `v0.4.1` shortcut `:` & header badge report).
- **Python `foundation_core` (`0.4.2`) & Packaging:** Added pure-stdlib [`sandbox_seam.py`](Dev/src/foundation_core/sandbox_seam.py) and [`pseudo_registry.py`](Dev/src/foundation_core/pseudo_registry.py) (`1:1` C# Stage 2 & hive parity), [`sandbox_ctl.py`](Dev/tools/sandbox_ctl.py), [`run_sandbox_e2e.py`](Dev/tools/run_sandbox_e2e.py) (12-point `S2-01`..`S2-12`), narrowed [`sync_foundation_sdk.py`](Dev/tools/sync_foundation_sdk.py) (`54` SDK files published to `SharedLibs\Foundation\v0.4.2\` & `Latest\`), aligned `build_release_universal.py --allow-sdk-zip` strict 6-file validation (no loose `LICENSE` in `Releases\`), and registered 14 canonical sandbox/registry schema fields.
- **Verification & Compliance:** `dotnet test Dev\Foundation.sln -c Release -p:TreatWarningsAsErrors=true` (`40/40` passed, `0` warnings), Python `unittest` & `pytest` (`8/8` passed), `audit_scanner.py` (`P0: 0, P1: 0, P2: 45, P3: 0`), live Stage 2 4-drive E2E dogfood (`run_sandbox_e2e.py` with `DogfoodSample-Setup.exe`: `12/12` passed, `S2-01`..`S2-12`, `hostDiffCount=0`, `ExitCode=0`), and independent release-gate + 2-tier log-sweep checks passed.

### ✅ `v0.5.0` — Declarative Markup Installer, `Foundation.Automation` Loopback & Pre-Public Readiness *(2026-11-16)*
- **Cornerstone Theme:** Eliminated per-app `InstallerWizardForm.cs` duplication via declarative `installer.manifest.json` (`schemaVersion: 2`), added shared `HKCU\Software\StreamItAll\LastInstallBaseDir` precedence, added non-focus-stealing `Foundation.Automation` loopback + PNG snapshot capture, and staged pre-public OSS hygiene (`SECURITY.md`, public `README.md` Quickstart, human-review gate report).
- **Deliverables:**
  - `InstallerManifestStore.cs` (`"single-line"`/`"multi-line"`, `"lightweight"`/`"full"`/`"custom"`, 2-line tooltips, theme/button/asset overrides, `secondaryPickers`, `allowAutoStart`) + `LastInstallBaseDir` registry precedence (`IPlatformRegistry` / `APP_ISOLATED_TEST=1` / reparse-point safe).
  - `Foundation.Automation` (`net8.0-windows`): `AutomationLoopbackServer` (`127.0.0.1`, Host/Origin `403`, constant-time Bearer `401`, UI-thread dispatch), `AutomationCliHelper`, `Win32VirtualInputDriver` (`WM_*` pointer/keyboard, zero `SendInput`), and `WindowSnapshotService` (`IRenderSurfaceSnapshotProvider` + `RgbaPngWriter` + `CapturePreviewBitmap`).
- **Verification & Compliance:** `dotnet test Dev\Foundation.sln -c Release -p:TreatWarningsAsErrors=true` (`45/45` passed, `0` warnings), Python `unittest` (`8/8` OK), `audit_scanner.py` (`P0: 0, P1: 0, P2: 45, P3: 0`), `57` SDK files published + strict 6-file `Releases\v0.5.0\` / `Latest\` bundle validated (`build_release_universal.py --allow-sdk-zip`), and release-gate + post-build verification passed.

### ✅ `v0.5.1` — ISO/IEC 18004 QR Code Suite (`Foundation.Core.QrCodeEncoder`) *(2026-11-19)*
- **Added:** Zero-dependency `Foundation.Core.QrCodeEncoder` (`QrCodeEncoder.cs`, `<= 350` SLOC, byte-mode Versions 1–10 ECC Level M up to 213 UTF-8 bytes, GF(256) Reed-Solomon, BCH format/version bits, 8-mask ISO penalty scoring, 4-module ISO quiet zone, high-contrast `RenderSvg`) + `FoundationQrCodeTests` and unified SDK `v0.5.1` bump (`Mother v0.3.0` prerequisite).

### ✅ `v0.5.2` — Installer Path-Poisoning Fix, `suiteId`, Extensible Launcher & 7-File Release Parity *(2026-11-19)*
- **Installer Path Hardening & `suiteId` (`Foundation.Installer`):** Prevented doubling `ProductName`/`DisplayName` on versioned `UmbrellaSubpath` leaves (`IsVersionLeafSegment` in [`InstallerContracts.cs`](Dev/src/Foundation.Installer/InstallerContracts.cs) & [`InstallerThemeOptions.cs`](Dev/src/Foundation.Installer/InstallerThemeOptions.cs)), hardened [`InstallerEngine.Core.cs`](Dev/src/Foundation.Installer/InstallerEngine.Core.cs) with studio/suite-anchored `ExtractInstallBaseDirectory`, `IsPoisonedInstallBaseDirectory` guards, and self-healing `LastInstallBaseDir` suggestions (preserving clean user roots like `D:\Games\Misc`), and added canonical `suiteId` support on [`InstallerManifestStore.cs`](Dev/src/Foundation.Installer/InstallerManifestStore.cs) (`ResolveEffectiveSuiteId()`, keeping `suiteName` as a non-`[Obsolete]` legacy JSON fallback).
- **Extensible Launcher Contracts (`Foundation.Launcher`):** Added zero-dependency [`RootLauncherOptions.cs`](Dev/src/Foundation.Launcher/RootLauncherOptions.cs) (`ExtraSyncWaitFlags`, `ExtraValueOptions`, `ExtraPathValueOptions`), binary-compatible [`RootLauncherHost.cs`](Dev/src/Foundation.Launcher/RootLauncherHost.cs) overloads, built-in suite/diagnostic/automation flags (`--diagnose`, `--doctor`, `--verify`, `--verify-module`, `--automation-port`, `--automation-token`, `--programs-dir`, `--save-dir`, `--preload-media`, `--backend`), and non-swallowing optional-value switches (`--suite-tab`, `--window`, `--browser`).
- **7-File Portable Release Validation & Focus-Free Mouse Dispatch (`Dev\tools\`, `Foundation.Automation`):** Added `--allow-portable-zip` (and auto-detection when `*-Portable.zip` accompanies `*-Setup-v*.exe`) plus `_inspect_portable_zip()` leak checks in [`build_release_universal.py`](Dev/tools/build_release_universal.py) (`validate_release_bundle`), updated [`Win32VirtualInputDriver.cs`](Dev/src/Foundation.Automation/Win32VirtualInputDriver.cs) (`InvokeManagedMouse`) for focus-free managed WinForms mouse dispatch (`0` `SetFocus()` steals), and added [`TestAssemblyConfig.cs`](Dev/tests/Foundation.Tests/TestAssemblyConfig.cs) (`DisableTestParallelization = true`) for deterministic test isolation.
- **Verification & Compliance:** `dotnet test Dev\Foundation.sln -c Release` (`50/50` passed, `0` failed, `0` skipped), Python `unittest` (`13/13` OK including `test_build_release_universal.py`), `audit_scanner.py` (`P0: 0, P1: 0, P2: 45, P3: 0`, `110` scanned files), and unified `0.5.2` SemVer lockstep across all C#, TypeScript, Python, and tool manifests.

---

## Upcoming Horizons (`v0.6.0` $\rightarrow$ `v1.0.0` Going Gold)

```
v0.5.2 (Live) ─▶ v0.6.0 (TrayDaemonHost + Python foundation_core.server + Self-Update)
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
  - `Foundation.Launcher` self-update check: GitHub Release check + SHA-256/signature verification + staged handoff (never auto-overwriting installed production binaries).
- **Schema & Verification:** Register `latestVersion`, `releaseUrl`, and `isUpdateAvailable` schema keys; verify clean tray handle teardown on kill/restart, `401`/disconnect SSE tests, and tampered-hash rejection.

### 🎯 `v0.7.0` — Shared Job Queue, Doctor/Backup & NativeAOT-Compatible Core Subset
- **Cornerstone Theme:** Unify background job/cron scheduling and SQLite Doctor/Backup across sister apps, and make `Foundation.Core` + `Foundation.Automation` usable in `Snowfall` (`.NET 10 NativeAOT`) and `Project26` (Unity 6).
- **Deliverables:**
  - `JobQueue`, `IntervalCronScheduler`, and `@foundation/ui` job queue drawer (`Mother`, `ShadowLibrary`, `Housekeeping`, `uStation`, `Greenroom`, `Redroom`).
  - `SystemDoctorAndBackupService`: SQLite integrity check, `VACUUM INTO`, and `10`-snapshot config rotation.
  - `FoundationCoreJsonContext` (`JsonSerializerContext`) + `<IsAotCompatible>true</IsAotCompatible>` reflection-free `Foundation.Core` & `Foundation.Automation` subset (separating reflection-heavy `HybridModuleLoader`).
- **Schema & Verification:** Register `cronExpression`, `jobType`, `maxRetries`, `backupPath`, `isIntegrityCheckPassed`, and `retentionCount` schema keys; verify `dotnet publish -p:PublishAot=true` with `0` `IL2026`/`IL3050` warnings.

### 🏆 `v1.0.0` — Going Gold: Local NuGet/PyPI Feeds, Full `pytest` Parity & ABI Freeze
- **Cornerstone Theme:** Graduate `Foundation` from `0.x` to production-locked `v1.0.0` across C#, Python, and TypeScript.
- **Deliverables:** Versioned `.nupkg` + Python wheel artifacts in `<PROJECTS_ROOT>\SharedLibs\Foundation\` local feeds, `100%` `pytest` parity suite mirroring all C# `[Fact]` invariants (`0` warnings), and automated public ABI diff gate.
- **Scope Non-Goals:** `Foundation` never owns SDL3/OpenGL console emulation loops (`reEngine`), ECS/physics/`PakCooker` (`Snowfall`), or app-specific domain schemas.
