# Foundation (`v0.5.6` — Beta) — Strategic Roadmap

> **Active Version:** `v0.5.6` (`Beta` — Pre-1.0 unified SemVer across all 8 C# assemblies, `@foundation/ui v0.5.6`, and `foundation_core v0.5.6`; `v0.6.0` active next until `v1.0.0` Going Gold)
> **Pinned Binary Hub:** Published local SDK snapshot (`SharedLibs/Foundation/Latest/`)

---

## 1. North Star Goal

Graduate **Foundation (`v1.0.0`)** as the unified, ABI-frozen C#, React/TypeScript, and Python SDK powering Windows desktop apps, NativeAOT installers, 4-drive sandboxes, and focus-free UI automation across the ecosystem.

---

## 2. Active In-Dev Milestone: `v0.6.0` — Shared Tray Daemon Host, Python Server/SSE Parity & Self-Update Check

- **Cornerstone Theme:** Unify daemon tray lifecycles across desktop background services, Python web/SSE auth, and launcher update checks.
- **Deliverables:**
  - `TrayDaemonHost` (`Foundation.UI` + `foundation_core`): shared tray icon, menu builder, and `PidSafeLockfile` zombie-handle cleanup.
  - `foundation_core.server`: Python parity for `TieredAuthEngine`, `suite.manifest.json` v2 endpoints, and bounded SSE log-tailing for companion web services.
  - `Foundation.Launcher` self-update check: GitHub Release check + SHA-256/signature verification + staged handoff (never auto-overwriting installed production binaries).
- **Schema & Verification:** Register `latestVersion`, `releaseUrl`, and `isUpdateAvailable` schema keys; verify clean tray handle teardown on kill/restart, `401`/disconnect SSE tests, and tampered-hash rejection.

---

## 3. Upcoming Horizons (`v0.7.0` -> `v1.0.0` Going Gold)

```
v0.5.6 (Beta) --> v0.6.0 (TrayDaemonHost + Python foundation_core.server + Self-Update)
                    |
                    v
                  v0.7.0 (JobQueue/Scheduler + Doctor/Backup + NativeAOT Core Subset)
                    |
                    v
                  v1.0.0 (Going Gold: Local NuGet/PyPI Feeds, Full pytest Parity & ABI Freeze)
```

### `v0.7.0` — Shared Job Queue, Doctor/Backup & NativeAOT-Compatible Core Subset
- **Cornerstone Theme:** Unify background job/cron scheduling and SQLite Doctor/Backup across companion apps, and make `Foundation.Core` + `Foundation.Automation` usable in `.NET` NativeAOT and game-engine targets.
- **Deliverables:**
  - `JobQueue`, `IntervalCronScheduler`, and `@foundation/ui` job queue drawer for companion desktop and web services.
  - `SystemDoctorAndBackupService`: SQLite integrity check, `VACUUM INTO`, and `10`-snapshot config rotation.
  - Full `<IsAotCompatible>true</IsAotCompatible>` reflection-free `Foundation.Core` & `Foundation.Automation` subset (separating reflection-heavy `HybridModuleLoader`).
- **Schema & Verification:** Register `cronExpression`, `jobType`, `maxRetries`, `backupPath`, `isIntegrityCheckPassed`, and `retentionCount` schema keys; verify `dotnet publish -p:PublishAot=true` with `0` `IL2026`/`IL3050` warnings.

### `v1.0.0` — Going Gold: Local NuGet/PyPI Feeds, Full `pytest` Parity & ABI Freeze
- **Cornerstone Theme:** Graduate `Foundation` from `0.x` to production-locked `v1.0.0` across C#, Python, and TypeScript.
- **Deliverables:** Versioned `.nupkg` + Python wheel artifacts in local SDK feeds, `100%` `pytest` parity suite mirroring all C# `[Fact]` invariants (`0` warnings), and automated public ABI diff gate.

---

## 4. Completed Milestones

### `v0.1.0` — Core & Web/Python Substrate
- **C# Core & UI (`Foundation.Core`, `Foundation.UI`):** `AppEnvironment`, `GranularSettingsManager`, `DpapiSecretStore`, `SqliteWalPool`, `PidSafeLockfile`, `HeartbeatLogger`, `CrashBundleService`, `JobObjectGuard` + `DualJobCancellationCoordinator`, `SafeNetworkTransfer`, `ToolResolver`, `HybridModuleLoader`, `IFoundationModule`, WinForms WebView2/STA/Tray helpers, and `foundation-terminal.js`/`css`.
- **Installer & Hub:** Tier 2 `InstallerEngineCore` + `InstallerThemeOptions` (`v0.2.0`), `foundation_core` (`v0.1.0`–`v0.1.2` in [`Dev/src/foundation_core/`](Dev/src/foundation_core/)), and [`sync_foundation_sdk.py`](Dev/tools/sync_foundation_sdk.py).

### `v0.2.0` — Universal Tooling & Component Expansion
- **Root Dev Tooling (`Dev\tools\`):** [`build_release_universal.py`](Dev/tools/build_release_universal.py), [`notion_task_helper.py`](Dev/tools/notion_task_helper.py).
- **UI & Primitives (`@foundation/ui v0.2.0`):** `CliArgumentHelper`, `BoundedTailStream`, `CappedSharedLogSink`, `UniversalDataTable`, `ResponsiveTable`, `SchemaSettingsRenderer`, `ModalDialog`, `ConfirmActionModal`, `StyledTooltip`, `GlobalTerminalDrawer`, `TerminalToolbarAndFilters`.

### `v0.3.0` — Consolidation & Tier 1 Installer Wizard (`Installer v0.2.1`)
- **New C# Assemblies:**
  - [`Foundation.Retro`](Dev/src/Foundation.Retro/) — 41-platform `ConsoleSystemCatalog`, `DatTitleNormalizer`, `NoIntroRedumpDatParser`, `HeaderlessRomStripper`, `MagicBytePlatformDetector`, `SoftPatchEngine`.
  - [`Foundation.Sidecars`](Dev/src/Foundation.Sidecars/) — `SinglePassStreamHasher`, `SevenZipAndZipInspector`, `HashSidecarService` (`.hash` v1 / `COMPOSITE_SHA1`), `HkLockSentinel` (`.hklock` / `.coexistence.json` v2).
  - [`Foundation.Server`](Dev/src/Foundation.Server/) — `TieredAuthEngine`, `FoundationTieredAuthMiddleware`, `SisterAppStandardEndpoints`.
  - [`Foundation.Launcher`](Dev/src/Foundation.Launcher/) — `RootLauncherHost` (`AttachConsole(-1)` + fail-closed self-spawn guard).
- **UI Additions (`Dev\src\Foundation.UI\Web\components\`):** `CoexistenceWarningBanner`, `HybridPathPicker`, `SidebarNavRail` (available via direct `@foundation/ui/components/<Name>` imports).
- **Installer Upgrade (`v0.2.1`):** Added Tier 1 [`StandardInstallerHost`](Dev/src/Foundation.Installer/StandardInstallerHost.cs), [`InstallerAppDescriptor`](Dev/src/Foundation.Installer/InstallerContracts.cs), `InstallerWizardForm`, and `WindowsShortcutWriter`.
- **Verification & Compliance:** `18` `[Fact]` unit tests across 4 files in [`Dev/tests/Foundation.Tests/`](Dev/tests/Foundation.Tests/); `100%` of files in `Dev\` `<= 400` SLOC (max `383` lines); published SDK snapshot.

### `v0.3.1` — Recomp Timing/Audio/PNG Primitives, BYO Media Extractor & Installer `v0.2.2`
- **Core Primitives (`Foundation.Core` `net8.0`, +5 files):** [`HighResFramePacer`](Dev/src/Foundation.Core/HighResFramePacer.cs), [`AudioRingBuffer`](Dev/src/Foundation.Core/AudioRingBuffer.cs), [`WavCaptureWriter`](Dev/src/Foundation.Core/WavCaptureWriter.cs), [`RgbaPngWriter`](Dev/src/Foundation.Core/RgbaPngWriter.cs), [`PixelOverlayCanvas`](Dev/src/Foundation.Core/PixelOverlayCanvas.cs).
- **Retro Media & Recomp Contracts (`Foundation.Retro v0.1.1`):** [`MediaContainerExtractor`](Dev/src/Foundation.Retro/MediaContainerExtractor.cs) (`.iso`/`.bin`+`.cue`/`.chd`/`.7z`/`.zip`, SHA-256 + `AssertPathContained`) and [`RecompContracts`](Dev/src/Foundation.Retro/RecompContracts.cs).
- **Installer Hardening (`Foundation.Installer v0.2.2`, +1 file):** Optional `AllowAutoStart = true` opt-out (`false` hides Startup checkbox & clamps `--autostart`) and [`FileLockRetryHelper`](Dev/src/Foundation.Installer/FileLockRetryHelper.cs).
- **Verification & Compliance:** Unit tests passed with `0` warnings; published SDK snapshot to downstream projects.

### `v0.4.0` — Wave 1: Studio/Suite Installer, AOT-Primary `.decomp` Contracts & Public-Repo Readiness
- **Installer & Runtime Environment (`Foundation.Installer`, `Foundation.Core`):** Studio/Suite default install root (`%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\`), opt-in `--migrate-legacy`, bounded retry self-delete trampoline, `InstallerProfileOption` (`Lightweight` / `Full`), `SecondaryPathPicker` (`--reengine-dir`, `--suite-dir`), and `AssertPathContained` containment.
- **Retro & Recomp Contracts (`Foundation.Retro`):** Added [`RecompTierAndBackendContracts.cs`](Dev/src/Foundation.Retro/RecompTierAndBackendContracts.cs) (`RecompExecutionTierMode` AOT-primary + `JitBringup` / `ThreadedIr`, `RecompRenderBackendIds`, `IDecompPackageManifest`, `AotCacheLayoutHelper`), replaced synthetic BIOS hashes with `IsVerifiedReferenceHash` metadata, and added `.ConfigureAwait(false)` in `MediaContainerExtractor.cs`.
- **Network, Sidecars & Launcher (`SafeNetworkTransfer`, `SevenZipAndZipInspector`, `RootLauncherHost`):** Sanitized `JobId` (`:` ADS fix), recovered HTTP `416`, excluded `400/401/403/404/410` from transient retries, enforced `ResponseHeadersTimeout`, added LZMA/Deflate64 `7z.exe` fallback + nested `::` inspection, and fixed `RootLauncherHost` `--wait`/`--open` blocking, relative arg rebasing, and Ctrl+C forwarding.
- **Unified `0.4.0` SemVer & Public-Repo Readiness:** Aligned all 7 `.csproj` assemblies, `@foundation/ui`, and `foundation_core` on `0.4.0`; deduplicated tripled `Foundation.UI` TS/TSX source copies into `Web\`; externalized secret/config templates (`Dev\.env.example`, `Dev\settings.example.json`); purged `14` stale `.pdb`s.

### `v0.4.1` — Wave 2: Modular Installer Wizard, Preview Z-Order & Profile/Secondary Forwarding
- **Modularized Installer & Preview Capture (`Foundation.Installer`):** Extracted `InstallerContracts.cs` and modular partials (`<= 400` SLOC), fixed composite back-to-front `CaptureWizardPreviewBitmap()` overdraw, decoupled `showLegacyMigration` visibility from `AllowLegacyMigration`, and forwarded `SelectedProfileId` / `SecondaryInstallDir` / `UmbrellaSubpath` / `MigrateLegacyChecked` into `InstallManifest`.
- **Packaging & Verification Tooling:** Added `--allow-sdk-zip` / `Foundation-v0.4.1.zip` release validation in `build_release_universal.py` & `sync_foundation_sdk.py` and composite `CapturePreviewBitmap` in [`DisposableWebViewHostForm.cs`](Dev/src/Foundation.UI/Desktop/DisposableWebViewHostForm.cs).

### `v0.4.1-hf.1` — Ecosystem Runtime Hygiene Purge & 4-Folder Normalization
- **Runtime Harness & Flag Purge:** Removed retired isolated runtime test directories, flags, and harnesses; standardized [`AppEnvironment`](Dev/src/Foundation.Core/AppEnvironment.cs) (`UiPresentationMode.TrayOrBackground`), [`InstallerRuntimeContext`](Dev/src/Foundation.Installer/InstallerRuntimeContext.cs), and [`InstallerEngine.CliAndRegistry.cs`](Dev/src/Foundation.Installer/InstallerEngine.CliAndRegistry.cs); split [`SevenZipAndZipInspector`](Dev/src/Foundation.Sidecars/SevenZipAndZipInspector.cs) & [`InstallerEngineCore`](Dev/src/Foundation.Installer/InstallerEngineCore.cs) cleanly under `<= 400` SLOC; added stale consumer test-harness cleanup in [`sync_foundation_sdk.py`](Dev/tools/sync_foundation_sdk.py); verified `33/33` `[Fact]` unit tests with `0` warnings.

### `v0.4.2` — Shared 4-Drive Sandbox Seams, Atomic Pseudo-Registry & Sandbox E2E Runner *(2026-11-16)*
- **`Foundation.Core` (`24` files):** Added [`KnownFolderPaths`](Dev/src/Foundation.Core/KnownFolderPaths.cs), [`SandboxEnvironmentSeam`](Dev/src/Foundation.Core/SandboxEnvironmentSeam.cs), [`IPlatformRegistry`](Dev/src/Foundation.Core/IPlatformRegistry.cs) / [`WindowsPlatformRegistry`](Dev/src/Foundation.Core/WindowsPlatformRegistry.cs) / [`JsonHivePlatformRegistry`](Dev/src/Foundation.Core/JsonHivePlatformRegistry.cs), [`SandboxDriveController`](Dev/src/Foundation.Core/SandboxDriveController.cs), and `AppEnvironment.Sandbox` + `PlatformRegistryFactory`.
- **`Foundation.Installer` (`17` files):** Wired 3-mode isolation (`ShouldSuppressHostMutations`, `ShouldSuppressLiveHostMutations`), `KnownFolderPaths` shortcuts, `IPlatformRegistry` ARP uninstall metadata, `FileLockRetryHelper`, `WindowsShortcutWriter.SanitizeShortcutFileName`, DPI-safe `_lblTitle`/`_lblSub` clamping, and `FOUNDATION_SUPPRESS_MODALS=1` modal suppression.
- **Python `foundation_core` (`0.4.2`) & Packaging:** Added pure-stdlib [`sandbox_seam.py`](Dev/src/foundation_core/sandbox_seam.py) and [`pseudo_registry.py`](Dev/src/foundation_core/pseudo_registry.py), [`sandbox_ctl.py`](Dev/tools/sandbox_ctl.py), [`run_sandbox_e2e.py`](Dev/tools/run_sandbox_e2e.py), narrowed [`sync_foundation_sdk.py`](Dev/tools/sync_foundation_sdk.py), and aligned `build_release_universal.py --allow-sdk-zip`.
- **Verification & Compliance:** `dotnet test Dev\Foundation.sln -c Release -p:TreatWarningsAsErrors=true` (`40/40` passed, `0` warnings), Python `unittest` & `pytest` (`8/8` passed), zero critical/high static findings, live 4-drive E2E dogfood (`12/12`, `hostDiffCount=0`).

### `v0.5.0` — Declarative Markup Installer, `Foundation.Automation` Loopback & Pre-Public Readiness *(2026-11-16)*
- **Deliverables:** `InstallerManifestStore.cs` (`schemaVersion: 2`), `HKCU\Software\StreamItAll\LastInstallBaseDir` registry precedence, `Foundation.Automation` (`AutomationLoopbackServer`, `AutomationCliHelper`, `Win32VirtualInputDriver`, `WindowSnapshotService`), and public OSS hygiene (`SECURITY.md`, public Quickstart).
- **Verification & Compliance:** `45/45` C# Release tests (`0` warnings), `8/8` Python `unittest`, zero critical/high static findings, strict 6-file release validation passed.

### `v0.5.1` — ISO/IEC 18004 QR Code Suite (`Foundation.Core.QrCodeEncoder`) *(2026-11-19)*
- **Added:** Zero-dependency `Foundation.Core.QrCodeEncoder` (`QrCodeEncoder.cs`, V1–V10 ECC-M up to 213 UTF-8 bytes, GF(256) Reed-Solomon, 8-mask scoring, 4-module quiet zone, `RenderSvg`) + `FoundationQrCodeTests` and unified SDK `v0.5.1` bump.

### `v0.5.2` — Installer Path-Poisoning Fix, `suiteId`, Extensible Launcher & 7-File Release Parity *(2026-11-19)*
- **Deliverables:** `IsVersionLeafSegment` non-doubling & self-healing `LastInstallBaseDir`, canonical `suiteId` (`suiteName` fallback), extensible `RootLauncherOptions`, `--allow-portable-zip` 7-file release validation, focus-free `InvokeManagedMouse`, and non-parallel test isolation (`50/50` C#, `13/13` Python).

### `v0.5.3` — Ownership Guard, Dual Online/Offline Setup, Core Helpers, WinForms-Free Automation & GitHub Pages *(2026-11-20)*
- **Deliverables:** `RollingFileBackupStore`, VRR late-latch timing in `HighResFramePacer`, HTTPS/SSRF/DNS/redirect validation in `SafeNetworkTransfer`, `ProductName` ownership guard (`FOREIGN APP DETECTED`) + wildcard-free cleanup, Dual Online/Offline Setup (`--offline-bundle`), WinForms-free `IAutomationSurfaceTarget` + `GET /v1/snapshot.png`, `ExeboxStreamHelpers.ps1` / `exebox_stream.py`, hardlink-safe release tooling, and visibility-gated `.github/workflows/pages.yml` (`55/55` C#, `16/16` Python, `12/12` Stage 2 sandbox).

### `v0.5.4` — CI Sandbox 8.3/USERPROFILE Fixes, Release Workflow Hardening & Python SDK Parity *(2026-11-22)*
- **Deliverables:** Windows 8.3 `GetLongPathNameW` + child-`USERPROFILE` unmapping (`SandboxEnvironmentSeam`, `KnownFolderPaths`, `sandbox_seam.py`), transient Drive scan exclusions in `sandbox_ctl.py`, fail-fast `release.yml` guards, and Python `foundation_core` upgrades (`cli.py` `add_help=False` + `config/settings.json`, `GranularSettingsStore`, `SqliteSchemaMigrationRunner`, `PidSentinelLock` `±2s` reuse guard, `RollingLogSink`, Zero-PII `CrashDumper`, pure-stdlib `AutomationLoopbackServer`) (`55/55` C#, `23/23` Python, `12/12` Stage 2 sandbox).

### `v0.5.5` — Windows CI `tempfile` `.resolve()` 8.3 Stability & SDK Lockstep *(2026-11-22)*
- **Deliverables:** Normalized `tempfile.TemporaryDirectory()` fixture paths with `.resolve()` for Windows CI runners (`55/55` C#, `23/23` Python, `0` warnings) and unified `0.5.5` SDK lockstep.

### `v0.5.6` — NativeAOT Win32 Installer, Python DPAPI & Sidecars, Lock-Free WAL Optimization & Release Hardening *(2026-11-28)*
- **NativeAOT Win32 `Foundation.Installer` & Ergonomics:** Replaced WinForms with pure Win32 P/Invoke UI (`IsAotCompatible=true`, `FoundationInstallerJsonContext`, `Win32Native`, `Win32FolderPicker`, `~7.04 MB` standalone binary with zero `.NET` runtime dependency), extracted slim embedded `.uninstaller.exe.gz` resources, guarded Start Menu file collisions, collapsed single-profile radio rows (`Top == 34`), and added `payloadFallbackUrls` + `betaPin` + single-wrapper unwrapping (`InstallerEngine.OnlinePayload.Extraction.cs`).
- **Core & Python Parity (`Foundation.Core`, `foundation_core`):** Added `FoundationCoreJsonContext`, GitHub-first + R2-fallback `ReleaseUpdaterClient`, lock-free DB-recreation-safe `SqliteWalPool` WAL init + `ResetJournalModeCache()` (superseding PR #1), pure-stdlib `foundation_core.secrets` (`DpapiSecretStore` wire-compatible with C#) and `foundation_core.sidecars` (`HashFilePlacement`, `compute_crc32_hex`, `compute_file_hashes`, `.hash` v1, `.archive_hashes.json`, `.hklock`), plus WebSocket Test Viewer Hub streaming (`http://127.0.0.1:5005`) and `README.md` SEO/onboarding examples.
- **Verification & Compliance:** `63/63` C# Release tests (`0` warnings), `29/29` Python `unittest` (`0` warnings), zero critical/high static findings, NativeAOT PE dependency + visual PNG verification, and `run_sandbox_e2e.py --no-subst` (`12/12`, `hostDiffCount=0`).

---

## 5. Icebox & Cutting-Room Floor

_No concepts are currently parked here. Ideas cut from a milestone are recorded in this section._
