# Foundation (`v0.4.2`) — Feature Matrix & Inventory

> **Verified Against:** `Dev\` source tree (all `.cs` / `.py` / `.ts` / `.tsx` / `.js` / `.css` source files pass `250/400/600` code-only SLOC) and `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\sdk-manifest.json` (`54` published SDK files).
> **For exact method signatures & props:** See [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md).

---

## 1. C# Shared Modules (`Dev\src\`)

### 1.1 `Foundation.Core` (`24` files, `net8.0`)
- **Runtime Environment & Presentation Mode (`AppEnvironment`):** Parses `--data-dir`, `--config`, `--log-dir`, `--port`, `--dry-run`, `--tray-only`, `--managed-by-mother`, `--suite-tab`, `--browser`/`--ui browser`, `--window`/`--ui desktop` into `UiPresentationMode` (`DesktopWindow`, `LocalBrowser`, `SuiteTab`, `TrayOrBackground`), attaches `Sandbox` (`SandboxResolution`), and enforces `AssertPathContained(...)` and `NormalizeLongPath(...)`.
- **4-Drive Known Folders & Sandbox Containment (`KnownFolderPaths`, `SandboxEnvironmentSeam`, `SandboxDriveController`):**
  - [`KnownFolderPaths`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/KnownFolderPaths.cs): `GetFolderPath`, `GetHostFolderPath` (Win32 `SHGetKnownFolderPath` + `APPDATA` unmapping for child-env isolation), `ResolveLocalProgramsDirectory`, and `MapHostPathToSandbox`.
  - [`SandboxEnvironmentSeam`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironmentSeam.cs): Process-env-only `FOUNDATION_SANDBOX_ROOT` and `SANDBOX_*_DRIVE` resolution where `APP_ISOLATED_TEST=1` beats `FOUNDATION_SANDBOX_ROOT`, plus fail-closed `ExitCode = 3` containment guards (`AssertSandboxPathIfActive`).
  - [`SandboxDriveController`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxDriveController.cs): Explicit opt-in `.secrets\.env` / `Dev\.env` loader and dynamic `subst` resolver (never auto-read by `AppEnvironment`).
- **Cross-Process Atomic JSON Pseudo-Registry (`IPlatformRegistry`, `WindowsPlatformRegistry`, `JsonHivePlatformRegistry`, `PlatformRegistryFactory`):**
  - [`IPlatformRegistry.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/IPlatformRegistry.cs), [`WindowsPlatformRegistry.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/WindowsPlatformRegistry.cs), [`JsonHivePlatformRegistry.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JsonHivePlatformRegistry.cs): 1-file atomic `.tmp -> .bak` JSON pseudo-registry implementing `DWORD`, `QWORD`, `String`, `ExpandString`, `MultiString`, and `Binary`, Windows `Reg*` `1223` error semantics, named `Mutex` + PID-safe `.lock`, and `PlatformRegistryFactory`.
- **Recomp Timing, Audio & Pure-.NET Frame Capture (`v0.3.1`):**
  - [`HighResFramePacer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HighResFramePacer.cs): `Stopwatch.GetTimestamp()` sub-ms spin-wait pacer, `winmm.dll` `timeBeginPeriod(1)` guarded for non-Windows hosts, locked/unlocked/multiplier timing, and `P99` jitter telemetry.
  - [`AudioRingBuffer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AudioRingBuffer.cs) & [`WavCaptureWriter`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/WavCaptureWriter.cs): Lock-free SPSC stereo PCM sample ring buffer (`UnderrunCount` attenuation) + 44-byte RIFF/WAVE file writer.
  - [`PixelOverlayCanvas`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PixelOverlayCanvas.cs) & [`RgbaPngWriter`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/RgbaPngWriter.cs): Pure-.NET RGBA pixel buffer (`Clear`, `FillRect`, `StrokeRect`, `SrcOver` alpha blend, built-in `5x7` bitmap `DrawText`) + `ZLibStream` PNG encoder (`0` WinForms/GDI+ dependencies).
- **CLI & Bounded Log Primitives (`CliArgumentHelper`, `BoundedTailStream`, `CappedSharedLogSink`):** Flag/option/positional parsing, fixed `64 KB` (`65536`-byte) ring stream, and `4 MB` capped shared log sink.
- **Atomic Granular Settings (`GranularSettingsManager`):** `.tmp -> .bak -> File.Replace` writes, corrupt-JSON `.bak` self-healing, and 20-snapshot `data\config_history\` rotation.
- **Encrypted Secrets (`DpapiSecretStore`):** Windows `DataProtectionScope.CurrentUser` storage in `data\secrets.dpapi` (never plaintext in `settings.json`).
- **SQLite WAL Concurrency (`SqliteWalPool`):** Enforces `SqliteCacheMode.Default`, `journal_mode = WAL`, `busy_timeout = 15000`, `synchronous = NORMAL`, `foreign_keys = ON`, `temp_store = MEMORY`, `cache_size = -65536`, plus online `VACUUM INTO`.
- **PID-Safe Locks (`PidSafeLockfile`):** Verifies `(pid, processStartTimeUtc)` in `PidSafeLockEnvelope` before honoring or breaking orphan locks.
- **Telemetry & Crash Recovery (`HeartbeatLogger`, `CrashBundleService`):** `<= 15s` throttled heartbeats + `\r\x1b[K` interactive terminal refresh; sanitized `logs\crashes\crash_<appId>_<ts>.json` (`CrashBundlePayload`).
- **Process Supervision & Cancellation (`JobObjectGuard`, `DualJobCancellationCoordinator`):** Win32 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` + independent `StopCurrentJob()` vs `StopAllJobs()`.
- **Safe Network Downloads (`SafeNetworkTransfer`):** 10s connect / 30s `ResponseHeadersTimeout` / 30s stall timeouts, sanitized `JobId` (`:` ADS fix), HTTP `416` partial reset recovery, fail-fast on `400/401/403/404/410` non-transient errors, jitter backoff (`MaxRetries = 3`), HTTP `Range` `.partial_<jobId>` resume, `1.2x + 512 MB` disk preflight, bandwidth throttling, SHA-256/SHA-1 verification.
- **Tool Resolution & Hybrid Modularity (`ToolResolver`, `HybridModuleLoader`, `IFoundationModule`):** 4-tier executable discovery and unified loading for in-assembly `FolderSlice`s and external `*.Module.*.dll` plugins.

### 1.2 `Foundation.Retro` (`v0.4.2`, `8` files, `net8.0`, references `Foundation.Core`)
- **System Catalog (`ConsoleSystemCatalog`):** 41 canonical platforms, alias/extension lookup (`ResolvePlatformId`, `ResolveTier2PlatformId`, `NormalizePlatformToken`), BIOS expectations with explicit `IsVerifiedReferenceHash` metadata (`v0.4.0`, zero synthetic sequential hashes), header-skip rules (`ines-16`, `fwnes-16`, `smc-512`, `swc-512`, `a78-128`, `lnx-64`), provenance tags (`[h]`, `(Homebrew)`, `[b]`).
- **Filename Parser & Healer (`DatTitleNormalizer`):** `ParseFilename(...)` + `HealMalformedTagSyntax(...)` extracting region, language, revision, serial, disc index, and malformed tag repair suggestions.
- **DAT Parser (`NoIntroRedumpDatParser`):** Streaming Logiqx/No-Intro/Redump/MAME/RetroAchievements XML, ClrMamePro text, `.sfv`/`.md5`/`.sha1`.
- **Header Stripping & Magic Sniffing (`HeaderlessRomStripper`, `MagicBytePlatformDetector`):** Detects copier/headerless offsets and inspects 20 binary signatures (`.pkg`, `.cnt`, `.pup`, `PSF`, `RVZ`/`WIA`, `GC`/`Wii`, `ISO9660`, `XBE`, `XISO`, `N64`, `GBA`, `NDS`, `3DS`, etc.).
- **In-Memory Patcher (`SoftPatchEngine`):** Native IPS, BPS (`BPS1`), and UPS (`UPS1`) binary patching with CRC32 validation.
- **BYO Media Container Extractor & Static Recomp Contracts (`v0.3.1`–`v0.4.2`):** [`MediaContainerExtractor`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/MediaContainerExtractor.cs) extracts `.iso`, `.bin`+`.cue`, `.chd`, `.7z`, and `.zip` via `ZipArchive` / `ToolResolver` (`chdman`, `7z`) with SHA-256 verification, `.ConfigureAwait(false)`, and `AppEnvironment.AssertPathContained` zip-slip protection; [`RecompContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompContracts.cs) and [`RecompTierAndBackendContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompTierAndBackendContracts.cs) (`RecompExecutionTierMode` AOT-primary + `JitBringup` / `ThreadedIr`, `RecompRenderBackendIds`, `IDecompPackageManifest`, `AotCacheLayoutHelper` under `<dataDir>\cache\`) standardize clean-room `re<Platform>` / `<Game>Recomp` execution.

### 1.3 `Foundation.Sidecars` (`v0.4.2`, `5` files, `0` external deps)
- **Single-Pass Stream Hashing (`SinglePassStreamHasher`, `Crc32Accumulator`):** Simultaneous `ArrayPool<byte>` CRC32 + MD5 + SHA1 + optional SHA256 + headerless ROM hashing in one sequential read.
- **Archive & TorrentZip Inspector (`SevenZipAndZipInspector` + `SevenZipAndZipInspector.ExtractAndZip`, both `<= 250` SLOC):** Fast `.zip` central-directory & `7z l -slt -sccUTF-8` listing, LZMA/Deflate64 `7z.exe` fallback, nested `::` archive inspection, single-entry extract, multipart `.partN.rar` detection, deterministic TorrentZip builder.
- **Sidecars & Coexistence (`HashSidecarService`, `HkLockSentinel`):** `.hash` v1 serializer/parser (`COMPOSITE_SHA1`, `TOTAL_FILES`, `TOTAL_SIZE`), reserved wrapper folders (`PS3_GAME`, `BDMV`, etc.), `.hklock` reuse-safe lock guard, and `.coexistence.json` v2 peer checks.

### 1.4 `Foundation.Server` (`v0.4.2`, `3` files, `net8.0`) & `Foundation.Launcher` (`v0.4.2`, `1` file)
- **`TieredAuthEngine` & `FoundationTieredAuthMiddleware`:** Loopback bypass (unless `X-Forwarded-For`), Tailscale CGNAT/ULA bypass, PBKDF2-SHA256 (`100,000` rounds), 30-day HMAC cookies, Bearer / `X-ShadowLibrary-Token` header auth, and `/api/*` `Cache-Control: no-store`.
- **`SisterAppStandardEndpoints`:** Serves `suite.manifest.json` on `/api/v1/suite/manifest`, `/api/suite/manifest`, and `/suite.manifest.json`.
- **`RootLauncherHost`:** Clean-root launcher forwarding CLI commands via `AttachConsole(-1)`, blocking/returning exit codes on `--wait`/`--open`/`--check-updates`/`--register-assoc`, rebasing caller-CWD relative args, and forwarding Ctrl+C.

### 1.5 `Foundation.Installer` (`v0.4.2`, `17` files)
- **Tier 1 (`StandardInstallerHost` + `InstallerAppDescriptor`):** One-call entry point delivering the Dark WinForms Setup Wizard (`InstallerWizardForm`), Studio/Suite default `%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\` resolution, 3-mode isolation (`ShouldSuppressHostMutations` = Stage 1, `ShouldSuppressLiveHostMutations` = Stage 1 || Stage 2), `KnownFolderPaths` shortcuts, `IPlatformRegistry` ARP uninstall metadata, `FileLockRetryHelper` (`<SandboxDDrive>\Programs` allowed under `IsSandboxActive`), `WindowsShortcutWriter.SanitizeShortcutFileName` (`:` -> ` -`), DPI-safe `_lblTitle`/`_lblSub` clamping preserving `650x460`/`650x570` `ClientSize`, `FOUNDATION_SUPPRESS_MODALS=1` modal suppression, decoupled `AllowLegacyMigration` / `showLegacyMigration` & `--migrate-legacy`, `InstallerProfileOption` (`Lightweight` / `Full`) + `SecondaryPathPicker` (`--reengine-dir`, `--suite-dir`) forwarded into `InstallManifest`, `AllowAutoStart = true` opt-out, composite `CaptureWizardPreviewBitmap()`, bounded retry self-delete trampoline, CLI (`--silent`, `--verify`, `--uninstall`, `--purge-data`, `--dry-run`, `--profile`, `--version`, `--help`), and `app\install-manifest.json`.
- **Tier 2 (`InstallerEngineCore` + `InstallerThemeOptions`):** Low-level custom installer helper (`FoundationInstallManifest`) wired to `KnownFolderPaths`, `IPlatformRegistry`, 3-mode containment, `AllowAutoStart`, `AvailableProfiles`, `SecondaryPathPicker`, `AllowLegacyMigration`, and `DetectLegacyFlatInstall` (`<SandboxDDrive>\Programs` under Stage 2).

---

## 2. UI Libraries (`Foundation.UI` + `@foundation/ui v0.4.2`)

- **Desktop WinForms Hosts:** [`ShellBrandingOptions`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/ShellBrandingOptions.cs) (`SdkVersion = "0.4.2"`), [`DisposableWebViewHostForm`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/DisposableWebViewHostForm.cs) (`AppEnvironment`-aware constructor, DWM dark frame + composite `CapturePreviewBitmap`), [`StaNativeDialogBridgeBase`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/StaNativeDialogBridgeBase.cs), [`TrayIconBadgeHelper`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/TrayIconBadgeHelper.cs).
- **React 18 / TypeScript (`@foundation/ui v0.4.2`, 10 components, single canonical `Web\` source):**
  - Barrel exports (`@foundation/ui`): `GlobalTerminalDrawer`, `TerminalToolbarAndFilters`, `StyledTooltip`, `ModalDialog`, `ConfirmActionModal`, `ResponsiveTable`, `UniversalDataTable`, `SchemaSettingsRenderer` + `terminalDrawerCore`.
  - Direct submodule exports (`@foundation/ui/components/<Name>`): `CoexistenceWarningBanner`, `HybridPathPicker`, `SidebarNavRail`.
- **Vanilla Web Assets:** [`foundation-tokens.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-tokens.css), [`foundation-shell.ts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-shell.ts), [`foundation-terminal.js`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.js), [`foundation-terminal.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.css).

---

## 3. Python Package `foundation_core` (`v0.4.2`, `10` files) & `Dev\tools\` (`5` files)

- **`foundation_core` (`Dev\src\foundation_core\`):** Zero-dependency pure-stdlib parity modules [`sandbox_seam.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/sandbox_seam.py) and [`pseudo_registry.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/pseudo_registry.py) (1:1 C# Stage 2 & hive parity), alongside [`cli.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/cli.py) (`StandardCliOptions` with `--data-dir`, `--config`, `--log-dir`, `--port`, `--dry-run`), [`settings.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/settings.py), [`logging.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/logging.py), [`crash_dumper.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/crash_dumper.py), [`locks.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/locks.py), [`db.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/db.py).
- **Universal Dev Tooling (`Dev\tools\`):** [`sandbox_ctl.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sandbox_ctl.py), [`run_sandbox_e2e.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/run_sandbox_e2e.py) (12-point `S2-01`..`S2-12` runner), [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py) (`54` SDK files published to `SharedLibs\Foundation\v0.4.2\` and `Latest\`, narrowed `--sync-consumer` cleanup), [`build_release_universal.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/build_release_universal.py) (`--allow-sdk-zip` strict 6-file validation, no loose `LICENSE` in `Releases\`), [`notion_task_helper.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/notion_task_helper.py).

---

## 4. Quality & Verification Baseline (`v0.4.2`)

- **C# Unit & Integration Tests:** `40/40` passed (`0` failed, `0` skipped, `0` warnings) under `dotnet test Dev\Foundation.sln -c Release -p:TreatWarningsAsErrors=true` across `7` files in [`Dev\tests\Foundation.Tests\`](file:///<PROJECTS_ROOT>/Foundation/Dev/tests/Foundation.Tests/).
- **Python `unittest` & `pytest`:** `8/8` OK / `8` passed in [`Dev\tests\foundation_core\`](file:///<PROJECTS_ROOT>/Foundation/Dev/tests/foundation_core/).
- **Live Stage 2 4-Drive E2E Dogfood (`run_sandbox_e2e.py`):** `12/12` passed (`S2-01`..`S2-12` with `DogfoodSample-Setup.exe`, `hostDiffCount=0`, `ExitCode=0`).
- **Static & Code-Size Compliance:** `audit_scanner.py` (`P0: 0, P1: 0, P2: 45, P3: 0`), `@dean` Gate 2 + Delta Gate 2 `PASS`, `@inspecter` Phase 3 + 2-tier log sweep `PASS`, and 100% of `.cs` / `.py` / `.ts` / `.tsx` / `.js` / `.css` source files in `Dev\` comply with `250/400/600` code-only SLOC.

---

## 5. Shipped `v0.4.2` & Roadmapped Capabilities (`v0.4.2` $\rightarrow$ `v1.0.0`)

| Code | Target | Capability & Guardrails | Status |
| :--- | :--- | :--- | :--- |
| `CORE-01` | `v0.4.2` | **`KnownFolderPaths` & `SandboxEnvironmentSeam`:** Deterministic `FOUNDATION_SANDBOX_ROOT` + 4-drive (`SANDBOX_*_DRIVE`) shell-folder redirect (`GetHostFolderPath` via `SHGetKnownFolderPath` + `APPDATA` unmapping), `APP_ISOLATED_TEST=1` precedence, `ExitCode = 3` containment guards. | `Shipped` |
| `CORE-02` | `v0.4.2` | **`IPlatformRegistry`, `WindowsPlatformRegistry` & `JsonHivePlatformRegistry`:** Cross-process C#/Python 1-file `.tmp -> .bak` atomic JSON pseudo-registry (`HKCU`/`HKLM`/`HKCR`, `DWORD`/`QWORD`/`String`/`ExpandString`/`MultiString`/`Binary`, `Reg*` 1223 semantics, PID-safe `.lock`). | `Shipped` |
| `TOOL-01` | `v0.4.2` | **`SandboxDriveController`, `sandbox_ctl.py` & `run_sandbox_e2e.py`:** Non-elevated 4-drive orchestrator, 12-point Stage 2 E2E runner (`S2-01`..`S2-12`), 3-mode `Foundation.Installer` wiring, shortcut `:` -> ` -` fix, DPI-safe header badge clamping, `FOUNDATION_SUPPRESS_MODALS=1`. | `Shipped` |
| `INST-01` | `v0.5.0` | **Markup Installer (`installer.manifest.json`) & `LastInstallBaseDir`:** Zero-dep `System.Text.Json` declarative setup UI + smart `HKCU\Software\StreamItAll\LastInstallBaseDir` fresh-install suggestion (post-success write only, `APP_ISOLATED_TEST=1` & reparse-point guarded). | `Roadmapped` |
| `AUTO-01` | `v0.5.0` | **`Foundation.Automation` Loopback Module:** Opt-in `127.0.0.1` HTTP/JSON (`--automation-port` + ephemeral bearer token + `Host`/`Origin` check) virtual pointer, virtual keyboard, control bounds, and offscreen HWND/WebView PNG snapshot. | `Roadmapped` |
| `TRAY-01` | `v0.6.0` | **Shared `TrayDaemonHost` (`Foundation.UI` + `foundation_core`):** Unified tray icon, menu, `PidSafeLockfile` zombie-handle cleanup, and daemon lifecycle replacing per-app tray hosts. | `Roadmapped` |
| `PY-01` | `v0.6.0` | **`foundation_core.server` Auth/SSE Parity:** Python `TieredAuthEngine`, `suite.manifest.json` v2 endpoints, and bounded SSE log-tailing for `Greenroom`, `Redroom`, and `_Goals`. | `Roadmapped` |
| `LNCH-01` | `v0.6.0` | **Shared Self-Update Check (`Foundation.Launcher`):** GitHub Release check + SHA-256/signature verification + staged update handoff (never auto-overwriting running prod). | `Roadmapped` |
| `JOB-01` | `v0.7.0` | **Shared `JobQueue`, `IntervalCronScheduler` & `@foundation/ui` Job Drawer:** Unified background queue/cron contracts, retry/deferral metadata, and UI job drawer. | `Roadmapped` |
| `DOC-01` | `v0.7.0` | **`SystemDoctorAndBackupService`:** Shared SQLite integrity check, `VACUUM INTO` backup, and `10`-snapshot config rotation. | `Roadmapped` |
| `AOT-01` | `v0.7.0` | **`IsAotCompatible` Core & Automation Subset:** `FoundationCoreJsonContext` (`JsonSerializerContext`) + reflection-free `Foundation.Core` & `Foundation.Automation` subset for `Snowfall` (`.NET 10 NativeAOT`) & `Project26` (Unity 6). | `Roadmapped` |
| `PKG-01` | `v1.0.0` | **Going Gold (`v1.0.0`) Packaging & Parity:** Local NuGet `.nupkg` + Python wheel feeds, `100%` `pytest` parity suite, and public C#/Python/TS ABI freeze. | `Roadmapped` |
