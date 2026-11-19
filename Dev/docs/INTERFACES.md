# Foundation (`v0.5.2`) — 1-Stop Interfaces & Agent API Reference

> **Purpose:** Single lookup reference for **every exported C# class/method, React/TS component (`@foundation/ui`), Vanilla JS/CSS asset, Python helper (`foundation_core`), `Dev\tools\*.py` CLI, and file/JSON schema** in `<PROJECTS_ROOT>\Foundation\`.
> **Snapshot Baseline:** Unified SDK `0.5.2` (`.NET 8`, `@foundation/ui v0.5.2`, `foundation_core v0.5.2`).

---

## 0. Fast-Path Lookup & Import Pitfalls (Read First)

| If you need... | Use this symbol / file | Exact import / CLI / path |
|---|---|---|
| CLI path overrides, path containment, UI presentation mode | `AppEnvironment`, `UiPresentationMode` | `using Foundation.Core;` |
| 4-drive external sandbox resolution, `subst` discovery, `ExitCode = 3` containment | `SandboxEnvironmentSeam`, `SandboxResolution`, `SandboxMountSpec`, `KnownFolderPaths`, `SandboxDriveController` | `using Foundation.Core;` |
| Host or atomic JSON pseudo-registry (`HKCU`/`HKLM`/`HKCR`, 6 `REG_*` kinds) | `IPlatformRegistry`, `WindowsPlatformRegistry`, `JsonHivePlatformRegistry`, `PlatformRegistryFactory` | `using Foundation.Core;` |
| Frame pacer, SPSC audio, WAV/PNG/RGBA overlay, ISO QR encoder, CLI, settings, DPAPI, SQLite WAL, PID locks, heartbeats, crash bundles, jobs, safe HTTP, tools, modules | `HighResFramePacer`, `AudioRingBuffer`, `WavCaptureWriter`, `RgbaPngWriter`, `PixelOverlayCanvas`, `QrCodeEncoder`, `QrCodeMatrix`, `CliArgumentHelper`, `GranularSettingsManager`, `DpapiSecretStore`, `SqliteWalPool`, `PidSafeLockfile`, `HeartbeatLogger`, `CrashBundleService`, `JobObjectGuard`, `SafeNetworkTransfer`, `ToolResolver`, `HybridModuleLoader` | `using Foundation.Core;` |
| 41-platform catalog, DAT parser, headerless stripper, soft-patch, BYO media, AOT/recomp contracts | `ConsoleSystemCatalog`, `DatTitleNormalizer`, `NoIntroRedumpDatParser`, `HeaderlessRomStripper`, `SoftPatchEngine`, `MediaContainerExtractor`, `IRecompHost`, `AotCacheLayoutHelper` | `using Foundation.Retro;` |
| Single-pass hash, ZIP/7z `-slt`, `.hash` v1, `.hklock` | `SinglePassStreamHasher`, `SevenZipAndZipInspector`, `HashSidecarService`, `HkLockSentinel` | `using Foundation.Sidecars;` |
| Kestrel tiered auth + `/suite/manifest` endpoints | `TieredAuthEngine`, `FoundationTieredAuthMiddleware`, `SisterAppStandardEndpoints` | `using Foundation.Server;` |
| Clean-root Win32 `AttachConsole(-1)` & extensible launcher shim (`v0.5.2`) | `RootLauncherHost`, `RootLauncherOptions` | `using Foundation.Launcher;` |
| WinForms WebView2 host, STA dialogs, tray helpers, branding | `DisposableWebViewHostForm`, `StaNativeDialogBridgeBase`, `TrayIconBadgeHelper`, `ShellBrandingOptions` | `using Foundation.UI;` / `.Desktop` |
| Full dark WinForms + CLI Setup/Uninstall Wizard + Markup Loader (`v0.5.2`) | `StandardInstallerHost`, `InstallerManifestStore`, `InstallerMarkupManifest`, `InstallerAppDescriptor`, `InstallerRuntimeContext`, `WindowsShortcutWriter`, `FileLockRetryHelper`, `InstallerWizardForm` | `using Foundation.Installer;` |
| Low-level app-dir & suite registry helper (`v0.5.2`) | `InstallerEngineCore`, `InstallerThemeOptions` | `using Foundation.Installer;` |
| Opt-in `127.0.0.1` UI automation, virtual pointer/keyboard & offscreen PNG snapshots (`v0.5.2`) | `AutomationLoopbackServer`, `AutomationCliHelper`, `Win32VirtualInputDriver`, `WindowSnapshotService`, `IRenderSurfaceSnapshotProvider` | `using Foundation.Automation;` |
| React components (`@foundation/ui v0.5.2`) & Vanilla terminal | 7 barrel exports + 3 direct submodule imports; Vanilla `FoundationTerminalDrawer` | `@foundation/ui` vs `@foundation/ui/components/<Name>` |
| Python SDK (`foundation_core`) & Sandbox E2E tools | `foundation_core` (`sandbox_seam`, `pseudo_registry`), `sandbox_ctl.py`, `run_sandbox_e2e.py`, `sync_foundation_sdk.py` | `Dev\src\foundation_core\` / `Dev\tools\` |

### Top Import & Caller Pitfalls
1. **React Barrel vs. Direct Submodule Imports (`Dev\src\Foundation.UI\Web\index.ts`):** `CoexistenceWarningBanner`, `HybridPathPicker`, and `SidebarNavRail` are NOT in the `@foundation/ui` barrel—import via `@foundation/ui/components/<Name>`.
2. **Two Installer Tiers:** Do not mix `InstallerAppDescriptor` + `StandardInstallerHost` (Tier 1) with `InstallerThemeOptions` + `InstallerEngineCore` (Tier 2).
3. **TFM Split:** `Foundation.Core`, `Foundation.Retro`, `Foundation.Sidecars`, and `Foundation.Server` target pure `net8.0` (zero WinForms/GDI+); `Foundation.Installer`, `Foundation.Automation`, `Foundation.UI`, and `Foundation.Launcher` target `net8.0-windows`.
4. **3-Mode Sandbox Safety (`v0.4.2+`):** Runtime libraries read process env ONLY (`APP_ISOLATED_TEST=1` beats `FOUNDATION_SANDBOX_ROOT`). Gate containment guards on `ShouldSuppressLiveHostMutations` (`IsStage1Isolated || IsSandboxActive`), NOT `ShouldSuppressHostMutations` (`IsStage1Isolated` only).

---

## 1. C# Shared Libraries (`v0.5.2`)

### 1.1 `Foundation.Core` (`Dev\src\Foundation.Core\`, `net8.0`, `v0.5.2`)

#### 4-Drive Sandbox Seam & Known-Folder Redirection (`v0.4.2`)
- **`SandboxMountSpec(string DriveRole, string DriveMount, string? DirectoryPath, bool IsEnabled)`**
- **`SandboxResolution(bool IsSandboxActive, string? SandboxRoot, string? SystemDriveRoot, string? StorageDriveRoot, string? RemovableDriveRoot, string? DiscDriveRoot, string? SandboxProfileDir, string? RegistryHivePath, string? RegistryBaselinePath, string? RegistryLockPath)`** (+ `SandboxResolution.Inactive`)
- **`SandboxEnvironmentSeam` (`SandboxEnvironmentSeam.cs`):** `ContainmentExitCode = 3`, `ResolveCurrent()`, `Resolve(IReadOnlyDictionary<string, string?>? envOverride)`, `ValidateResolution(SandboxResolution)`, `AssertSandboxPathIfActive(string candidatePath, SandboxResolution? sandbox = null)`, `IsPathWithinSandbox(string? candidatePath, SandboxResolution? sandbox = null)`, `IsContainmentViolation(Exception?)`, `ThrowContainmentViolation(string message, Exception? inner = null)`, `TryResolveSubstBackingDirectory(string? driveOrPath)`, `GetProcessEnvironment()`.
- **`KnownFolderPaths` (`KnownFolderPaths.cs`):** `GetFolderPath(SpecialFolder, SandboxResolution? = null)`, `GetFolderPath(SpecialFolder, SpecialFolderOption, SandboxResolution? = null)`, `GetHostFolderPath(SpecialFolder)`, `ResolveLocalProgramsDirectory(SandboxResolution? = null)`, `MapHostPathToSandbox(string, SandboxResolution? = null)`. Maps `UserProfile`, `LocalApplicationData`, `ApplicationData`, `Desktop`/`DesktopDirectory`, `Personal`/`MyDocuments`, `Programs`, `StartMenu`, `Startup`, `ProgramFiles`, `ProgramFilesX86`, `CommonApplicationData`, `Windows`.
- **`SandboxDriveController` (`SandboxDriveController.cs`):** Explicit opt-in tooling controller (never called by `AppEnvironment`/`SandboxEnvironmentSeam`): `GetDefaultUserSecretsEnvPath()`, `LoadEnvStore(...)`, `ParseEnvFile(string)`, `GetSubstMappings()`, `ParseSubstOutput(string?)`, `InferSandboxRoot(...)`, `ResolveDriveMounts(...)`, `ValidateNoHostCollisions(...)`.

#### Shared Atomic JSON Pseudo-Registry (`v0.4.2`)
- **`PlatformRegistryHive` (`CurrentUser`, `LocalMachine`, `ClassesRoot`)**, **`PlatformRegistryValueKind` (`String`, `ExpandString`, `MultiString`, `DWord`, `QWord`, `Binary`)**, **`PlatformRegistryValue(Kind, Data)`** (`AsString()`, `AsInt32()`, `AsInt64()`, `AsMultiString()`, `AsBinary()`).
- **`IPlatformRegistry`:** `SubKeyExists`, `CreateSubKey`, `DeleteSubKeyTree`, `GetValue`, `SetValue`, `SetValues`, `DeleteValue`, `GetSubKeyNames`, `GetValueNames`, `GetValues`.
- **Implementations:** `NoOpPlatformRegistry.Instance`, `WindowsPlatformRegistry` (`[SupportedOSPlatform("windows")]` in-box registry), `JsonHivePlatformRegistry(hiveFilePath, sandboxRoot?, lockFilePath?, lockTimeout?)` (`EnsureInitialized()`, Mutex `Local\FoundationSandboxReg_<sha8>` + `PidSafeLockfile` + `.tmp -> Flush(true) -> .bak -> File.Move`), and `PlatformRegistryFactory.CreateDefault(SandboxResolution? sandbox = null, bool suppressMutations = false)`.

#### Runtime Environment & Core Primitives (`v0.1.0`–`v0.5.1`)
| Class (`File`) | Key Public Signatures | Contract |
|---|---|---|
| `AppEnvironment` (`AppEnvironment.cs`) | `AppEnvironment(appId, args?, baseDir?, defaultConfig?)` + 5-arg `sandboxOverride`; `Sandbox`, `AssertPathContained`, `NormalizeLongPath` | CLI `--data-dir`/`--config`/`--log-dir`/`--port`/4 UI modes + sandbox seam |
| `HighResFramePacer`, `AudioRingBuffer`, `WavCaptureWriter`, `RgbaPngWriter`, `PixelOverlayCanvas` | `WaitForNextFrame()`, `Write`/`Read`, `WriteInterleavedSamples`, `WritePng`, `DrawText`/`SavePng` | Timing (`timeBeginPeriod(1)`), lock-free SPSC audio, WAV/PNG/HUD |
| `QrCodeEncoder`, `QrCodeMatrix` (`QrCodeEncoder.cs`, `v0.5.1`) | `Encode(string payload) -> QrCodeMatrix(Version, Size, Modules)` (`GetModule(row, col)`); `RenderSvg(string payload, string darkColor = "#0b0e14", string lightColor = "#ffffff", int quietZoneModules = 4, string? ariaLabel = "QR code") -> string` | Zero-dep ISO/IEC 18004 byte-mode V1–V10 ECC-M (`<= 213` UTF-8 bytes), GF(256) RS, 8-mask scoring, `>= 4`-module quiet zone |
| `CliArgumentHelper`, `BoundedTailStream`, `CappedSharedLogSink` | `HasFlag`, `GetOption`, `GetPositional`, `FormatBytes` | CLI parsing + 64 KB ring / 4 MB log cap |
| `GranularSettingsManager`, `DpapiSecretStore` | `LoadOrSelfHeal<T>`, `SaveAtomically<T>`, `SetSecret`/`GetSecret` | `.tmp -> .bak`, 20 snapshots; Windows DPAPI `CurrentUser` |
| `SqliteWalPool`, `PidSafeLockfile` | `OpenConnection()`, `VacuumIntoAsync()`, `Acquire`/`TryAcquire` | `WAL`, `busy_timeout=15000`; `pid` + `processStartTimeUtc` locks |
| `HeartbeatLogger`, `CrashBundleService`, `JobObjectGuard`, `SafeNetworkTransfer`, `ToolResolver`, `HybridModuleLoader` | `ReportProgress`, `RegisterGlobalHandlers`, `BeginJobScope`/`StopAllJobs`, `DownloadFileAsync`, `ResolveTool`, `DiscoverExternalDllModules` | `<= 15s` `\r\x1b[K` heartbeats, crash JSON, `KILL_ON_JOB_CLOSE`, `Range` resume, 4-tier tools, plugins |

### 1.2 `Foundation.Retro`, `Foundation.Sidecars`, `Foundation.Server` & `Foundation.Launcher`
- **`Foundation.Retro` (`net8.0`):** `ConsoleSystemCatalog`, `DatTitleNormalizer`, `NoIntroRedumpDatParser`, `HeaderlessRomStripper`, `MagicBytePlatformDetector`, `SoftPatchEngine`, `MediaContainerExtractor`, `RecompContracts`, `RecompTierAndBackendContracts`.
- **`Foundation.Sidecars` (`net8.0`):** `SinglePassStreamHasher`, `SevenZipAndZipInspector`, `HashSidecarService`, `HkLockSentinel`.
- **`Foundation.Server` (`net8.0`):** `TieredAuthEngine`, `FoundationTieredAuthMiddleware`, `SisterAppStandardEndpoints`.
- **`Foundation.Launcher` (`net8.0-windows`, `v0.5.2`):**
  - `RootLauncherOptions(IReadOnlyCollection<string>? ExtraSyncWaitFlags = null, IReadOnlyCollection<string>? ExtraValueOptions = null, IReadOnlyCollection<string>? ExtraPathValueOptions = null)` (`RootLauncherOptions.Default`).
  - `RootLauncherHost`: `ResolveTargetExecutable(...)`, `Run(appRelPath, displayName, args)` + `Run(..., RootLauncherOptions? options)`, `NormalizeRelativePathArgs(args, callerCwd, options?)`, `ShouldWaitForChild(args, options?)`, `DefaultSyncWaitFlags`, `DefaultValueOptions`, `DefaultPathValueOptions`. Built-in sync flags include `--diagnose`, `--doctor`, `--verify`, `--verify-module`; built-in value/path options include `--programs-dir`, `--save-dir`, `--preload-media`, `--backend`, `--automation-port`, `--automation-token`; optional-value flags `--suite-tab`, `--window`, `--browser` consume a non-flag argument without swallowing trailing `--wait`/`--diagnose`.

### 1.3 `Foundation.Installer` (`net8.0-windows`, `v0.5.2`), `Foundation.Automation` (`v0.5.2`) & `Foundation.UI` (`v0.5.2`)
- **Declarative Markup Installer (`InstallerManifestStore` & DTO Contracts):**
  - `InstallerMarkupTheme(string? Background = null, string? Surface = null, string? Accent = null, string? Text = null, string? Border = null)` (JSON: `background`, `surface`, `accent`, `text`, `border`)
  - `InstallerMarkupButtons(string? InstallText = null, string? UpgradeText = null, string? UninstallText = null, string? CancelText = null)` (JSON: `installText`, `upgradeText`, `uninstallText`, `cancelText`)
  - `InstallerMarkupTooltip(string Title, string Description)` (`FormatTwoLine() -> string`)
  - `InstallerMarkupSecondaryPicker(string FlagName, string Label, string? DefaultPath = null, string? Placeholder = null, string? Tooltip = null)`
  - `InstallerMarkupManifest`: `$schema` (`"https://schemas.local/manifest.v2.json"`), `schemaVersion: int = 2`, `manifestType: string = "installer-manifest"`, `appId: string`, `version: string`, `updatedAtUtc: string`, `displayName: string?`, `appTitle: string?` (legacy fallback alias), `publisher: string?`, `subtitle: string?`, `studioName: string?`, `suiteId: string?` (canonical Standard 18 field; `suiteName: string?` retained as legacy read-only JSON fallback via `EffectiveSuiteId` / `ResolveEffectiveSuiteId()`), `umbrellaSubpath: string?`, `mainExecutableRelativePath: string?`, `logoPath: string?`, `iconPath: string?`, `theme: InstallerMarkupTheme?`, `layoutMode: "single-line" | "multi-line"`, `supportedProfiles: List<string>` (`"lightweight"`, `"full"`, `"custom"`), `defaultProfile: string = "lightweight"`, `buttons: InstallerMarkupButtons?`, `tooltips: Dictionary<string, InstallerMarkupTooltip>?`, `secondaryPickers: List<InstallerMarkupSecondaryPicker>?`, `allowAutoStart: bool?`, `allowLegacyMigration: bool?`.
  - `InstallerManifestStore.LoadFromJson(string json) -> InstallerMarkupManifest` (throws `ArgumentException` on blank input; `InvalidDataException` if `SchemaVersion != 2`, `ManifestType != "installer-manifest"`, missing `appId`, or unsupported `layoutMode`).
  - `InstallerManifestStore.LoadFromFile(string filePath) -> InstallerMarkupManifest`
  - `InstallerManifestStore.TryLoadFromDirectoryOrAssembly(string? directory, Assembly? assembly, string fileName = "installer.manifest.json") -> InstallerMarkupManifest?`
  - `InstallerManifestStore.ApplyToDescriptor(InstallerAppDescriptor baseDescriptor, InstallerMarkupManifest manifest) -> InstallerAppDescriptor` (propagates `ResolveEffectiveSuiteId()`)
  - `InstallerManifestStore.ToThemeOptions(InstallerMarkupManifest manifest, string? defaultInstallDir = null) -> InstallerThemeOptions` (propagates `ResolveEffectiveSuiteId()`)
  - `StandardInstallerHost.RunWithResult(InstallerAppDescriptor descriptor, string[] args, Assembly? payloadAssembly = null) -> InstallerHostRunResult` auto-merges `installer.manifest.json` from `AppContext.BaseDirectory` or `payloadAssembly` before wizard/CLI dispatch.
- **Smart Fresh-Install Path Suggestion & Self-Healing (`LastInstallBaseDir`):**
  - `InstallerEngine.SharedStreamItAllRegistrySubKey = @"Software\StreamItAll"`; `InstallerEngine.LastInstallBaseDirValueName = "LastInstallBaseDir"`.
  - `InstallerEngine.TryResolveSuggestedInstallDirFromHistory(IPlatformRegistry registry, string umbrellaSubpath, SandboxResolution sandbox, out string suggestedInstallDir) -> bool` (fresh installs only; self-heals poisoned `LastInstallBaseDir` values by stripping `StreamItAll` / `reEngine\engines|games` segments before validation while keeping legitimate user paths like `D:\Games\Misc`; existing `InstallLocation` takes precedence on upgrades).
  - `InstallerEngine.ExtractInstallBaseDirectory(string installDir, string umbrellaSubpath) -> string` (studio/suite-anchored base extraction rejecting poisoned `StreamItAll`/`reEngine`/`engines` candidates).
  - `InstallerEngine.TrySaveLastInstallBaseDir(IPlatformRegistry registry, string installDir, string umbrellaSubpath, SandboxResolution sandbox, bool suppressHostMutations) -> bool` (persisted post-success only; uses studio/suite-anchored `ExtractInstallBaseDirectory`; skips `ShouldSuppressHostMutations` / `APP_ISOLATED_TEST=1`, `NoOpPlatformRegistry`, unqualified paths, drive/sandbox roots, `%TEMP%`, poisoned `StreamItAll`/`reEngine`/`engines` candidates, and reparse points).
  - `InstallerEngine.TryValidateLastInstallBaseDir(string? rawBase, SandboxResolution sandbox, out string validBase) -> bool` (checks `Path.IsPathFullyQualified`, rejects drive/sandbox roots, temp paths, poisoned bases, sandbox escapes, and reparse points).
- **3-Mode `InstallerRuntimeContext`:** `Sandbox`, `IsSandboxActive` (`Sandbox.IsSandboxActive && !IsIsolatedTestProcess`), `IsStage1Isolated`, `ShouldSuppressHostMutations => IsStage1Isolated`, `ShouldSuppressLiveHostMutations => IsStage1Isolated || IsSandboxActive`, `Registry` (`IPlatformRegistry`), ABI-intact 5-arg ctor + 7-arg override ctor.
- **Tier 1 (`StandardInstallerHost` + `InstallerAppDescriptor`) & Tier 2 (`InstallerEngineCore`):**
  - `InstallerAppDescriptor.ResolveCanonicalDefaultInstallDir(SandboxResolution? sandbox = null)` & `GetEffectiveUmbrellaSubpath()` route via `KnownFolderPaths` and avoid doubling `ProductName`/`DisplayName` on versioned `UmbrellaSubpath` leaves (`vX.Y.Z*`); `StandardInstallerHost` maps `SandboxEnvironmentSeam.IsContainmentViolation(ex)` to `ExitCode = 3`.
  - `FileLockRetryHelper.IsSafeIsolatedInstallDirectory` & `IsCanonicalProductionOrSystemPath` add trailing `SandboxResolution? sandbox = null`, permitting verified `<SandboxDDrive>\Programs\...` while blocking raw host OS/user/host-`Programs` targets (`ExitCode = 3`).
  - `WindowsShortcutWriter.SanitizeShortcutFileName(string? displayName)` normalizes `:` -> ` -` and replaces `Path.GetInvalidFileNameChars()` with `_`; `RegisterCurrentUserUninstallEntry`/`RemoveCurrentUserUninstallEntry` accept trailing `IPlatformRegistry?`.
  - `InstallerWizardForm`: exposes `LayoutMode` (`string`), `SupportedProfiles` (`IReadOnlyList<string>`), `CustomProfileRadioButton` (`RadioButton`), `GetControlTooltip(Control control) -> string?`, `_lblTitle` & `_lblSub` clamping (`AutoEllipsis = true` in `single-line`), `650x460`/`650x570` default `ClientSize`; `ShouldSuppressModalDialogs` honors `FOUNDATION_SUPPRESS_MODALS=1`. Trailing optional `Assembly? payloadAssembly = null` parameter on both constructors.
- **`Foundation.Automation` (`net8.0-windows`, `v0.5.0`):**
  - `IRenderSurfaceSnapshotProvider`: `bool TryCaptureRgba(out int width, out int height, out byte[] rgbaBytes)` (Raylib/OpenGL RGBA hook via `RgbaPngWriter`).
  - `AutomationServerOptions(int AutomationPort, string BearerToken, string AppId = "app", string? AllowedSnapshotRoot = null)`
  - `AutomationControlNode(string Id, string ControlType, string Name, string Text, int X, int Y, int Width, int Height, bool IsVisible, bool IsEnabled, IReadOnlyList<AutomationControlNode> Children)` (`id`, `controlType`, `name`, `text`, `x`, `y`, `width`, `height`, `isVisible`, `isEnabled`, `children`)
  - `AutomationPointerRequest(string Action = "click", int X = 0, int Y = 0, int? ToX = null, int? ToY = null, string Button = "left", int DeltaY = 0, string? TargetId = null)`
  - `AutomationKeyboardRequest(string Action = "type", string? Text = null, string? Key = null, IReadOnlyList<string>? Modifiers = null, string? TargetId = null)`
  - `AutomationSnapshotRequest(string FilePath, string SnapshotFormat = "png", string? TargetId = null)`
  - `AutomationSnapshotResult(string FilePath, int Width, int Height, long FileSizeBytes, string SnapshotFormat)`
  - `AutomationHealthPayload(string AppId, int AutomationPort, string Status, bool IsPassed, string GeneratedAtUtc)` (`appId`, `automationPort`, `status`, `isPassed`, `generatedAtUtc`)
  - `AutomationResponse<T>(bool IsPassed, string Status, string? ErrorMessage, T? Data)` (`AutomationResponse<T>.Ok(T data)`, `AutomationResponse<T>.Fail(string errorMessage)`)
  - `AutomationCliHelper.TryCreateFromArgs(string appId, string[]? args, Control? rootControl, IRenderSurfaceSnapshotProvider? renderProvider = null, string? allowedSnapshotRoot = null) -> AutomationLoopbackServer?` (parses `--automation-port <0..65535>` and `--automation-token <token>`; falls back to `FOUNDATION_AUTOMATION_TOKEN` or 32-hex CSPRNG; returns `null` if `--automation-port` omitted).
  - `AutomationLoopbackServer(AutomationServerOptions options, Control? rootControl = null, IRenderSurfaceSnapshotProvider? renderProvider = null, SandboxResolution? sandbox = null)`: `Start() -> void`, `AutomationPort` (`int`), `BearerToken` (`string`), `IsListening` (`bool`), `Dispose() / DisposeAsync()`. Binds `127.0.0.1` (`Host`/`Origin` `403`, SHA-256 `FixedTimeEquals` `Bearer` `401`). Routes: `GET /v1/health`, `GET /v1/tree`, `POST /v1/pointer`, `POST /v1/keyboard`, `POST /v1/snapshot` (returns `400`/`403` with `isPassed: false` on invalid `targetId`, unsupported action, or containment violation).
  - `Win32VirtualInputDriver`: `InspectTree(Control? root) -> AutomationControlNode?`, `ExecutePointer(Control? root, AutomationPointerRequest request) -> bool`, `ExecuteKeyboard(Control? root, AutomationKeyboardRequest request) -> bool` (throws `ArgumentException` when `targetId` not found or action/key unsupported; `0` `SendInput` / `0` focus steal).
  - `WindowSnapshotService.CaptureSnapshot(Control? rootControl, IRenderSurfaceSnapshotProvider? renderProvider, AutomationSnapshotRequest request, string? allowedSnapshotRoot = null, SandboxResolution? sandbox = null) -> AutomationSnapshotResult` (throws `ArgumentException` for non-`"png"` format or missing `targetId`, `InvalidOperationException` with `ExitCode=3` on sandbox/root escape).
- **`Foundation.UI`:** `ShellBrandingOptions` (`SdkVersion = "0.5.2"`), `DisposableWebViewHostForm`, `StaNativeDialogBridgeBase`, `TrayIconBadgeHelper`, `@foundation/ui` (`Web\index.ts` + 3 submodule components + `WebAssets\*`).

---

## 2. Python `foundation_core` (`v0.5.2`), `Dev\tools\*.py` & Standard 18 Contracts

- **Python `foundation_core` (`Dev\src\foundation_core\`, `SDK_VERSION = "0.5.2"`):**
  - `sandbox_seam.py`: `CONTAINMENT_EXIT_CODE = 3`, `SandboxContainmentError`, `is_containment_violation`, `SandboxMountSpec`, `SandboxResolution`, `get_subst_mappings`, `resolve_sandbox_environment(env_override=None)` (process env only; `APP_ISOLATED_TEST=1` overrides), `resolve_known_folders`, `get_known_folder`, `is_path_within_sandbox`, `assert_sandbox_path_if_active`, `map_host_path_to_sandbox`.
  - `pseudo_registry.py`: `REGISTRY_HIVE_SCHEMA_URI`, `REGISTRY_HIVE_SCHEMA_VERSION = 1`, `PlatformRegistryHive`, `PlatformRegistryValueKind`, `JsonHivePlatformRegistry` (Standard 18 JSON + PID-safe OS file lock + `.tmp -> fsync -> .bak -> os.replace`).
  - Existing modules: `cli.py`, `settings.py`, `logging.py`, `crash_dumper.py`, `locks.py`, `db.py`.
- **Tools (`Dev\tools\`):**
  - `sandbox_ctl.py`: 4-drive orchestrator (`init`, `mount`, `unmount-removable`, `remount-removable`, `mount-disc`, `unmount-disc`, `status`, `snapshot`, `diff-host`, `reset`), resolving `CLI -> env -> Dev\.env -> %USERPROFILE%\.gemini\.secrets\.env` + dynamic `subst`, exit `3` on host collisions.
  - `run_sandbox_e2e.py`: Standard 02 §4 12-point (`S2-01`..`S2-12`) Stage 2 E2E runner with scrubbed child env (`FOUNDATION_SUPPRESS_MODALS=1`), hooks, removable toggle, 2-tier log sweep, and `hostDiffCount == 0`.
  - `sync_foundation_sdk.py`: `--version 0.5.2`, `--ui-version 0.5.2`, `--publish` (strict 6-file `Releases\Latest\` without loose `LICENSE`; `app\LICENSE` in `Builds\` & `.zip`), `--sync-consumer` (narrowed legacy cleanup globs preserving `sandbox_seam.py`/`sandbox_ctl.py`/`run_sandbox_e2e.py`).
  - `build_release_universal.py`: `validate_six_file_release_bundle(..., allow_sdk_zip=..., allow_portable_zip=...)` (alias `validate_release_bundle`) enforcing 6-file SDK/app bundles or 7-file app bundles (`--allow-portable-zip` / auto-detected `*-Portable.zip` with zero `*Setup*.exe` leaks and zero loose subdirs).
- **Standard 18 / 40 Sandbox JSON Schemas:**
  - `registry.hive.json` (`<SandboxRoot>\registry\registry.hive.json`): `$schema` (`"https://schemas.local/registry-hive.v1.json"`), `schemaVersion: 1`, `sandboxRoot`, `updatedAtUtc`, `registryHives: { HKCU, HKLM, HKCR }` with entries `{ registryValueKind, registryValueData }` (`REG_BINARY` lowercase hex).
  - `sandbox.manifest.json` (`<SandboxRoot>\sandbox.manifest.json`): `$schema`, `schemaVersion: 2`, `appId: "foundation-sandbox"`, `version`, `updatedAtUtc` (Standard 40 header omitting `manifestType`), `sandboxRoot`, `sandboxProfileDir`, `registryHivePath`, `sandboxMounts[]` (`{ driveRole, driveMount, directoryPath, isEnabled }`).
  - Stage 2 E2E Receipt (`<SandboxRoot>\runs\<appId>-v<Ver>-<ts>.json`): `$schema`, `schemaVersion: 1`, `id`, `appId`, `version`, `status`, `isPassed`, `isStage2SandboxPassed`, `hostDiffCount`, `sandboxRoot`, `sandboxProfileDir`, `registryHivePath`, `sandboxMounts`, `filePath`, `directoryPath`, `startedAtUtc`, `completedAtUtc`, `verifiedAtUtc`, `durationMs`, `exitCode`, `errorCode`, `errorMessage`, `errorRemedy`, `notes`, `checklistResults[]` (`{ id, title, isPassed, durationMs, notes }`).
