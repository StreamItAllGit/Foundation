# Foundation (`v0.5.6`) — Consumer User Guide & Integration Recipes

> **Goal:** Copy-paste integration recipes for consuming `Foundation` across C#, React/TypeScript, Vanilla Web, Python, and Stage 2 external 4-drive sandboxes without re-reading SDK source files.
> **Full API Dictionary:** [Dev/docs/INTERFACES.md](INTERFACES.md) · **Operations Runbook:** [Dev/docs/OPERATIONS.md](OPERATIONS.md) · **Stage 2 E2E Ledger:** [Dev/docs/OPERATIONS.E2ECoverage.md](OPERATIONS.E2ECoverage.md)

---

## 1. Syncing Foundation into a Consumer Project

Consumer repositories (`Housekeeping`, `ShadowLibrary`, `reStation`, `KingsFieldRecomp`, `MediaSync`) reference published binaries in `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\` (or vendored copies under `<Consumer>\Dev\lib\` synced via [`Dev/tools/sync_foundation_sdk.py`](../tools/sync_foundation_sdk.py)):

```powershell
# Verify hub health, pull Latest SDK DLLs/@foundation/ui/foundation_core/tools, and purge retired legacy test artifacts
python <PROJECTS_ROOT>\Foundation\Dev\tools\sync_foundation_sdk.py --sync-consumer <PROJECTS_ROOT>\<ProjectName> --source-version Latest
```

> **Rule Reminder:** Consumer development sessions treat `<PROJECTS_ROOT>\Foundation\` and `<PROJECTS_ROOT>\SharedLibs\Foundation\` as **strictly read-only**. Log upstream SDK gaps in `Dev\docs\reports\`.

### 1.1 Validating SDK, Portable & Dual-Installer Release Bundles (`--allow-portable-zip`, `--allow-dual-installer`, `--stage-sdk-zip`)
[`Dev/tools/build_release_universal.py`](../tools/build_release_universal.py) validates 6-file SDK/classic bundles (`--allow-sdk-zip`), 7-file app+portable bundles (`--allow-portable-zip`), and 8-file Dual Online+Offline+Portable bundles (`--allow-dual-installer`, checking `*-Setup-v*.exe`, `*-Offline-Setup-v*.exe`, `*-Portable.zip`, `README.md`, `FEATURES.md`, `RELEASE_NOTES.md`, `CHANGELOG.md`, and `SHA256SUMS.txt` with zero `*Setup*.exe` or `-Offline-Setup-` leaks inside `*-Portable.zip` and hardlink-safe `.tmp -> os.replace` checksum writes):

```powershell
# 7-file app + portable release bundle
python Dev\tools\build_release_universal.py path\to\Releases\Latest --regen-sums --allow-portable-zip

# 8-file Dual Online + Offline + Portable release bundle
python Dev\tools\build_release_universal.py path\to\Releases\Latest --regen-sums --allow-dual-installer

# Stage Foundation-v0.5.6.zip from SDK snapshot & atomically write SHA256SUMS.txt
python Dev\tools\build_release_universal.py Releases\Latest --stage-sdk-zip path\to\SharedLibs\Foundation\v0.5.6 --version 0.5.6 --allow-sdk-zip
```

---

## 2. Core Startup, Lock-Free SQLite WAL, Rotating Backups, Late-Latch Pacing & Safe Updates (`Foundation.Core`)

```csharp
using Foundation.Core;

var env = new AppEnvironment("myapp", args);
CrashBundleService.RegisterGlobalHandlers(env.LogDirectory, env.AppId, "0.1.0");

// KnownFolderPaths & PlatformRegistryFactory automatically route inside Stage 2 sandbox when active,
// or delegate to host Windows/Environment.GetFolderPath when FOUNDATION_SANDBOX_ROOT is unset.
string userDocs = KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.MyDocuments, env.Sandbox);
IPlatformRegistry registry = PlatformRegistryFactory.CreateDefault(env.Sandbox, suppressMutations: env.DryRun);

var settingsMgr = new GranularSettingsManager(env.DataDirectory);
var outcome = settingsMgr.LoadOrSelfHeal("settings.json", () => new MySettings());
var secrets = new DpapiSecretStore(env.DataDirectory);

// Lock-free Volatile.Read WAL pool (detects DB deletion/recreation automatically; call ResetJournalModeCache() if needed)
var dbPool = new SqliteWalPool(Path.Combine(env.DataDirectory, "myapp.db"));
await using var conn = await dbPool.OpenConnectionAsync();

using var lockHandle = PidSafeLockfile.Acquire(Path.Combine(env.DataDirectory, "myapp.lock"), env.AppId, "startup");
```

- Pass `--data-dir`, `--config`, `--log-dir`, or `--port` to override runtime paths; `AppEnvironment.Sandbox` exposes `IsSandboxActive` (process env only; `APP_ISOLATED_TEST=1` always disables Stage 2 sandbox mode so Stage 1 unit tests stay strictly isolated).
- Call `SandboxEnvironmentSeam.AssertSandboxPathIfActive(candidatePath, env.Sandbox)` before filesystem writes when supporting Stage 2 verification; host escapes throw `InvalidOperationException` with `Data["ExitCode"] = 3`.
- `SqliteWalPool` (`v0.5.6`) skips redundant `PRAGMA journal_mode = WAL` schema locks after initialization via lock-free `Volatile.Read` while checking the SQLite file's creation timestamp synchronously so deleted/recreated databases re-apply `WAL` cleanly without holding locks across `await OpenAsync`.

### 2.1 Rotating Backups (`RollingFileBackupStore`), VRR Late-Latch (`HighResFramePacer`), Verified HTTPS (`SafeNetworkTransfer`) & Updates (`ReleaseUpdaterClient`)
```csharp
// 1. Atomic rotating backups (.tmp -> Flush(true) -> File.Move + .bak + DefaultMaxBackups = 10)
var backups = new RollingFileBackupStore(Path.Combine(env.DataDirectory, "backups"));
RollingBackupCommitResult commit = backups.WriteAtomicallyWithBackup(
    Path.Combine(env.DataDirectory, "vault.json"), jsonPayload, label: "autosave");
RollingBackupEntry? restored = backups.RestoreLatestBackup(Path.Combine(env.DataDirectory, "vault.json"));

// 2. VRR late-latch wait timing (tracks LastLateLatchWaitMs / AverageLateLatchWaitMs)
using var pacer = new HighResFramePacer(targetFps: 60.0) { LateLatchMarginMs = 1.5 };
pacer.BeginLateLatchFrame();
double waitedMs = pacer.WaitBeforePresent(); // or await pacer.WaitBeforePresentAsync(cancellationToken: ct)

// 3. Verified HTTPS transfer (enforces https://, TLS 1.2/1.3, AllowAutoRedirect = false,
//    MaxRedirects = 5, DNS IP ConnectCallback SSRF guard, SHA-256 + ExpectedSizeBytes)
SafeTransferResult transfer = await SafeNetworkTransfer.DownloadVerifiedAsync(
    new Uri("https://example.com/releases/MyApp-v0.1.0.zip"),
    Path.Combine(env.DataDirectory, "downloads", "MyApp.zip"),
    expectedSha256Hex: expectedSha256,
    expectedSizeBytes: expectedSizeBytes,
    cancellationToken: ct);

// 4. GitHub-first + R2-fallback updater client (supports betaPin resolution)
ReleaseUpdateManifest? updateManifest = ReleaseUpdaterClient.LoadManifest(env.BaseDirectory, typeof(Program).Assembly, "MyApp");
if (updateManifest is not null)
{
    ReleaseUpdateCheckResult check = await ReleaseUpdaterClient.CheckForUpdateAsync(updateManifest, preferredAssetSuffix: "-Portable.zip", ct: ct);
    if (check.IsUpdateAvailable)
    {
        await ReleaseUpdaterClient.DownloadVerifiedUpdateAsync(check, Path.Combine(env.DataDirectory, "updates", "MyApp-Update.zip"), ct: ct);
    }
}
```

---

## 3. NativeAOT Win32, Ownership-Guarded & Dual Online/Offline Installer (`Foundation.Installer v0.5.6`)

### 3.1 Tier 1 — Zero-Boilerplate `StandardInstallerHost`, NativeAOT Win32 UI, Slim Uninstaller & `installer.manifest.json`
`Foundation.Installer` (`v0.5.6`) targets pure Win32 P/Invoke (`IsAotCompatible=true`, `FoundationInstallerJsonContext`, zero `System.Windows.Forms` / `System.Drawing` dependencies). Publishing an Online Setup stub with `-p:PublishAot=true -p:OptimizationPreference=Size -p:StripSymbols=true -p:InvariantGlobalization=true` produces a standalone `win-x64` binary (~7 MB uncompressed, zero `.NET` runtime prerequisites).

Add a ~15-line `Dev\Installer\Program.cs` referencing `Foundation.Installer.dll`, embedding `payload.zip` as `<ProductName>.Installer.Embedded.payload.zip` (plus optional slim `<ProductName>.Installer.Embedded.uninstaller.exe.gz` or `.uninstaller.exe` so Offline Setup does not copy the entire `payload.zip` into `app\<ProductName>-Uninstall.exe`), or configuring `"installerDeliveryKind": "onlineSetup"` with `"payloadDownloadUrl"`, `"payloadFallbackUrls"`, `"betaPin"`, and `"payloadAssets"`. `StandardInstallerHost` automatically merges `installer.manifest.json`, collapses the `"Profile:"` header and radio group cleanly when `_supportedProfiles.Count <= 1` (`ShortcutsCheckBox.Top == 34`), guards `ResolveShortcutDirectories` against stray Start Menu files (auto-removing 0-byte stray files or falling back to `Programs\`), avoids doubling `ProductName` on versioned `umbrellaSubpath` leaves (`vX.Y.Z*`), and uses self-healing `HKCU\Software\StreamItAll\LastInstallBaseDir` suggestions for fresh installs:

```json
{
  "$schema": "https://schemas.local/manifest.v2.json",
  "schemaVersion": 2,
  "manifestType": "installer-manifest",
  "appId": "myapp",
  "version": "0.1.0",
  "updatedAtUtc": "2026-11-28T00:00:00Z",
  "displayName": "My App",
  "publisher": "StreamItAll",
  "studioName": "StreamItAll",
  "suiteId": "MySuite",
  "layoutMode": "single-line",
  "supportedProfiles": ["lightweight", "full"],
  "defaultProfile": "lightweight",
  "installerDeliveryKind": "onlineSetup",
  "payloadDownloadUrl": "https://github.com/Example/MyApp/releases/download/v0.1.0",
  "payloadFallbackUrls": ["https://releases.example.com/myapp/v0.1.0"],
  "betaPin": "optional-pin-token",
  "payloadAssets": [
    {
      "installProfile": "lightweight",
      "fileName": "MyApp-Core-v0.1.0.zip",
      "expectedSizeBytes": 1048576,
      "expectedSha256": "<64-hex-sha256>"
    }
  ]
}
```

*(Note: `"suiteId"` is the canonical manifest field; legacy `"suiteName"` remains supported as a read-only JSON fallback. `"installerDeliveryKind"` defaults to `"offlineSetup"` when omitted, preserving 100% backward compatibility. Single top-level wrapper folders in ZIP payloads are unwrapped automatically via `DetectSingleWrapperPrefix`.)*

```csharp
using Foundation.Installer;

var descriptor = new InstallerAppDescriptor(
    ProductName: "MyApp",
    DisplayName: "My App",
    Publisher: "Stream",
    Version: "0.1.0",
    MainExecutableRelativePath: @"app\MyApp.exe",
    Subtitle: "Portable & Suite-Compatible Installer",
    BrandAccentHex: "#0EA5E9",
    AllowAutoStart: false,
    StudioName: "StreamItAll",
    SuiteName: "MySuite")
{
    InstallerDeliveryKind = InstallerDeliveryKinds.OfflineSetup // or OnlineSetup + PayloadDownloadUrl + PayloadFallbackUrls + BetaPin + PayloadAssets
};

return StandardInstallerHost.Run(descriptor, args, typeof(Program).Assembly);
```

- **Install-Directory Ownership Guard (`ProductMismatch:` / `FOREIGN APP DETECTED`):** Before stopping running processes, backing up, extracting, or deleting files, both Tier 1 (`InstallerEngine.CheckDirectoryOwnership`) and Tier 2 (`InstallerEngineCore` via `CheckForeignOwnershipForTheme`) inspect `install-manifest.json` and `uninstall_manifest.json` (`IsOwnInstallManifest`). If the target directory belongs to another product, setup refuses silent overwrite or uninstall with a `ProductMismatch:` error and highlights a red **`FOREIGN APP DETECTED`** badge in `InstallerWizardForm`. Both uninstaller cleanup and `BuildSelfDeleteTrampolineCommand` strictly target `Descriptor.UninstallerExeName` (never `"*-Uninstall.exe"` wildcards).
- **Dual Online/Offline Setup & `--offline-bundle`:** Online Setup installers filter `PayloadAssets` by `--profile`, download over HTTPS (with `payloadFallbackUrls` mirror fallback and `betaPin` support) with SHA-256 + byte-size verification via `SafeNetworkTransfer.DownloadVerifiedAsync`, extract safe `relativeSubdir` payloads, or verify a local archive/folder passed via `--offline-bundle <zipOrDir>`. Network/offline/hash failures fail closed with actionable guidance pointing to `*-Offline-Setup-v<Ver>.exe`, `*-Portable.zip`, or `--offline-bundle`.
- **Slim Uninstaller Extraction (`TryExtractEmbeddedUninstallerResource`):** Offline Setup looks for embedded `<ProductName>.Installer.Embedded.uninstaller.exe.gz` or `.uninstaller.exe` resources before falling back to copying `selfExe`, keeping installed `app\<ProductName>-Uninstall.exe` binaries small without duplicating `payload.zip`.
- **3-Mode Isolation Precedence:**
  1. **Host Live:** `FOUNDATION_SANDBOX_ROOT` unset and no `--data-dir` / `APP_ISOLATED_TEST=1` -> normal `%LOCALAPPDATA%\Programs\...` + host registry + `.lnk` shortcuts.
  2. **Stage 1 Isolated (`APP_ISOLATED_TEST=1` or host `--data-dir`):** `ShouldSuppressHostMutations == true` -> writes suppressed on host registry/shortcuts; canonical `Programs\` rejected.
  3. **Stage 2 Sandbox (`FOUNDATION_SANDBOX_ROOT` active & `APP_ISOLATED_TEST != 1`):** `IsSandboxActive == true`, `ShouldSuppressHostMutations == false`, `ShouldSuppressLiveHostMutations == true` -> installs into `<SandboxDDrive>\Programs\...`, writes `.lnk` into `<SandboxCDrive>\Users\Sandbox\...` (sanitizing `:` -> ` -` via `WindowsShortcutWriter.SanitizeShortcutFileName`), persists uninstall metadata to `registry.hive.json`, and aborts any host escape with **exit code `3`**.
- **UI & Modals:** The pure Win32 `InstallerWizardForm` renders with `PerMonitorV2` DPI scaling, DWM dark titlebars, `COMCTL32` tooltips/progress, COM `IFileOpenDialog` folder browsing (`Win32FolderPicker`), exposes the `WizardControl`/`WizardColor`/`WizardSize` facade, clamps `_lblTitle` and `_lblSub` left of `_lblModeBadge` while preserving `650x460` / `650x570` `ClientSize`, and suppresses modal prompts when `FOUNDATION_SUPPRESS_MODALS=1`.

### 3.2 Tier 2 — Custom / Low-Level Host (`InstallerThemeOptions` + `InstallerEngineCore`)
Configure `InstallerThemeOptions` (`AllowAutoStart`, `AllowLegacyMigration`, `AvailableProfiles`, `SecondaryPathPicker`, `InstallerDeliveryKind`, `PayloadDownloadUrl`, `PayloadAssets`) and construct `InstallerEngineCore(theme, env)`. Under `IsSandboxActive`, `DetectLegacyFlatInstall()` automatically scans `<SandboxDDrive>\Programs\`, and foreign product directories fail closed via `CheckForeignOwnershipForTheme`.

---

## 4. Clean-Root Launcher, Server, Retro, Sidecars & `Foundation.Automation` (`v0.5.6`)

- **`Foundation.Launcher` (Built-in + Extensible via `RootLauncherOptions`):** Built-in sync flags (`--diagnose`, `--doctor`, `--verify`, `--verify-module`, `--wait`, `--open`), value/path options (`--programs-dir`, `--save-dir`, `--preload-media`, `--backend`, `--automation-port`, `--automation-token`), and non-swallowing optional flags (`--suite-tab`, `--window`, `--browser`) work out of the box; pass `RootLauncherOptions` to append app-specific flags:
  ```csharp
  using Foundation.Launcher;

  var launcherOpts = new RootLauncherOptions(
      ExtraSyncWaitFlags: new[] { "--export-manifest" },
      ExtraValueOptions: new[] { "--profile-id" },
      ExtraPathValueOptions: new[] { "--rom-dir" });

  return RootLauncherHost.Run(@"app\MyApp.exe", "My App", args, launcherOpts);
  ```
- **`Foundation.Server` (`net8.0`):** `TieredAuthEngine.LoadOrCreateSigningKey(env.DataDirectory)`, `FoundationTieredAuthMiddleware`, and `SisterAppStandardEndpoints.MapSuiteManifestEndpoints(...)`.
- **`Foundation.Retro` + `Foundation.Sidecars`:** `ConsoleSystemCatalog.ResolvePlatformId`, `DatTitleNormalizer.ParseFilename`, `SinglePassStreamHasher.HashFileSinglePassAsync`, `HashSidecarService`, `HkLockSentinel.TryAcquireDirectoryHkLock`.
- **`Foundation.Automation` (Opt-In Loopback, WinForms-Free `IAutomationSurfaceTarget` & `GET /v1/snapshot.png`):**
  ```csharp
  using Foundation.Automation;

  // WinForms host (adapts Control -> WinFormsAutomationSurfaceTarget internally)
  using AutomationLoopbackServer? auto = AutomationCliHelper.TryCreateFromArgs(
      appId: "myapp", args: args, rootControl: mainForm, renderProvider: myRaylibProvider);

  // Or WinForms-free custom engine/SDL3 surface implementing IAutomationSurfaceTarget:
  // using AutomationLoopbackServer? auto = AutomationCliHelper.TryCreateForSurface(
  //     appId: "myapp", args: args, surfaceTarget: mySurfaceTarget, renderProvider: myRenderProvider);

  auto?.Start(); // Exposes /v1/{health,tree,pointer,keyboard,snapshot} + in-memory GET /v1/snapshot.png
  ```

---

## 5. React `@foundation/ui` (`v0.5.6`) & Vanilla Web Integration

```tsx
import { GlobalTerminalDrawer, UniversalDataTable, SchemaSettingsRenderer, ModalDialog, StyledTooltip } from "@foundation/ui";
// Direct submodule imports (not in barrel):
import { CoexistenceWarningBanner } from "@foundation/ui/components/CoexistenceWarningBanner";
import { HybridPathPicker } from "@foundation/ui/components/HybridPathPicker";
import { SidebarNavRail } from "@foundation/ui/components/SidebarNavRail";
```

For Vanilla JS / FastAPI / Razor hosts, include `foundation-tokens.css`, `foundation-terminal.css`, and `foundation-terminal.js` from `Dev\src\Foundation.UI\WebAssets\`.

---

## 6. Python `foundation_core` (`v0.5.6`, Pure Stdlib) Quickstart

### 6.1 Subcommand-Safe CLI Flags & `GranularSettingsStore`
`foundation_core` (`dependencies = []`) defaults `build_standard_cli_parser(..., add_help=False)` and `parse_standard_cli_flags(..., add_help=False, default_config_relative=Path("config") / "settings.json")` so downstream subcommands receive `--help` through `cli.extra_args` and `--config` points to `<data_dir>/config/settings.json` (pass `add_help=True` to opt back into top-level help interception). `ThreeFileSettingsStore` remains preserved alongside `GranularSettingsStore` + `SettingsLoadResult`:

```python
import argparse
from foundation_core import (
    GranularSettingsStore,
    JsonHivePlatformRegistry,
    build_standard_cli_parser,
    parse_standard_cli_flags,
    resolve_known_folders,
    resolve_sandbox_environment,
)

# 1. Standard CLI flags + downstream subcommand --help passthrough via extra_args
cli = parse_standard_cli_flags(app_name="my_py_app", app_version="0.5.6")
parent = build_standard_cli_parser(app_name="my_py_app", app_version="0.5.6")
subparser = argparse.ArgumentParser(parents=[parent], add_help=True)
_, _ = subparser.parse_known_args(cli.extra_args)

sandbox = resolve_sandbox_environment()
folders = resolve_known_folders(sandbox)
reg = JsonHivePlatformRegistry.from_sandbox(sandbox) if sandbox.is_sandbox_active else None

# 2. GranularSettingsStore: config/settings.json + settings.<domain>.json + 20-snapshot history + quarantine/
store = GranularSettingsStore(cli.data_dir, max_snapshots=20)
loaded = store.load_or_self_heal(
    "settings.json",
    default_factory=lambda: {"schemaVersion": 2, "theme": "dark"},
    current_schema_version=2,
    migrations={1: lambda old: {**old, "theme": old.get("theme", "dark")}},
    known_keys={"theme"},
)
# Unknown top-level keys stay in loaded.extra without writing raw "_extra"; re-emit via extra=loaded.extra:
store.save_atomic("settings.json", {**loaded.value, "theme": "midnight"}, extra=loaded.extra)
```

### 6.2 `SqliteSchemaMigrationRunner`, `PidSentinelLock`, `RollingLogSink`, `CrashDumper` & Loopback Automation
```python
from pathlib import Path
from foundation_core import (
    AutomationDisabledError,
    AutomationLoopbackServer,
    CrashDumper,
    PidSentinelLock,
    RollingLogSink,
    SqliteSchemaMigrationRunner,
    encode_rgba_png,
    open_wal_sqlite,
    parse_automation_cli_args,
    sanitize_pii_text,
    vacuum_backup_sqlite,
    verify_snapshot_integrity,
)

# 1. SQLite WAL + PRAGMA user_version migrations (BEGIN IMMEDIATE/COMMIT/ROLLBACK) + read-only VACUUM INTO
conn = open_wal_sqlite(cli.data_dir / "app.db")
runner = SqliteSchemaMigrationRunner(conn)  # bridges legacy _foundation_schema_migrations -> user_version
runner.apply_sql_migrations(Path("migrations"))
snap_path = vacuum_backup_sqlite(conn, cli.data_dir / "backups" / "app_snapshot.db")  # verified via ?mode=ro
with open_wal_sqlite(snap_path, read_only=True) as ro_conn:
    verify_snapshot_integrity(snap_path)

# 2. Standard 31 PID-reuse-safe locks (hostName + appId + ±2s start-time check + refresh_heartbeat)
with PidSentinelLock(cli.data_dir, app_id="my_py_app", version="0.5.6", lock_filename=".lock") as lock:
    lock.refresh_heartbeat()
    is_held, envelope = PidSentinelLock.inspect_lock(cli.data_dir / ".lock")

# 3. Structured UTC logs (50-line ring buffer + 30d prune) & Zero-PII camelCase crash JSON (max 20)
logs = RollingLogSink(cli.log_dir, app_version="0.5.6", default_slice="core")
logs.write_line("INFO", sanitize_pii_text("Started cleanly"))
dumper = CrashDumper(cli.log_dir, "my_py_app", "0.5.6", recent_logs_provider=logs.recent_lines)
dumper.install_global_hooks()

# 4. Pure-stdlib 127.0.0.1 automation loopback (ExitCode=2 unless <APP>_TEST_AUTOMATION=1)
try:
    auto_opts = parse_automation_cli_args("my_py_app", cli.extra_args, allowed_snapshot_root=cli.data_dir)
except AutomationDisabledError as ex:
    raise SystemExit(ex.exit_code)  # AUTOMATION_DISABLED_EXIT_CODE == 2

if auto_opts is not None:
    srv = AutomationLoopbackServer(
        auto_opts,
        state_provider=lambda: {"status": "ready"},
        snapshot_provider=lambda _target: encode_rgba_png(320, 180, bytes([18, 22, 30, 255]) * (320 * 180)),
    )
    srv.start()  # Serves /v1/{health,state,snapshot,snapshot.png,shutdown}
```

### 6.3 `foundation_core.secrets` (Windows DPAPI) & `foundation_core.sidecars` (Standard 44)
```python
from pathlib import Path
from foundation_core import (
    DirectoryHkLock,
    DpapiSecretStore,
    HashFilePlacement,
    compute_crc32_hex,
    compute_file_hashes,
    read_hash_sidecar,
    write_archive_hashes_json_atomic,
    write_hash_sidecar_atomic,
)

# 1. Wire-compatible Windows DPAPI secret vault (shares data\secrets.dpapi seamlessly with C# DpapiSecretStore)
vault = DpapiSecretStore(cli.data_dir)
vault.set_secret("apiToken", "secret-value")
token = vault.get_secret("apiToken")

# 2. Standard 44 single-pass file hashing, .hash v1 sidecars, .archive_hashes.json & PID-safe .hklock
rom_dir = Path("roms/snes")
digest = compute_file_hashes(rom_dir / "game.sfc")
sidecar_path = write_hash_sidecar_atomic(
    rom_dir,
    placement=HashFilePlacement.INSIDE_FOLDER,
    entry_paths=[rom_dir / "game.sfc"],
)
doc = read_hash_sidecar(sidecar_path)
write_archive_hashes_json_atomic(rom_dir, {"game.sfc": digest})

with DirectoryHkLock(rom_dir, app_id="my_py_app", version="0.5.6"):
    crc_hex = compute_crc32_hex(rom_dir / "game.sfc")
```

---

## 7. Stage 2 External 4-Drive Sandbox E2E & Live GUI Streaming

Configure `SANDBOX_C_DRIVE`, `SANDBOX_D_DRIVE`, `SANDBOX_REMOVABLE_DRIVE`, `SANDBOX_DISC_DRIVE` (and optional `FOUNDATION_SANDBOX_ROOT`) in `%USERPROFILE%\.gemini\.secrets\.env` or gitignored `Dev\.env` (copy `Dev\.env.example`), then run:

```powershell
python <PROJECTS_ROOT>\Foundation\Dev\tools\sandbox_ctl.py init
python <PROJECTS_ROOT>\Foundation\Dev\tools\run_sandbox_e2e.py `
  --app-id myapp --version 0.1.0 --installer Releases\Latest\MyApp-Setup-v0.1.0.exe `
  --offline-bundle path\to\MyApp-Offline-Bundle.zip --probe-arg --version
```

The runner executes the 12-point Stage 2 sandbox matrix (`S2-01`..`S2-12`) with scrubbed child environments (`FOUNDATION_SUPPRESS_MODALS=1` plus preserved Windows OS keys `SystemDrive`, `ProgramData`, `ProgramFiles*`, `CommonProgramFiles*`, `ALLUSERSPROFILE`, `PUBLIC`), forwards `--offline-bundle` to install/upgrade runs, verifies `hostDiffCount == 0`, and emits a structured JSON receipt under `<SandboxRoot>\runs\`.

- **8.3 Short Paths, Child `USERPROFILE` & Transient Drive Exclusions (`v0.5.4`):** Both C# (`SandboxEnvironmentSeam.TryToLongPath`, `KnownFolderPaths.GetHostFolderPath`) and Python (`sandbox_seam._to_long_path`, `resolve_known_folders`) normalize Windows 8.3 short paths via `GetLongPathNameW` (for example `RUNNER~1` -> `runneradmin`) and unmap redirected child-`USERPROFILE` `Desktop`, `Documents`, and `AppData` paths before host-collision checks, while `sandbox_ctl.py` (`_scan_host_state`) ignores `.tmp.driveupload`, `.tmp.drivedownload`, and OS junk (`desktop.ini`, `Thumbs.db`, `ehthumbs.db`, `.DS_Store`, `._*`).

### 7.1 Live GUI WebSocket Test Viewer Hub (`http://127.0.0.1:5005`) & Legacy VLC Streaming
Stage 2 GUI runs stream live frames and test events to the `exebox-stream` WebSocket Test Viewer Hub (`http://127.0.0.1:5005`) via [`Dev\tools\ExeboxStreamHelpers.ps1`](../tools/ExeboxStreamHelpers.ps1) and [`Dev\tools\exebox_stream.py`](../tools/exebox_stream.py) (preserving 100% backward-compatible VLC `udp://@127.0.0.1:5004` shims, while verifying `GetThreadDesktop()` truthfulness and never auto-launching `Test Viewer.lnk` inside `WinSta0\exebox-*`):

```powershell
. Dev\tools\ExeboxStreamHelpers.ps1
$preflight = Initialize-ExeboxTestViewerPreflight -HubUrl "http://127.0.0.1:5005"
$stream = Start-ExeboxDesktopStream -AppId "myapp" -DesktopName $preflight.DesktopName
Attach-ExeboxAutomationWindow -AutomationPort $port -AutomationToken $token -TargetPid $proc.Id
Send-ExeboxStreamEvent -AppId "myapp" -Step "installer-wizard" -Status "PASS"
# ... drive /v1/pointer, /v1/keyboard, and GET /v1/snapshot.png ...
Detach-ExeboxAutomationWindow
Stop-ExeboxDesktopStream -StreamHandle $stream
```

*(Python suites import `get_thread_desktop_name`, `get_viewer_hub_health`, `is_viewer_active`, `start_exebox_desktop_stream`, `attach_exebox_automation_window`, `send_exebox_stream_event`, `detach_exebox_automation_window`, and `stop_exebox_desktop_stream` from `Dev\tools\exebox_stream.py`. See [Dev/docs/OPERATIONS.E2ECoverage.md](OPERATIONS.E2ECoverage.md) for current Stage 2 surface coverage and performance receipts.)*

---

## 8. Public Documentation Site (`.github/workflows/pages.yml`)

When the repository is public (`github.event.repository.visibility == 'public'`), [`.github/workflows/pages.yml`](../../.github/workflows/pages.yml) renders root Markdown docs and `Dev/docs/*.md` (excluding `plans/`, `reports/`, `tests/`, `archive/`, and `AGENTS.md`) into clean dark/light static HTML with `.nojekyll`, `404.html`, `robots.txt`, and `sitemap.xml` at [https://streamitallgit.github.io/Foundation/](https://streamitallgit.github.io/Foundation/).
