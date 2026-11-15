# Foundation (`v0.4.0`) — 1-Stop Interfaces & Agent API Reference

> **Purpose:** Single lookup reference for **every exported C# class/method, React/TS component (`@foundation/ui`), Vanilla JS/CSS asset, Python helper (`foundation_core`), `Dev\tools\*.py` CLI, and file/JSON schema** in `<PROJECTS_ROOT>\Foundation\`.
> **Snapshot Baseline:** Unified SDK `0.4.0` (`.NET 8`, `@foundation/ui v0.4.0`, `foundation_core v0.4.0`).

---

## 0. Fast-Path Lookup & Import Pitfalls (Read First)

| If you need... | Use this symbol / file | Exact import / CLI / path |
|---|---|---|
| Fail-closed sandbox, `--test-mode`, `--visible-test`, `--data-dir`, UI mode | `SandboxEnvironment`, `UiPresentationMode` | `using Foundation.Core;` |
| High-res FPS frame pacer (`timeBeginPeriod(1)`, unlocked `0`, P99) | `HighResFramePacer` | `using Foundation.Core;` |
| Lock-free SPSC PCM audio ring buffer + underrun ramp-to-zero | `AudioRingBuffer` | `using Foundation.Core;` |
| 16-bit RIFF/WAV capture, pure-.NET RGBA PNG, 5×7 HUD canvas | `WavCaptureWriter`, `RgbaPngWriter`, `PixelOverlayCanvas`, `RgbaColor` | `using Foundation.Core;` |
| CLI flags / options / positionals / bounded logs | `CliArgumentHelper`, `BoundedTailStream`, `CappedSharedLogSink` | `using Foundation.Core;` |
| Atomic JSON settings + self-heal + 20 snapshots | `GranularSettingsManager` | `using Foundation.Core;` |
| Windows DPAPI secret storage (`data\secrets.dpapi`) | `DpapiSecretStore` | `using Foundation.Core;` |
| SQLite WAL (`busy_timeout=15000`, `VACUUM INTO`) | `SqliteWalPool` | `using Foundation.Core;` |
| PID + `processStartTimeUtc` lockfiles | `PidSafeLockfile`, `HkLockSentinel` | `using Foundation.Core;` / `Foundation.Sidecars;` |
| `<= 15s` telemetry heartbeats + `\r\x1b[K` ANSI | `HeartbeatLogger` | `using Foundation.Core;` |
| Unhandled JSON crash bundles | `CrashBundleService` | `using Foundation.Core;` |
| Windows Job Object + Stop Current vs Stop All | `JobObjectGuard`, `DualJobCancellationCoordinator` | `using Foundation.Core;` |
| Safe downloads (`Range` resume, 1.2x+512 MB disk preflight) | `SafeNetworkTransfer` | `using Foundation.Core;` |
| 4-tier tool discovery (`settings -> app\tools\ -> PATH`) | `ToolResolver` | `using Foundation.Core;` |
| In-assembly slices + external DLL plugin loader | `HybridModuleLoader`, `IFoundationModule` | `using Foundation.Core;` / `Foundation.Core.Contracts;` |
| 41-platform catalog, filename normalizer, DAT parser, headerless, soft-patch | `ConsoleSystemCatalog`, `DatTitleNormalizer`, `NoIntroRedumpDatParser`, `HeaderlessRomStripper`, `MagicBytePlatformDetector`, `SoftPatchEngine` | `using Foundation.Retro;` |
| BYO `.iso`/`.bin`+`.cue`/`.chd`/`.7z`/`.zip` extractor + SHA-256 | `MediaContainerExtractor`, `MediaContainerFormat`, `MediaExtractionOptions`, `MediaExtractionResult` | `using Foundation.Retro;` |
| Static recomp host/module/timing/render/fixture/AOT & `.decomp` contracts | `IRecompHost`, `IRecompGameModule`, `IRecompModuleManifest`, `IRecompRenderBackend`, `IVisibleTestHarness`, `RecompExecutionTierMode`, `RecompRenderBackendIds`, `IDecompPackageManifest`, `RecompRuntimeTelemetrySnapshot`, `AotCacheLayoutHelper`, `SaveStateFixtureRunner` | `using Foundation.Retro;` |
| Single-pass hash, ZIP/7z `-slt`, `.hash` v1, `.hklock` | `SinglePassStreamHasher`, `SevenZipAndZipInspector`, `HashSidecarService`, `HkLockSentinel` | `using Foundation.Sidecars;` |
| Kestrel tiered auth + `/suite/manifest` endpoints | `TieredAuthEngine`, `FoundationTieredAuthMiddleware`, `SisterAppStandardEndpoints` | `using Foundation.Server;` |
| Clean-root Win32 `AttachConsole(-1)` launcher shim | `RootLauncherHost` | `using Foundation.Launcher;` |
| WinForms WebView2 host, STA dialogs, tray badges | `DisposableWebViewHostForm`, `StaNativeDialogBridgeBase`, `TrayIconBadgeHelper` | `using Foundation.UI;` |
| Full dark WinForms + CLI Setup/Uninstall Wizard (`v0.4.0`) | **`StandardInstallerHost`** + `InstallerAppDescriptor` (`StudioName`, `SuiteName`, `AllowAutoStart`), `InstallerProfileOption`, `SecondaryPathPickerOptions`, `FileLockRetryHelper` | `using Foundation.Installer;` |
| Low-level app-dir & suite registry helper (`v0.4.0`) | `InstallerEngineCore` + `InstallerThemeOptions` (`InstallerProfileKind`, `SecondaryPathPickerOptions`, `AllowLegacyMigration`, `AllowAutoStart`) | `using Foundation.Installer;` |
| React components (`@foundation/ui v0.4.0`) | 7 barrel exports from `@foundation/ui`; **3 direct submodule imports** | `@foundation/ui` vs `@foundation/ui/components/<Name>` |
| Vanilla HTML/JS terminal drawer & Python SDK | `FoundationTerminalDrawer` (`WebAssets\`), `foundation_core` (`Dev\src\foundation_core\`) | `Dev\src\Foundation.UI\WebAssets\` |

### Top Import & Caller Pitfalls
1. **React Barrel vs. Direct Submodule Imports (`Dev\src\Foundation.UI\Web\index.ts`):** `CoexistenceWarningBanner`, `HybridPathPicker`, and `SidebarNavRail` are NOT in the `@foundation/ui` barrel—import via `@foundation/ui/components/<Name>`.
2. **Two Installer Tiers:** Do not mix `InstallerAppDescriptor` + `StandardInstallerHost` (Tier 1) with `InstallerThemeOptions` + `InstallerEngineCore` (Tier 2).
3. **TFM Split:** `Foundation.Core`, `Foundation.Retro`, and `Foundation.Sidecars` target pure `net8.0` (zero WinForms/GDI+); `Foundation.Installer`, `Foundation.UI`, `Foundation.Server`, and `Foundation.Launcher` target `net8.0-windows`.

---

## 1. C# Shared Libraries

### 1.1 `Foundation.Core` (`Dev\src\Foundation.Core\`, `net8.0`, `v0.4.0`)
Depends on: `Microsoft.Data.Sqlite 8.0.11`, `System.Security.Cryptography.ProtectedData 8.0.0`.

#### `SandboxEnvironment` (`SandboxEnvironment.cs`)
- `UiPresentationMode { DesktopWindow, LocalBrowser, SuiteTab, HeadlessOrTrayOnly }`
- `SandboxEnvironment(string appId, IReadOnlyList<string>? args = null, string? baseDirectoryOverride = null, string? defaultConfigFileName = "settings.json")`
- Properties: `AppId`, `BaseDirectory`, `DataDirectory`, `ConfigFilePath`, `LogDirectory`, `PortOverride`, `TestMode`, `DryRun`, `Headless`, `VisibleTest` (`--visible-test` / `{PREFIX}_VISIBLE_TEST` / `VISIBLE_TEST`, distinct from `Headless`, resolves `DesktopWindow`), `TrayOnly`, `ManagedByMother`, `SuiteTab`, `PresentationMode`, `IsContainedSandbox`.
- Static helpers: `AssertPathContained(string candidatePath, string allowedRoot)`, `ResolveSiblingSandboxRoot(string? dataDir)` (permits `Sandbox\{app,data,logs,installed}` sibling containment while returning `null` for host escapes), `NormalizeLongPath(string path)`.

#### Timing, Audio, PNG & Overlay Primitives (`v0.3.1`)
- **`HighResFramePacer` (`HighResFramePacer.cs`, 197 LOC):** `IDisposable` (`TargetFps` where `<= 0` = unlocked), Windows `winmm.dll` `timeBeginPeriod(1)`/`timeEndPeriod(1)` guarded by `OperatingSystem.IsWindows()`, `WaitForNextFrame()`, `Reset()`, `TotalFrames`, `CurrentFps`, `AverageFrameMs`, `P99FrameMs`, `IsHighResolutionTimerActive`.
- **`AudioRingBuffer` (`AudioRingBuffer.cs`, 223 LOC):** Lock-free SPSC (`capacitySamples, channels = 2`), `Write(ReadOnlySpan<float|short>)`, `Read(Span<float|short>)`, `Clear()`, `CapacitySamples`, `BufferedSamples`, `AvailableWriteSamples`, `FillRatio`, `UnderrunCount`; smooth 32-frame ramp-to-zero attenuation on underrun.
- **`WavCaptureWriter` (`WavCaptureWriter.cs`, 136 LOC):** `IDisposable` (`outputPath, sampleRate = 44100, channels = 2`), `WriteInterleavedSamples(ReadOnlySpan<short|float>)`, `Flush()`, patches RIFF/data byte lengths on `Dispose()`.
- **`RgbaPngWriter` (`RgbaPngWriter.cs`, 133 LOC):** Pure-.NET lossless RGBA PNG via `ZLibStream` + IEEE CRC32: `Encode(int width, int height, ReadOnlySpan<byte> rgba)`, `WritePng(string filePath, int width, int height, ReadOnlySpan<byte> rgba)`.
- **`PixelOverlayCanvas` (`PixelOverlayCanvas.cs`, 247 LOC):** `RgbaColor(R, G, B, A = 255)`, `Clear`, `BlendSrcOver` (Porter-Duff `SrcOver`), `FillRect`, `StrokeRect`, `MeasureText`, `DrawText` (built-in 5×7 printable ASCII font), `ToPngBytes()`, `SavePng(string filePath)`.

#### Core Infrastructure (`v0.1.0`–`v0.3.0`)
| Class (`File`) | Key Public Signatures | Contract |
|---|---|---|
| `CliArgumentHelper`, `BoundedTailStream`, `CappedSharedLogSink` (`CliAndProcessPrimitives.cs`) | `HasFlag`, `GetOption`, `GetPositional`, `FormatBytes`; `BoundedTailStream(65536)` | CLI parsing + 64 KB ring / 4 MB log cap |
| `GranularSettingsManager` (`GranularSettingsManager.cs`) | `LoadOrSelfHeal<T>(fileName, defaultFactory)`, `SaveAtomically<T>(fileNameOrPath, value)` | `.tmp -> .bak -> File.Replace`, 20 history snapshots |
| `DpapiSecretStore` (`DpapiSecretStore.cs`) | `SetSecret(key, val)`, `GetSecret(key)`, `RemoveSecret(key)` | `ProtectedData` `CurrentUser` encrypted store |
| `SqliteWalPool` (`SqliteWalPool.cs`) | `OpenConnection()`, `OpenConnectionAsync()`, `ApplyStandardPragmas(conn, 15000)`, `VacuumIntoAsync(dest)` | `SqliteCacheMode.Default`, `WAL`, `busy_timeout=15000`, `cache_size=-65536` |
| `PidSafeLockfile` (`PidSafeLockfile.cs`) | `Acquire(path, appId, op, lease?)`, `TryAcquire(..., out handle, out holder)` | Compares `pid` + `processStartTimeUtc` |
| `HeartbeatLogger` (`HeartbeatLogger.cs`) | `ReportProgress(done, total, item?, forceEmit = false)` | `<= 15s` heartbeats + `\r\x1b[K` TTY flush |
| `CrashBundleService` (`CrashBundleService.cs`) | `RegisterGlobalHandlers(logDir, appId, version, ...)`, `TryWriteCrashBundle(...)` | Writes `logs\crashes\crash_<appId>_<ts>.json` |
| `DualJobCancellationCoordinator`, `JobObjectGuard` (`JobObjectGuard.cs`) | `BeginJobScope`, `StopCurrentJob`, `StopAllJobs`, `EndJobScope` | Dual-scope cancel + `KILL_ON_JOB_CLOSE` |
| `SafeNetworkTransfer` (`SafeNetworkTransfer.cs`) | `AssertSufficientDiskSpace`, `DownloadFileAsync(uri, dest, options?, handler?, ct?)` | `Range` resume via `.partial_<jobId>`, 1.2x + 512 MB reserve |
| `ToolResolver` (`ToolResolver.cs`) | `ResolveTool(toolId, exeNames, explicitOverride?, baseDir?, extraDirs?)` | Override -> `app\tools\` -> `PATH`/extra -> `MissingFallback` |
| `HybridModuleLoader`, `IFoundationModule` (`HybridModuleLoader.cs`, `IFoundationModule.cs`) | `RegisterModule`, `DiscoverFromAssemblies`, `DiscoverExternalDllModules` | `FolderSlice` & `*.Module.*.dll` plugin loader |

---

### 1.2 `Foundation.Retro` (`Dev\src\Foundation.Retro\`, `net8.0`, `v0.4.0`)
Depends on: `Foundation.Core`. Zero external NuGet packages.

| Class / Contract (`File`) | Key Public Signatures | Purpose |
|---|---|---|
| `ConsoleSystemCatalog` (`ConsoleSystemCatalog.cs`) | `BuildDefaultPlatforms()`, `BuildDefaultHeaderSkipRules()`, `BuildDefaultProvenanceTagRules()`, `ResolvePlatformId`, `ResolveTier2PlatformId` | 41 platforms, BIOS specs (`IsVerifiedReferenceHash`), copier header rules, provenance tags |
| `DatTitleNormalizer` (`DatTitleNormalizer.cs`) | `ParseFilename(path, rules?)`, `HealMalformedTagSyntax(...)` | Extracts clean title, region, languages, revision, serial, disc |
| `NoIntroRedumpDatParser` (`NoIntroRedumpDatParser.cs`) | `ParseDatFile(datPath, ...)` → `(Catalog, Entries)` | Logiqx/Redump/RA XML, ClrMamePro, `.sfv`/`.md5`/`.sha1` |
| `HeaderlessRomStripper`, `MagicBytePlatformDetector` (`HeaderlessRomStripper.cs`) | `DetectHeaderSkipLength(...)`, `InspectBinaryMagic(filePath)` | Skips `ines-16`/`smc-512`/etc., sniffs 20 binary magics |
| `SoftPatchEngine` (`SoftPatchEngine.cs`) | `ComputeCrc32UInt32`, `ApplyIpsPatchInMemory`, `ApplyBpsPatchInMemory`, UPS | Pure managed IPS / BPS1 / UPS1 patchers |
| `MediaContainerExtractor` (`MediaContainerExtractor.cs`) | `DetectFormat(path)`, `ExtractOrVerifyAsync(srcPath, destDir, MediaExtractionOptions?, ct)` → `MediaExtractionResult` | Extracts/verifies `.iso`, `.bin`+`.cue`, `.chd` (`chdman`), `.7z` (`7z`/`7za`), `.zip` (`AssertPathContained` + SHA-256) |
| `RecompContracts` (`RecompContracts.cs`) | `DiscChecksumEntry`, `IRecompModuleManifest`, `RecompTimingMode`, `RecompPatchDescriptor`, `RecompHostOptions`, `RecompFrameStats`, `IRecompRenderBackend`, `IRecompHost`, `IRecompGameModule`, `SaveStateFixtureSpec`, `VisibleTestShowcaseOptions`, `IVisibleTestHarness` | Pure-.NET static recomp host/module/timing/render/fixture/visible-test contracts |
| `RecompTierAndBackendContracts` (`RecompTierAndBackendContracts.cs`) | `RecompExecutionTierMode` (`AotPrimary`, `JitBringup`, `ThreadedIr`), `RecompRenderBackendIds` (`software`, `opengl`, `IsSupported`), `IDecompPackageManifest` (`ModuleId`, `PlatformId`, `DecompSchemaVersion`, `TargetDecompTier`, `GoalDecompTier`, `PackageVersion`, `UpstreamResearchPath`), `RecompRuntimeTelemetrySnapshot`, `AotCacheLayoutHelper` (`ResolveModuleCacheRoot`, `ResolveGeneratedAssemblyPath`, `ResolveBringupProfilePath`) | AOT-primary execution tier modes, render backend IDs, `.decomp` manifest & `<dataDir>\cache\` AOT path layout |
| `SaveStateFixtureRunner` (`SaveStateFixtureRunner.cs`) | `RunFixtureAsync(spec, stepFrameAsync, artifactDir?, refRgba?, ct)`, `RunAllFixturesAsync(...)` | Multi-scene PNG + WAV fixture runner & SHA-256 / pixel-delta validator |

---

### 1.3 `Foundation.Sidecars`, `Foundation.Server` & `Foundation.Launcher`
- **`Foundation.Sidecars` (`net8.0`):** `SinglePassStreamHasher` (`HashFileSinglePassAsync`, `HashStreamSinglePassAsync`), `SevenZipAndZipInspector` (`ListArchiveEntriesFastAsync`, TorrentZip), `HashSidecarService` (`.hash` v1 + `COMPOSITE_SHA1`), `HkLockSentinel` (`.hklock` + `.coexistence.json` v2).
- **`Foundation.Server` (`net8.0-windows`):** `TieredAuthEngine` (loopback, Tailscale CGNAT/ULA, PBKDF2-SHA256 100k, 30-day HMAC-SHA256 cookies), `FoundationTieredAuthMiddleware`, `SisterAppStandardEndpoints` (`/api/v1/suite/manifest`, `/api/suite/manifest`, `/suite.manifest.json`).
- **`Foundation.Launcher` (`net8.0-windows`):** `RootLauncherHost.ResolveTargetExecutable(rootDir, relPath, selfPath?)`, `RootLauncherHost.Run(relPath, displayName, args)` (`AttachConsole(-1)` + fail-closed self-spawn guard).

---

### 1.4 `Foundation.Installer` (`Dev\src\Foundation.Installer\`, `net8.0-windows`, `v0.4.0`)
Depends on: `Foundation.Core`, `UseWindowsForms = true`.
- **Tier 1 (`StandardInstallerHost` + `InstallerAppDescriptor`, Recommended):**
  - `InstallerAppDescriptor` (`ProductName`, `DisplayName`, `Publisher`, `Version`, `MainExecutableRelativePath = ""`, `Subtitle = null`, `EmbeddedPayloadResourceName = null`, `BrandAccentHex = null`, `AllowAutoStart = true`, plus `StudioName = "StreamItAll"`, `SuiteName = null`, `UmbrellaSubpath = null`, `AvailableProfiles`, `DefaultProfileId = "lightweight"`, `SecondaryPathPicker = null`, `AllowLegacyMigration = false`).
  - `InstallerProfileKind { Lightweight, Full }`, `InstallerProfileOption(Id, Kind, Label, Description, IsDefault = false)`, `SecondaryPathPickerOptions(FlagName, Label, DefaultPath = null, Placeholder = null, Tooltip = null)`.
  - `StandardInstallerHost.Run(InstallerAppDescriptor descriptor, string[] args, Assembly? payloadAssembly = null)`
  - CLI Flags: `--install-dir <dir>`, `--payload <path>`, `--data-dir <dir>`, `--config <path>`, `--log-dir <dir>`, `--profile <id>`, `--reengine-dir <dir>`, `--suite-dir <dir>`, `--sandbox-root <dir>`, `--migrate-legacy`, `--uninstall`, `--purge-data`, `--verify`, `--no-shortcuts`, `--autostart`, `--launch`, `--no-launch` / `--no-restart`, `--silent` / `--quiet`, `--headless`, `--test-mode`, `--visible-test`, `--dry-run`, `--version` / `-v`, `--help`.
  - **Studio/Suite Default Root:** Resolves `%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\` (HKCU / `*HOME` discovery first; opt-in `--migrate-legacy` for legacy flat `<PROGRAMS_ROOT>\<App>`).
  - **`AllowAutoStart = false`:** Hides & disables `_chkAutoStart` in `InstallerWizardForm`, vertically rebalances `_chkShortcuts` (`Y=42`) & `_chkLaunchAfter` (`Y=74`), and clamps `--autostart` / `ConfigOverride.AutoStart` with a logged warning.
  - **`--visible-test`:** Opens `InstallerWizardForm` in foreground with primary/browse actions disabled (`ExitCode = 0`) and auto-closes cleanly after `3000ms` via WinForms `Timer`.
  - **`--test-mode --install-dir` Sibling Containment:** Uses `SandboxEnvironment.ResolveSiblingSandboxRoot(dataDir)` to permit sibling `<SandboxRoot>\installed` paired with `<SandboxRoot>\data`, while host `Programs\` paths fail closed.
  - **`FileLockRetryHelper`:** Bounded 10-attempt retry (`150ms -> 250ms -> 500ms`) around `File.Move`, `File.Copy`, `File.Delete`, and `ZipArchiveEntry.ExtractToFile` (`MoveWithOverwrite`, `CopyWithOverwrite`, `DeleteIfExists`, `ExecuteWithRetry`, `IsSafeTestInstallDirectory`).
- **Tier 2 (`InstallerEngineCore` + `InstallerThemeOptions`):** `InstallerThemeOptions` adds `StudioName`, `SuiteName`, `UmbrellaSubpath`, `AvailableProfiles`, `DefaultProfileId`, `SecondaryPathPicker`, `AllowLegacyMigration`, and `AllowAutoStart = true`; `InstallerWizardForm` constructors accept trailing `int showShowcaseAutoCloseMs = 0`.

---

### 1.5 `Foundation.UI` Desktop & Web (`@foundation/ui v0.4.0`)
- **Desktop (`net8.0-windows`, WebView2):** `ShellBrandingOptions` (`SdkVersion = "0.4.0"`), `DisposableWebViewHostForm`, `StaNativeDialogBridgeBase`, `TrayIconBadgeHelper`.
- **React `@foundation/ui` (`Dev\src\Foundation.UI\Web\`):** Barrel (`index.ts`) exports `GlobalTerminalDrawer`, `TerminalToolbarAndFilters`, `StyledTooltip`, `ModalDialog`, `ConfirmActionModal`, `ResponsiveTable`, `UniversalDataTable`, `SchemaSettingsRenderer`, `terminalDrawerCore`. Direct submodule imports required for `CoexistenceWarningBanner`, `HybridPathPicker`, and `SidebarNavRail`.
- **Vanilla Assets (`Dev\src\Foundation.UI\WebAssets\`):** `foundation-tokens.css`, `foundation-shell.ts`, `foundation-terminal.js` (`mountFoundationTerminalDrawer`, `window.FoundationTerminalDrawer`), `foundation-terminal.css`.

---

## 2. Python `foundation_core` (`v0.4.0`), `Dev\tools\*.py` & JSON Contracts
- **Python `foundation_core` (`Dev\src\foundation_core\`):** `cli.py` (`UiMode`, `SandboxPaths`, `resolve_sandbox_paths`), `settings.py` (`GranularSettingsStore`), `logging.py` (`HeartbeatTracker`, `CappedLogFile`), `crash_dumper.py` (`CrashDumper`), `locks.py` (`PidSafeLock`), `db.py` (`SqliteWalHelper`).
- **Tools (`Dev\tools\`):**
  - `sync_foundation_sdk.py`: `--version` (`0.4.0`), `--ui-version` (`0.4.0`), `--configuration` (`Release`), `--publish`, `--sync-consumer <Root>`, `--source-version` (`Latest`), `--web-dir <Dir>`.
  - `build_release_universal.py`: `--project-root`, `--version`, `--csproj`, `--installer-csproj`, `--bundle-exe-name`.
  - `verify_sandbox_universal.py`: `--app-exe`, `--sandbox-root`, `--extra-args`.
  - `notion_task_helper.py`: Standard 16 Notion Task Backbone helpers (`resolve_notion_ids` via `FOUNDATION_NOTION_TASK_BACKBONE_ID` / `FOUNDATION_NOTION_PROJECTS_HUB_ID` or `<dataDir>\notion.config.json`).
- **On-Disk Contracts:** `sdk-manifest.json` (`version`, `uiVersion`, `publishedAtUtc`, `fileCount`, `files[]`); Tier 1 `install-manifest.json` (`product_name`, `version`, `installed_relative_files[]`, `created_shortcuts[]`); Tier 2 `install-manifest.json` (`FoundationInstallManifest`); `--config` (`install_dir`, `create_shortcuts`, `autostart`); `PidSafeLockEnvelope`; `HkLockEnvelope`; `.hash` v1; `CrashBundlePayload`; `suite.manifest.json`.
