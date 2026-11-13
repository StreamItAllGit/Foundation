# Foundation (`v0.3.1`) — Feature Matrix & Inventory

> **Verified Against:** `Dev\` source tree (`100% <= 400 LOC`) and `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\sdk-manifest.json` (`63` files).
> **For exact method signatures & props:** See [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md).

---

## 1. C# Shared Modules (`Dev\src\`)

### 1.1 `Foundation.Core` (`18` files, `net8.0`)
- **Fail-Closed Sandbox & Presentation Mode (`SandboxEnvironment`):** Parses `--data-dir`, `--config`, `--log-dir`, `--port`, `--test-mode`, `--dry-run`, `--headless`, `--visible-test` (`VisibleTest`, `VISIBLE_TEST=1`, strictly distinct from `Headless`), `--tray-only`, `--managed-by-mother`, `--suite-tab`, `--browser`/`--ui browser`, `--window`/`--ui desktop` into `UiPresentationMode` (`DesktopWindow`, `LocalBrowser`, `SuiteTab`, `HeadlessOrTrayOnly`), enforces `AssertPathContained(...)` and `NormalizeLongPath(...)`.
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
- **Safe Network Downloads (`SafeNetworkTransfer`):** 10s connect / 30s stall timeouts, jitter backoff (`MaxRetries = 3`), HTTP `Range` `.partial_<jobId>` resume, `1.2x + 512 MB` disk preflight, bandwidth throttling, SHA-256/SHA-1 verification.
- **Tool Resolution & Hybrid Modularity (`ToolResolver`, `HybridModuleLoader`, `IFoundationModule`):** 4-tier executable discovery and unified loading for in-assembly `FolderSlice`s and external `*.Module.*.dll` plugins.

### 1.2 `Foundation.Retro` (`v0.1.1`, `8` files, `net8.0`, references `Foundation.Core`)
- **System Catalog (`ConsoleSystemCatalog`):** 41 canonical platforms, alias/extension lookup (`ResolvePlatformId`, `ResolveTier2PlatformId`, `NormalizePlatformToken`), BIOS expectations, header-skip rules (`ines-16`, `fwnes-16`, `smc-512`, `swc-512`, `a78-128`, `lnx-64`), provenance tags (`[h]`, `(Homebrew)`, `[b]`).
- **Filename Parser & Healer (`DatTitleNormalizer`):** `ParseFilename(...)` + `HealMalformedTagSyntax(...)` extracting region, language, revision, serial, disc index, and malformed tag repair suggestions.
- **DAT Parser (`NoIntroRedumpDatParser`):** Streaming Logiqx/No-Intro/Redump/MAME/RetroAchievements XML, ClrMamePro text, `.sfv`/`.md5`/`.sha1`.
- **Header Stripping & Magic Sniffing (`HeaderlessRomStripper`, `MagicBytePlatformDetector`):** Detects copier/headerless offsets and inspects 20 binary signatures (`.pkg`, `.cnt`, `.pup`, `PSF`, `RVZ`/`WIA`, `GC`/`Wii`, `ISO9660`, `XBE`, `XISO`, `N64`, `GBA`, `NDS`, `3DS`, etc.).
- **In-Memory Patcher (`SoftPatchEngine`):** Native IPS, BPS (`BPS1`), and UPS (`UPS1`) binary patching with CRC32 validation.
- **BYO Media Container Extractor & Static Recomp Contracts (`v0.3.1`):** [`MediaContainerExtractor`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/MediaContainerExtractor.cs) extracts `.iso`, `.bin`+`.cue`, `.chd`, `.7z`, and `.zip` via `ZipArchive` / `ToolResolver` (`chdman`, `7z`) with SHA-256 verification and `SandboxEnvironment.AssertPathContained` zip-slip protection; [`RecompContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompContracts.cs) (`IRecompHost`, `IRecompGameModule`, `IRecompModuleManifest`, `IRecompRenderBackend`, `IVisibleTestHarness`, `RecompTimingMode`, `SaveStateFixtureSpec`) & [`SaveStateFixtureRunner`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/SaveStateFixtureRunner.cs) standardize clean-room `re<Platform>` / `<Game>Recomp` execution and multi-scene save-state fixture sweeps.

### 1.3 `Foundation.Sidecars` (`4` files, `0` external deps)
- **Single-Pass Stream Hashing (`SinglePassStreamHasher`, `Crc32Accumulator`):** Simultaneous `ArrayPool<byte>` CRC32 + MD5 + SHA1 + optional SHA256 + headerless ROM hashing in one sequential read.
- **Archive & TorrentZip Inspector (`SevenZipAndZipInspector`):** Fast `.zip` central-directory & `7z l -slt -sccUTF-8` listing, single-entry extract, multipart `.partN.rar` detection, deterministic TorrentZip builder.
- **Sidecars & Coexistence (`HashSidecarService`, `HkLockSentinel`):** `.hash` v1 serializer/parser (`COMPOSITE_SHA1`, `TOTAL_FILES`, `TOTAL_SIZE`), reserved wrapper folders (`PS3_GAME`, `BDMV`, etc.), `.hklock` reuse-safe lock guard, and `.coexistence.json` v2 peer checks.

### 1.4 `Foundation.Server` (`3` files) & `Foundation.Launcher` (`1` file)
- **`TieredAuthEngine` & `FoundationTieredAuthMiddleware`:** Loopback bypass (unless `X-Forwarded-For`), Tailscale CGNAT/ULA bypass, PBKDF2-SHA256 (`100,000` rounds), 30-day HMAC cookies, Bearer / `X-ShadowLibrary-Token` header auth, and `/api/*` `Cache-Control: no-store`.
- **`SisterAppStandardEndpoints`:** Serves `suite.manifest.json` on `/api/v1/suite/manifest`, `/api/suite/manifest`, and `/suite.manifest.json`.
- **`RootLauncherHost`:** Clean-root launcher forwarding CLI commands via `AttachConsole(-1)` or spawning GUI/tray process from `app\`.

### 1.5 `Foundation.Installer` (`v0.2.2`, `15` files)
- **Tier 1 (`StandardInstallerHost` + `InstallerAppDescriptor`):** One-call entry point delivering the 650×434 Dark WinForms Setup Wizard (`InstallerWizardForm`), trailing `AllowAutoStart = true` opt-out (when `false`, hides `_chkAutoStart`, rebalances layout spacing, and clamps `--autostart`), `FileLockRetryHelper` (`10` attempts / `150–500 ms` backoff), safe `--test-mode --install-dir` auto-containment inside `\Sandbox\` or `%TEMP%`, `--visible-test` auto-closing (`~3s`) WinForms showcase, silent CLI (`--silent`, `--verify`, `--uninstall`, `--purge-data`, `--test-mode`, `--dry-run`, `--headless`, `--visible-test`, `--version`, `--help`), COM `.lnk` shortcuts, ARP uninstall registration, and `app\install-manifest.json`.
- **Tier 2 (`InstallerEngineCore` + `InstallerThemeOptions`):** Low-level custom installer helper (`FoundationInstallManifest`), also supporting `AllowAutoStart` (`true` default).

---

## 2. UI Libraries (`Foundation.UI` + `@foundation/ui v0.2.0`)

- **Desktop WinForms Hosts:** [`ShellBrandingOptions`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/ShellBrandingOptions.cs), [`DisposableWebViewHostForm`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/DisposableWebViewHostForm.cs), [`StaNativeDialogBridgeBase`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/StaNativeDialogBridgeBase.cs), [`TrayIconBadgeHelper`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/TrayIconBadgeHelper.cs).
- **React 18 / TypeScript (`@foundation/ui v0.2.0`, 10 components):**
  - Barrel exports (`@foundation/ui`): `GlobalTerminalDrawer`, `TerminalToolbarAndFilters`, `StyledTooltip`, `ModalDialog`, `ConfirmActionModal`, `ResponsiveTable`, `UniversalDataTable`, `SchemaSettingsRenderer` + `terminalDrawerCore`.
  - Direct submodule exports (`@foundation/ui/components/<Name>`): `CoexistenceWarningBanner`, `HybridPathPicker`, `SidebarNavRail`.
- **Vanilla Web Assets:** [`foundation-tokens.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-tokens.css), [`foundation-shell.ts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-shell.ts), [`foundation-terminal.js`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.js), [`foundation-terminal.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.css).

---

## 3. Python Package `foundation_core` (`v0.1.2`) & `Dev\tools\`

- **`foundation_core` (`Dev\src\foundation_core\`):** Zero-dependency parity modules [`cli.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/cli.py), [`settings.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/settings.py), [`logging.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/logging.py), [`crash_dumper.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/crash_dumper.py), [`locks.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/locks.py), [`db.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/db.py).
- **Universal Dev Tooling (`Dev\tools\`):** [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py), [`build_release_universal.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/build_release_universal.py), [`verify_sandbox_universal.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/verify_sandbox_universal.py), [`notion_task_helper.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/notion_task_helper.py).

---

## 4. Quality & Verification Baseline

- **Unit Tests:** `26` `[Fact]` tests across `5` files in [`Dev\tests\Foundation.Tests\`](file:///<PROJECTS_ROOT>/Foundation/Dev/tests/Foundation.Tests/) (`FoundationCoreTests`: 6, `FoundationSettingsAndInstallerTests`: 5, `FoundationDecouplingTests`: 4, `FoundationTerminalDrawerTests`: 3, `FoundationV031PrimitivesTests`: 8).
- **Code Size Compliance:** `100%` of source/test/tooling files in `Dev\` are `<= 400 LOC`.
