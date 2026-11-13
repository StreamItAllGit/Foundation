# Foundation (`v0.3.1`) — Strategic Roadmap

> **Active Version:** `v0.3.1` (`Foundation.Retro v0.1.1`, `Foundation.Installer v0.2.2`, `@foundation/ui v0.2.0`, `foundation_core v0.1.2`)
> **Pinned Binary Hub:** `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\` (`63` published files)

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

---

## Remaining Upstream Consumer Backlog (`v0.3.2+`)

- **Remaining Open Reports ([`Dev\docs\reports\`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/)):**
  1. **Network Stream ID & HTTP 416 Hardening:** [`0.2.1.20261109.0835.-.safe-network-transfer-ads-and-416.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/0.2.1.20261109.0835.-.safe-network-transfer-ads-and-416.md) — Handle NTFS ADS colons (`:Zone.Identifier`) and HTTP `416 Range Not Satisfiable`.
  2. **Real BIOS Checksum Verification:** [`0.2.1.20261109.0856.-.console-system-catalog-synthetic-bios-hashes.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/0.2.1.20261109.0856.-.console-system-catalog-synthetic-bios-hashes.md) — Replace placeholder BIOS MD5/SHA1 digests on secondary platforms in `ConsoleSystemCatalog.cs`.
  3. **ZIP LZMA & Nested Archive Extraction:** [`0.2.1.20261109_005500.-.zip-lzma-fallback-and-nested-extract.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/0.2.1.20261109_005500.-.zip-lzma-fallback-and-nested-extract.md) — Add `7z` fallback for LZMA-compressed ZIP entries and nested archive inspection.
  4. **ShadowLibrary Upstream SDK Gaps:** [`0.2.1.20261110_1603.-.shadowlibrary-upstream-sdk-gaps.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/0.2.1.20261110_1603.-.shadowlibrary-upstream-sdk-gaps.md) — Consolidate barrel exports (`CoexistenceWarningBanner`, `HybridPathPicker`, `SidebarNavRail`) into `@foundation/ui/index.ts`.
  5. **Deferred Non-Blocking `P3` (`MediaContainerExtractor.cs`):** Add `.ConfigureAwait(false)` on `await using` stream disposals (`L171-L173`, `L289-L296`) logged in [`0.3.1.2026-11-13_1227.-.v031-sandbox-and-visible-test-verify.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/0.3.1.2026-11-13_1227.-.v031-sandbox-and-visible-test-verify.md) to prevent sync-over-async deadlocks if invoked on a UI `SynchronizationContext`.

---

## Future Horizon (`v0.4.0+`)

- Promote `Foundation.Core`, `Foundation.Retro`, and `Foundation.Sidecars` as versioned NuGet `.nupkg` artifacts in a local feed alongside `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest`.
- Add Python `pytest` parity suite mirroring the `18` C# `[Fact]` invariants for `foundation_core`.
