# Foundation (`v0.4.2`) — 1-Stop Interfaces & Agent API Reference

> **Purpose:** Single lookup reference for **every exported C# class/method, React/TS component (`@foundation/ui`), Vanilla JS/CSS asset, Python helper (`foundation_core`), `Dev\tools\*.py` CLI, and file/JSON schema** in `<PROJECTS_ROOT>\Foundation\`.
> **Snapshot Baseline:** Unified SDK `0.4.2` (`.NET 8`, `@foundation/ui v0.4.2`, `foundation_core v0.4.2`).

---

## 0. Fast-Path Lookup & Import Pitfalls (Read First)

| If you need... | Use this symbol / file | Exact import / CLI / path |
|---|---|---|
| CLI path overrides, path containment, UI presentation mode | `AppEnvironment`, `UiPresentationMode` | `using Foundation.Core;` |
| 4-drive external sandbox resolution, `subst` discovery, `ExitCode = 3` containment | `SandboxEnvironmentSeam`, `SandboxResolution`, `SandboxMountSpec`, `KnownFolderPaths`, `SandboxDriveController` | `using Foundation.Core;` |
| Host or atomic JSON pseudo-registry (`HKCU`/`HKLM`/`HKCR`, 6 `REG_*` kinds) | `IPlatformRegistry`, `WindowsPlatformRegistry`, `JsonHivePlatformRegistry`, `PlatformRegistryFactory` | `using Foundation.Core;` |
| Frame pacer, SPSC audio, WAV/PNG/RGBA overlay, CLI, settings, DPAPI, SQLite WAL, PID locks, heartbeats, crash bundles, jobs, safe HTTP, tools, modules | `HighResFramePacer`, `AudioRingBuffer`, `WavCaptureWriter`, `RgbaPngWriter`, `PixelOverlayCanvas`, `CliArgumentHelper`, `GranularSettingsManager`, `DpapiSecretStore`, `SqliteWalPool`, `PidSafeLockfile`, `HeartbeatLogger`, `CrashBundleService`, `JobObjectGuard`, `SafeNetworkTransfer`, `ToolResolver`, `HybridModuleLoader` | `using Foundation.Core;` |
| 41-platform catalog, DAT parser, headerless stripper, soft-patch, BYO media, AOT/recomp contracts | `ConsoleSystemCatalog`, `DatTitleNormalizer`, `NoIntroRedumpDatParser`, `HeaderlessRomStripper`, `SoftPatchEngine`, `MediaContainerExtractor`, `IRecompHost`, `AotCacheLayoutHelper` | `using Foundation.Retro;` |
| Single-pass hash, ZIP/7z `-slt`, `.hash` v1, `.hklock` | `SinglePassStreamHasher`, `SevenZipAndZipInspector`, `HashSidecarService`, `HkLockSentinel` | `using Foundation.Sidecars;` |
| Kestrel tiered auth + `/suite/manifest` endpoints | `TieredAuthEngine`, `FoundationTieredAuthMiddleware`, `SisterAppStandardEndpoints` | `using Foundation.Server;` |
| Clean-root Win32 `AttachConsole(-1)` launcher shim | `RootLauncherHost` | `using Foundation.Launcher;` |
| WinForms WebView2 host, STA dialogs, tray helpers, branding | `DisposableWebViewHostForm`, `StaNativeDialogBridgeBase`, `TrayIconBadgeHelper`, `ShellBrandingOptions` | `using Foundation.UI;` / `.Desktop` |
| Full dark WinForms + CLI Setup/Uninstall Wizard (`v0.4.2`) | `StandardInstallerHost`, `InstallerAppDescriptor`, `InstallerRuntimeContext`, `WindowsShortcutWriter`, `FileLockRetryHelper`, `InstallerWizardForm` | `using Foundation.Installer;` |
| Low-level app-dir & suite registry helper (`v0.4.2`) | `InstallerEngineCore`, `InstallerThemeOptions` | `using Foundation.Installer;` |
| React components (`@foundation/ui v0.4.2`) & Vanilla terminal | 7 barrel exports + 3 direct submodule imports; Vanilla `FoundationTerminalDrawer` | `@foundation/ui` vs `@foundation/ui/components/<Name>` |
| Python SDK (`foundation_core`) & Sandbox E2E tools | `foundation_core` (`sandbox_seam`, `pseudo_registry`), `sandbox_ctl.py`, `run_sandbox_e2e.py`, `sync_foundation_sdk.py` | `Dev\src\foundation_core\` / `Dev\tools\` |

### Top Import & Caller Pitfalls
1. **React Barrel vs. Direct Submodule Imports (`Dev\src\Foundation.UI\Web\index.ts`):** `CoexistenceWarningBanner`, `HybridPathPicker`, and `SidebarNavRail` are NOT in the `@foundation/ui` barrel—import via `@foundation/ui/components/<Name>`.
2. **Two Installer Tiers:** Do not mix `InstallerAppDescriptor` + `StandardInstallerHost` (Tier 1) with `InstallerThemeOptions` + `InstallerEngineCore` (Tier 2).
3. **TFM Split:** `Foundation.Core`, `Foundation.Retro`, `Foundation.Sidecars`, and `Foundation.Server` target pure `net8.0` (zero WinForms/GDI+); `Foundation.Installer`, `Foundation.UI`, and `Foundation.Launcher` target `net8.0-windows`.
4. **3-Mode Sandbox Safety (`v0.4.2`):** Runtime libraries read process env ONLY (`APP_ISOLATED_TEST=1` beats `FOUNDATION_SANDBOX_ROOT`). Gate containment guards on `ShouldSuppressLiveHostMutations` (`IsStage1Isolated || IsSandboxActive`), NOT `ShouldSuppressHostMutations` (`IsStage1Isolated` only).

---

## 1. C# Shared Libraries (`v0.4.2`)

### 1.1 `Foundation.Core` (`Dev\src\Foundation.Core\`, `net8.0`, `v0.4.2`)

#### 4-Drive Sandbox Seam & Known-Folder Redirection (`v0.4.2`)
- **`SandboxMountSpec(string DriveRole, string DriveMount, string? DirectoryPath, bool IsEnabled)`**
- **`SandboxResolution(bool IsSandboxActive, string? SandboxRoot, string? SystemDriveRoot, string? StorageDriveRoot, string? RemovableDriveRoot, string? DiscDriveRoot, string? SandboxProfileDir, string? RegistryHivePath, string? RegistryBaselinePath, string? RegistryLockPath)`** (+ `SandboxResolution.Inactive`)
- **`SandboxEnvironmentSeam` (`SandboxEnvironmentSeam.cs`):** `ContainmentExitCode = 3`, `ResolveCurrent()`, `Resolve(IReadOnlyDictionary<string, string?>? envOverride)`, `ValidateResolution(SandboxResolution)`, `AssertSandboxPathIfActive(string candidatePath, SandboxResolution? sandbox = null)`, `IsPathWithinSandbox(string? candidatePath, SandboxResolution? sandbox = null)`, `IsContainmentViolation(Exception?)`, `ThrowContainmentViolation(string message, Exception? inner = null)`, `TryResolveSubstBackingDirectory(string? driveOrPath)`, `GetProcessEnvironment()`.
- **`KnownFolderPaths` (`KnownFolderPaths.cs`):** `GetFolderPath(SpecialFolder, SandboxResolution? = null)`, `GetFolderPath(SpecialFolder, SpecialFolderOption, SandboxResolution? = null)`, `ResolveLocalProgramsDirectory(SandboxResolution? = null)`, `MapHostPathToSandbox(string, SandboxResolution? = null)`. Maps `UserProfile`, `LocalApplicationData`, `ApplicationData`, `Desktop`/`DesktopDirectory`, `Personal`/`MyDocuments`, `Programs`, `StartMenu`, `Startup`, `ProgramFiles`, `ProgramFilesX86`, `CommonApplicationData`, `Windows`.
- **`SandboxDriveController` (`SandboxDriveController.cs`):** Explicit opt-in tooling controller (never called by `AppEnvironment`/`SandboxEnvironmentSeam`): `GetDefaultUserSecretsEnvPath()`, `LoadEnvStore(...)`, `ParseEnvFile(string)`, `GetSubstMappings()`, `ParseSubstOutput(string?)`, `InferSandboxRoot(...)`, `ResolveDriveMounts(...)`, `ValidateNoHostCollisions(...)`.

#### Shared Atomic JSON Pseudo-Registry (`v0.4.2`)
- **`PlatformRegistryHive` (`CurrentUser`, `LocalMachine`, `ClassesRoot`)**, **`PlatformRegistryValueKind` (`String`, `ExpandString`, `MultiString`, `DWord`, `QWord`, `Binary`)**, **`PlatformRegistryValue(Kind, Data)`** (`AsString()`, `AsInt32()`, `AsInt64()`, `AsMultiString()`, `AsBinary()`).
- **`IPlatformRegistry`:** `SubKeyExists`, `CreateSubKey`, `DeleteSubKeyTree`, `GetValue`, `SetValue`, `SetValues`, `DeleteValue`, `GetSubKeyNames`, `GetValueNames`, `GetValues`.
- **Implementations:** `NoOpPlatformRegistry.Instance`, `WindowsPlatformRegistry` (`[SupportedOSPlatform("windows")]` in-box registry), `JsonHivePlatformRegistry(hiveFilePath, sandboxRoot?, lockFilePath?, lockTimeout?)` (`EnsureInitialized()`, Mutex `Local\FoundationSandboxReg_<sha8>` + `PidSafeLockfile` + `.tmp -> Flush(true) -> .bak -> File.Move`), and `PlatformRegistryFactory.CreateDefault(SandboxResolution? sandbox = null, bool suppressMutations = false)`.

#### Runtime Environment & Core Primitives (`v0.1.0`–`v0.4.2`)
| Class (`File`) | Key Public Signatures | Contract |
|---|---|---|
| `AppEnvironment` (`AppEnvironment.cs`) | `AppEnvironment(appId, args?, baseDir?, defaultConfig?)` + 5-arg `sandboxOverride`; `Sandbox`, `AssertPathContained`, `NormalizeLongPath` | CLI `--data-dir`/`--config`/`--log-dir`/`--port`/4 UI modes + sandbox seam |
| `HighResFramePacer`, `AudioRingBuffer`, `WavCaptureWriter`, `RgbaPngWriter`, `PixelOverlayCanvas` | `WaitForNextFrame()`, `Write`/`Read`, `WriteInterleavedSamples`, `WritePng`, `DrawText`/`SavePng` | Timing (`timeBeginPeriod(1)`), lock-free SPSC audio, WAV/PNG/HUD |
| `CliArgumentHelper`, `BoundedTailStream`, `CappedSharedLogSink` | `HasFlag`, `GetOption`, `GetPositional`, `FormatBytes` | CLI parsing + 64 KB ring / 4 MB log cap |
| `GranularSettingsManager`, `DpapiSecretStore` | `LoadOrSelfHeal<T>`, `SaveAtomically<T>`, `SetSecret`/`GetSecret` | `.tmp -> .bak`, 20 snapshots; Windows DPAPI `CurrentUser` |
| `SqliteWalPool`, `PidSafeLockfile` | `OpenConnection()`, `VacuumIntoAsync()`, `Acquire`/`TryAcquire` | `WAL`, `busy_timeout=15000`; `pid` + `processStartTimeUtc` locks |
| `HeartbeatLogger`, `CrashBundleService`, `JobObjectGuard`, `SafeNetworkTransfer`, `ToolResolver`, `HybridModuleLoader` | `ReportProgress`, `RegisterGlobalHandlers`, `BeginJobScope`/`StopAllJobs`, `DownloadFileAsync`, `ResolveTool`, `DiscoverExternalDllModules` | `<= 15s` `\r\x1b[K` heartbeats, crash JSON, `KILL_ON_JOB_CLOSE`, `Range` resume, 4-tier tools, plugins |

### 1.2 `Foundation.Retro`, `Foundation.Sidecars`, `Foundation.Server` & `Foundation.Launcher`
- **`Foundation.Retro` (`net8.0`):** `ConsoleSystemCatalog`, `DatTitleNormalizer`, `NoIntroRedumpDatParser`, `HeaderlessRomStripper`, `MagicBytePlatformDetector`, `SoftPatchEngine`, `MediaContainerExtractor`, `RecompContracts`, `RecompTierAndBackendContracts`.
- **`Foundation.Sidecars` (`net8.0`):** `SinglePassStreamHasher`, `SevenZipAndZipInspector`, `HashSidecarService`, `HkLockSentinel`.
- **`Foundation.Server` (`net8.0`):** `TieredAuthEngine`, `FoundationTieredAuthMiddleware`, `SisterAppStandardEndpoints`.
- **`Foundation.Launcher` (`net8.0-windows`):** `RootLauncherHost.ResolveTargetExecutable`, `RootLauncherHost.Run` (`AttachConsole(-1)` + self-spawn guard).

### 1.3 `Foundation.Installer` (`net8.0-windows`, `v0.4.2`) & `Foundation.UI` (`v0.4.2`)
- **3-Mode `InstallerRuntimeContext`:** `Sandbox`, `IsSandboxActive` (`Sandbox.IsSandboxActive && !IsIsolatedTestProcess`), `IsStage1Isolated`, `ShouldSuppressHostMutations => IsStage1Isolated`, `ShouldSuppressLiveHostMutations => IsStage1Isolated || IsSandboxActive`, `Registry` (`IPlatformRegistry`), ABI-intact 5-arg ctor + 7-arg override ctor.
- **Tier 1 (`StandardInstallerHost` + `InstallerAppDescriptor`) & Tier 2 (`InstallerEngineCore`):**
  - `InstallerAppDescriptor.ResolveCanonicalDefaultInstallDir(SandboxResolution? sandbox = null)` routes via `KnownFolderPaths`; `StandardInstallerHost` maps `SandboxEnvironmentSeam.IsContainmentViolation(ex)` to `ExitCode = 3`.
  - `FileLockRetryHelper.IsSafeIsolatedInstallDirectory` & `IsCanonicalProductionOrSystemPath` add trailing `SandboxResolution? sandbox = null`, permitting verified `<SandboxDDrive>\Programs\...` while blocking raw host OS/user/host-`Programs` targets (`ExitCode = 3`).
  - `WindowsShortcutWriter.SanitizeShortcutFileName(string? displayName)` normalizes `:` -> ` -` and replaces `Path.GetInvalidFileNameChars()` with `_`; `RegisterCurrentUserUninstallEntry`/`RemoveCurrentUserUninstallEntry` accept trailing `IPlatformRegistry?`.
  - `InstallerWizardForm`: `_lblTitle` & `_lblSub` clamp `AutoEllipsis = true` cleanly before `_lblModeBadge.Left - 12` with DPI-safe `Height >= 28` (`650x460`/`650x570` default `ClientSize`); `ShouldSuppressModalDialogs` honors `FOUNDATION_SUPPRESS_MODALS=1`.
- **`Foundation.UI`:** `ShellBrandingOptions` (`SdkVersion = "0.4.2"`), `DisposableWebViewHostForm`, `StaNativeDialogBridgeBase`, `TrayIconBadgeHelper`, `@foundation/ui` (`Web\index.ts` + 3 submodule components + `WebAssets\*`).

---

## 2. Python `foundation_core` (`v0.4.2`), `Dev\tools\*.py` & Standard 18 Contracts

- **Python `foundation_core` (`Dev\src\foundation_core\`, `SDK_VERSION = "0.4.2"`):**
  - `sandbox_seam.py`: `CONTAINMENT_EXIT_CODE = 3`, `SandboxContainmentError`, `is_containment_violation`, `SandboxMountSpec`, `SandboxResolution`, `get_subst_mappings`, `resolve_sandbox_environment(env_override=None)` (process env only; `APP_ISOLATED_TEST=1` overrides), `resolve_known_folders`, `get_known_folder`, `is_path_within_sandbox`, `assert_sandbox_path_if_active`, `map_host_path_to_sandbox`.
  - `pseudo_registry.py`: `REGISTRY_HIVE_SCHEMA_URI`, `REGISTRY_HIVE_SCHEMA_VERSION = 1`, `PlatformRegistryHive`, `PlatformRegistryValueKind`, `JsonHivePlatformRegistry` (Standard 18 JSON + PID-safe OS file lock + `.tmp -> fsync -> .bak -> os.replace`).
  - Existing modules: `cli.py`, `settings.py`, `logging.py`, `crash_dumper.py`, `locks.py`, `db.py`.
- **Tools (`Dev\tools\`):**
  - `sandbox_ctl.py`: 4-drive orchestrator (`init`, `mount`, `unmount-removable`, `remount-removable`, `mount-disc`, `unmount-disc`, `status`, `snapshot`, `diff-host`, `reset`), resolving `CLI -> env -> Dev\.env -> %USERPROFILE%\.gemini\.secrets\.env` + dynamic `subst`, exit `3` on host collisions.
  - `run_sandbox_e2e.py`: Standard 02 §4 12-point (`S2-01`..`S2-12`) Stage 2 E2E runner with scrubbed child env (`FOUNDATION_SUPPRESS_MODALS=1`), hooks, removable toggle, 2-tier log sweep, and `hostDiffCount == 0`.
  - `sync_foundation_sdk.py`: `--version 0.4.2`, `--ui-version 0.4.2`, `--publish` (strict 6-file `Releases\Latest\` without loose `LICENSE`; `app\LICENSE` in `Builds\` & `.zip`), `--sync-consumer` (narrowed legacy cleanup globs preserving `sandbox_seam.py`/`sandbox_ctl.py`/`run_sandbox_e2e.py`).
  - `build_release_universal.py`: `validate_six_file_release_bundle(..., allow_sdk_zip=...)` enforcing strictly 6 files (no loose `LICENSE`).
- **Standard 18 / 40 Sandbox JSON Schemas:**
  - `registry.hive.json` (`<SandboxRoot>\registry\registry.hive.json`): `$schema` (`"https://schemas.local/registry-hive.v1.json"`), `schemaVersion: 1`, `sandboxRoot`, `updatedAtUtc`, `registryHives: { HKCU, HKLM, HKCR }` with entries `{ registryValueKind, registryValueData }` (`REG_BINARY` lowercase hex).
  - `sandbox.manifest.json` (`<SandboxRoot>\sandbox.manifest.json`): `$schema`, `schemaVersion: 2`, `appId: "foundation-sandbox"`, `version`, `updatedAtUtc` (Standard 40 header omitting `manifestType`), `sandboxRoot`, `sandboxProfileDir`, `registryHivePath`, `sandboxMounts[]` (`{ driveRole, driveMount, directoryPath, isEnabled }`).
  - Stage 2 E2E Receipt (`<SandboxRoot>\runs\<appId>-v<Ver>-<ts>.json`): `$schema`, `schemaVersion: 1`, `id`, `appId`, `version`, `status`, `isPassed`, `isStage2SandboxPassed`, `hostDiffCount`, `sandboxRoot`, `sandboxProfileDir`, `registryHivePath`, `sandboxMounts`, `filePath`, `directoryPath`, `startedAtUtc`, `completedAtUtc`, `verifiedAtUtc`, `durationMs`, `exitCode`, `errorCode`, `errorMessage`, `errorRemedy`, `notes`, `checklistResults[]` (`{ id, title, isPassed, durationMs, notes }`).
