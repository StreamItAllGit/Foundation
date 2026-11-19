# Foundation (`v0.5.2`) — Feature Matrix & Inventory

> **Verified Against:** `Dev\` source tree (all `.cs` / `.py` / `.ts` / `.tsx` / `.js` / `.css` source files pass `250/400/600` code-only SLOC) and `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\sdk-manifest.json`.
> **For exact method signatures & props:** See `Dev\docs\INTERFACES.md`.

---

## 1. C# Shared Modules (`Dev\src\`)

### 1.1 `Foundation.Core` (`25` files, `net8.0`)
- **Runtime Environment & Presentation Mode (`AppEnvironment`):** Parses `--data-dir`, `--config`, `--log-dir`, `--port`, `--dry-run`, `--tray-only`, `--managed-by-mother`, `--suite-tab`, `--browser`/`--ui browser`, `--window`/`--ui desktop` into `UiPresentationMode` (`DesktopWindow`, `LocalBrowser`, `SuiteTab`, `TrayOrBackground`), attaches `Sandbox` (`SandboxResolution`), and enforces `AssertPathContained(...)` and `NormalizeLongPath(...)`.
- **4-Drive Known Folders & Sandbox Containment (`KnownFolderPaths`, `SandboxEnvironmentSeam`, `SandboxDriveController`):**
  - `KnownFolderPaths` (`Dev\src\Foundation.Core\KnownFolderPaths.cs`): `GetFolderPath`, `GetHostFolderPath` (Win32 `SHGetKnownFolderPath` + `APPDATA` unmapping for child-env isolation), `ResolveLocalProgramsDirectory`, and `MapHostPathToSandbox`.
  - `SandboxEnvironmentSeam` (`Dev\src\Foundation.Core\SandboxEnvironmentSeam.cs`): Process-env-only `FOUNDATION_SANDBOX_ROOT` and `SANDBOX_*_DRIVE` resolution where `APP_ISOLATED_TEST=1` beats `FOUNDATION_SANDBOX_ROOT`, plus fail-closed `ExitCode = 3` containment guards (`AssertSandboxPathIfActive`).
  - `SandboxDriveController` (`Dev\src\Foundation.Core\SandboxDriveController.cs`): Explicit opt-in `.secrets\.env` / `Dev\.env` loader and dynamic `subst` resolver (never auto-read by `AppEnvironment`).
- **Cross-Process Atomic JSON Pseudo-Registry (`IPlatformRegistry`, `WindowsPlatformRegistry`, `JsonHivePlatformRegistry`, `PlatformRegistryFactory`):**
  - `IPlatformRegistry.cs`, `WindowsPlatformRegistry.cs`, `JsonHivePlatformRegistry.cs` (`Dev\src\Foundation.Core\`): 1-file atomic `.tmp -> .bak` JSON pseudo-registry implementing `DWORD`, `QWORD`, `String`, `ExpandString`, `MultiString`, and `Binary`, Windows `Reg*` `1223` error semantics, named `Mutex` + PID-safe `.lock`, and `PlatformRegistryFactory`.
- **Recomp Timing, Audio & Pure-.NET Frame Capture (`v0.3.1`):**
  - `HighResFramePacer` (`Dev\src\Foundation.Core\HighResFramePacer.cs`): `Stopwatch.GetTimestamp()` sub-ms spin-wait pacer, `winmm.dll` `timeBeginPeriod(1)` guarded for non-Windows hosts, locked/unlocked/multiplier timing, and `P99` jitter telemetry.
  - `AudioRingBuffer` & `WavCaptureWriter` (`Dev\src\Foundation.Core\`): Lock-free SPSC stereo PCM sample ring buffer (`UnderrunCount` attenuation) + 44-byte RIFF/WAVE file writer.
  - `PixelOverlayCanvas` & `RgbaPngWriter` (`Dev\src\Foundation.Core\`): Pure-.NET RGBA pixel buffer (`Clear`, `FillRect`, `StrokeRect`, `SrcOver` alpha blend, built-in `5x7` bitmap `DrawText`) + `ZLibStream` PNG encoder (`0` WinForms/GDI+ dependencies).
- **CLI & Bounded Log Primitives (`CliArgumentHelper`, `BoundedTailStream`, `CappedSharedLogSink`):** Flag/option/positional parsing, fixed `64 KB` (`65536`-byte) ring stream, and `4 MB` capped shared log sink.
- **Atomic Granular Settings (`GranularSettingsManager`):** `.tmp -> .bak -> File.Replace` writes, corrupt-JSON `.bak` self-healing, and 20-snapshot `data\config_history\` rotation.
- **Encrypted Secrets (`DpapiSecretStore`):** Windows `DataProtectionScope.CurrentUser` storage in `data\secrets.dpapi` (never plaintext in `settings.json`).
- **SQLite WAL Concurrency (`SqliteWalPool`):** Enforces `SqliteCacheMode.Default`, `journal_mode = WAL`, `busy_timeout = 15000`, `synchronous = NORMAL`, `foreign_keys = ON`, `temp_store = MEMORY`, `cache_size = -65536`, plus online `VACUUM INTO`.
- **PID-Safe Locks (`PidSafeLockfile`):** Verifies `(pid, processStartTimeUtc)` in `PidSafeLockEnvelope` before honoring or breaking orphan locks.
- **Telemetry & Crash Recovery (`HeartbeatLogger`, `CrashBundleService`):** `<= 15s` throttled heartbeats + `\r\x1b[K` interactive terminal refresh; sanitized `logs\crashes\crash_<appId>_<ts>.json` (`CrashBundlePayload`).
- **Process Supervision & Cancellation (`JobObjectGuard`, `DualJobCancellationCoordinator`):** Win32 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` + independent `StopCurrentJob()` vs `StopAllJobs()`.
- **Safe Network Downloads (`SafeNetworkTransfer`):** 10s connect / 30s `ResponseHeadersTimeout` / 30s stall timeouts, sanitized `JobId` (`:` ADS fix), HTTP `416` partial reset recovery, fail-fast on `400/401/403/404/410` non-transient errors, jitter backoff (`MaxRetries = 3`), HTTP `Range` `.partial_<jobId>` resume, `1.2x + 512 MB` disk preflight, bandwidth throttling, SHA-256/SHA-1 verification.
- **Tool Resolution & Hybrid Modularity (`ToolResolver`, `HybridModuleLoader`, `IFoundationModule`):** 4-tier executable discovery and unified loading for in-assembly `FolderSlice`s and external `*.Module.*.dll` plugins.
- **ISO/IEC 18004 QR Encoder (`QrCodeEncoder`, `v0.5.1`):** Zero-dependency byte-mode Versions 1–10 ECC Level M (`<= 213` UTF-8 bytes) encoder with GF(256) Reed-Solomon (`0x11D`), BCH format/version bits, 8-mask ISO penalty scoring, 4-module quiet zone, and high-contrast `RenderSvg`.

### 1.2 `Foundation.Retro` (`v0.5.2`, `8` files, `net8.0`, references `Foundation.Core`)
- **System Catalog (`ConsoleSystemCatalog`):** 41 canonical platforms, alias/extension lookup (`ResolvePlatformId`, `ResolveTier2PlatformId`, `NormalizePlatformToken`), BIOS expectations with explicit `IsVerifiedReferenceHash` metadata (`v0.4.0`, zero synthetic sequential hashes), header-skip rules (`ines-16`, `fwnes-16`, `smc-512`, `swc-512`, `a78-128`, `lnx-64`), provenance tags (`[h]`, `(Homebrew)`, `[b]`).
- **Filename Parser & Healer (`DatTitleNormalizer`):** `ParseFilename(...)` + `HealMalformedTagSyntax(...)` extracting region, language, revision, serial, disc index, and malformed tag repair suggestions.
- **DAT Parser (`NoIntroRedumpDatParser`):** Streaming Logiqx/No-Intro/Redump/MAME/RetroAchievements XML, ClrMamePro text, `.sfv`/`.md5`/`.sha1`.
- **Header Stripping & Magic Sniffing (`HeaderlessRomStripper`, `MagicBytePlatformDetector`):** Detects copier/headerless offsets and inspects 20 binary signatures (`.pkg`, `.cnt`, `.pup`, `PSF`, `RVZ`/`WIA`, `GC`/`Wii`, `ISO9660`, `XBE`, `XISO`, `N64`, `GBA`, `NDS`, `3DS`, etc.).
- **In-Memory Patcher (`SoftPatchEngine`):** Native IPS, BPS (`BPS1`), and UPS (`UPS1`) binary patching with CRC32 validation.
- **BYO Media Container Extractor & Static Recomp Contracts (`v0.3.1`–`v0.4.2`):** `MediaContainerExtractor` (`Dev\src\Foundation.Retro\MediaContainerExtractor.cs`) extracts `.iso`, `.bin`+`.cue`, `.chd`, `.7z`, and `.zip` via `ZipArchive` / `ToolResolver` (`chdman`, `7z`) with SHA-256 verification, `.ConfigureAwait(false)`, and `AppEnvironment.AssertPathContained` zip-slip protection; `RecompContracts` and `RecompTierAndBackendContracts` (`RecompExecutionTierMode` AOT-primary + `JitBringup` / `ThreadedIr`, `RecompRenderBackendIds`, `IDecompPackageManifest`, `AotCacheLayoutHelper` under `<dataDir>\cache\`) standardize clean-room `re<Platform>` / `<Game>Recomp` execution.

### 1.3 `Foundation.Sidecars` (`v0.5.2`, `5` files, `0` external deps)
- **Single-Pass Stream Hashing (`SinglePassStreamHasher`, `Crc32Accumulator`):** Simultaneous `ArrayPool<byte>` CRC32 + MD5 + SHA1 + optional SHA256 + headerless ROM hashing in one sequential read.
- **Archive & TorrentZip Inspector (`SevenZipAndZipInspector` + `SevenZipAndZipInspector.ExtractAndZip`, both `<= 250` SLOC):** Fast `.zip` central-directory & `7z l -slt -sccUTF-8` listing, LZMA/Deflate64 `7z.exe` fallback, nested `::` archive inspection, single-entry extract, multipart `.partN.rar` detection, deterministic TorrentZip builder.
- **Sidecars & Coexistence (`HashSidecarService`, `HkLockSentinel`):** `.hash` v1 serializer/parser (`COMPOSITE_SHA1`, `TOTAL_FILES`, `TOTAL_SIZE`), reserved wrapper folders (`PS3_GAME`, `BDMV`, etc.), `.hklock` reuse-safe lock guard, and `.coexistence.json` v2 peer checks.

### 1.4 `Foundation.Server` (`v0.5.2`, `3` files, `net8.0`) & `Foundation.Launcher` (`v0.5.2`, `2` files)
- **`TieredAuthEngine` & `FoundationTieredAuthMiddleware`:** Loopback bypass (unless `X-Forwarded-For`), Tailscale CGNAT/ULA bypass, PBKDF2-SHA256 (`100,000` rounds), 30-day HMAC cookies, Bearer / `X-ShadowLibrary-Token` header auth, and `/api/*` `Cache-Control: no-store`.
- **`SisterAppStandardEndpoints`:** Serves `suite.manifest.json` on `/api/v1/suite/manifest`, `/api/suite/manifest`, and `/suite.manifest.json`.
- **`RootLauncherHost` & `RootLauncherOptions` (`v0.5.2`):** Zero-dependency clean-root launcher forwarding CLI commands via `AttachConsole(-1)`, blocking/returning exit codes on sync flags (`--wait`, `--open`, `--check-updates`, `--register-assoc`, `--diagnose`, `--doctor`, `--verify`, `--verify-module`), rebasing relative path args (`--programs-dir`, `--save-dir`, `--preload-media`, `--verify-module`, etc.), consuming optional-value flags (`--suite-tab`, `--window`, `--browser`) without swallowing trailing sync switches, forwarding Ctrl+C, and supporting caller-supplied `RootLauncherOptions` (`ExtraSyncWaitFlags`, `ExtraValueOptions`, `ExtraPathValueOptions`) via binary-compatible overloads.

### 1.5 `Foundation.Installer` (`v0.5.2`, `18` files, `net8.0-windows`)
- **Declarative Markup Manifests (`InstallerManifestStore` + `InstallerMarkupManifest`):** Parses `schemaVersion: 2` `"installer-manifest"` descriptors (`installer.manifest.json` via `--manifest`, adjacent folder, or embedded `<ProductName>.Installer.Embedded.installer.manifest.json`), supporting canonical `suiteId` identity (`EffectiveSuiteId` / `ResolveEffectiveSuiteId()`) with non-`[Obsolete]` `suiteName` legacy JSON fallback, `"single-line"`/`"multi-line"` layout modes, `"lightweight"`/`"full"`/`"custom"` profile modes with fallback, 2-line tooltips, theme/button overrides, custom `logoPath`/`iconPath`, declarative `secondaryPickers`, and `allowAutoStart` policy defaults.
- **Persistent Shared Base Directory & Umbrella Anti-Doubling (`LastInstallBaseDir`, `v0.5.2`):** Reads/writes `HKCU\Software\StreamItAll\LastInstallBaseDir` via `IPlatformRegistry` (`registry.hive.json` in Stage 2 sandbox mode; strictly suppressed under `APP_ISOLATED_TEST=1` / `--data-dir`), giving existing upgrade paths precedence over `LastInstallBaseDir`, anchoring `ExtractInstallBaseDirectory` on studio/`reEngine` segments, rejecting poisoned candidates (`StreamItAll`, `reEngine\engines|games`, `engines`), self-healing poisoned values during `TryResolveSuggestedInstallDirFromHistory` while preserving clean user directories (such as `D:\Games\Misc`), and checking `IsVersionLeafSegment` in `GetEffectiveUmbrellaSubpath()` so versioned leaves (`vX.Y.Z*`) never double `ProductName`/`DisplayName`.
- **Tier 1 (`StandardInstallerHost` + `InstallerAppDescriptor`):** One-call entry point delivering the Dark WinForms Setup Wizard (`InstallerWizardForm`), Studio/Suite default `%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\` resolution, 3-mode isolation (`ShouldSuppressHostMutations` = Stage 1, `ShouldSuppressLiveHostMutations` = Stage 1 || Stage 2), `KnownFolderPaths` shortcuts, `IPlatformRegistry` ARP uninstall metadata, `FileLockRetryHelper` (`<SandboxDDrive>\Programs` allowed under `IsSandboxActive`), `WindowsShortcutWriter.SanitizeShortcutFileName` (`:` -> ` -`), DPI-safe `_lblTitle`/`_lblSub` clamping preserving `650x460`/`650x570` `ClientSize`, `FOUNDATION_SUPPRESS_MODALS=1` modal suppression, decoupled `AllowLegacyMigration` / `showLegacyMigration` & `--migrate-legacy`, `InstallerProfileOption` (`Lightweight` / `Full` / `Custom`) + `SecondaryPathPicker` (`--reengine-dir`, `--suite-dir`) forwarded into `InstallManifest`, `AllowAutoStart = true` opt-out, composite `CaptureWizardPreviewBitmap()`, bounded retry self-delete trampoline, CLI (`--silent`, `--verify`, `--uninstall`, `--purge-data`, `--dry-run`, `--profile`, `--version`, `--help`), and `app\install-manifest.json`.
- **Tier 2 (`InstallerEngineCore` + `InstallerThemeOptions`):** Low-level custom installer helper (`FoundationInstallManifest`) wired to `KnownFolderPaths`, `IPlatformRegistry`, 3-mode containment, `AllowAutoStart`, `AvailableProfiles`, `SecondaryPathPicker`, `AllowLegacyMigration`, and `DetectLegacyFlatInstall` (`<SandboxDDrive>\Programs` under Stage 2).

### 1.6 `Foundation.Automation` (`v0.5.2`, `4` files, `net8.0-windows`)
- **`AutomationLoopbackServer` & `AutomationCliHelper`:** Opt-in `127.0.0.1` HTTP/1.1 JSON loopback server (`--automation-port`, `--automation-token`) enforcing strict `Host`/`Origin` (`403`), constant-time SHA-256 Bearer (`401`), `/v1/health`, `/v1/tree`, `/v1/pointer`, `/v1/keyboard`, and `/v1/snapshot` with UI-thread dispatch (`AutomationResponse<T>`).
- **`Win32VirtualInputDriver` (`v0.5.2`):** Focus-free managed WinForms mouse dispatch (`InvokeManagedMouse` for `click`/`doubleClick`/`drag`, bypassing native `WM_*BUTTONDOWN` `user32!SetFocus()` foreground steals on `Button`/`CheckBox`/`RadioButton`/`TextBox`) and window-scoped keyboard (`type`, `press`, `Ctrl+A`, `Backspace`, `Enter`, `Tab`, `Escape`) input injection (`0` `SendInput` calls).
- **`WindowSnapshotService` & `IRenderSurfaceSnapshotProvider`:** Captures custom render surfaces via `IRenderSurfaceSnapshotProvider` + pure-.NET `RgbaPngWriter` or WinForms composite `CapturePreviewBitmap`, enforcing PNG output and sandbox/`allowedSnapshotRoot` containment.

---

## 2. UI Libraries (`Foundation.UI` + `@foundation/ui v0.5.2`)

- **Desktop WinForms Hosts:** `ShellBrandingOptions` (`SdkVersion = "0.5.2"`), `DisposableWebViewHostForm` (`AppEnvironment`-aware constructor, DWM dark frame + composite `CapturePreviewBitmap`), `StaNativeDialogBridgeBase`, `TrayIconBadgeHelper` (`Dev\src\Foundation.UI\`).
- **React 18 / TypeScript (`@foundation/ui v0.5.2`, single canonical `Web\` source):**
  - Barrel exports (`@foundation/ui`): `GlobalTerminalDrawer`, `TerminalToolbarAndFilters`, `StyledTooltip`, `ModalDialog`, `ConfirmActionModal`, `ResponsiveTable`, `UniversalDataTable`, `SchemaSettingsRenderer` + `terminalDrawerCore`.
  - Direct submodule exports (`@foundation/ui/components/<Name>`): `CoexistenceWarningBanner`, `HybridPathPicker`, `SidebarNavRail`.
- **Vanilla Web Assets (`Dev\src\Foundation.UI\WebAssets\`):** `foundation-tokens.css`, `foundation-shell.ts`, `foundation-terminal.js`, `foundation-terminal.css`.

---

## 3. Python Package `foundation_core` (`v0.5.2`, `8` modules) & `Dev\tools\` (`5` files)

- **`foundation_core` (`Dev\src\foundation_core\`):** Zero-dependency pure-stdlib parity modules `sandbox_seam.py` and `pseudo_registry.py` (1:1 C# Stage 2 & hive parity), alongside `cli.py` (`StandardCliOptions` with `--data-dir`, `--config`, `--log-dir`, `--port`, `--dry-run`), `settings.py`, `logging.py`, `crash_dumper.py`, `locks.py`, and `db.py`.
- **Universal Dev Tooling (`Dev\tools\`):** `sandbox_ctl.py`, `run_sandbox_e2e.py` (12-point `S2-01`..`S2-12` runner), `sync_foundation_sdk.py` (publishes SDK files to `SharedLibs\Foundation\v0.5.2\` and `Latest\`, narrowed `--sync-consumer` cleanup), `build_release_universal.py` (`validate_release_bundle` supporting `--allow-sdk-zip` strict 6-file validation and `--allow-portable-zip` / auto-detected `*-Portable.zip` strict 7-file app+portable validation rejecting `*Setup*.exe` leaks and loose directories, no loose `LICENSE` in `Releases\`), and `notion_task_helper.py`.

---

## 4. Quality & Verification Baseline (`v0.5.2`)

- **C# Unit & Integration Tests:** `50/50` passed (`0` failed, `0` skipped) under `dotnet test Dev\Foundation.sln -c Release` across `Dev\tests\Foundation.Tests\` (`TestAssemblyConfig.cs` disables cross-class parallelization for deterministic env/Win32 isolation).
- **Python `unittest`:** `13/13` OK (`0` failures) in `Dev\tests\foundation_core\` (including `test_build_release_universal.py` 6/7-file bundle coverage).
- **Static & Code-Size Compliance:** `audit_scanner.py` (`P0: 0, P1: 0, P2: 45, P3: 0`, `110` scanned files), and 100% of `.cs` / `.py` / `.ts` / `.tsx` / `.js` / `.css` source files in `Dev\` comply with `250/400/600` code-only SLOC.

---

## 5. Shipped `v0.5.2` & Roadmapped Capabilities (`v0.5.2` → `v1.0.0`)

| Code | Target | Capability & Guardrails | Status |
| :--- | :--- | :--- | :--- |
| `CORE-01` | `v0.4.2` | **`KnownFolderPaths` & `SandboxEnvironmentSeam`:** Deterministic `FOUNDATION_SANDBOX_ROOT` + 4-drive (`SANDBOX_*_DRIVE`) shell-folder redirect (`GetHostFolderPath` via `SHGetKnownFolderPath` + `APPDATA` unmapping), `APP_ISOLATED_TEST=1` precedence, `ExitCode = 3` containment guards. | `Shipped` |
| `CORE-02` | `v0.4.2` | **`IPlatformRegistry`, `WindowsPlatformRegistry` & `JsonHivePlatformRegistry`:** Cross-process C#/Python 1-file `.tmp -> .bak` atomic JSON pseudo-registry (`HKCU`/`HKLM`/`HKCR`, `DWORD`/`QWORD`/`String`/`ExpandString`/`MultiString`/`Binary`, `Reg*` 1223 semantics, PID-safe `.lock`). | `Shipped` |
| `TOOL-01` | `v0.4.2` | **`SandboxDriveController`, `sandbox_ctl.py` & `run_sandbox_e2e.py`:** Non-elevated 4-drive orchestrator, 12-point Stage 2 E2E runner (`S2-01`..`S2-12`), 3-mode `Foundation.Installer` wiring, shortcut `:` -> ` -` fix, DPI-safe header badge clamping, `FOUNDATION_SUPPRESS_MODALS=1`. | `Shipped` |
| `INST-01` | `v0.5.0` | **Markup Installer (`installer.manifest.json`) & `LastInstallBaseDir`:** Declarative `schemaVersion: 2` setup UI (`InstallerManifestStore`) + shared `HKCU\Software\StreamItAll\LastInstallBaseDir` fresh-install suggestion (upgrade precedence, `APP_ISOLATED_TEST=1` & reparse-point guarded). | `Shipped` |
| `AUTO-01` | `v0.5.0` | **`Foundation.Automation` Loopback Module:** Opt-in `127.0.0.1` HTTP/JSON loopback (`--automation-port` + constant-time Bearer `401` + `Host`/`Origin` `403`), non-stealing `WM_*` virtual pointer/keyboard, control bounds/`inspectTree`, and PNG snapshot (`IRenderSurfaceSnapshotProvider` + `CapturePreviewBitmap`). | `Shipped` |
| `CORE-03` | `v0.5.1` | **`QrCodeEncoder` (`Foundation.Core`):** Zero-dependency ISO/IEC 18004 byte-mode Versions 1–10 ECC-M encoder (`<= 213` UTF-8 bytes) with GF(256) Reed-Solomon, 8-mask penalty scoring, 4-module quiet zone, and crisp `RenderSvg`. | `Shipped` |
| `INST-02` | `v0.5.2` | **Canonical `suiteId`, Version-Leaf Umbrella & `LastInstallBaseDir` Self-Healing:** `InstallerMarkupManifest.SuiteId` (`suiteName` legacy JSON fallback), `IsVersionLeafSegment` non-doubling on versioned `UmbrellaSubpath` leaves, studio/suite-anchored `ExtractInstallBaseDirectory`, and self-healing `LastInstallBaseDir` preserving valid user roots (`D:\Games\Misc`). | `Shipped` |
| `TOOL-02` | `v0.5.2` | **Extensible `RootLauncherOptions` & `--allow-portable-zip` 7-File Release Parity:** Zero-dependency `RootLauncherOptions` + binary-compatible `RootLauncherHost` overloads (`--diagnose`, `--doctor`, `--verify`, `--verify-module`, `--programs-dir`, `--save-dir`, `--backend`, `--preload-media`, `--automation-port`/`--token`, optional-value `--suite-tab`/`--window`/`--browser`) and 7-file app+portable validation in `build_release_universal.py` (`validate_release_bundle`, `_inspect_portable_zip`). | `Shipped` |
| `AUTO-02` | `v0.5.2` | **Focus-Free `InvokeManagedMouse` & Deterministic Test Isolation:** `Win32VirtualInputDriver` dispatches mouse events via managed WinForms handlers (`0` `user32!SetFocus()` foreground steals) paired with `TestAssemblyConfig.cs` (`DisableTestParallelization = true`). | `Shipped` |
| `TRAY-01` | `v0.6.0` | **Shared `TrayDaemonHost` (`Foundation.UI` + `foundation_core`):** Unified tray icon, menu, `PidSafeLockfile` zombie-handle cleanup, and daemon lifecycle replacing per-app tray hosts. | `Roadmapped` |
| `PY-01` | `v0.6.0` | **`foundation_core.server` Auth/SSE Parity:** Python `TieredAuthEngine`, `suite.manifest.json` v2 endpoints, and bounded SSE log-tailing for `Greenroom`, `Redroom`, and `_Goals`. | `Roadmapped` |
| `LNCH-01` | `v0.6.0` | **Shared Self-Update Check (`Foundation.Launcher`):** GitHub Release check + SHA-256/signature verification + staged update handoff (never auto-overwriting running prod). | `Roadmapped` |
| `JOB-01` | `v0.7.0` | **Shared `JobQueue`, `IntervalCronScheduler` & `@foundation/ui` Job Drawer:** Unified background queue/cron contracts, retry/deferral metadata, and UI job drawer. | `Roadmapped` |
| `DOC-01` | `v0.7.0` | **`SystemDoctorAndBackupService`:** Shared SQLite integrity check, `VACUUM INTO` backup, and `10`-snapshot config rotation. | `Roadmapped` |
| `AOT-01` | `v0.7.0` | **`IsAotCompatible` Core & Automation Subset:** `FoundationCoreJsonContext` (`JsonSerializerContext`) + reflection-free `Foundation.Core` & `Foundation.Automation` subset for `Snowfall` (`.NET 10 NativeAOT`) & `Project26` (Unity 6). | `Roadmapped` |
| `PKG-01` | `v1.0.0` | **Going Gold (`v1.0.0`) Packaging & Parity:** Local NuGet `.nupkg` + Python wheel feeds, `100%` `pytest` parity suite, and public C#/Python/TS ABI freeze. | `Roadmapped` |
