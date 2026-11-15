# Foundation (`v0.4.1-hf.1`) — Strategic Roadmap

> **Active Version:** `v0.4.1-hf.1` (Unified SemVer across all 7 C# assemblies, `@foundation/ui v0.4.1-hf.1`, and `foundation_core v0.4.1-hf.1`)
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

### ✅ `v0.4.1-hf.1` — Ecosystem Unit-Test-Only Runtime Hygiene Purge & Tier A 5-Folder Normalization
- **Runtime Harness & Flag Purge:** Removed retired isolated runtime test directories, flags, and harnesses; standardized [`AppEnvironment`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AppEnvironment.cs) (`UiPresentationMode.TrayOrBackground`), [`InstallerRuntimeContext`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerRuntimeContext.cs), and [`InstallerEngine.CliAndRegistry.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngine.CliAndRegistry.cs); split [`SevenZipAndZipInspector`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Sidecars/SevenZipAndZipInspector.cs) & [`InstallerEngineCore`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngineCore.cs) cleanly under `<= 400 LOC`; added stale consumer test-harness cleanup in [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py); verified `33/33` `[Fact]` unit tests with `0` warnings.

---

## Remaining Upstream Consumer Backlog (`v0.4.2+`)

- **Open Diagnostic Reports ([`Dev\docs\reports\`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/)):** `1` open report ([`0.4.1.2026-11-15.-.installer-shortcut-colon-and-header-badge-overlap.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/0.4.1.2026-11-15.-.installer-shortcut-colon-and-header-badge-overlap.md): shortcut filename colon sanitization & header title/badge bounding); `16` historical reports archived in `Dev\docs\reports\archive\`.
- **Consumer Fleet `0.4.1-hf.1` Sync (`15` projects):** Sync clean `Foundation v0.4.1-hf.1` SDK artifacts and strip stale test harnesses across audited ecosystem workspaces via `sync_foundation_sdk.py --sync-consumer`.

---

## Future Horizon (`v0.5.0+` / `v1.0.0`)

- Promote `Foundation.Core`, `Foundation.Retro`, and `Foundation.Sidecars` as versioned NuGet `.nupkg` artifacts in a local feed alongside `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest`.
- Add Python `pytest` parity suite mirroring the C# `[Fact]` invariants for `foundation_core`.
