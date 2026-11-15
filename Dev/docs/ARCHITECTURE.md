# Foundation (`v0.4.1-hf.1`) — Architecture & File Registry

> **Audience:** Engineers and autonomous agents working on `<PROJECTS_ROOT>\Foundation\` or tracing internals referenced by consumer apps (`Housekeeping`, `ShadowLibrary`, `reStation`, `KingsFieldRecomp`, `MediaSync`).
> **Note for API Lookups:** If you only need public signatures, props, or CLI flags, read [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md) first instead of opening source files.

---

## 1. System Overview & Assembly Dependency Topology

`Foundation` is the zero-duplication Shared SDK & Infrastructure Core (`v0.4.1-hf.1`, `.NET 8`, React/TypeScript `@foundation/ui v0.4.1-hf.1`, Python `foundation_core v0.4.1-hf.1`) serving all companion apps under `<PROJECTS_ROOT>\`. Consumers treat both `<PROJECTS_ROOT>\Foundation\` and the published binary hub `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\` as **strictly read-only** during consumer development.

```mermaid
flowchart LR
    Core["Foundation.Core v0.4.1-hf.1"]
    Retro["Foundation.Retro v0.4.1-hf.1"]
    Sidecars["Foundation.Sidecars v0.4.1-hf.1"]
    Server["Foundation.Server v0.4.1-hf.1"]
    Installer["Foundation.Installer v0.4.1-hf.1"]
    UI["Foundation.UI + @foundation/ui v0.4.1-hf.1"]
    Launcher["Foundation.Launcher v0.4.1-hf.1"]
    PyCore["foundation_core v0.4.1-hf.1 (Python >= 3.12)"]
    Hub["SharedLibs/Foundation/Latest"]

    Core --> Retro
    Core --> Sidecars
    Retro --> Sidecars
    Core --> Server
    Core --> Installer
    Core -. "Pushes binaries & TS/Py" .-> Hub
    Retro -.-> Hub
    Sidecars -.-> Hub
    Server -.-> Hub
    Installer -.-> Hub
    UI -.-> Hub
    Launcher -.-> Hub
    PyCore -.-> Hub
```

### 1.1 Subproject Dependency & Target Matrix

| Subproject | Target / Runtime | Depends On (Project / External) | Primary Role |
|---|---|---|---|
| `Foundation.Core` (`v0.4.1-hf.1`) | `net8.0` | `Microsoft.Data.Sqlite 8.0.11`, `System.Security.Cryptography.ProtectedData 8.0.0` | Runtime environment (`AppEnvironment`, `UiPresentationMode.TrayOrBackground`, `AssertPathContained`, `NormalizeLongPath`), CLI/logs, atomic settings, DPAPI, SQLite WAL, locks, telemetry, crash dumps, jobs, hardened HTTP (`ResponseHeadersTimeout`, `416` recovery), tools, module loader, high-res frame pacer, SPSC audio ring buffer, WAV/PNG/RGBA overlay |
| `Foundation.Retro` (`v0.4.1-hf.1`) | `net8.0` | `Foundation.Core` | 41-platform catalog (`IsVerifiedReferenceHash`), filename normalizer, Logiqx/Redump/RA/CMP DAT parser, headerless stripper + 20-magic sniff, IPS/BPS/UPS patcher, BYO media extractor, AOT-primary / `.decomp` contracts (`RecompTierAndBackendContracts.cs`) |
| `Foundation.Sidecars` (`v0.4.1-hf.1`) | `net8.0` | `Foundation.Core`, `Foundation.Retro` | Single-pass CRC32/MD5/SHA1/SHA256 + headerless hasher, ZIP/7z `-slt` inspector (LZMA/Deflate64 fallback + nested `::` inspection) + TorrentZip, `.hash` v1 sidecars, `.hklock` sentinel |
| `Foundation.Server` (`v0.4.1-hf.1`) | `net8.0-windows` | `Foundation.Core`, `Microsoft.AspNetCore.App` | Tiered Kestrel localhost/Tailscale/PBKDF2/HMAC cookie auth + Sister-App `/suite/manifest` endpoints |
| `Foundation.Launcher` (`v0.4.1-hf.1`) | `net8.0-windows` | *(None — zero dependencies)* | Clean-root Win32 `AttachConsole(-1)` CLI/GUI shim launching `app\<App>.exe` with `--wait`/`--open`/`--check-updates`/`--register-assoc` sync wait, relative arg rebase & Ctrl+C forwarding |
| `Foundation.Installer` (`v0.4.1-hf.1`) | `net8.0-windows` (WinForms) | `Foundation.Core` | Tier 1 drop-in `StandardInstallerHost` (`Run` + `RunWithResult`) + Dark Setup Wizard (`<= 400 LOC` partials, composite `CaptureWizardPreviewBitmap`, `showLegacyMigration` decoupling, manifest forwarding of `InstallProfile` / companion dirs / `MigrateLegacy`, `InstallerRuntimeContext` + `FileLockRetryHelper` isolated test guards); Tier 2 `InstallerEngineCore` |
| `Foundation.UI` (`v0.4.1-hf.1` Web) | `net8.0-windows` (WinForms) + React 19 / TS / Vanilla | `Foundation.Core`, `Microsoft.Web.WebView2 1.0.2903.40` (desktop); `react ^19` peer (web) | `DisposableWebViewHostForm` (`AppEnvironment`-aware ctor, DWM dark frame + composite PNG capture), STA dialogs, tray badges, 10 React components, Vanilla drawer + tokens |
| `foundation_core` (`v0.4.1-hf.1`) | Python `>= 3.12` | *(Python standard library only)* | Parity CLI (`--data-dir`, `--config`, `--log-dir`, `--port`, `--dry-run`, `resolve_app_paths`), atomic settings, heartbeat/capped logging, crash dumper, PID lock, SQLite WAL helper |

---

## 2. Core End-to-End Runtime Pipelines

1. **Startup & Path Containment Pipeline:**
   `Program.cs` constructs `new AppEnvironment("myapp", args)` → resolves CLI overrides (`--data-dir`, `--config`, `--log-dir`, `--port`, `--dry-run`, `--tray-only`, `--managed-by-mother`, `--suite-tab`, `--window`/`--browser`) → enforces `AppEnvironment.AssertPathContained` against allowed roots → registers `CrashBundleService.RegisterGlobalHandlers(...)` → loads granular configs via `GranularSettingsManager.LoadOrSelfHeal(...)` → opens `SqliteWalPool` connections.
2. **Single-Pass Media & `.hash` v1 Sidecar Pipeline:**
   File is checked against `HkLockSentinel.IsTemporaryOrPartialFileName` + `.hklock` → `HeaderlessRomStripper.DetectHeaderSkipLength` inspects leading bytes → `SinglePassStreamHasher.HashFileSinglePassAsync` computes full + headerless CRC32/MD5/SHA1/(SHA256) in a single `ArrayPool<byte>` read pass → `HashSidecarService` persists or verifies `.hash` v1 / `COMPOSITE_SHA1`.
3. **Kestrel Tiered Request Auth Pipeline:**
   Incoming request hits `FoundationTieredAuthMiddleware` → skips public health/auth routes (`/api/v1/health`, `/api/v1/auth/*`) and non-protected SPA static routes → `TieredAuthEngine.EvaluateRequest` checks API token / direct loopback bypass (absent `X-Forwarded-For`) / Tailscale CGNAT (`100.64.0.0/10`) or ULA IPv6 + Serve headers / `HMAC-SHA256` session cookie → returns HTTP `401` with `requiresSetup` if unauthenticated.
4. **Two-Tier Drop-In Installer Pipeline:**
   Setup executable calls `StandardInstallerHost.Run(new InstallerAppDescriptor(...), args)` → `--silent`/`--dry-run`/`--verify`/`--uninstall` enters `RunCliFlowAsync`, interactive GUI opens `InstallerWizardForm` → extracts embedded/external `payload.zip` via `InstallerEngine` (stopping running instances safely, writing `app\install-manifest.json`, preserving `data\` & `logs\`, creating `.lnk` shortcuts, ARP uninstall keys, and optional `-Uninstall.exe`; suppressing HKCU writes when `InstallerRuntimeContext.ShouldSuppressHostMutations` is active and blocking canonical `Programs\` targets during isolated test runs).
5. **Hybrid Module Discovery Pipeline:**
   `HybridModuleLoader` scans host assemblies via `DiscoverFromAssemblies` (`FolderSlice`) and `app\modules\*.Module.*.dll` via `DiscoverExternalDllModules` (`DllModule`) → reads `IFoundationModule.SettingsSchema` → UI renders controls automatically with `SchemaSettingsRenderer`.
6. **SDK Hub Publish & Consumer Sync Pipeline:**
   `python Dev\tools\sync_foundation_sdk.py --publish` copies compiled `Release` DLL/XML outputs, `@foundation/ui` TypeScript sources, `foundation_core`, and `Dev\tools\*.py` into `<PROJECTS_ROOT>\SharedLibs\Foundation\v0.4.1-hf.1\` and `Latest\` with `sdk-manifest.json` → `--sync-consumer <ProjectRoot>` mirrors pinned libraries into consumer workspaces and removes stale retired test-harness artifacts.

---

## 3. Complete File & `<= 400 LOC` Registry

Every source, build, test, and tooling file in `Dev\` (excluding build-mirrored `Foundation.UI\WebAssets\Web` / `components` copies and generated `bin/` / `obj/`) complies with **Standard 01 / 07 `<= 400 LOC`** (`max = 393 LOC` in `InstallerWizardForm.LayoutAndTheme.cs`).

### 3.1 Solution Root (`Dev\`)

| File | LOC | Responsibility |
|---|---|---|
| [`Dev\Directory.Build.props`](file:///<PROJECTS_ROOT>/Foundation/Dev/Directory.Build.props) | 24 | Enforces `net8.0`, nullable/implicit usings, `Deterministic`, `ContinuousIntegrationBuild`, unified SDK `Version=0.4.1-hf.1` |
| [`Dev\Foundation.sln`](file:///<PROJECTS_ROOT>/Foundation/Dev/Foundation.sln) | 60 | Visual Studio solution binding all 7 C# library projects + `Foundation.Tests` |

### 3.2 `Dev\src\Foundation.Core\` (`18` `.cs` files + `.csproj`)

| File | LOC | Exported / Key Symbols |
|---|---|---|
| `Foundation.Core.csproj` | 13 | Package refs (`Microsoft.Data.Sqlite`, `ProtectedData`) |
| `AppEnvironment.cs` | 232 | `UiPresentationMode` (`DesktopWindow`, `LocalBrowser`, `SuiteTab`, `TrayOrBackground`), `AppEnvironment`, `AssertPathContained`, `NormalizeLongPath` |
| `AudioRingBuffer.cs` | 223 | `AudioRingBuffer` (lock-free SPSC `short`/`float` PCM + smooth ramp-to-zero underrun) |
| `CliAndProcessPrimitives.cs` | 172 | `CliArgumentHelper`, `BoundedTailStream` (64 KB), `CappedSharedLogSink` (4 MB) |
| `CrashBundleService.cs` | 197 | `CrashBundlePayload`, `CrashBundleService` (`crash_<appId>_<ts>.json`) |
| `DpapiSecretStore.cs` | 161 | `DpapiSecretStore` (`data\secrets.dpapi` CurrentUser protection) |
| `GranularSettingsManager.cs` | 257 | `SettingsLoadOutcome<T>`, `GranularSettingsManager` (`.tmp -> .bak`, 20 snapshots) |
| `HeartbeatLogger.cs` | 124 | `HeartbeatSnapshot`, `HeartbeatLogger` (`<= 15s`, `\r\x1b[K`) |
| `HighResFramePacer.cs` | 197 | `HighResFramePacer` (`winmm.dll` `timeBeginPeriod(1)`, sleep+spinwait, 120-frame FPS/P99) |
| `HybridModuleLoader.cs` | 166 | `DiscoveredModuleRecord`, `HybridModuleLoader` |
| `IFoundationModule.cs` | 57 | `ModuleType`, `SchemaSettingDescriptor`, `IFoundationModule` |
| `JobObjectGuard.cs` | 228 | `JobObjectGuard`, `DualJobCancellationCoordinator` |
| `PidSafeLockfile.cs` | 266 | `PidSafeLockEnvelope`, `PidSafeLockfile` |
| `PixelOverlayCanvas.cs` | 247 | `RgbaColor`, `PixelOverlayCanvas` (Porter-Duff `SrcOver`, 5×7 ASCII bitmap font) |
| `RgbaPngWriter.cs` | 133 | `RgbaPngWriter` (pure-.NET lossless RGBA PNG via `ZLibStream` + IEEE CRC32) |
| `SafeNetworkTransfer.cs` | 303 | `SafeTransferOptions`, `SafeTransferResult`, `SafeNetworkTransfer` |
| `SqliteWalPool.cs` | 116 | `SqliteWalPool` (`WAL`, `busy_timeout=15000`, `VACUUM INTO`) |
| `ToolResolver.cs` | 162 | `ToolResolutionSource`, `ToolResolutionResult`, `ToolResolver` |
| `WavCaptureWriter.cs` | 136 | `WavCaptureWriter` (16-bit PCM RIFF/WAV writer) |

### 3.3 `Dev\src\Foundation.Retro\` (`8` `.cs` files + `.csproj`, `v0.4.1-hf.1`)

| File | LOC | Exported / Key Symbols |
|---|---|---|
| `Foundation.Retro.csproj` | 12 | Project ref `Foundation.Core` (inherits `v0.4.1-hf.1`) |
| `ConsoleSystemCatalog.cs` | 265 | `RetroBiosExpectation` (`IsVerifiedReferenceHash`), `RetroPlatformCatalogEntry`, `RetroHeaderSkipRule`, `RetroProvenanceTagRule`, `ConsoleSystemCatalog` |
| `DatTitleNormalizer.cs` | 335 | `ParsedRomFilenameMetadata`, `DatTitleNormalizer` |
| `HeaderlessRomStripper.cs` | 206 | `HeaderlessRomStripper`, `MagicBytePlatformDetector` |
| `MediaContainerExtractor.cs` | 321 | `MediaContainerFormat`, `MediaExtractionOptions`, `MediaExtractionResult`, `MediaContainerExtractor` |
| `NoIntroRedumpDatParser.cs` | 368 | `ParsedDatCatalogHeader`, `ParsedDatRomRecord`, `NoIntroRedumpDatParser` |
| `RecompContracts.cs` | 88 | `DiscChecksumEntry`, `IRecompModuleManifest`, `RecompTimingMode`, `RecompPatchDescriptor`, `RecompHostOptions`, `RecompFrameStats`, `IRecompRenderBackend`, `IRecompHost`, `IRecompGameModule` |
| `RecompTierAndBackendContracts.cs` | 125 | `RecompExecutionTierMode`, `RecompRenderBackendIds`, `IDecompPackageManifest`, `RecompRuntimeTelemetrySnapshot`, `AotCacheLayoutHelper` |
| `SoftPatchEngine.cs` | 298 | `SoftPatchEngine` (IPS, `BPS1`, `UPS1`, CRC32) |

### 3.4 `Dev\src\Foundation.Sidecars\` (`5` `.cs` files + `.csproj`)

| File | LOC | Exported / Key Symbols |
|---|---|---|
| `Foundation.Sidecars.csproj` | 13 | Project refs `Foundation.Core`, `Foundation.Retro` |
| `HashSidecarService.cs` | 287 | `SidecarDocument`, `HashSidecarService` (`.hash` v1, `COMPOSITE_SHA1`) |
| `HkLockSentinel.cs` | 181 | `HkLockEnvelope`, `HkLockSentinel` (`.hklock`, `.coexistence.json` v2) |
| `SevenZipAndZipInspector.cs` | 208 | `InspectedArchiveEntry`, `SevenZipAndZipInspector` (ZIP/7z listing & stream hashing partial) |
| `SevenZipAndZipInspector.ExtractAndZip.cs` | 258 | `SevenZipAndZipInspector` (bounded reads, single-entry extract & TorrentZip builder partial) |
| `SinglePassStreamHasher.cs` | 187 | `ComputedStreamHashes`, `Crc32Accumulator`, `SinglePassStreamHasher` |

### 3.5 `Dev\src\Foundation.Server\` & `Foundation.Launcher\` (`4` `.cs` files + 2 `.csproj`)

| File | LOC | Exported / Key Symbols |
|---|---|---|
| `Foundation.Server.csproj` | 13 | FrameworkReference `Microsoft.AspNetCore.App`, ref `Foundation.Core` |
| `SisterAppStandardEndpoints.cs` | 52 | `SisterAppStandardEndpoints` (`/api/v1/suite/manifest`, `/api/suite/manifest`, `/suite.manifest.json`) |
| `TieredAuthEngine.cs` | 285 | `TieredAuthConfigSnapshot`, `TieredAuthEvaluation`, `TieredAuthEngine` |
| `TieredAuthMiddleware.cs` | 57 | `FoundationTieredAuthMiddleware` |
| `Foundation.Launcher.csproj` | 8 | `net8.0-windows`, zero external deps |
| `RootLauncherHost.cs` | 270 | `RootLauncherHost` (Win32 `AttachConsole(-1)`, fail-closed self-spawn guard) |

### 3.6 `Dev\src\Foundation.Installer\` (`17` `.cs` files + `.csproj`, `v0.4.1-hf.1`)

| File | LOC | Role |
|---|---|---|
| `Foundation.Installer.csproj` | 13 | `UseWindowsForms`, ref `Foundation.Core`, inherits `v0.4.1-hf.1` |
| `FileLockRetryHelper.cs` | 126 | Bounded 10-attempt lock retry (`150/250/500ms`) + `IsSafeIsolatedInstallDirectory` / `IsCanonicalProductionOrSystemPath` |
| `InstallerContracts.cs` | 184 | `InstallerAppDescriptor` (`AllowLegacyMigration`), `InstallerConfigOverride`, `InstallManifest`, `InstallerInstallOptions`, `InstallerHostRunResult` |
| `InstallerRuntimeContext.cs` | 118 | `DataDirectory` / `ConfigOverride` resolution + `ShouldSuppressHostMutations` (`APP_ISOLATED_TEST`) |
| `StandardInstallerHost.cs` | 220 | **Tier 1** `Run` + `RunWithResult` CLI and interactive GUI wizard entry point |
| `InstallerEngine.Core.cs` | 137 | Core `InstallerEngine` state, installation detection & canonical production path guard |
| `InstallerEngine.InstallAndExtract.cs` | 344 | Process shutdown, ZIP extraction, options forwarding (`InstallProfile`, `ReEngineInstallDir`, `SuiteInstallDir`, `MigrateLegacy`) |
| `InstallerEngine.CliAndRegistry.cs` | 252 | `InstallerCliOptions`, `ParseCliArgs`, ARP metadata, pre-upgrade backup & self-delete trampoline scheduling |
| `InstallerEngine.UninstallAndShortcuts.cs` | 271 | ARP uninstall registry keys (suppressed when isolated), `.lnk` shortcuts, clean removal |
| `InstallerEngine.VerifyAndUpgrade.cs` | 80 | `HasEmbeddedPayload`, `HasPayloadSource`, SHA-256 payload & archive verification |
| `InstallerWizardForm.cs` | 320 | 650×434 Dark WinForms Setup Wizard constructors (`showLegacyMigration`), header & destination controls |
| `InstallerWizardForm.ExecutionAndLogs.cs` | 378 | Surface setup, upgrade badge, profile/secondary/legacy forwarding & `CaptureWizardPreviewBitmap` |
| `InstallerWizardForm.LayoutAndTheme.cs` | 393 | Secondary destination controls, options/footer controls, responsive bounds, WinForms lifecycle & DWM dark frame |
| `InstallerWizardControls.cs` | 167 | Styled buttons, cards, secondary path pickers, DWM dark titlebar & progress controls |
| `WindowsShortcutWriter.cs` | 216 | COM `IShellLinkW` shortcut creator + isolated HKCU suppression |
| `InstallerThemeOptions.cs` | 123 | **Tier 2** visual customization DTO (`AllowAutoStart`, `AllowLegacyMigration`, `InstallerThemeOptions`) |
| `InstallerEngineCore.cs` | 321 | **Tier 2** core clean-root helper (`FoundationInstallManifest`) + `IsIsolatedOverride` guard |
| `InstallerEngineCore.BackupAndVerify.cs` | 379 | **Tier 2** data backup/restore, integrity verify & opt-in legacy migration |

### 3.7 `Dev\src\Foundation.UI\` (`@foundation/ui v0.4.1-hf.1`)

| File | LOC | Exported / Key Symbols |
|---|---|---|
| `Foundation.UI.csproj` | 28 | `WebView2 1.0.2903.40`, copies `Web\` to `$(OutputPath)\WebAssets\`, gated `SyncFoundationWebAndSharedLibs` (`v0.4.1-hf.1`) |
| `ShellBrandingOptions.cs` | 51 | `ShellBrandingOptions` (`SdkVersion = "0.4.1-hf.1"`) |
| `Desktop\DisposableWebViewHostForm.cs` | 94 | `DisposableWebViewHostForm` (`AppEnvironment`-aware ctor, DWM dark frame, composite `CapturePreviewBitmap`) |
| `Desktop\StaNativeDialogBridgeBase.cs` | 191 | `StaNativeDialogBridgeBase` |
| `Desktop\TrayIconBadgeHelper.cs` | 56 | `TrayIconBadgeHelper` |
| `Web\package.json` & `Web\index.ts` | 14 / 51 | `@foundation/ui v0.4.1-hf.1` barrel (exports 7 of 10 React components + drawer utils) |
| `Web\components\GlobalTerminalDrawer.tsx` | 291 | `GlobalTerminalDrawer` |
| `Web\components\TerminalToolbarAndFilters.tsx` | 275 | `TerminalToolbarAndFilters` |
| `Web\components\terminalDrawerCore.ts` | 162 | Line level/subsystem classifier, timestamp formatter, `.log` exporter |
| `Web\components\UniversalDataTable.tsx` | 238 | `UniversalDataTable` |
| `Web\components\HybridPathPicker.tsx` | 215 | `HybridPathPicker` *(direct submodule import only)* |
| `Web\components\SchemaSettingsRenderer.tsx` | 171 | `SchemaSettingsRenderer` |
| `Web\components\ModalDialog.tsx` | 161 | `ModalDialog`, `ConfirmActionModal` |
| `Web\components\SidebarNavRail.tsx` | 158 | `SidebarNavRail` *(direct submodule import only)* |
| `Web\components\CoexistenceWarningBanner.tsx` | 150 | `CoexistenceWarningBanner` *(direct submodule import only)* |
| `Web\components\StyledTooltip.tsx` | 121 | `StyledTooltip` |
| `Web\components\ResponsiveTable.tsx` | 50 | `ResponsiveTable` |
| `WebAssets\foundation-shell.ts` | 58 | WebView2 / Browser host bridge |
| `WebAssets\foundation-tokens.css` | 117 | Canonical surface/accent/responsive CSS variables |
| `WebAssets\foundation-terminal.js` & `.css` | 305 / 111 | Vanilla JS `FoundationTerminalDrawer` & styling |

### 3.8 `Dev\src\foundation_core\` (`8` files, Python `v0.4.1-hf.1`) & `Dev\tools\` (`3` files)

| File | LOC | Key Symbols / Purpose |
|---|---|---|
| `foundation_core\pyproject.toml` & `__init__.py` | 13 / 32 | Package metadata (`foundation-core v0.4.1-hf.1`, `SDK_VERSION = "0.4.1-hf.1"`) |
| `foundation_core\cli.py` | 60 | `StandardCliOptions`, `parse_standard_cli_flags`, `resolve_app_paths`, `assert_path_contained` |
| `foundation_core\settings.py` | 87 | `SettingsLoadResult`, `GranularSettingsStore` |
| `foundation_core\logging.py` | 68 | `HeartbeatTracker`, `CappedLogFile` |
| `foundation_core\crash_dumper.py` | 80 | `CrashDumper` |
| `foundation_core\locks.py` | 74 | `PidSafeLock` |
| `foundation_core\db.py` | 83 | `SqliteWalHelper` |
| `tools\sync_foundation_sdk.py` | 253 | Publish hub (`SharedLibs\Foundation\v0.4.1-hf.1` & `Latest`), `purge_stale_pdbs`, `stage_clean_release_bundle`, & sync consumers + stale artifact cleanup *(enforced `<= 260 LOC`)* |
| `tools\build_release_universal.py` | 119 | Universal 6-file bundle validator (`--allow-sdk-zip`), `stage_sdk_release_zip`, `generate_sha256sums` |
| `tools\notion_task_helper.py` | 77 | Standard 16 Notion Task Backbone helper (`resolve_notion_ids` via env/`notion.config.json`) |

---

## 4. SQLite WAL Concurrency & Security Invariants

- **SQLite MVCC Configuration (`SqliteWalPool.cs` & `foundation_core\db.py`):**
  - `Cache = SqliteCacheMode.Default` (avoids `Shared` cache table locks under async multi-reader workloads).
  - `PRAGMA journal_mode = WAL;`, `busy_timeout = 15000`, `synchronous = NORMAL`, `foreign_keys = ON`, `temp_store = MEMORY`, `cache_size = -65536` (~64 MB page cache).
  - Online backups execute `VACUUM INTO` into a temporary file followed by atomic replace.
- **Security & Containment Boundaries:**
  - `AppEnvironment.AssertPathContained` canonicalizes full paths and aborts traversal outside explicit allowed roots.
  - `InstallerRuntimeContext` (`ShouldSuppressHostMutations`) and `InstallerEngineCore` (`IsIsolatedOverride`) suppress HKCU registry writes and reject canonical production/system install targets (`Programs\`, `Windows\`) during `APP_ISOLATED_TEST` / `--data-dir` unit test runs.
  - `DpapiSecretStore` encrypts secrets under Windows `DataProtectionScope.CurrentUser` so secrets never rest in plaintext `settings.json`.
  - `PidSafeLockfile` and `HkLockSentinel` verify both PID and `ProcessStartTimeUtc` to defeat OS PID reuse on crash recovery.
  - `TieredAuthEngine` disables localhost bypass when `X-Forwarded-For` is present, limits Tailscale bypass to `100.64.0.0/10` / `fd7a:115c:a1e0::/48`, hashes passwords with PBKDF2-SHA256 (`100,000` iterations), and signs session cookies with HMAC-SHA256.

---

## 5. Unit Test Suite (`33` `[Fact]` Tests across 6 Files)

| Test File (`Dev\tests\Foundation.Tests\`) | LOC | Verified Coverage |
|---|---|---|
| `FoundationCoreTests.cs` | 241 | `AppEnvironment` flags/containment, `GranularSettingsManager` `.bak` heal + 20-snapshot trim, `DpapiSecretStore` Windows round-trip, `PidSafeLockfile` contention + orphan breaking, `HeartbeatLogger` `\r\x1b[K` + 15s cap, `SqliteWalPool` WAL pragmas + `VACUUM INTO` |
| `FoundationSettingsAndInstallerTests.cs` | 270 | `InstallerEngineCore` isolated install/wizard lifecycle + `data\` preservation on upgrade, `CrashBundleService` JSON dump, `DualJobCancellationCoordinator` StopCurrent vs StopAll, `SafeNetworkTransfer` HTTP Range `.partial_<jobId>` resume + disk preflight, `StandardInstallerHost` + `InstallerEngine` isolated `--silent` lifecycle |
| `FoundationDecouplingTests.cs` | 154 | `HybridModuleLoader` slice & assembly discovery, `ToolResolver` 4-tier priority, `BoundedTailStream` 64 KB ring + `CappedSharedLogSink` 4 MB cap, `TieredAuthEngine` loopback/`X-Forwarded-For`/PBKDF2/HMAC cookie verification |
| `FoundationTerminalDrawerTests.cs` | 211 | `terminalDrawerCore` line level/subsystem classification, search/verbosity filtering, dual-token button contract verification, `sync_foundation_sdk.py` `v0.4.1-hf.1` publish, consumer sync & stale test-harness cleanup |
| `FoundationV031PrimitivesTests.cs` | 310 | `HighResFramePacer`, `AudioRingBuffer`, `WavCaptureWriter`, `RgbaPngWriter`, `PixelOverlayCanvas`, `AppEnvironment.TrayOrBackground` / `DesktopWindow`, `MediaContainerExtractor`, `RecompContracts`, `AllowAutoStart = false`, `FileLockRetryHelper` & isolated path fail-closed guards |
| `FoundationV040ContractsTests.cs` | 306 | `InstallerAppDescriptor` `%LOCALAPPDATA%` Programs root resolution, `InstallerEngine` isolated production-path fail-closed guard, `ParseCliArgs` flags, `SafeNetworkTransfer` HTTP 416/404/headers timeout, `RootLauncherHost`, `ConsoleSystemCatalog` verified flags, `AotCacheLayoutHelper` & recomp tier contracts |

---

## 6. Architectural Decision Records & Extension Playbooks

### 6.1 Key ADRs
- **ADR-001 (Zero App Domain Coupling):** `Foundation` may never reference specific consumer apps; domain rules belong in callers via descriptors (`InstallerAppDescriptor`, `SchemaSettingDescriptor`, `IReadOnlyList<RetroPlatformCatalogEntry>`).
- **ADR-002 (400 LOC File Cap):** 100% of source files stay strictly `<= 400` lines (`max = 393 LOC` in `InstallerWizardForm.LayoutAndTheme.cs`) via focused partial-class splits (`InstallerEngine.*.cs`, `InstallerWizardForm.*.cs`, `SevenZipAndZipInspector.*.cs`) and dedicated helper modules (`InstallerRuntimeContext.cs`, `InstallerContracts.cs`).
- **ADR-003 (Consumer Read-Only Fence):** Companion app sessions cannot mutate `<PROJECTS_ROOT>\Foundation\` or `SharedLibs\Foundation\`; upstream fixes are logged in `Dev\docs\reports\` and executed only in a dedicated `Foundation` session.
- **ADR-004 (`SqliteCacheMode.Default` + `15000 ms` busy timeout):** Prevents shared-cache read/write deadlocks across background indexers and web UI API requests.
- **ADR-005 (Dual-Scope Job Cancellation):** Separating active-job cancellation from queue drain preserves queued jobs when stopping a stuck item.
- **ADR-006 (Two-Tier Installer Split):** Retaining `InstallerEngineCore` (`v0.2.0`) underneath while promoting `StandardInstallerHost` (`v0.2.1+`) lets tiny apps adopt the full dark wizard in 15 lines without breaking custom hosts.
- **ADR-007 (Sidecar & Directory Coexistence Protocol):** `.hash` v1 and `.hklock` use identical schemas across `Housekeeping` and `ShadowLibrary` via `Foundation.Sidecars`.
- **ADR-008 (Side-Effect Warning on `Foundation.UI.csproj`):** Building `Foundation.UI.csproj` with `-p:PublishFoundationSdk=true` triggers `SyncFoundationWebAndSharedLibs`, which copies built assets to `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\`. Never enable that flag during read-only audits.
