# Foundation (`v0.4.1`) — Strategic Roadmap

> **Active Version:** `v0.4.1` (Unified SemVer across all 7 C# assemblies, `@foundation/ui v0.4.1`, and `foundation_core v0.4.1`)
> **Pinned Binary Hub:** `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\` (`51` published files)

---

## Completed Milestones

### ✅ `v0.1.0` — Core & Web/Python Substrate
- **C# Core & UI (`Foundation.Core`, `Foundation.UI`):** `SandboxEnvironment`, `GranularSettingsManager`, `DpapiSecretStore`, `SqliteWalPool`, `PidSafeLockfile`, `HeartbeatLogger`, `CrashBundleService`, `JobObjectGuard` + `DualJobCancellationCoordinator`, `SafeNetworkTransfer`, `ToolResolver`, `HybridModuleLoader`, `IFoundationModule`, WinForms WebView2/STA/Tray helpers, and `foundation-terminal.js`/`css`.
- **Installer & Hub:** Tier 2 `InstallerEngineCore` + `InstallerThemeOptions` (`v0.2.0`), `foundation_core` (`v0.1.0`–`v0.1.2` in [`Dev\src\foundation_core\`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/)), and [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py).

### ✅ `v0.2.0` — Universal Tooling & Component Expansion
- **Root Dev Tooling (`Dev\tools\`):** [`verify_sandbox_universal.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/verify_sandbox_universal.py), [`build_release_universal.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/build_release_universal.py), [`notion_task_helper.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/notion_task_helper.py).
- **UI & Primitives (`@foundation/ui v0.2.0`):** `CliArgumentHelper`, `BoundedTailStream`, `CappedSharedLogSink`, `UniversalDataTable`, `ResponsiveTable`, `SchemaSettingsRenderer`, `ModalDialog`, `ConfirmActionModal`, `StyledTooltip`, `GlobalTerminalDrawer`, `TerminalToolbarAndFilters`.

### ✅ `v0.3.0` — Consolidation & Tier 1 Installer Wizard (`Installer v0.2.1`)
- **New C# Assemblies:**
  - [`Foundation.Retro`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/) — 41-platform `ConsoleSystemCatalog`, `DatTitleNormalizer`, `NoIntroRedumpDatParser`, `HeaderlessRomStripper`, `MagicBytePlatformDetector`, `SoftPatchEngine`.
  - [`Foundation.Sidecars`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Sidecars/) — `SinglePassStreamHasher`, `SevenZipAndZipInspector`, `HashSidecarService` (`.hash` v1 / `COMPOSITE_SHA1`), `HkLockSentinel` (`.hklock` / `.coexistence.json` v2).
  - [`Foundation.Server`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Server/) — `TieredAuthEngine`, `FoundationTieredAuthMiddleware`, `SisterAppStandardEndpoints`.
  - [`Foundation.Launcher`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Launcher/) — `RootLauncherHost` (`AttachConsole(-1)` + fail-closed self-spawn guard).
- **UI Additions (`Dev\src\Foundation.UI\Web\components\`):** `CoexistenceWarningBanner`, `HybridPathPicker`, `SidebarNavRail` (available via direct `@foundation/ui/components/<Name>` imports).
- **Installer Upgrade (`v0.2.1`):** Added Tier 1 [`StandardInstallerHost`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/StandardInstallerHost.cs), [`InstallerAppDescriptor`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngine.Core.cs), `InstallerWizardForm`, and `WindowsShortcutWriter`.
- **Verification & Compliance:** `18` `[Fact]` unit tests across 4 files in [`Dev\tests\Foundation.Tests\`](file:///<PROJECTS_ROOT>/Foundation/Dev/tests/Foundation.Tests/); `100%` of files in `Dev\` `<= 400 LOC` (max `383` lines); published to `<PROJECTS_ROOT>\SharedLibs\Foundation\v0.3.0`.

### ✅ `v0.3.1` — Recomp Timing/Audio/PNG Primitives, BYO Media Extractor & Installer `v0.2.2`
- **Core Primitives (`Foundation.Core` `net8.0`, +5 files):** [`HighResFramePacer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HighResFramePacer.cs), [`AudioRingBuffer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AudioRingBuffer.cs), [`WavCaptureWriter`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/WavCaptureWriter.cs), [`RgbaPngWriter`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/RgbaPngWriter.cs), [`PixelOverlayCanvas`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PixelOverlayCanvas.cs), and `SandboxEnvironment.VisibleTest` (`--visible-test` / `VISIBLE_TEST=1`).
- **Retro Media & Recomp Contracts (`Foundation.Retro v0.1.1`, +3 files):** [`MediaContainerExtractor`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/MediaContainerExtractor.cs) (`.iso`/`.bin`+`.cue`/`.chd`/`.7z`/`.zip`, SHA-256 + `AssertPathContained`), [`RecompContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompContracts.cs), and [`SaveStateFixtureRunner`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/SaveStateFixtureRunner.cs).
- **Installer Hardening (`Foundation.Installer v0.2.2`, +1 file):** Optional `AllowAutoStart = true` opt-out (`false` hides Startup checkbox & clamps `--autostart`), [`FileLockRetryHelper`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/FileLockRetryHelper.cs), safe `--test-mode --install-dir` containment in `\Sandbox\`/`%TEMP%`, and `--visible-test` auto-closing showcase.
- **Verification & Compliance:** Implemented per [`v0.3.1.2026-11-13.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/plans/v0.3.1.2026-11-13.md) (`@dean` Gate 1/2 `PASS`; `@inspecter` live `Sandbox\` + `--visible-test` `PASS` in [`0.3.1.2026-11-13_1227.-.v031-sandbox-and-visible-test-verify.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/0.3.1.2026-11-13_1227.-.v031-sandbox-and-visible-test-verify.md)); `26/26` `[Fact]` unit tests (`0` warnings); `63` files published to `SharedLibs\Foundation\v0.3.1` & `Latest` and synced to `reStation` & `KingsFieldRecomp`.

### ✅ `v0.4.0` — Wave 1: Studio/Suite Installer, AOT-Primary `.decomp` Contracts & Public-Repo Readiness
- **Installer & Sandbox (`Foundation.Installer`, `Foundation.Core`):** Studio/Suite default install root (`%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\`), opt-in `--migrate-legacy`, bounded retry self-delete trampoline, `InstallerProfileOption` (`Lightweight` / `Full`), `SecondaryPathPicker` (`--reengine-dir`, `--suite-dir`), and `ResolveSiblingSandboxRoot` fail-closed sibling containment.
- **Retro & Recomp Contracts (`Foundation.Retro`):** Added [`RecompTierAndBackendContracts.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompTierAndBackendContracts.cs) (`RecompExecutionTierMode` AOT-primary + `JitBringup` / `ThreadedIr`, `RecompRenderBackendIds`, `IDecompPackageManifest`, `AotCacheLayoutHelper`), replaced synthetic BIOS hashes with `IsVerifiedReferenceHash` metadata, and added `.ConfigureAwait(false)` in `MediaContainerExtractor.cs`.
- **Network, Sidecars & Launcher (`SafeNetworkTransfer`, `SevenZipAndZipInspector`, `RootLauncherHost`):** Sanitized `JobId` (`:` ADS fix), recovered HTTP `416`, excluded `400/401/403/404/410` from transient retries, enforced `ResponseHeadersTimeout`, added LZMA/Deflate64 `7z.exe` fallback + nested `::` inspection, and fixed `RootLauncherHost` `--wait`/`--open`/`--visible-test` blocking, relative arg rebasing, and Ctrl+C forwarding.
- **Unified `0.4.0` SemVer & Public-Repo Readiness:** Aligned all 7 `.csproj` assemblies, `@foundation/ui`, and `foundation_core` on `0.4.0`; deduplicated tripled `Foundation.UI` TS/TSX source copies into `Web\`; externalized Notion IDs (`Dev\.env.example`, `Dev\settings.example.json`); purged `14` stale `.pdb`s; verified `33/33` `[Fact]` tests (`0` warnings) and `--visible-test` wizard UI; archived all `12` diagnostic reports (`0` open reports).

### ✅ `v0.4.1` — Wave 2: Visible-by-Default `--test-mode`, Installer Preview Z-Order & Profile/Secondary Forwarding
- **Visible-by-Default `--test-mode` Contracts (`Foundation.Core`, `foundation_core`, `Foundation.UI`, `Dev\tools\`):** Added `ExplicitVisibleTest` / `EffectiveVisibleTest` and `HEADLESS` / `VISIBLE_TEST` fallbacks across [`SandboxEnvironment.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs) and [`foundation_core\cli.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/cli.py); added [`VisibleTestWindowHelper.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/VisibleTestWindowHelper.cs) + `SandboxEnvironment`-aware [`DisposableWebViewHostForm.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/DisposableWebViewHostForm.cs), foreground `--new-window` browser verification in `verify_sandbox_universal.py`, and `--allow-sdk-zip` / `Foundation-v0.4.1.zip` release validation in `build_release_universal.py` & `sync_foundation_sdk.py`.
- **Installer Real Visible Auto-Run, Preview Z-Order & Forwarding (`Foundation.Installer`):** Extracted `InstallerContracts.cs`, `InstallerSandboxContext.cs`, and `InstallerWizardForm.VisibleTest.cs` (`<= 400 LOC`), removed artificial 3s preview dwell during `--test-mode` auto-run, fixed composite back-to-front `CaptureWizardPreviewBitmap()` overdraw, decoupled `showLegacyMigration` visibility from `AllowLegacyMigration`, and forwarded `SelectedProfileId` / `SecondaryInstallDir` / `UmbrellaSubpath` / `MigrateLegacyChecked` into `InstallManifest`.
- **Verification & Compliance:** Implemented per [`0.4.1.2026-11-15.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/plans/0.4.1.2026-11-15.md) (`@dean` Gate 1 & Gate 2 `PASS`; `40/40` `[Fact]` tests, `0` warnings; live `@inspecter-games` `Sandbox\` visible-test `VerifyHarness.exe` run verified centered `TopMost` window, `1064 ms` exit `0`, visual PNG inspection, `--test-mode --headless` fallback, `6/6` `--run-suite`, and `0` errors across all 3 log tiers); archived both `0.4.0` installer reports (`0` open reports); published `51` files to `SharedLibs\Foundation\v0.4.1` & `Latest` and staged `Releases\Latest\Foundation-v0.4.1.zip`.

---

## Remaining Upstream Consumer Backlog (`v0.4.2+`)

- **Open Diagnostic Reports ([`Dev\docs\reports\`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/)):** `0` open reports (`17` historical reports reconciled and archived in `Dev\docs\reports\archive\`).
- **Consumer Fleet Visible-by-Default `--test-mode` Sync (`15` projects):** Propagate Foundation `v0.4.1` visible-by-default `--test-mode` contracts and foreground browser CDP harnesses across the 15 audited ecosystem workspaces.

---

## Future Horizon (`v0.5.0+` / `v1.0.0`)

- Promote `Foundation.Core`, `Foundation.Retro`, and `Foundation.Sidecars` as versioned NuGet `.nupkg` artifacts in a local feed alongside `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest`.
- Add Python `pytest` parity suite mirroring the `18` C# `[Fact]` invariants for `foundation_core`.
