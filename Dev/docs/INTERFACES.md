# Foundation — Public SDK Interfaces, File Contracts & Schema Reference (`INTERFACES.md`)

> **Active Baseline Version:** `v0.3.0` (Introducing `Foundation.UI v0.2.0` / `@foundation/ui v0.2.0` ShadowLibrary Strict Standard Docked Terminal Drawer, Dual-Target Vanilla ES Module `foundation-terminal.js` + `foundation-terminal.css`, and `sync_foundation_sdk.py` Auto-Sync Pipeline) | **Full SemVer (`MAJOR.MINOR.PATCH`)** | **Owner:** `@devv` & `@tessa` ("The Manual Maker") | **Verified By:** `@dean` ([Standard 16 §3.5](file:///<PROJECTS_ROOT>/_DevBible/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md))

This reference manual documents every public C#, TypeScript, CSS, and Python API surface exported by **`Foundation`** (`<PROJECTS_ROOT>\Foundation\`), along with the standardized HTTP `/api/v1/*` contracts, file formats (`suite.manifest.json`, `install-manifest.json`, `.hklock`, `crash_*.json`), and [Standard 18](file:///<PROJECTS_ROOT>/_DevBible/18_DATA_DICTIONARY_AND_SCHEMA_LOOKUP.md) data dictionary schemas that `Foundation` enforces across the sister-app ecosystem.

---

## 1. Authentication & Transport Security

1. **Windows DPAPI Local Secret Encryption ([`DpapiSecretStore.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/DpapiSecretStore.cs#L19-L161)):**
   - Encrypts sensitive credentials, Discord webhook URLs, and API tokens at rest in `<DataDirectory>\secrets.dpapi` using `ProtectedData.Protect(plainBytes, EntropyBytes, DataProtectionScope.CurrentUser)`.
   - Static entropy salt: `UTF8("Foundation.Core.DpapiSecretStore.v1")` ([line 21](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/DpapiSecretStore.cs#L21)).
2. **6-Pillar Outbound HTTP Transport Security ([`SafeNetworkTransfer.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SafeNetworkTransfer.cs#L50-L255)):**
   - Uses `.NET 8` `SocketsHttpHandler` with `ConnectTimeout = 10s` (default), per-read `CancellationTokenSource` stall timeout (`30s` default), automatic gzip/brotli/deflate decompression (`DecompressionMethods.All`), HTTP `Range` partial content resume, and mandatory post-download `SHA-256` / `SHA-1` verification before file commit.
3. **Loopback & Sandbox Containment ([`SandboxEnvironment.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L133-L161)):**
   - Enforces fail-closed path containment (`AssertPathContained`) and provides `ManagedByMother` (`--managed-by-mother`) detection so child sister workers bind strictly to `127.0.0.1` behind the Shared Suite Daemon (`:5100`).

---

## 2. REST API Reference `/api/v1/*` & Public C#/TS/Python SDK Interfaces

Because `Foundation` is an in-process SDK rather than a standalone HTTP daemon, it exports strongly-typed C#, TypeScript, CSS, and Python interfaces that power the `/api/v1/*` endpoints across all consuming sister apps.

### 2.1 `Foundation.Core.dll` (`namespace Foundation.Core` & `Foundation.Core.Contracts`)

| Public Type / Interface | Source File & Lines | Key Methods & Properties |
| :--- | :--- | :--- |
| **`ModuleType`** *(enum)* | [`IFoundationModule.cs#L11-L15`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/IFoundationModule.cs#L11-L15) | `FolderSlice` (`0`), `DllModule` (`1`). |
| **`SchemaSettingDescriptor`** *(record)* | [`IFoundationModule.cs#L20-L31`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/IFoundationModule.cs#L20-L31) | `(string Key, string Label, string InputType, object? DefaultValue, string TooltipTitle, string TooltipBody, string? Hotkey = null, double? MinValue = null, double? MaxValue = null, IReadOnlyList<string>? AllowedOptions = null)` |
| **`IFoundationModule`** *(interface)* | [`IFoundationModule.cs#L37-L57`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/IFoundationModule.cs#L37-L57) | `string ModuleId`, `string DisplayName`, `ModuleType ModuleType`, `string Category`, `string IconName`, `IReadOnlyList<SchemaSettingDescriptor> SettingsSchema`, `Task<int> ExecuteCliAsync(IReadOnlyList<string> args, CancellationToken ct = default)` |
| **`UiPresentationMode`** *(enum)* | [`SandboxEnvironment.cs#L15-L21`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L15-L21) | `DesktopWindow`, `LocalBrowser`, `SuiteTab`, `HeadlessOrTrayOnly`. |
| **`SandboxEnvironment`** *(class)* | [`SandboxEnvironment.cs#L26-L243`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L26-L243) | Properties: `AppId`, `BaseDirectory`, `DataDirectory`, `ConfigFilePath`, `LogDirectory`, `PortOverride`, `TestMode`, `DryRun`, `Headless`, `TrayOnly`, `ManagedByMother`, `SuiteTab`, `PresentationMode`, `IsContainedSandbox`. Methods: `AssertPathContained(candidatePath, allowedRoot)`, `AssertSandboxPathContained(candidatePath)`, `NormalizeLongPath(path)`. |
| **`CrashBundlePayload`** *(record)* | [`CrashBundleService.cs#L22-L65`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/CrashBundleService.cs#L22-L65) | `SchemaVersion` (`1`), `AppId`, `Version`, `CrashedAtUtc`, `Pid`, `OsDescription`, `FrameworkDescription`, `WorkingSetBytes`, `ExceptionType`, `ExceptionMessage`, `StackTrace`, `InnerException`, `RecentLogs`, `ActiveJobs`. |
| **`CrashBundleService`** *(static class)* | [`CrashBundleService.cs#L70-L197`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/CrashBundleService.cs#L70-L197) | `RegisterGlobalHandlers(logDir, appId, version, recentLogsProvider, activeJobsProvider)`, `WriteCrashBundle(...)`, `TryWriteCrashBundle(...)`. |
| **`SqliteWalPool`** *(class)* | [`SqliteWalPool.cs#L18-L115`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SqliteWalPool.cs#L18-L115) | Constructor: `SqliteWalPool(databasePath, busyTimeoutMs = 15000, pooling = true)` (`Cache = SqliteCacheMode.Default`). Methods: `OpenConnection()`, `OpenConnectionAsync(ct)`, `ApplyStandardPragmas(conn, busyTimeoutMs)`, `VacuumIntoAsync(destinationDbPath, ct)`. |
| **`PidSafeLockEnvelope`** *(record)* | [`PidSafeLockfile.cs#L18-L46`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PidSafeLockfile.cs#L18-L46) | `SchemaVersion` (`1`), `AppId`, `Pid`, `ProcessStartTimeUtc`, `HostName`, `Operation`, `AcquiredAtUtc`, `HeartbeatAtUtc`, `ExpiresAtUtc`. |
| **`PidSafeLockfile`** *(class, `IDisposable`)* | [`PidSafeLockfile.cs#L52-L266`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PidSafeLockfile.cs#L52-L266) | `Acquire(lockFilePath, appId, operation, leaseDuration)`, `TryAcquire(lockFilePath, appId, operation, out lockHandle, out activeHolder, leaseDuration)`, `RefreshHeartbeat(leaseDuration)`, `IsLockedByLiveProcess(lockFilePath, out envelope)`, `Dispose()`. |
| **`ToolResolutionResult`** & **`ToolResolver`** | [`ToolResolver.cs#L12-L162`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/ToolResolver.cs#L12-L162) | `ResolveTool(toolId, candidateExecutableNames, explicitOverridePath, baseDirectory, extraSearchDirectories)` returning `ToolResolutionResult(ToolId, IsAvailable, ResolvedPath, Source, DiagnosticMessage)`. |
| **`HeartbeatSnapshot`** & **`HeartbeatLogger`** | [`HeartbeatLogger.cs#L12-L124`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HeartbeatLogger.cs#L12-L124) | `MaxHeartbeatIntervalSeconds = 15`. Methods: `ReportProgress(completedUnits, totalUnits, currentItem, forceEmit)`, `Complete(totalUnits, summaryMessage)`. |
| **`DualJobCancellationCoordinator`** | [`JobObjectGuard.cs#L18-L114`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JobObjectGuard.cs#L18-L114) | `ActiveJobId`, `IsAllJobsStopRequested`, `BeginJobScope(jobId, hostShutdownToken)`, `StopCurrentJob()`, `StopAllJobs()`, `EndJobScope(jobId)`. |
| **`JobObjectGuard`** *(class, `IDisposable`)* | [`JobObjectGuard.cs#L120-L228`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JobObjectGuard.cs#L120-L228) | `IsSupported`, `TryAttachProcess(Process process)`, `Dispose()`. |
| **`SafeTransferOptions`**, **`SafeTransferResult`** & **`SafeNetworkTransfer`** | [`SafeNetworkTransfer.cs#L18-L255`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SafeNetworkTransfer.cs#L18-L255) | `AssertSufficientDiskSpace(destinationFilePath, expectedPayloadBytes, reserveBytes)`, `DownloadFileAsync(sourceUri, destinationFilePath, options, customHandler, ct)`. |
| **`DiscoveredModuleRecord`** & **`HybridModuleLoader`** | [`HybridModuleLoader.cs#L15-L166`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HybridModuleLoader.cs#L15-L166) | `Modules`, `RegisterModule(module, assemblyName)`, `DiscoverFromAssemblies(assemblies, factory)`, `DiscoverExternalDllModules(modulesDirectory, filePattern, factory)`, `TryGetModule(moduleId)`, `GetAggregatedSettingsSchema()`. |
| **`SettingsLoadOutcome<T>`** & **`GranularSettingsManager`** | [`GranularSettingsManager.cs#L16-L257`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/GranularSettingsManager.cs#L16-L257) | `DefaultMaxHistorySnapshots = 20`. Methods: `ResolveSettingsPath(fileName)`, `LoadOrSelfHeal<T>(fileName, defaultFactory, customOptions)`, `SaveAtomically<T>(fileNameOrPath, settingsObject, customOptions, recordSnapshot)`, `GetHistorySnapshots(fileName)`. |
| **`DpapiSecretStore`** *(class)* | [`DpapiSecretStore.cs#L19-L161`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/DpapiSecretStore.cs#L19-L161) | `SecretsFilePath`, `SetSecret(key, secretValue)`, `GetSecret(key)`, `RemoveSecret(key)`, `GetSecretKeys()`. |

### 2.2 `Foundation.Installer.dll` (`v0.2.1` — `namespace Foundation.Installer`)

| Public Type | Source File & Lines | Key Methods & Properties |
| :--- | :--- | :--- |
| **`InstallerAppDescriptor`** *(record)* | [`InstallerEngine.Core.cs#L16-L56`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngine.Core.cs#L16-L56) | `(string ProductName, string DisplayName, string Publisher, string Version, string MainExecutableRelativePath = "", string? Subtitle = null, string? EmbeddedPayloadResourceName = null, string? BrandAccentHex = null)` |
| **`StandardInstallerHost`** *(static class)* | [`StandardInstallerHost.cs#L16-L286`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/StandardInstallerHost.cs#L16-L286) | `Run(InstallerAppDescriptor descriptor, string[] args, Assembly? payloadAssembly = null)` — turnkey CLI + GUI setup/uninstall host. |
| **`InstallerSandboxContext`** & **`InstallerEngine`** | [`InstallerEngine.Core.cs#L106-L316`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngine.Core.cs#L106-L316) | `InstallAsync(...)`, `UninstallAsync(...)`, `VerifyPayloadAsync(...)`, `CreatePreUpgradeBackupAsync(...)`, `ResolveDefaultInstallDirectory()`, `ValidateAndNormalizeInstallDirectory(...)`. |
| **`InstallerCustomToggle`** *(record)* | [`InstallerThemeOptions.cs#L14-L19`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerThemeOptions.cs#L14-L19) | `(string Id, string Label, string TooltipSummary, bool DefaultChecked = true)` |
| **`InstallerThemeOptions`** *(record)* | [`InstallerThemeOptions.cs#L25-L68`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerThemeOptions.cs#L25-L68) | `AppId`, `DisplayName`, `Version`, `Subtitle`, `BrandAccentHex` (`"#0EA5E9"`), `SurfaceBackdropHex` (`"#0E0E10"`), `SurfaceCardHex` (`"#18181B"`), `WindowIconResource`, `HeaderLogoResource`, `DefaultInstallDir`, `ExecutableFileName`, `UninstallerFileName`, `BootstrapSharedSuiteDaemon` (`true`), `CustomToggles`. |
| **`FoundationInstallManifest`** & **`FoundationInstallResult`** | [`InstallerEngineCore.cs#L21-L40`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngineCore.cs#L21-L40) | `FoundationInstallResult(bool Success, string InstallDirectory, string PayloadSha256, int FileCount, string Message, bool WasUpgrade)`. |
| **`InstallerEngineCore`** *(class)* | [`InstallerEngineCore.cs#L46-L276`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngineCore.cs#L46-L276) | `ResolveTargetInstallDir(explicitInstallDir)`, `HasExistingInstallation(targetDir)`, `IsTargetProcessRunning(targetDir)`, `InstallOrUpgradeFromZipAsync(payloadZipStream, targetInstallDir, onProgress, ct)`, `VerifyInstallation(targetInstallDir, out diagnosticMessage)`, `Uninstall(targetInstallDir, purgeUserData)`. |
| **`WindowsShortcutWriter`** *(static class)* | [`WindowsShortcutWriter.cs#L18-L216`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/WindowsShortcutWriter.cs#L18-L216) | `WriteShortcut(outputLnkPath, targetExecutablePath, arguments, workingDirectory, description, iconFilePath, iconIndex)`, `BuildShortcutBytes(...)`, `IsValidShellLinkHeader(data)`, `RegisterCurrentUserUninstallEntry(...)`, `RemoveCurrentUserUninstallEntry(...)`. |
| **`InstallerWizardControls`** *(static class)* | [`InstallerWizardControls.cs#L16-L105`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerWizardControls.cs#L16-L105) | `ParseHexColor(hex, fallback)`, `CreateDarkCheckBox(...)`, `CreateFlatButton(...)`, `CreateDarkLogTextBox(...)`. |
| **`InstallerWizardForm`** *(WinForms `Form`)* | [`InstallerWizardForm.cs#L16-L299`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerWizardForm.cs#L16-L299) | Constants: `DefaultClientWidth = 650`, `CollapsedClientHeight = 434`, `ExpandedClientHeight = 544`. Properties: `ExitCode`, `PrimaryButton`, `CancelButton`, `BrowseButton`, `DetailsToggleButton`, `ModeBadgeLabel`, `InstallDirTextBox`, `LogTextBox`, `SetupProgressBar`, `StatusLabel`, `IsDetailsExpanded`. Methods: `ToggleDetailsDrawer()`, `SetDetailsExpanded(expanded)`, `AppendLogLine(message)`. |

### 2.3 `Foundation.UI.dll` & Web Assets (`foundation-shell.ts`, `foundation-tokens.css`)

| Public Type / Function / Selector | Source File & Lines | Signature / Contract |
| :--- | :--- | :--- |
| **`TableColumnSchema`** *(C# record)* & **`TableColumnSchemaDto`** *(TS)* | [`ShellBrandingOptions.cs#L14-L20`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/ShellBrandingOptions.cs#L14-L20), [`foundation-shell.ts#L29-L35`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-shell.ts#L29-L35) | `(string JsonKey, string TitleCaseHeader, bool IsNumericTabular = false, bool IsSortable = true, string? StatusBadgeMap = null)` |
| **`ShellBrandingOptions`** *(C# record)* | [`ShellBrandingOptions.cs#L26-L51`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/ShellBrandingOptions.cs#L26-L51) | `AppId`, `DisplayName`, `SdkVersion` (`"0.3.0"`), `BrandAccentHex` (`"#0EA5E9"`), `BrandIconUri`, `SidebarInitiallyExpanded` (`true`), `DefaultTerminalPreset` (`"M"`), `ShowDriveTelemetryStrip` (`true`). |
| **`TerminalPreset`** & **`TerminalLayoutState`** *(TS)* | [`foundation-shell.ts#L6-L14`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-shell.ts#L6-L14) | `type TerminalPreset = "S" \| "M" \| "L";` `interface TerminalLayoutState { isOpen: boolean; heightPx: number; preset: TerminalPreset; autoScroll: boolean; levelFilter: "ALL" \| "INFO" \| "WARN" \| "ERROR"; }` |
| **`clampTerminalHeight`** & **`computeTerminalPresetHeight`** *(TS)* | [`foundation-shell.ts#L42-L50`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-shell.ts#L42-L50) | Clamps terminal height between `MIN_TERMINAL_HEIGHT_PX` (`120`) and `bodyColumnHeightPx - MIN_MAIN_VIEWPORT_RESERVE_PX` (`160`). Presets: `"S"` = `25%`, `"M"` = `40%`, `"L"` = `60%`. |
| **`isTerminalToggleShortcut`** & **`isSidebarToggleShortcut`** *(TS)* | [`foundation-shell.ts#L52-L58`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-shell.ts#L52-L58) | `Ctrl+\`` (or `Meta+\``) toggles docked terminal; `Ctrl+B` (or `Meta+B`) toggles `220px`/`56px` sidebar rail. |
| **CSS Design Tokens (`:root`) & Layout Classes** | [`foundation-tokens.css#L6-L117`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-tokens.css#L6-L117) | Tokens: `--surface-0` (`#0B0F17`) .. `--surface-3` (`#334155`), `--brand-accent` (`#6366F1`), `--sidebar-width-expanded` (`220px`), `--sidebar-width-collapsed` (`56px`), `--terminal-default-height` (`280px`), `--terminal-min-height` (`120px`). Classes: `.app-shell`, `.sidebar-nav-rail`, `.sidebar-nav-rail.is-collapsed`, `.app-body-column`, `.app-main-pane`, `.docked-terminal-drawer`, `.docked-terminal-sash`, `.foundation-tooltip`, `.tabular-nums`. |

### 2.4 Python Companion SDK (`foundation_core` — [`__init__.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/__init__.py#L1-L47))

| Symbol | Signature | Description |
| :--- | :--- | :--- |
| **`SDK_VERSION`** | `str = "0.1.0"` | Active Full SemVer string of the Python `foundation_core` package. |
| **`assert_path_contained`** | `(candidate_path: str \| Path, allowed_root: str \| Path) -> Path` | Resolves both paths and raises `ValueError("Fail-Closed Sandbox Violation: ...")` if `candidate_path` escapes `allowed_root`. |
| **`open_wal_sqlite`** | `(db_path: str \| Path, busy_timeout_ms: int = 5000) -> sqlite3.Connection` | Creates parent folders, opens SQLite connection, and executes `PRAGMA journal_mode = WAL;`, `PRAGMA busy_timeout`, `PRAGMA synchronous = NORMAL;`, `PRAGMA foreign_keys = ON;`. |
| **`emit_heartbeat`** | `(operation_name: str, completed: int, total: int, current_item: str = "working") -> str` | Formats `[HEARTBEAT] <op>: <completed>/<total> (<pct:.1f}%) \| <item>` and writes `\r\x1b[K` in-place when `sys.stdout.isatty()` is true. |
| **`AtomicSettingsManager`** | [`settings.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/settings.py) | Atomic `.tmp` $\rightarrow$ `.bak` JSON settings loader/writer with 20-snapshot `data/config_history/` rotation and automatic `.bak` self-healing. |
| **`RingBufferLogHandler`** & **`CrashBundleService`** | [`logging.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/logging.py), [`crash_dumper.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/crash_dumper.py) | 50-line ring buffer, `10 MB x 5` rotating log setup, PII path sanitizer, and `sys.excepthook` JSON crash bundle writer. |
| **`PidSafeLockfile`** & **`has_active_foreign_hklock`** | [`locks.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/locks.py) | Standard 19 §4 `(pid, processStartTimeUtc)` JSON lockfile manager and foreign `.hklock` directory scanner. |
| **`SqliteMigrationRunner`** | [`db.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/db.py) | Ordered idempotent SQLite migration runner (`_foundation_migrations`) on top of `open_wal_sqlite` (`PRAGMA cache_size = -65536`). |
| **`create_standard_arg_parser`** & **`HeartbeatEmitter`** | [`cli.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/cli.py) | Standard `--data-dir`, `--config`, `--log-dir`, `--port`, `--test-mode`, `--dry-run`, `--headless` parser and `<= 15s` heartbeat emitter. |

### 2.5 `Foundation.Retro.dll` (`v0.1.0` — `namespace Foundation.Retro`)

| Public Type / Method | Source File | Contract Summary |
| :--- | :--- | :--- |
| **`ConsoleSystemCatalog`** | [`ConsoleSystemCatalog.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/ConsoleSystemCatalog.cs) | `BuildDefaultPlatforms()` (64 platforms + 50+ BIOS MD5/SHA-1/Size records), `BuildDefaultHeaderSkipRules()`, `BuildDefaultProvenanceTags()`, `ResolvePlatformId(tokenOrExtension, platforms)`. |
| **`DatTitleNormalizer`** | [`DatTitleNormalizer.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/DatTitleNormalizer.cs) | `ParseFilename(rawNameOrPath, provenanceTags)` returning `ParsedRomFilenameMetadata`, `NormalizeTitleKey(title)`, `InferSaveKind(extension)`. |
| **`NoIntroRedumpDatParser`** | [`NoIntroRedumpDatParser.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/NoIntroRedumpDatParser.cs) | `ParseDatFile(datFilePath, explicitPlatformId, explicitSourceKind, platformResolver, provenanceTags)` returning `(ParsedDatCatalogHeader Header, List<ParsedDatRomRecord> Records)`. |
| **`HeaderlessRomStripper`** & **`MagicBytePlatformDetector`** | [`HeaderlessRomStripper.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/HeaderlessRomStripper.cs) | `DetectHeaderSkipLength(leadingBytes, totalFileSize, fileExtension, rules, out matchedRuleId)`, `DetectPlatformByMagicBytes(headerBytes, fileSize, fileExtension)`. |
| **`SoftPatchEngine`** | [`SoftPatchEngine.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/SoftPatchEngine.cs) | `ApplyIpsPatchInMemory(baseRom, ipsPatch)`, `ApplyBpsPatchInMemory(sourceRom, bpsPatch)`, `CreateSimpleBpsPatch(sourceRom, targetRom)`, `ApplyUpsPatchInMemory(sourceRom, upsPatch)`, `CreateSimpleUpsPatch(sourceRom, targetRom)`. |

### 2.6 `Foundation.Sidecars.dll` (`v0.1.0` — `namespace Foundation.Sidecars`)

| Public Type / Method | Source File | Contract Summary |
| :--- | :--- | :--- |
| **`SinglePassStreamHasher`** & **`Crc32Accumulator`** | [`SinglePassStreamHasher.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Sidecars/SinglePassStreamHasher.cs) | `HashFileSinglePassAsync(filePath, headerSkipBytes, matchedHeaderRule, computeSha256, onBytesRead, ct)` and `HashStreamSinglePassAsync(...)` returning `ComputedStreamHashes`. |
| **`SevenZipAndZipInspector`** | [`SevenZipAndZipInspector.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Sidecars/SevenZipAndZipInspector.cs) | `ListArchiveEntriesAsync`, `ReadArchiveTextEntryAsync`, `ExtractSingleArchiveEntryToFileAsync`, `CreateDeterministicZipAsync`, `ResolveSevenZipExe`. |
| **`HashSidecarService`** | [`HashSidecarService.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Sidecars/HashSidecarService.cs) | `NormalizeSidecarRelativePath`, `HasMissingWrapperInSidecar`, `ComputeCompositeFolderSha1`, `ParseHashSidecarLines`, `FormatSingleOrArchiveSidecar`, `FormatCompositeFolderSidecar`, `ValidateSidecarAgainstArchiveCrcIndex`. |
| **`HkLockSentinel`** & **`SisterAppCoexistenceClient`** | [`HkLockSentinel.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Sidecars/HkLockSentinel.cs) | `IsTemporaryOrPartialFileName`, `HasActiveForeignLockInDirectory`, `WriteDirectoryHkLock`, `RemoveDirectoryHkLock`, `ReadOrUpdateCoexistenceManifest`, `ProbeSisterPeerAsync`. |

### 2.7 `Foundation.Server.dll` (`v0.1.0`), `Foundation.Launcher.dll` (`v0.1.1`), `Foundation.UI.Desktop` (`v0.2.0`), `@foundation/ui` (`v0.2.0`) & Vanilla WebAssets

| Public Type / Component | Source File | Contract Summary |
| :--- | :--- | :--- |
| **`TieredAuthEngine`** & **`FoundationTieredAuthMiddleware`** | [`TieredAuthEngine.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Server/TieredAuthEngine.cs), [`TieredAuthMiddleware.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Server/TieredAuthMiddleware.cs) | `EvaluateRequest`, `HashPasswordPbkdf2`, `VerifyPasswordPbkdf2`, `IssueSessionCookieToken`, `ValidateSessionCookieToken`, `IsLoopbackConnection`, `IsTailscaleAddress`. |
| **`SisterAppStandardEndpoints`** | [`SisterAppStandardEndpoints.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Server/SisterAppStandardEndpoints.cs) | `LoadOrBuildSuiteManifestJson(baseDir, fallbackFactory)`, `MapSisterAppSuiteManifestEndpoints(app, apiV1Group, fallbackFactory)`. |
| **`RootLauncherHost`** | [`RootLauncherHost.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Launcher/RootLauncherHost.cs) | `Run(appExecutableRelativePath, displayName, args)` and `ResolveTargetExecutable(rootDir, appExecutableRelativePath, currentProcessPath)` with `AttachConsole(-1)` and CLI exit code forwarding. |
| **`StaNativeDialogBridgeBase`**, **`DisposableWebViewHostForm`**, **`TrayIconBadgeHelper`** | [`Desktop/`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Desktop/) | WinForms STA native folder/file dialog bridge, `0 MB` idle-RAM disposable WebView2 host window, and GDI+ tray icon badge helper. |
| **`@foundation/ui` (`v0.2.0`)** | [`Web/index.ts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Web/index.ts) | Exports `StyledTooltip`, `ModalDialog`, `ConfirmActionModal`, `ResponsiveTable`, `UniversalDataTable`, `SchemaSettingsRenderer`, `GlobalTerminalDrawer`, `GlobalTerminalDrawerProps`, `TerminalJobStatusDto`, `TerminalToolbarAndFilters`, `TerminalToolbarAndFiltersProps`, and all `terminalDrawerCore.ts` helpers (`shouldShowLogLine`, `isReplaceableProgressLine`, `coalesceTerminalLogBatch`, `clampTerminalDrawerHeight`, `computeTerminalDrawerPresetHeight`, `filterTerminalLines`). |
| **`foundation-terminal.js` & `foundation-terminal.css`** | [`WebAssets/foundation-terminal.js`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.js), [`WebAssets/foundation-terminal.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.css) | Self-contained Vanilla ES Module exporting `mountFoundationTerminalDrawer(options)` (`{ appendLogs, setLogs, setJobStatus, setConnected, setExpanded, destroy }`) + `window.FoundationTerminalDrawer` and dual `.fn-terminal-*`/`.sl-terminal-*` styles. |
| **`sync_foundation_sdk.py`** | [`tools/sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py) | CLI publisher & consumer sync utility (`--publish`, `--version 0.3.0`, `--ui-version 0.2.0`, `--sync-consumer <ProjectRoot>`, `--web-dir <Path>`) writing `sdk-manifest.json` to `SharedLibs\Foundation\v0.3.0\` and `SharedLibs\Foundation\Latest\`. |

---

## 3. SignalR / SSE / Event Bus Contracts

`Foundation` provides the client/server data structures and callback hooks that feed real-time SignalR and Server-Sent Events (SSE) streams in consuming sister apps:
1. **Progress & Heartbeat Event Stream ([`HeartbeatSnapshot`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HeartbeatLogger.cs#L12-L20)):**
   - Emits `(OperationName, CompletedUnits, TotalUnits, PercentComplete, ElapsedSeconds, CurrentItem, FormattedLine)` to the host's `Action<string>? logSink`, which consuming apps broadcast over SignalR/SSE to the docked `Ctrl+\`` terminal drawer.
2. **Byte-Level Transfer Progress Callback ([`SafeTransferOptions.OnBytesProgress`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SafeNetworkTransfer.cs#L29)):**
   - `Action<long, long?>? OnBytesProgress` invoked on every 64 KB chunk in [`StreamToPartialFileAsync`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SafeNetworkTransfer.cs#L191) with `(totalWrittenBytes, totalExpectedBytes)`.
3. **Installer Progress Callback ([`InstallerEngineCore.InstallOrUpgradeFromZipAsync`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngineCore.cs#L117)):**
   - `Action<int, string>? onProgress` reporting `(percentComplete, statusLine)` to [`InstallerWizardForm`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerWizardForm.cs#L233-L238).

---

## 4. MCP Server / Agent Contracts

While `Foundation` does not host an MCP server directly, its [`IFoundationModule`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/IFoundationModule.cs#L37-L57) and [`HybridModuleLoader`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HybridModuleLoader.cs#L29-L166) contracts allow AI agents and sister-app MCP servers (`Greenroom`, `ShadowLibrary`) to inspect and invoke registered modules uniformly:
- **Module Discovery:** `HybridModuleLoader.Modules` returns deterministic `IReadOnlyList<DiscoveredModuleRecord>` ordered by `(Category, DisplayName)`.
- **Headless Agent/CLI Invocation:** `IFoundationModule.ExecuteCliAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)` executes any module deterministically and returns a standard exit code.

---

## 5. CLI Subcommands & Exit Codes

Applications and installers built on `Foundation.Core` and `Foundation.Installer` enforce the [Standard 17 §3.3 (`STD-03`)](file:///<PROJECTS_ROOT>/_DevBible/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md) deterministic exit-code taxonomy:

| Exit Code | Meaning | Trigger in `Foundation` / Consuming Host |
| :---: | :--- | :--- |
| **`0`** | **Success** | Clean execution, passing `--test-mode` verification, or completed [`InstallerWizardForm.ExitCode = 0`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerWizardForm.cs#L251). |
| **`1`** | **Runtime / Unhandled Error** | Uncaught exception (after [`CrashBundleService`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/CrashBundleService.cs#L78-L110) writes `logs\crashes\crash_*.json`) or cancelled/failed wizard (`InstallerWizardForm.ExitCode = 1` default). |
| **`2`** | **Validation / Audit Failure** | Invalid CLI arguments, failed `InstallerEngineCore.VerifyInstallation`, or failed `--doctor` check. |
| **`3`** | **Fail-Closed Sandbox Violation** | [`SandboxEnvironment.AssertPathContained`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L133-L156) or Python [`assert_path_contained`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/__init__.py#L17-L25) blocked an attempt to access a path outside the allowed root. |
| **`130`** | **User Abort (`SIGINT` / `Ctrl+C`)** | `OperationCanceledException` triggered via `DualJobCancellationCoordinator` or console `Ctrl+C`. |

---

## 6. File Contracts — `install-manifest.json`, `suite.manifest.json`, `.hklock`, `crash_*.json`

### 6.1 `app\install-manifest.json` ([`FoundationInstallManifest`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngineCore.cs#L21-L31))
Written by `InstallerEngineCore.InstallOrUpgradeFromZipAsync` and verified by `VerifyInstallation`:
```json
{
  "schemaVersion": 1,
  "appId": "shadowlibrary",
  "displayName": "ShadowLibrary",
  "version": "1.0.0-rc.14",
  "installedAtUtc": "2026-11-04T18:00:00.000Z",
  "installDirectory": "<PROGRAMS_ROOT>\\ShadowLibrary",
  "payloadSha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
  "installedRelativeFiles": [
    "ShadowLibrary.exe",
    "app/ShadowLibrary.Server.exe",
    "app/suite.manifest.json"
  ]
}
```

### 6.2 PID-Safe Lockfile Envelope (`.hklock` — [`PidSafeLockEnvelope`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PidSafeLockfile.cs#L18-L46))
Written atomically by `PidSafeLockfile.Acquire` / `TryAcquire` per [Standard 19 §4](file:///<PROJECTS_ROOT>/_DevBible/19_SETTINGS_MANIFESTS_LOCKS_AND_NETWORK_SAFETY.md):
```json
{
  "schemaVersion": 1,
  "appId": "shadowlibrary",
  "pid": 14820,
  "processStartTimeUtc": "2026-11-04T17:30:12.450Z",
  "hostName": "WORKSTATION-01",
  "operation": "bitrot-scrub",
  "acquiredAtUtc": "2026-11-04T17:45:00.000Z",
  "heartbeatAtUtc": "2026-11-04T17:45:00.000Z",
  "expiresAtUtc": "2026-11-04T18:00:00.000Z"
}
```

### 6.3 Crash Bundle Envelope (`logs\crashes\crash_<appId>_<timestamp>.json` — [`CrashBundlePayload`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/CrashBundleService.cs#L22-L65))
```json
{
  "schemaVersion": 1,
  "appId": "shadowlibrary",
  "version": "1.0.0-rc.14",
  "crashedAtUtc": "2026-11-04T17:50:00.123Z",
  "pid": 14820,
  "osDescription": "Microsoft Windows 10.0.22631",
  "frameworkDescription": ".NET 8.0.11",
  "workingSetBytes": 54525952,
  "exceptionType": "System.InvalidOperationException",
  "exceptionMessage": "Simulated fault",
  "stackTrace": "   at ...",
  "recentLogs": ["Line 1", "Line 2"]
}
```

---

## 7. SQLite / JSON / DTO Schema Reference ([Standard 18](file:///<PROJECTS_ROOT>/_DevBible/18_DATA_DICTIONARY_AND_SCHEMA_LOOKUP.md))

All DTOs in `Foundation` strictly enforce [Standard 18](file:///<PROJECTS_ROOT>/_DevBible/18_DATA_DICTIONARY_AND_SCHEMA_LOOKUP.md) canonical field names (`camelCase` in JSON/TS, `PascalCase` in C#):

| Canonical JSON Key (`camelCase`) | C# Property (`PascalCase`) | Type | Owning DTO / Record | Standard 18 Definition |
| :--- | :--- | :--- | :--- | :--- |
| `schemaVersion` | `SchemaVersion` | `int` | `CrashBundlePayload`, `PidSafeLockEnvelope`, `FoundationInstallManifest` | Integer schema version (`1` or `2`) for forward-compatible migrations. |
| `appId` | `AppId` | `string` | `SandboxEnvironment`, `CrashBundlePayload`, `PidSafeLockEnvelope`, `FoundationInstallManifest`, `InstallerThemeOptions`, `ShellBrandingOptions` | Lowercase alphanumeric application slug (e.g. `"mother"`, `"shadowlibrary"`). |
| `moduleId` | `ModuleId` | `string` | `IFoundationModule`, `DiscoveredModuleRecord` | Lowercase-kebab-case module identifier (e.g. `"sample-slice"`). |
| `displayName` | `DisplayName` | `string` | `IFoundationModule`, `DiscoveredModuleRecord`, `FoundationInstallManifest`, `InstallerThemeOptions`, `ShellBrandingOptions` | Human-facing Title Case name. |
| `moduleType` | `ModuleType` | `ModuleType` | `IFoundationModule`, `DiscoveredModuleRecord` | `"FolderSlice"` or `"DllModule"`. |
| `sdkVersion` | `SdkVersion` | `string` | `ShellBrandingOptions`, `suite.manifest.json` | Full 3-part SemVer of pinned `Foundation` SDK (`"0.1.0"`). |
| `version` | `Version` | `string` | `CrashBundlePayload`, `FoundationInstallManifest`, `InstallerThemeOptions` | Active SemVer string of the host application. |
| `pid` | `Pid` | `int` | `CrashBundlePayload`, `PidSafeLockEnvelope` | OS process identifier holding the lock or crashing. |
| `processStartTimeUtc` | `ProcessStartTimeUtc` | `string` (ISO-8601 UTC) | `PidSafeLockEnvelope` | Exact UTC process creation timestamp preventing recycled-PID lock collisions. |
| `acquiredAtUtc` / `heartbeatAtUtc` / `expiresAtUtc` | `AcquiredAtUtc` / `HeartbeatAtUtc` / `ExpiresAtUtc` | `string` (ISO-8601 UTC) | `PidSafeLockEnvelope` | Lock acquisition, heartbeat refresh, and lease expiration UTC timestamps. |
| `crashedAtUtc` / `installedAtUtc` | `CrashedAtUtc` / `InstalledAtUtc` | `string` (ISO-8601 UTC) | `CrashBundlePayload`, `FoundationInstallManifest` | UTC event timestamps. |
| `payloadSha256` | `PayloadSha256` | `string` (64-char hex) | `FoundationInstallManifest`, `FoundationInstallResult` | Lowercase SHA-256 hex digest of the installed ZIP payload. |

---

## 8. Backward Compatibility & Sister-App Integration Notes

1. **Strict Non-Breaking Additive Evolution ([Standard 17 §1.6](file:///<PROJECTS_ROOT>/_DevBible/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md)):**
   - Public classes, records, and interfaces in `Foundation.Core`, `Foundation.Installer`, `Foundation.UI`, and `foundation_core` must **never** remove properties, rename methods, or reorder existing parameters.
   - All enhancements must be added via new optional parameters with default values or new properties with default initializers.
2. **Side-by-Side SharedLibs Retention:**
   - Every release (`v0.1.0`, `v0.1.1`, `v0.2.0`, `v1.0.0`) is stored permanently in its own subfolder under `<PROJECTS_ROOT>\SharedLibs\Foundation\v<Version>\`.
   - Currently pinned consumers on `v0.1.0`: **`ShadowLibrary v1.0.0-rc.14`**, **`Housekeeping v1.0.0-rc.12`**, and **`Mother v0.1.0`**.
3. **Suite Manifest Registration Hook:**
   - When a sister app includes `app\suite.manifest.json` inside its installer ZIP payload, [`InstallerEngineCore.RegisterSuiteManifestIfPresent`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngineCore.cs#L260-L275) automatically copies that manifest to `<PROGRAMS_ROOT>\Mother\manifests\<appId>.manifest.json` so `Mother` (`:5100`) discovers the app without custom installer scripting.
