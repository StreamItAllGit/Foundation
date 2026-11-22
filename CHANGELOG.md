# Changelog

All notable changes to `Foundation` will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to strict 3-part [Semantic Versioning (`MAJOR.MINOR.PATCH`)](https://semver.org/spec/v2.0.0.html) per [Dev Bible Standard 03 §1](file:///<PROJECTS_ROOT>/_DevBible/03_VERSIONING_AND_RELEASES.md).

## [Unreleased]

## [0.5.5] - 2026-11-22
### Fixed
- **CI Windows 8.3 Short-Path Test Assertion (`test_std19_31_35_43_and_automation.py`):** Normalized `tempfile.TemporaryDirectory()` fixture paths with `.resolve()` across test cases so `GranularSettingsStore` quarantine and split-file assertions match cleanly on GitHub `windows-latest` runners (`RUNNER~1` vs. `runneradmin`).

### Changed
- **Unified `0.5.5` SDK Version Lockstep:** Incremented `.NET 8` assemblies (`SdkVersion = "0.5.5"`), `@foundation/ui` (`0.5.5`), `foundation_core` (`0.5.5`), `Dev\tools\`, manifests, and Manual Shelf to `0.5.5` while preserving immutable shipped `0.5.4` release artifacts.

## [0.5.4] - 2026-11-22
### Added
- **Standard 19 Granular Settings (`foundation_core.settings`):** Added `GranularSettingsStore` and `SettingsLoadResult` alongside existing `ThreeFileSettingsStore`, supporting `<data_dir>/config/settings.json` + optional `settings.<domain>.json` splits, 20-snapshot `config_history/` pruning, `quarantine/` recovery (`.bak -> config_history -> default`), sequential `schemaVersion` migrations, and unknown top-level field preservation.
- **SQLite WAL Snapshot Integrity & `PRAGMA user_version` (`foundation_core.db`):** Added `read_only=True` (`?mode=ro`, `uri=True`, zero `mkdir`, `PRAGMA query_only = ON`) to `open_wal_sqlite`, plus `split_sql_statements`, `vacuum_backup_sqlite`, `verify_snapshot_integrity`, `apply_sql_migrations`, explicit `BEGIN IMMEDIATE`/`COMMIT`/`ROLLBACK` transaction control, and automatic `_foundation_schema_migrations` -> `PRAGMA user_version` legacy bridge.
- **Zero-PII Standard 43 Crash Bundles & Standard 35 Logging (`foundation_core.logging`, `crash_dumper`):** Upgraded `RollingLogSink` with Standard 35 UTC ms lines, thread-safe 50-line `recent_lines()` ring buffer, and 30-day `prune_old_logs()`; added `sanitize_pii_text` (`[USER_HOME]`, `[REDACTED_TOKEN]`) and Zero-PII `camelCase` `crashes/crash_*.json` bundles with `max 20` retention in `CrashDumper`.
- **Pure-Stdlib Python Automation Server (`foundation_core.automation`):** Added `AutomationLoopbackServer`, `AutomationServerOptions`, `AutomationDisabledError` (`AUTOMATION_DISABLED_EXIT_CODE = 2`), `parse_automation_cli_args` (`0 <= port <= 65535`, `<APP>_TEST_AUTOMATION=1` fail-closed), and stdlib `encode_rgba_png`, matching C# `Foundation.Automation` wire contracts across `/v1/{health,state,snapshot,snapshot.png,shutdown}`.
- **Subcommand Help Passthrough Builder (`foundation_core.cli`):** Added `build_standard_cli_parser(..., add_help=False)` forwarding downstream subcommand `--help` arguments via `extra_args`.

### Changed
- **Intentional Standard 19 CLI Defaults (`foundation_core.cli`):** Switched `parse_standard_cli_flags` defaults to `add_help=False` and `--config` `<data_dir>/config/settings.json` (pass `add_help=True` to opt back into top-level help interception).
- **Unified `0.5.4` SDK Version Lockstep:** Aligned `.NET 8` assemblies (`SdkVersion = "0.5.4"`), `@foundation/ui` (`0.5.4`), `foundation_core` (`0.5.4`), `Dev\tools\sandbox_ctl.py` / `sync_foundation_sdk.py`, `Dev\app.manifest.json`, and `Dev\Directory.Build.props`.
- **Standard 31 PID-Reuse Lock Hardening (`foundation_core.locks`, `pseudo_registry`):** Centralized `utc_iso_ms`, `parse_iso_utc`, `get_process_state`, and `get_process_start_time_utc` in `locks.py` (shared by `pseudo_registry.py`); upgraded `PidSentinelLock` with `hostName` + `appId` (accepting legacy `hostname`/`host`/`ownerAppId` on read), 4-step `±2s` PID-reuse eviction, `inspect_lock`, and `refresh_heartbeat`.
- **Release Workflow Fail-Fast Guards (`.github\workflows\release.yml`):** Scoped `$ErrorActionPreference = 'Stop'`, `$PSNativeCommandUseErrorActionPreference = $true`, and `$LASTEXITCODE` checks cleanly to the `meta` build/stage step while leaving `sign`'s `gh release view` probe intact.

### Fixed
- **CI Stage 2 Sandbox 8.3 Short-Path & Child-`USERPROFILE` Collisions (`sandbox_seam.py`, `SandboxEnvironmentSeam.cs`, `KnownFolderPaths.cs`):** Resolved CI failure `44810959469` by normalizing Windows 8.3 short paths (`GetLongPathNameW` / `RUNNER~1` -> `runneradmin`) and unmapping redirected child-`USERPROFILE` `Desktop`/`Documents`/`AppData` paths before host-collision checks.
- **Local Git Tag Integrity:** Repaired corrupt local `refs/tags/v0.4.1-hf.1` from `origin` (`git fsck --no-progress` clean).

## [0.5.3] - 2026-11-20
### Added
- **`Foundation.Core` Rotating Backups, Frame-Pacer Late-Latch & HTTPS/SSRF Validation:**
  - Added [`RollingFileBackupStore.cs`](Dev/src/Foundation.Core/RollingFileBackupStore.cs) (`RollingBackupEntry`, `RollingBackupCommitResult`, `RollingFileBackupStore`, `DefaultMaxBackups = 10`) providing atomic `.tmp -> Flush(true) -> File.Move` commits, `.bak` mirrors, monotonic sequence collision guards, `AppEnvironment.AssertPathContained` restore validation, and bounded history pruning.
  - Extended [`HighResFramePacer.cs`](Dev/src/Foundation.Core/HighResFramePacer.cs) with VRR late-latch timing (`LateLatchMarginMs`, `LastLateLatchWaitMs`, `AverageLateLatchWaitMs`, `BeginLateLatchFrame()`, `WaitBeforePresent()`, `WaitBeforePresentAsync()`).
  - Added [`SafeNetworkTransfer.Validation.cs`](Dev/src/Foundation.Core/SafeNetworkTransfer.Validation.cs) and updated [`SafeNetworkTransfer.cs`](Dev/src/Foundation.Core/SafeNetworkTransfer.cs) with `DownloadVerifiedAsync`, `ValidateRequestUri`, `IsBlockedSsrfAddress`, `ExpectedSizeBytes` enforcement, `RequireHttps`, `AllowLoopbackHttpForTesting`, and bounded `MaxRedirects = 5`.
- **`Foundation.Installer` Dual Online/Offline Setup & P1 `ProductName` Ownership Guard:**
  - Added [`InstallerEngine.Ownership.cs`](Dev/src/Foundation.Installer/InstallerEngine.Ownership.cs) (`InstallDirectoryOwnership`, `InstallOwnershipCheckResult`, `IsOwnInstallManifest`, `TryReadInstallManifest`, `CheckDirectoryOwnership`, `BuildForeignOwnershipErrorMessage`, `CheckForeignOwnershipForTheme`) across Tier 1 (`InstallerEngine`) and Tier 2 (`InstallerEngineCore`), refusing silent overwrites or uninstalls against foreign product directories (`ProductMismatch:`) before stopping processes or touching files, and displaying a red `FOREIGN APP DETECTED` badge in [`InstallerWizardForm.ExecutionAndLogs.cs`](Dev/src/Foundation.Installer/InstallerWizardForm.ExecutionAndLogs.cs).
  - Added [`InstallerEngine.OnlinePayload.cs`](Dev/src/Foundation.Installer/InstallerEngine.OnlinePayload.cs) plus `InstallerDeliveryKinds` (`onlineSetup`, `offlineSetup`, `portableZip`), `InstallerPayloadAsset`, `OfflineBundlePath` (`--offline-bundle <zipOrDir>`), `PayloadDownloadUrl`, and `PayloadAssets` across [`InstallerContracts.cs`](Dev/src/Foundation.Installer/InstallerContracts.cs), [`InstallerManifestStore.cs`](Dev/src/Foundation.Installer/InstallerManifestStore.cs), [`InstallerThemeOptions.cs`](Dev/src/Foundation.Installer/InstallerThemeOptions.cs), [`StandardInstallerHost.cs`](Dev/src/Foundation.Installer/StandardInstallerHost.cs), and [`InstallerEngine.VerifyAndUpgrade.cs`](Dev/src/Foundation.Installer/InstallerEngine.VerifyAndUpgrade.cs), with profile filtering, safe `relativeSubdir` extraction, HTTPS + SHA-256 + byte-size verification, and 3-part offline remediation messages.
- **`Foundation.Automation` WinForms-Free Surface Contract & Live `GET /v1/snapshot.png` Streaming:**
  - Moved WinForms `Control` coupling out of [`AutomationContracts.cs`](Dev/src/Foundation.Automation/AutomationContracts.cs) into [`AutomationCliHelper.cs`](Dev/src/Foundation.Automation/AutomationCliHelper.cs) (`IAutomationSurfaceTarget`, `WinFormsAutomationSurfaceTarget`, `AutomationCliHelper.TryCreateForSurface`), and wired `_surfaceTarget` + in-memory `GET /v1/snapshot.png` backed by `WindowSnapshotService.CaptureSnapshotPngBytes` across [`AutomationLoopbackServer.cs`](Dev/src/Foundation.Automation/AutomationLoopbackServer.cs) and [`WindowSnapshotService.cs`](Dev/src/Foundation.Automation/WindowSnapshotService.cs).
  - Added [`ExeboxStreamHelpers.ps1`](Dev/tools/ExeboxStreamHelpers.ps1) and [`exebox_stream.py`](Dev/tools/exebox_stream.py) shared Stage 2 live GUI UDP streaming helpers (`Get-ExeboxDesktopName` truthfulness checks, `Initialize-ExeboxTestViewerPreflight`, `Start-ExeboxLiveVlcStream` / `Stop-ExeboxLiveVlcStream`).
- **Dual-Installer Release Tooling, Public Docs Site & Test Coverage:**
  - Extended [`build_release_universal.py`](Dev/tools/build_release_universal.py) with `--allow-dual-installer` (8-file Online+Offline+Portable bundle validator + `-Offline-Setup-` leak guard), lowercase SHA-256 hex output in `compute_sha256()`, and `--stage-sdk-zip`.
  - Added visibility-gated [`.github/workflows/pages.yml`](.github/workflows/pages.yml) static documentation publisher and SignPath/unsigned SDK ZIP CI staging updates in [`.github/workflows/release.yml`](.github/workflows/release.yml).
  - Added [`FoundationV053ContractsTests.cs`](Dev/tests/Foundation.Tests/FoundationV053ContractsTests.cs) (`55/55` C# Release tests, `0` warnings) and expanded [`test_build_release_universal.py`](Dev/tests/foundation_core/test_build_release_universal.py) & [`test_sandbox_and_registry.py`](Dev/tests/foundation_core/test_sandbox_and_registry.py) (`16/16` Python tests `OK`), plus live Stage 2 GUI `InstallerWizardForm` + `run_sandbox_e2e.py --offline-bundle` verification.

### Changed
- **Unified `0.5.3` SemVer Lockstep:** Aligned `Directory.Build.props`, `Dev\app.manifest.json`, `Foundation.UI.csproj`, `ShellBrandingOptions.cs`, `Web\package.json`, `foundation_core`, `sandbox_ctl.py`, `sync_foundation_sdk.py`, test suites, `active.state`, Root Quartet, and Manual Shelf on `0.5.3`.
- **Stage 2 Child Environment & Code Signing Policy (`Dev\tools\run_sandbox_e2e.py`, `README.md`):** Preserved Windows OS path env keys (`SystemDrive`, `ProgramData`, `ProgramFiles*`, `CommonProgramFiles*`, `ALLUSERSPROFILE`, `PUBLIC`) via `_env_lookup_ci` and forwarded `--offline-bundle` in `s02`/`s09`; added transparent public Code Signing Policy in `README.md`.

### Fixed
- **Self-Delete Trampoline & Wildcard Uninstall Hardening (`Foundation.Installer`):** Removed `"*-Uninstall.exe"` wildcards from both [`InstallerEngine.UninstallAndShortcuts.cs`](Dev/src/Foundation.Installer/InstallerEngine.UninstallAndShortcuts.cs) and `BuildSelfDeleteTrampolineCommand` in [`InstallerEngine.CliAndRegistry.cs`](Dev/src/Foundation.Installer/InstallerEngine.CliAndRegistry.cs), scoping cleanup strictly to `Descriptor.UninstallerExeName`.
- **NTFS Hardlink In-Place Overwrite Bug (`Dev\tools\build_release_universal.py`, `Dev\tools\sync_foundation_sdk.py`):** Replaced in-place file writes with atomic `.tmp -> os.replace` in `generate_sha256sums()` (`_atomic_write_text`) and unlinked existing destinations before zip writes in `stage_sdk_release_zip()` so regenerating `Latest\SHA256SUMS.txt` never corrupts hardlinked prior releases (`Releases\v0.5.2\SHA256SUMS.txt` repaired and re-verified).
- **`SafeNetworkTransfer` Redirect SSRF, DNS IP & TLS Protocol Hardening (`Foundation.Core`):** Disabled implicit redirects (`AllowAutoRedirect = false`), enforced `Tls12 | Tls13`, added pre-connect `ConnectCallback` DNS IP filtering and hop-by-hop `SendWithValidatedRedirectsAsync` re-validation, rejecting HTTP downgrades and link-local/metadata IPs (`169.254.0.0/16`, `::ffff:169.254.*`, `fe80::/10`, `fd00:ec2::254`, `metadata.google.internal`, `metadata.goog`).

## [0.5.2] - 2026-11-19
### Added
- **Canonical `suiteId` Manifest Support (`Foundation.Installer`):**
  - Added `[JsonPropertyName("suiteId")]` (`SuiteId`) and `EffectiveSuiteId` / `ResolveEffectiveSuiteId()` on [`InstallerMarkupManifest`](Dev/src/Foundation.Installer/InstallerManifestStore.cs), preferring canonical `suiteId` while retaining `suiteName` as a non-`[Obsolete]` legacy JSON fallback and propagating both through `ApplyToDescriptor` and `ToThemeOptions`.
- **Extensible Launcher Contracts (`Foundation.Launcher`):**
  - Added zero-dependency [`RootLauncherOptions`](Dev/src/Foundation.Launcher/RootLauncherOptions.cs) (`ExtraSyncWaitFlags`, `ExtraValueOptions`, `ExtraPathValueOptions`) and backward-compatible `RootLauncherHost.Run`, `ShouldWaitForChild`, and `NormalizeRelativePathArgs` overloads, plus built-in suite flags (`--diagnose`, `--doctor`, `--verify`, `--verify-module`, `--automation-port`, `--automation-token`, `--programs-dir`, `--save-dir`, `--preload-media`, `--backend`) and non-swallowing optional-value handling (`--suite-tab`, `--window`, `--browser`).
- **7-File App+Portable Release Validator (`Dev\tools\build_release_universal.py`):**
  - Added `--allow-portable-zip` (plus automatic 7-file detection when `*-Portable.zip` accompanies `*-Setup-v*.exe`), `validate_release_bundle` alias, and `_inspect_portable_zip` matching `audit_installer.ps1` (`0` `*Setup*.exe` leaks, `0` loose directories, `0` corrupt/empty archives).

### Changed
- **Unified `0.5.2` SemVer & Test Coverage:**
  - Bumped `0.5.2` across `Directory.Build.props`, `Dev\app.manifest.json`, `Foundation.UI.csproj`, `ShellBrandingOptions.cs`, `Web\package.json`, `foundation_core`, `sandbox_ctl.py`, `sync_foundation_sdk.py`, test suites, `active.state`, Root Quartet, and Manual Shelf.
  - Added isolated unit tests in `FoundationMarkupInstallerTests`, `FoundationV040ContractsTests`, and `test_build_release_universal.py`.

### Fixed
- **Versioned `UmbrellaSubpath` & `LastInstallBaseDir` Poisoning (`Foundation.Installer`):**
  - Updated `GetEffectiveUmbrellaSubpath()` in [`InstallerContracts.cs`](Dev/src/Foundation.Installer/InstallerContracts.cs) and [`InstallerThemeOptions.cs`](Dev/src/Foundation.Installer/InstallerThemeOptions.cs) (`IsVersionLeafSegment`) so versioned leaves (`vX.Y.Z*`) never double `ProductName`/`DisplayName`.
  - Hardened [`InstallerEngine.Core.cs`](Dev/src/Foundation.Installer/InstallerEngine.Core.cs) with studio/suite-anchored `ExtractInstallBaseDirectory`, poisoned-candidate rejection (`StreamItAll`, `reEngine\engines|games`, `engines`), sandbox drive-root checks, and self-healing `TryResolveSuggestedInstallDirFromHistory` while keeping legitimate user paths like `D:\Games\Misc` valid.
- **Focus-Free Win32 Mouse Dispatch & Deterministic Test Isolation (`Foundation.Automation`, `Foundation.Tests`):**
  - Updated [`Win32VirtualInputDriver.cs`](Dev/src/Foundation.Automation/Win32VirtualInputDriver.cs) (`DispatchMouseButton`, `drag`) to invoke managed WinForms mouse handlers (`InvokeManagedMouse`) rather than native `WM_*BUTTONDOWN` messages that triggered `user32!SetFocus()` foreground steal on `Button`/`CheckBox`/`RadioButton`/`TextBox` controls.
  - Added [`TestAssemblyConfig.cs`](Dev/tests/Foundation.Tests/TestAssemblyConfig.cs) (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) so tests mutating process-wide environment variables or Win32 desktop state run deterministically without cross-class races.
  - Added `.ConfigureAwait(false)` across `Foundation.Installer` async engine methods (`InstallerEngine`, `InstallerEngineCore`) and restored `SynchronizationContext` around headless `InstallerWizardForm` instantiation in tests so WinForms form construction under non-parallel xUnit no longer deadlocks subsequent `await` continuations.

## [0.5.1] - 2026-11-19
### Added
- **ISO/IEC 18004 QR Code Suite (`Foundation.Core.QrCodeEncoder`):**
  - Added zero-dependency [`QrCodeEncoder.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/QrCodeEncoder.cs) (`QrCodeMatrix` + `QrCodeEncoder`) supporting ISO/IEC 18004 byte-mode Versions 1–10 at ECC Level M (`<= 213` UTF-8 bytes for long Tailscale MagicDNS URLs), GF(256) Reed-Solomon (`0x11D`), BCH(15,5)/(18,6) format & version bits, 8-mask penalty scoring, 4-module ISO quiet zone, and high-contrast `RenderSvg`.
  - Added [`FoundationQrCodeTests.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/tests/Foundation.Tests/FoundationQrCodeTests.cs) verifying V1–V10 sizing, finder/timing/dark-module geometry, capacity limits, 4-module quiet zone, and deterministic SVG output (`48/48` C# Release tests passed with `0` warnings; `8/8` Python `unittest` passed; `audit_scanner.py` `P0: 0, P1: 0`).
- **IDE Workspace (`Dev\Foundation.code-workspace`):** Added multi-root VS Code / Antigravity workspace (`1. Foundation — Dev` + `2. Foundation — Project Root`) with `dotnet.defaultSolution`, Python interpreter settings, build/test/scaffold-audit tasks, file excludes, and extension recommendations, and updated [`Dev\docs\ARCHITECTURE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/ARCHITECTURE.md) and [`Dev\docs\OPERATIONS.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/OPERATIONS.md).

### Changed
- **Unified `v0.5.1` SemVer & SDK Publish:** Bumped `0.5.1` across `Directory.Build.props`, `app.manifest.json`, `Foundation.UI.csproj`, `ShellBrandingOptions.cs`, `Web\package.json`, `foundation_core`, `sandbox_ctl.py`, `sync_foundation_sdk.py`, test suites, `active.state` (strict UTC `Z`), Root Quartet, and Manual Shelf.

## [0.5.0] - 2026-11-16
### Added
- **`Foundation.Installer` Declarative `installer.manifest.json` (`schemaVersion: 2`) Markup Engine:**
  - Added [`InstallerManifestStore.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerManifestStore.cs) and `InstallerMarkupManifest` parsing Standard 40 `"installer-manifest"` descriptors (`--manifest`, adjacent directory, or embedded assembly resources), supporting `"single-line"`/`"multi-line"` layout modes, `"lightweight"`/`"full"`/`"custom"` profiles with fallback, 2-line tooltips, theme/button overrides, custom `logoPath`/`iconPath`, declarative `secondaryPickers`, and `allowAutoStart`.
- **Persistent `HKCU\Software\StreamItAll\LastInstallBaseDir` Precedence & Isolation (`Foundation.Installer`):**
  - Added `InstallerEngine.SharedStreamItAllRegistrySubKey` / `LastInstallBaseDirValueName` persistence across `IPlatformRegistry` (`registry.hive.json` in Stage 2 sandbox mode; strictly suppressed under `APP_ISOLATED_TEST=1` / `--data-dir`), giving upgrade targets precedence over `LastInstallBaseDir`, rejecting non-rooted/reparse paths, and remapping paths into active sandboxes.
- **`Foundation.Automation` (`net8.0-windows`) Opt-In Loopback UI Driver & Snapshot Module:**
  - Added `Foundation.Automation` (`AutomationContracts.cs`, `AutomationLoopbackServer.cs`, `Win32VirtualInputDriver.cs`, `WindowSnapshotService.cs`) with `127.0.0.1` HTTP/1.1 loopback (`Host`/`Origin` `403`, constant-time Bearer `401`, `/v1/health`, `/v1/tree`, `/v1/pointer`, `/v1/keyboard`, `/v1/snapshot`), UI-thread dispatch, `AutomationCliHelper`, non-focus-stealing `WM_*` pointer/keyboard driver (`0` `SendInput` calls), and `IRenderSurfaceSnapshotProvider` + `RgbaPngWriter` / `CapturePreviewBitmap` PNG capture.
- **Public-Repo Security & Readiness Baseline:**
  - Added outer [`SECURITY.md`](file:///<PROJECTS_ROOT>/Foundation/SECURITY.md), expanded public Quickstart in [`README.md`](file:///<PROJECTS_ROOT>/Foundation/README.md), documented human-gated `git-filter-repo` pre-public runbook in [`Dev\docs\OPERATIONS.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/OPERATIONS.md), and opened [`0.5.0.2026-11-16_1840.-.pre-public-human-review-gate.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/0.5.0.2026-11-16_1840.-.pre-public-human-review-gate.md) while keeping GitHub private pending human review.
- **Automated Tests & Gate Sign-Off:**
  - Added `FoundationMarkupInstallerTests.cs` and `FoundationAutomationTests.cs`; verified `45/45` C# Release tests (`0` warnings), `8/8` Python `unittest`, `audit_scanner.py` (`P0: 0, P1: 0, P2: 45, P3: 0`), `@dean` Gate 2 `PASS`, and `@inspecter` Phase 3 `PASS`.

### Changed
- **Unified `v0.5.0` SemVer, Manual Shelf & Single-Pass SDK Publish:**
  - Bumped `0.5.0` across `Directory.Build.props`, `app.manifest.json`, `Foundation.UI.csproj`, `ShellBrandingOptions.cs`, `Web\package.json`, `foundation_core`, `sandbox_ctl.py`, `sync_foundation_sdk.py` (`57` published SDK files in `Foundation-v0.5.0.zip`), and [`Dev\docs\`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/ARCHITECTURE.md).

## [0.4.2] - 2026-11-16
### Added
- **`Foundation.Core` 4-Drive Sandbox Seams & Atomic JSON Pseudo-Registry:**
  - [`KnownFolderPaths.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/KnownFolderPaths.cs): Added `GetFolderPath`, `GetHostFolderPath` (Win32 `SHGetKnownFolderPath` + `APPDATA` unmapping for child-env isolation), `ResolveLocalProgramsDirectory`, and `MapHostPathToSandbox`.
  - [`SandboxEnvironmentSeam.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironmentSeam.cs) & [`AppEnvironment.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AppEnvironment.cs): Added `FOUNDATION_SANDBOX_ROOT` and `SANDBOX_*_DRIVE` process-env resolution (`APP_ISOLATED_TEST=1` beats `FOUNDATION_SANDBOX_ROOT`), `ExitCode = 3` containment guards, and `AppEnvironment.Sandbox`.
  - [`IPlatformRegistry.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/IPlatformRegistry.cs), [`WindowsPlatformRegistry.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/WindowsPlatformRegistry.cs), and [`JsonHivePlatformRegistry.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JsonHivePlatformRegistry.cs): Added 1-file atomic `.tmp -> .bak` JSON pseudo-registry supporting `DWORD`, `QWORD`, `String`, `ExpandString`, `MultiString`, and `Binary`, Windows `Reg*` `1223` error semantics, PID-safe `.lock`, and `PlatformRegistryFactory`.
  - [`SandboxDriveController.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxDriveController.cs): Added explicit opt-in `.secrets\.env` / `Dev\.env` and `subst` resolver.
- **Python `foundation_core` (`0.4.2`) Parity & Stage 2 Sandbox Tooling:**
  - [`sandbox_seam.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/sandbox_seam.py) & [`pseudo_registry.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/pseudo_registry.py): Added pure-stdlib Python modules with 1:1 C# Stage 2 & hive parity.
  - [`sandbox_ctl.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sandbox_ctl.py) & [`run_sandbox_e2e.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/run_sandbox_e2e.py): Added 4-drive sandbox orchestrator and 12-point `S2-01`..`S2-12` Stage 2 E2E runner.
- **Automated & Live Stage 2 Dogfood Tests:**
  - Added [`FoundationSandboxAndRegistryTests.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/tests/Foundation.Tests/FoundationSandboxAndRegistryTests.cs) and [`test_sandbox_and_registry.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tests/foundation_core/test_sandbox_and_registry.py); verified `40/40` C# tests (`0` warnings), `8/8` Python tests (`unittest` & `pytest`), and `12/12` live Stage 2 4-drive dogfood checks (`DogfoodSample-Setup.exe`, `hostDiffCount=0`, `ExitCode=0`).

### Changed
- **`Foundation.Installer` 3-Mode Isolation & Seam Wiring:** Wired 3-mode isolation (`ShouldSuppressHostMutations` = Stage 1, `ShouldSuppressLiveHostMutations` = Stage 1 || Stage 2), `KnownFolderPaths` shortcuts, `IPlatformRegistry` ARP uninstall metadata, `FileLockRetryHelper` (`<SandboxDDrive>\Programs` allowed under `IsSandboxActive`), and `FOUNDATION_SUPPRESS_MODALS=1` modal suppression.
- **Dev Bible v1.6.0 Schema, SDK Publish & Strict 6-File Release Packaging:** Registered 14 sandbox/registry columns in Standard 18 (`18_DATA_DICTIONARY.md`); narrowed [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py) (`54` SDK files published to `SharedLibs\Foundation\v0.4.2\` and `Latest\`); aligned [`build_release_universal.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/build_release_universal.py) `--allow-sdk-zip` strict 6-file validation (no loose `LICENSE` in `Releases\`); bumped `v0.4.2` across `.csproj`s, `@foundation/ui v0.4.2`, `foundation_core 0.4.2`, `ShellBrandingOptions`, `AGENTS.md`, Root Quartet, and Manual Shelf.

### Fixed
- **Shortcut Colon Sanitization & DPI-Safe Header Badge Clamping (`Foundation.Installer`):** Added `WindowsShortcutWriter.SanitizeShortcutFileName` (`:` -> ` -`) and DPI-safe `_lblTitle`/`_lblSub` clamping preserving `650x460`/`650x570` `ClientSize` (resolving archived [`0.4.1.2026-11-15.-.installer-shortcut-colon-and-header-badge-overlap.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/archive/0.4.1.2026-11-15.-.installer-shortcut-colon-and-header-badge-overlap.md)).

## [0.4.1-hf.1] - 2026-11-15
### Removed
- **Retired Runtime Test Harnesses & Flags (`Foundation`, Tier A 5-Folder Normalization):**
  - Removed the retired isolated runtime test directory and its root ignore rule; normalized `active.state` to `Tier A (5-Folder)`.
  - Deleted obsolete universal verification scripts, WinForms showcase helpers, recomp fixture runner harnesses, virtual uninstall-registry mock stubs, and their obsolete test suite.
  - Purged retired CLI test, showcase, and fixture flags plus containment-bypass properties across C# (`Foundation.Core`, `Foundation.Installer`, `Foundation.Launcher`, `Foundation.UI`, `Foundation.Tests`) and Python (`foundation_core\cli.py`).

### Changed
- **Refactored Core/Installer Environments & Clean `<= 400 LOC` Splits:**
  - Renamed environment options to [`AppEnvironment.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AppEnvironment.cs) (`UiPresentationMode.TrayOrBackground`, retaining `--data-dir`, `--config`, `--log-dir`, `--port`, `--dry-run`, `--tray-only`, `--managed-by-mother`, `--suite-tab`, `--window`/`--browser`, `AssertPathContained`, `NormalizeLongPath`).
  - Renamed installer context and CLI/registry modules to [`InstallerRuntimeContext.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerRuntimeContext.cs) (`ShouldSuppressHostMutations`, `APP_ISOLATED_TEST`) and [`InstallerEngine.CliAndRegistry.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngine.CliAndRegistry.cs); added fail-closed `FileLockRetryHelper.IsSafeIsolatedInstallDirectory` & `IsCanonicalProductionOrSystemPath` guards; moved WinForms layout/dark-frame/composite preview hooks into [`InstallerWizardForm.LayoutAndTheme.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerWizardForm.LayoutAndTheme.cs) and [`InstallerWizardForm.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerWizardForm.cs).
  - Split legacy `SevenZipAndZipInspector.cs` (`445` LOC) into two partials ([`SevenZipAndZipInspector.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Sidecars/SevenZipAndZipInspector.cs) + [`SevenZipAndZipInspector.ExtractAndZip.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Sidecars/SevenZipAndZipInspector.ExtractAndZip.cs)) and trimmed `InstallerEngineCore.cs` under `<= 400 LOC` so 100% of `Dev\` code files obey the `400`-line cap.
  - Updated [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py) so `--sync-consumer` automatically removes stale test-harness artifacts from consumer `Dev\lib\` directories, and re-verified `33/33` `[Fact]` unit tests (`0` warnings).

## [0.4.1] - 2026-11-15
### Added
- **Modular Installer Splits, Composite Preview & Option Forwarding (`Foundation.Installer`, `Foundation.UI`):**
  - [`InstallerContracts.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerContracts.cs): Added `InstallerInstallOptions`, `InstallerHostRunResult`, `InstallManifest.MigrateLegacy`, and trailing `AllowLegacyMigration` on `InstallerAppDescriptor`.
  - [`DisposableWebViewHostForm.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/DisposableWebViewHostForm.cs): Added DWM dark titlebar styling, composite `CapturePreviewBitmap()`, and `AppEnvironment`-aware constructor.

### Changed
- **SDK Version Bump (`0.4.1`), Modularization & Packaging (`build_release_universal.py`, `sync_foundation_sdk.py`):**
  - Updated `Directory.Build.props`, `Dev\app.manifest.json`, `ShellBrandingOptions.SdkVersion`, `Web\package.json`, `foundation_core` (`pyproject.toml`, `__init__.py`), `Dev\tools\sync_foundation_sdk.py`, `Dev\tools\build_release_universal.py`, `AGENTS.md`, `active.state`, Root Quartet, and Manual Shelf to `0.4.1`; published `SharedLibs\Foundation\v0.4.1` & `Latest` (`Foundation-v0.4.1.zip` + `SHA256SUMS.txt` verified via `--allow-sdk-zip`).
  - Split `InstallerEngine.Core.cs` (`137 LOC`) and `InstallerWizardForm.*.cs` (`<= 393 LOC` partials), and added `--allow-sdk-zip` / `stage_sdk_release_zip` to `build_release_universal.py`.

### Fixed
- **Installer Preview Z-Order & Decoupled Legacy Migration / Option Forwarding (`Foundation.Installer`):**
  - Fixed `CaptureWizardPreviewBitmap()` to paint the parent surface first and composite non-fill visible child controls back-to-front so `_bodyPanel` never obscures interactive controls in `DrawToBitmap` screenshots (resolving archived `0.4.0` report).
  - Added explicit `showLegacyMigration` parameter decoupled from `initialMigrateLegacy` checked state on `InstallerWizardForm` and forwarded `SelectedProfileId`, `SecondaryInstallDir`, `UmbrellaSubpath`, and `MigrateLegacyChecked` from GUI primary execution to [`InstallerEngine.InstallAndExtract.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngine.InstallAndExtract.cs) and `InstallManifest` (resolving archived `0.4.0` report).
  - Escaped `&lt;PROGRAMS_ROOT&gt;` XML doc tag in `InstallerThemeOptions.cs` (`CS1570`), replaced unrooted fixture path in `FoundationDecouplingTests.cs`, and replaced empty `catch` blocks in installer helpers.

## [0.4.0] - 2026-11-15
### Added
- **Studio/Suite Default Install Root, Profiles, Secondary Picker & Opt-In Legacy Migration in `Foundation.Installer` (`v0.4.0`)**:
  - [`InstallerEngine.Core.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngine.Core.cs) & [`InstallerEngineCore.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngineCore.cs): Added `StudioName` (`"StreamItAll"`), `SuiteName`, `UmbrellaSubpath`, `AvailableProfiles`, `DefaultProfileId`, `SecondaryPathPicker`, and `AllowLegacyMigration` to `InstallerAppDescriptor` / `InstallerThemeOptions`; resolves `%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\`, adds opt-in `DetectLegacyFlatInstall` / `MigrateLegacyInstallAsync` (`--migrate-legacy`), and replaces hardcoded `<PROGRAMS_ROOT>\` paths.
  - [`StandardInstallerHost.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/StandardInstallerHost.cs) & [`InstallerWizardForm.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerWizardForm.cs): Added `InstallerProfileKind` (`Lightweight` / `Full`), `InstallerProfileOption`, `SecondaryPathPickerOptions`, CLI flags (`--profile`, `--reengine-dir`, `--suite-dir`, `--migrate-legacy`), and dark-theme wizard controls.
- **AOT-Primary Static Recomp & `.decomp` Contracts in `Foundation.Retro` (`v0.4.0`)**:
  - [`RecompTierAndBackendContracts.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompTierAndBackendContracts.cs) & [`RecompContracts.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompContracts.cs): Added `RecompExecutionTierMode` (`AotPrimary`, `JitBringup`, `ThreadedIr`), `RecompRenderBackendIds` (`software`, `opengl`), `IDecompPackageManifest`, `RecompRuntimeTelemetrySnapshot`, and `AotCacheLayoutHelper` (`<dataDir>\cache\<ModuleId>\<Version>\`).
- **Fail-Closed Path Containment in `Foundation.Core` (`v0.4.0`)**:
  - Strengthened fail-closed containment checks for isolated `--data-dir` execution while rejecting host escapes.
- **Public-Repo Config Overlays (`Dev\.env.example`, `Dev\settings.example.json`)**:
  - Added environment and `<dataDir>\notion.config.json` overlay templates for zero-PII public distribution.

### Changed
- **Unified `0.4.0` SemVer & Single-Source `Foundation.UI` Layout**:
  - Aligned `Directory.Build.props`, all 7 C# `.csproj` assemblies, `@foundation/ui` (`Web\package.json`), `foundation_core` (`pyproject.toml`), `Dev\app.manifest.json`, `active.state`, and `sync_foundation_sdk.py` on `0.4.0`.
  - Deduplicated tripled `Foundation.UI` TS/TSX copies (`Web\` is now the single canonical TS/TSX source; `Foundation.UI.csproj` copies `Web\` into `$(OutputPath)\WebAssets\` and gates SDK publishing behind `-p:PublishFoundationSdk=true`).
  - Externalized Notion IDs in `Dev\tools\notion_task_helper.py` (`FOUNDATION_NOTION_TASK_BACKBONE_ID`, `FOUNDATION_NOTION_PROJECTS_HUB_ID`, `notion.config.json`), scrubbed workstation paths from `Dev\app.manifest.json` and `sync_foundation_sdk.py`, purged `14` stale `.pdb` files from `Builds\`, and reduced `Releases\Latest\` to the canonical 6-file bundle (`LICENSE` + `SHA256SUMS.txt`).

### Fixed
- **Installer Self-Delete Trampoline (`Foundation.Installer`):** Replaced one-shot `ping -n 2` self-delete in [`InstallerEngine.CliAndRegistry.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngine.CliAndRegistry.cs) / [`InstallerEngine.UninstallAndShortcuts.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngine.UninstallAndShortcuts.cs) with a bounded retry `cmd.exe` trampoline.
- **Retro Catalog & Extractor (`Foundation.Retro`):** Replaced synthetic sequential hex BIOS hashes in `ConsoleSystemCatalog.cs` with `IsVerifiedReferenceHash` metadata and added `.ConfigureAwait(false)` in `MediaContainerExtractor.cs`.
- **Network Transfer & Archive Extraction (`Foundation.Core`, `Foundation.Sidecars`):** Sanitized `opts.JobId` (`:` ADS fix), recovered HTTP `416`, excluded `400/401/403/404/410` from `IsTransient`, and enforced `ResponseHeadersTimeout` in `SafeNetworkTransfer.cs`; added LZMA/Deflate64 `7z.exe` fallback and nested `::` inspection in `SevenZipAndZipInspector.cs`.
- **Launcher Wait, Relative Arg Rebase & Signal Forwarding (`Foundation.Launcher`):** Fixed `ShouldWaitForChild` (`--wait`, `--open`, `--check-updates`, `--register-assoc`), rebased caller-CWD relative path arguments, and forwarded Ctrl+C in `RootLauncherHost.cs`.

## [0.3.1] - 2026-11-13
### Added
- **Pure-`.NET` Timing, Lock-Free Audio, PNG & Overlay Primitives in `Foundation.Core.dll` (`v0.3.1`, `net8.0`)**:
  - [`HighResFramePacer.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HighResFramePacer.cs): `IDisposable` frame pacer with Windows `winmm.dll` `timeBeginPeriod(1)`/`timeEndPeriod(1)`, sleep+spinwait pacing, unlocked mode (`TargetFps <= 0`), and 120-frame `CurrentFps`/`AverageFrameMs`/`P99FrameMs` telemetry.
  - [`AudioRingBuffer.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AudioRingBuffer.cs): Lock-free SPSC ring buffer (`Volatile.Read`/`Volatile.Write`) for interleaved `short`/`float` PCM samples with smooth ramp-to-zero underrun attenuation.
  - [`WavCaptureWriter.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/WavCaptureWriter.cs), [`RgbaPngWriter.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/RgbaPngWriter.cs), and [`PixelOverlayCanvas.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PixelOverlayCanvas.cs): Deterministic 16-bit RIFF/WAV capture, pure-.NET lossless RGBA PNG encoder (`ZLibStream` + IEEE CRC32), and Porter-Duff `SrcOver` RGBA canvas with built-in 5×7 ASCII bitmap font.
- **BYO Optical/Archive Extractor & Static Recomp Contracts in `Foundation.Retro.dll` (`v0.1.1`, `net8.0`)**:
  - [`MediaContainerExtractor.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/MediaContainerExtractor.cs): Extracts/verifies `.iso`, `.bin`+`.cue`, `.chd` (`chdman`), `.7z` (`7z`/`7za`), and `.zip` (`ZipArchive` + `AssertPathContained` zip-slip guard) with SHA-256 checksum validation and `ToolResolver` graceful fallback.
  - [`RecompContracts.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompContracts.cs): Added `IRecompHost`, `IRecompGameModule`, `IRecompModuleManifest`, `IRecompRenderBackend`, `RecompHostOptions`, `RecompFrameStats`, `RecompPatchDescriptor`, and `DiscChecksumEntry`.
- **`Foundation.Installer.dll` (`v0.2.2`) Resilience & `AllowAutoStart` Toggle**:
  - Added [`FileLockRetryHelper.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/FileLockRetryHelper.cs) (`10` attempts, `150ms -> 250ms -> 500ms` backoff) across `InstallerEngine.InstallAndExtract.cs`, `InstallerEngineCore.cs`, and `InstallerEngineCore.BackupAndVerify.cs`.
  - Added backward-compatible trailing `bool AllowAutoStart = true` on `InstallerAppDescriptor` and `InstallerThemeOptions`; setting `false` hides/disables Startup shortcut UI, rebalances checkboxes (`Y = 42` / `74`), and clamps `--autostart`.
  - Added [`FoundationV031PrimitivesTests.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/tests/Foundation.Tests/FoundationV031PrimitivesTests.cs) verifying `v0.3.1` Core, Retro, and Installer APIs with `0` warnings.

### Changed
- **SDK Version Bump (`v0.3.1`):** Updated `Directory.Build.props` (`0.3.1`), `Foundation.Retro.csproj` (`0.1.1`), `Foundation.Installer.csproj` (`0.2.2`), `Foundation.UI.csproj`, `ShellBrandingOptions.SdkVersion`, `sync_foundation_sdk.py`, `active.state`, `AGENTS.md`, `ARCHITECTURE.md`, and `INTERFACES.md`.

## [0.3.0] - 2026-11-07
### Added
- **Turnkey 4-Pillar `StandardInstallerHost`, Parameterized `InstallerEngine` & Multi-Partial `InstallerWizardForm` in `Foundation.Installer.dll` (`v0.2.1`)**:
  - Promoted `InstallerAppDescriptor`, `InstallerRuntimeContext`, `InstallerConfigOverride`, `InstallManifest`, `InstallerOperationResult`, `InstallerEngine` partials, `InstallerWizardForm` partials, and `StandardInstallerHost.Run(InstallerAppDescriptor, string[], Assembly?)` into `Foundation.Installer.dll` (`v0.2.1`).
- **Canonical ShadowLibrary Strict Standard Terminal Drawer Core (`@foundation/ui v0.2.0` — [`terminalDrawerCore.ts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Web/components/terminalDrawerCore.ts))**:
  - Added pure types and functions implementing Standard 09 §4.5 docked resizable terminal drawer contracts.
- **Dual-Target Vanilla ES Module & Stylesheet ([`foundation-terminal.js`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.js), [`foundation-terminal.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.css))**:
  - Authored self-contained Vanilla ES Module exporting `mountFoundationTerminalDrawer(options)` + `window.FoundationTerminalDrawer` with mobile filter toggle (`⚙ Flt`) and responsive geometry rules.
- **Automated SDK Publisher & Consumer Sync Pipeline ([`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py), [`Foundation.UI.csproj`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Foundation.UI.csproj))**:
  - Added `sync_foundation_sdk.py` publishing compiled DLL/XML/WebAssets/`foundation_core`/`tools` snapshots to `<PROJECTS_ROOT>\SharedLibs\Foundation\` and supporting `--sync-consumer <ProjectRoot>`.
- **Terminal Drawer & SDK Sync Unit Test Suite ([`FoundationTerminalDrawerTests.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/tests/Foundation.Tests/FoundationTerminalDrawerTests.cs))**:
  - Added 3 unit tests verifying `@foundation/ui v0.2.0` React components, Vanilla ES Module `foundation-terminal.js`/`foundation-terminal.css`, `<400 LOC` limits, and `sync_foundation_sdk.py` publishing and consumer sync.

### Changed
- **Upgraded React 19 `GlobalTerminalDrawer` & `TerminalToolbarAndFilters` to ShadowLibrary Strict Standard (`@foundation/ui v0.2.0`)**.

*(Older 0.1.x–0.2.x history trimmed per Standard 04 3-minor retention (`0.5`/`0.4`/`0.3`); snapshots preserved in Archive\\.)*
