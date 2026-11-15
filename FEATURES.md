# Foundation (`v0.4.1`) — Feature Matrix & Inventory

> **Verified Against:** `Dev\` source tree (all new & `v0.4.1`-modularized files `<= 400 LOC`, `max = 397 LOC`) and `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\sdk-manifest.json` (`51` files).
> **For exact method signatures & props:** See [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md).

---

## 1. C# Shared Modules (`Dev\src\`)

### 1.1 `Foundation.Core` (`18` files, `net8.0`)
- **Fail-Closed Sandbox & Presentation Mode (`SandboxEnvironment`):** Parses `--data-dir`, `--config`, `--log-dir`, `--port`, `--test-mode`, `--dry-run`, `--headless` (`HEADLESS=1` / `{PREFIX}_HEADLESS=1`), `--visible-test` (`ExplicitVisibleTest`, `VISIBLE_TEST=1`), exposing `VisibleTest` and `EffectiveVisibleTest` (visible foreground execution by default whenever `--test-mode` runs without `--headless`, `--tray-only`, `--managed-by-mother`, `--suite-tab`, or `--browser`, while keeping `--headless --visible-test` explicit precedence), `--tray-only`, `--managed-by-mother`, `--suite-tab`, `--browser`/`--ui browser`, `--window`/`--ui desktop` into `UiPresentationMode` (`DesktopWindow`, `LocalBrowser`, `SuiteTab`, `HeadlessOrTrayOnly`), enforces `AssertPathContained(...)`, `NormalizeLongPath(...)`, and `ResolveSiblingSandboxRoot(...)` (sibling `Sandbox\{app,data,logs,installed}` containment).
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

### 1.2 `Foundation.Retro` (`v0.4.1`, `9` files, `net8.0`, references `Foundation.Core`)
- **System Catalog (`ConsoleSystemCatalog`):** 41 canonical platforms, alias/extension lookup (`ResolvePlatformId`, `ResolveTier2PlatformId`, `NormalizePlatformToken`), BIOS expectations with explicit `IsVerifiedReferenceHash` metadata (`v0.4.0`, zero synthetic sequential hashes), header-skip rules (`ines-16`, `fwnes-16`, `smc-512`, `swc-512`, `a78-128`, `lnx-64`), provenance tags (`[h]`, `(Homebrew)`, `[b]`).
- **Filename Parser & Healer (`DatTitleNormalizer`):** `ParseFilename(...)` + `HealMalformedTagSyntax(...)` extracting region, language, revision, serial, disc index, and malformed tag repair suggestions.
- **DAT Parser (`NoIntroRedumpDatParser`):** Streaming Logiqx/No-Intro/Redump/MAME/RetroAchievements XML, ClrMamePro text, `.sfv`/`.md5`/`.sha1`.
- **Header Stripping & Magic Sniffing (`HeaderlessRomStripper`, `MagicBytePlatformDetector`):** Detects copier/headerless offsets and inspects 20 binary signatures (`.pkg`, `.cnt`, `.pup`, `PSF`, `RVZ`/`WIA`, `GC`/`Wii`, `ISO9660`, `XBE`, `XISO`, `N64`, `GBA`, `NDS`, `3DS`, etc.).
- **In-Memory Patcher (`SoftPatchEngine`):** Native IPS, BPS (`BPS1`), and UPS (`UPS1`) binary patching with CRC32 validation.
- **BYO Media Container Extractor & Static Recomp Contracts (`v0.3.1`–`v0.4.0`):** [`MediaContainerExtractor`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/MediaContainerExtractor.cs) extracts `.iso`, `.bin`+`.cue`, `.chd`, `.7z`, and `.zip` via `ZipArchive` / `ToolResolver` (`chdman`, `7z`) with SHA-256 verification, `.ConfigureAwait(false)`, and `SandboxEnvironment.AssertPathContained` zip-slip protection; [`RecompContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompContracts.cs), [`RecompTierAndBackendContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompTierAndBackendContracts.cs) (`RecompExecutionTierMode` AOT-primary + `JitBringup` / `ThreadedIr`, `RecompRenderBackendIds`, `IDecompPackageManifest`, `AotCacheLayoutHelper` under `<dataDir>\cache\`), and [`SaveStateFixtureRunner`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/SaveStateFixtureRunner.cs) standardize clean-room `re<Platform>` / `<Game>Recomp` execution and multi-scene save-state fixture sweeps.

### 1.3 `Foundation.Sidecars` (`v0.4.1`, `4` files, `0` external deps)
- **Single-Pass Stream Hashing (`SinglePassStreamHasher`, `Crc32Accumulator`):** Simultaneous `ArrayPool<byte>` CRC32 + MD5 + SHA1 + optional SHA256 + headerless ROM hashing in one sequential read.
- **Archive & TorrentZip Inspector (`SevenZipAndZipInspector`):** Fast `.zip` central-directory & `7z l -slt -sccUTF-8` listing, LZMA/Deflate64 `7z.exe` fallback, nested `::` archive inspection, single-entry extract, multipart `.partN.rar` detection, deterministic TorrentZip builder.
- **Sidecars & Coexistence (`HashSidecarService`, `HkLockSentinel`):** `.hash` v1 serializer/parser (`COMPOSITE_SHA1`, `TOTAL_FILES`, `TOTAL_SIZE`), reserved wrapper folders (`PS3_GAME`, `BDMV`, etc.), `.hklock` reuse-safe lock guard, and `.coexistence.json` v2 peer checks.

### 1.4 `Foundation.Server` (`v0.4.1`, `3` files) & `Foundation.Launcher` (`v0.4.1`, `1` file)
- **`TieredAuthEngine` & `FoundationTieredAuthMiddleware`:** Loopback bypass (unless `X-Forwarded-For`), Tailscale CGNAT/ULA bypass, PBKDF2-SHA256 (`100,000` rounds), 30-day HMAC cookies, Bearer / `X-ShadowLibrary-Token` header auth, and `/api/*` `Cache-Control: no-store`.
- **`SisterAppStandardEndpoints`:** Serves `suite.manifest.json` on `/api/v1/suite/manifest`, `/api/suite/manifest`, and `/suite.manifest.json`.
- **`RootLauncherHost`:** Clean-root launcher forwarding CLI commands via `AttachConsole(-1)`, blocking/returning exit codes on `--wait`/`--open`/`--check-updates`/`--register-assoc`/`--visible-test`/`--test-mode`, rebasing caller-CWD relative args, and forwarding Ctrl+C.

### 1.5 `Foundation.Installer` (`v0.4.1`, `18` files)
- **Tier 1 (`StandardInstallerHost` + `InstallerAppDescriptor`):** One-call entry point delivering the Dark WinForms Setup Wizard (`InstallerWizardForm`), Studio/Suite default `%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\` resolution with decoupled `AllowLegacyMigration` / `showLegacyMigration` & `--migrate-legacy`, `InstallerProfileOption` (`Lightweight` / `Full`) + `SecondaryPathPicker` (`--reengine-dir`, `--suite-dir`) forwarded into `InstallManifest`, trailing `AllowAutoStart = true` opt-out, `FileLockRetryHelper`, sibling `Sandbox\` containment (`--sandbox-root`), modularized `InstallerContracts.cs` + `InstallerSandboxContext.cs` + `InstallerWizardForm.VisibleTest.cs`, visible-by-default `--test-mode` / `--visible-test` centered `TopMost` real contained execution (auto-expanded `[v] Show Details`, zero artificial preview wait, composite `CaptureWizardPreviewBitmap()` PNG `<LogDirectory>\screenshots\installer-visible-test.png`, modal popup suppression, `StandardInstallerHost.RunWithResult`), bounded retry self-delete trampoline, CLI (`--silent`, `--verify`, `--uninstall`, `--purge-data`, `--test-mode`, `--dry-run`, `--headless`, `--visible-test`, `--profile`, `--version`, `--help`), COM `.lnk` shortcuts, ARP uninstall registration, and `app\install-manifest.json`.
- **Tier 2 (`InstallerEngineCore` + `InstallerThemeOptions`):** Low-level custom installer helper (`FoundationInstallManifest`), also supporting `AllowAutoStart`, `AvailableProfiles`, `SecondaryPathPicker`, `AllowLegacyMigration`, and `DetectLegacyFlatInstall` / `MigrateLegacyInstallAsync`.

---

## 2. UI Libraries (`Foundation.UI` + `@foundation/ui v0.4.1`)

- **Desktop WinForms Hosts:** [`ShellBrandingOptions`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/ShellBrandingOptions.cs) (`SdkVersion = "0.4.1"`), [`VisibleTestWindowHelper`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/VisibleTestWindowHelper.cs), [`DisposableWebViewHostForm`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/DisposableWebViewHostForm.cs) (`SandboxEnvironment` visible-test activation + `CapturePreviewBitmap` / `CaptureVisibleTestScreenshot`), [`StaNativeDialogBridgeBase`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/StaNativeDialogBridgeBase.cs), [`TrayIconBadgeHelper`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/TrayIconBadgeHelper.cs).
- **React 18 / TypeScript (`@foundation/ui v0.4.1`, 10 components, single canonical `Web\` source):**
  - Barrel exports (`@foundation/ui`): `GlobalTerminalDrawer`, `TerminalToolbarAndFilters`, `StyledTooltip`, `ModalDialog`, `ConfirmActionModal`, `ResponsiveTable`, `UniversalDataTable`, `SchemaSettingsRenderer` + `terminalDrawerCore`.
  - Direct submodule exports (`@foundation/ui/components/<Name>`): `CoexistenceWarningBanner`, `HybridPathPicker`, `SidebarNavRail`.
- **Vanilla Web Assets:** [`foundation-tokens.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-tokens.css), [`foundation-shell.ts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-shell.ts), [`foundation-terminal.js`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.js), [`foundation-terminal.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.css).

---

## 3. Python Package `foundation_core` (`v0.4.1`) & `Dev\tools\`

- **`foundation_core` (`Dev\src\foundation_core\`):** Zero-dependency parity modules [`cli.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/cli.py) (`StandardCliOptions` with `headless`, `visible_test`, `effective_visible_test`; `--headless` / `HEADLESS=1`, `--visible-test` / `VISIBLE_TEST=1`), [`settings.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/settings.py), [`logging.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/logging.py), [`crash_dumper.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/crash_dumper.py), [`locks.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/locks.py), [`db.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/db.py).
- **Universal Dev Tooling (`Dev\tools\`):** [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py) (also stages clean-room `Foundation-v<Ver>.zip`), [`build_release_universal.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/build_release_universal.py) (`--allow-sdk-zip` / `stage_sdk_release_zip`), [`verify_sandbox_universal.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/verify_sandbox_universal.py) (foreground `--new-window` browser launch, `--headless=new` only when headless), [`notion_task_helper.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/notion_task_helper.py).

---

## 4. Quality & Verification Baseline

- **Unit Tests:** `40` `[Fact]` tests across `7` files in [`Dev\tests\Foundation.Tests\`](file:///<PROJECTS_ROOT>/Foundation/Dev/tests/Foundation.Tests/) (`FoundationCoreTests`: 6, `FoundationSettingsAndInstallerTests`: 5, `FoundationDecouplingTests`: 4, `FoundationTerminalDrawerTests`: 3, `FoundationV031PrimitivesTests`: 8, `FoundationV040ContractsTests`: 7, `FoundationV041VisibleTestTests`: 7).
- **Code Size Compliance:** All new and `v0.4.1`-modularized source, test, and tooling files in `Dev\` comply with `<= 400 LOC` (`max = 397 LOC` in `InstallerWizardForm.cs`; two pre-0.4.1 legacy files remain at `441` / `445 LOC` tracked for follow-up decomposition).
