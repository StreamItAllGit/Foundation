# Foundation — Operations, Build, Pinning & Maintenance Runbook (`OPERATIONS.md`)

> **Active Baseline Version:** `v0.3.0` (Introducing `Foundation.UI v0.2.0` / `@foundation/ui v0.2.0` Canonical ShadowLibrary Strict Standard Docked Terminal Drawer across React 19 + Vanilla ES Module `foundation-terminal.js` + `foundation-terminal.css`, `sync_foundation_sdk.py` Auto-Sync Pipeline, `Foundation.Launcher.dll v0.1.1`, and `v0.1.0` of `Foundation.Retro.dll`, `Foundation.Sidecars.dll`, `Foundation.Server.dll`, and `foundation_core`) | **Full SemVer (`MAJOR.MINOR.PATCH`)** | **Owner:** `@tessa` ("The Manual Maker") & `@devv` | **Verified By:** `@dean` ([Standard 16 §3.5](file:///<PROJECTS_ROOT>/_DevBible/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md))

This operational runbook covers canonical directory topologies, CLI/MSBuild/environment properties, configuration persistence rules, port/mutex/lockfile coexistence contracts, logging and crash-bundle diagnostics, backup/migration procedures, build/publish/rollback runbooks, sandbox verification recipes, and incident playbooks for **`Foundation`** (`<PROJECTS_ROOT>\Foundation\`).

---

## 1. Canonical Production vs Development vs Sandbox Topology

Unlike end-user applications that deploy a live daemon to `<PROGRAMS_ROOT>\<AppName>\`, `Foundation` is the ecosystem's shared multi-language SDK ([Standard 17 §1.6](file:///<PROJECTS_ROOT>/_DevBible/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md)). Its canonical "production" distribution root is **`<PROJECTS_ROOT>\SharedLibs\Foundation\v<Version>\`** (with **`<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\`** always mirroring the newest verified release), from which individual sister apps sync or pin immutable copies into their own `Dev\lib\` directories via `sync_foundation_sdk.py`.

```text
<PROJECTS_ROOT>\
├── SharedLibs\
│   └── Foundation\                                    # Canonical Shared SDK Distribution Root (Side-by-Side Retention)
│       ├── v0.1.0\ .. v0.2.1\                         # Immutable Historical Releases (Never Overwritten)
│       ├── v0.3.0\                                    # Active v0.3.0 Release Snapshot
│       └── Latest\                                    # Auto-Synced Mirror of Active Release (v0.3.0)
│           ├── Foundation.Core.dll                    # .NET 8 Core Primitives (Sandbox, Crash, WAL, Locks, 6-Pillar Net)
│           ├── Foundation.Retro.dll                   # .NET 8 Retro & Media Engines (v0.1.0: 64 Platforms, Multi-DAT, SoftPatch)
│           ├── Foundation.Sidecars.dll                # .NET 8 Stream Hashing, 7z/Zip & .hash/.hklock Sidecars (v0.1.0)
│           ├── Foundation.Server.dll                  # .NET 8 7-Step Tiered Kestrel Auth & Suite Manifest Endpoints (v0.1.0)
│           ├── Foundation.Launcher.dll                # .NET 8 Clean-Root Launcher Host with Self-Spawn Guard (v0.1.1)
│           ├── Foundation.Installer.dll               # .NET 8-windows Dark WinForms Setup Wizard & <1.5s Backup (v0.2.0)
│           ├── Foundation.UI.dll                      # .NET 8-windows UI Branding, STA Dialogs, WebView2 & Tray Badge (v0.2.0)
│           ├── WebAssets\                             # Dual-Target Web Assets (foundation-terminal.js/css + Web\ @foundation/ui v0.2.0)
│           ├── foundation_core\                       # Python 3.12+ Companion Package (v0.3.0: settings, logging, locks, db, cli)
│           ├── tools\                                 # sync_foundation_sdk.py + Universal Sandbox/Release/Notion Scripts
│           └── sdk-manifest.json                      # Version (0.3.0), UI Version (0.2.0) & Per-File SHA-256 Inventory
│
└── Personal\
    └── Foundation\                                    # Tier A 6-Folder Project Workspace
        ├── active.state                               # Single Lifecycle State File (version: "0.3.0")
        ├── AGENTS.md                                  # <= 40-Line / <= 2 KB Context Anchor
        ├── README.md                                  # Root Documentation Quartet #1
        ├── FEATURES.md                                # Root Documentation Quartet #2
        ├── ROADMAP.md                                 # Root Documentation Quartet #3
        ├── CHANGELOG.md                               # Root Documentation Quartet #4
        ├── Archive\                                   # Historical .7z Source Snapshots
        ├── Builds\
        │   └── v0.3.0\                                # Local Compiled Output Mirror of SharedLibs\Foundation\v0.3.0\
        ├── Dev\
        │   ├── Directory.Build.props                  # Deterministic C# 12 / Zero-PDB Build Properties (v0.3.0)
        │   ├── Foundation.sln                         # Solution Binding Core, Retro, Sidecars, Server, Launcher, Installer, UI, Tests
        │   ├── docs\                                  # 4-Document Manual Shelf (ARCHITECTURE, USER_GUIDE, OPERATIONS, INTERFACES)
        │   ├── src\
        │   │   ├── Foundation.Core\                   # C# Core Primitives
        │   │   ├── Foundation.Retro\                  # C# Retro & Media Engines (v0.1.0)
        │   │   ├── Foundation.Sidecars\               # C# Stream Hashing & Sidecar Coexistence (v0.1.0)
        │   │   ├── Foundation.Server\                 # C# ASP.NET Core Tiered Auth & Suite Endpoints (v0.1.0)
        │   │   ├── Foundation.Launcher\               # C# Clean-Root Launcher Host (v0.1.1)
        │   │   ├── Foundation.Installer\              # C# WinForms Installer Engine (v0.2.0)
        │   │   ├── Foundation.UI\                     # C# Desktop Host + Web\ (@foundation/ui v0.2.0) + WebAssets\ (foundation-terminal.js/css)
        │   │   └── foundation_core\                   # Python 3.12+ Package
        │   ├── tools\
        │   │   └── sync_foundation_sdk.py             # Automated SDK Publisher & Consumer Sync Pipeline
        │   └── tests\
        │       └── Foundation.Tests\                  # xUnit Test Suite (17/17 Passing Tests)
        ├── Installers\                                # SDK Packaging Artifacts
        ├── Releases\                                  # Versioned SDK Release Bundles
        └── Sandbox\                                   # Isolated Test Execution Workspace (app\, data\, logs\)
```

---

## 2. Complete CLI Flags, MSBuild/NuGet/NPM Properties & Environment Variables Reference

### 2.1 Standardized CLI Flags Resolved by [`SandboxEnvironment`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L43-L128)
Every application initializing `new SandboxEnvironment(appId, args)` automatically supports both `--flag value` and `--flag=value` syntax across these canonical switches:

| CLI Flag | Environment Variable Fallbacks (`<APP_ID>` = uppercase slug) | Property on `SandboxEnvironment` | Default Resolution | Operational Behavior |
| :--- | :--- | :--- | :--- | :--- |
| `--data-dir <path>` | `<APP_ID>_DATA_DIR`, `DATA_DIR`, `APP_HOME` | [`DataDirectory`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L30) | `<BaseDirectory>\data` | Sets the canonical data root and marks `IsContainedSandbox = true` when explicitly overridden. |
| `--config <path>` | `<APP_ID>_CONFIG`, `CONFIG_PATH` | [`ConfigFilePath`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L31) | `<DataDirectory>\settings.json` | Overrides the primary JSON configuration file path. |
| `--log-dir <path>` | `<APP_ID>_LOG_DIR`, `LOG_DIR` | [`LogDirectory`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L32) | `<DataDirectory>\logs` (sandbox) or `<BaseDirectory>\logs` (prod) | Sets the root directory for rotating logs and `logs\crashes\`. |
| `--port <int>` | `<APP_ID>_PORT`, `PORT` | [`PortOverride`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L33) | `null` (`1..65535` validated) | Overrides the HTTP listener port (e.g. `15178` in Sandbox). |
| `--test-mode` | `<APP_ID>_TEST_MODE=1`, `TEST_MODE=1` | [`TestMode`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L34), [`IsContainedSandbox`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L41) | `false` | Enables strict fail-closed path containment (`AssertSandboxPathContained`) and suppresses host `HKCU` registry writes. |
| `--dry-run` | `<APP_ID>_DRY_RUN=1` | [`DryRun`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L35) | `false` | Simulates file mutations and installer extraction without writing destructive changes to disk. |
| `--headless` | `<APP_ID>_HEADLESS=1` | [`Headless`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L36) | `false` | Sets `PresentationMode = HeadlessOrTrayOnly` for CI/automated verification runs. |
| `--tray-only` | — | [`TrayOnly`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L37) | `false` | Starts background worker in system tray mode (`PresentationMode = HeadlessOrTrayOnly`). |
| `--managed-by-mother` | `MANAGED_BY_MOTHER=1` | [`ManagedByMother`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L38) | `false` | Signals child worker is supervised by `Mother` (`:5100`), suppressing standalone tray icons. |
| `--suite-tab` | — | [`SuiteTab`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L39) | `false` | Sets `PresentationMode = UiPresentationMode.SuiteTab`. |
| `--window` / `--ui=desktop` | — | [`PresentationMode`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L40) | `UiPresentationMode.DesktopWindow` | Launches dedicated standalone desktop window mode. |
| `--browser` / `--ui=browser` | — | [`PresentationMode`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L40) | `UiPresentationMode.LocalBrowser` | Launches default web browser pointed at `/app/<app-id>/`. |

### 2.2 MSBuild & Packaging Properties ([`Dev\Directory.Build.props`](file:///<PROJECTS_ROOT>/Foundation/Dev/Directory.Build.props#L1-L24))

| Property | Value in `v0.1.0` | Purpose |
| :--- | :--- | :--- |
| `LangVersion` / `Nullable` / `ImplicitUsings` | `12.0` / `enable` / `enable` | Enforces modern C# 12 with strict nullable reference analysis across all projects. |
| `TreatWarningsAsErrors` | `true` | Zero-warning build gate enforced at compile time (`@dean` Gate 2). |
| `Deterministic` & `ContinuousIntegrationBuild` | `true` | Guarantees byte-reproducible builds. |
| `PathMap` | `$(MSBuildThisFileDirectory)=/src/` | Strips local `<PROJECTS_ROOT>\...` and `%USERPROFILE%\..\...` paths from compiler diagnostics and metadata. |
| `DebugType` / `DebugSymbols` (`Release`) | `none` / `false` | Prevents `.pdb` symbol files or developer paths from leaking into `Release` assemblies or installers. |
| `Version` / `AssemblyVersion` / `FileVersion` | `0.1.0` / `0.1.0.0` / `0.1.0.0` | Full 3-part SemVer lockstep across all compiled `.dll` files. |

---

## 3. Configuration & Settings Reference

### 3.1 Granular Settings Files (`data\settings*.json` — [`GranularSettingsManager`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/GranularSettingsManager.cs#L28-L257))
- **Serialization Defaults ([`DefaultJsonOptions`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/GranularSettingsManager.cs#L32-L38)):** `WriteIndented = true`, `PropertyNameCaseInsensitive = true`, `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`, `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`, UTF-8 No-BOM.
- **Atomic Write & Backup Lifecycle ([`SaveAtomically`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/GranularSettingsManager.cs#L124-L189)):**
  1. Serializes to `<targetPath>.<guid>.tmp`.
  2. Copies existing `<targetPath>` to `<targetPath>.bak`.
  3. Atomically moves `.tmp` over `<targetPath>` (retrying up to 3 times with `15ms * attempt` backoff on transient `IOException` / `UnauthorizedAccessException`).
  4. Writes a timestamped snapshot `<stem>_<yyyyMMdd_HHmmss_fff>_<guid6>.json` to `data\config_history\` and prunes snapshots beyond `MaxHistorySnapshots` (`20`).

### 3.2 Encrypted Secret Store (`data\secrets.dpapi` — [`DpapiSecretStore`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/DpapiSecretStore.cs#L19-L161))
- Stores key-value secrets as a JSON dictionary encrypted via `ProtectedData.Protect(plainBytes, EntropyBytes, DataProtectionScope.CurrentUser)` where `EntropyBytes = UTF8("Foundation.Core.DpapiSecretStore.v1")`.
- Written atomically via `<SecretsFilePath>.<guid>.tmp` $\rightarrow$ `File.Move(..., overwrite: true)`.

---

## 4. Ports `:5100`, Mutexes, Lockfiles & Sister-App Coexistence

| Mechanism | Canonical Identifier / Path | Implementation in `Foundation` | Operational Behavior |
| :--- | :--- | :--- | :--- |
| **Shared Suite Gateway Port** | `:5100` (Prod HTTP), `:8500` (Tailscale HTTPS), `:15100` (Sandbox) | [`SandboxEnvironment.PortOverride`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L123-L127) | Consuming apps accept `--port <15000-15999>` in Sandbox to avoid colliding with live production ports (`:5100`, `:5178..5180`, `:8420..8460`). |
| **PID-Safe Directory Lockfiles** | `<TargetDir>\.<appId>_<pid>.hklock` | [`PidSafeLockfile`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PidSafeLockfile.cs#L52-L266) | Default lease TTL is `15 minutes` (`TimeSpan.FromMinutes(15)`). Refreshable via `RefreshHeartbeat()`. Automatically breaks orphan locks if the holding PID has exited or its `Process.StartTime` differs from `processStartTimeUtc` by `> 2.0 seconds`. Cross-host locks (`HostName != Environment.MachineName`) are respected until `expiresAtUtc`. |
| **Windows Job Object Containment** | Win32 Kernel Job Handle (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) | [`JobObjectGuard`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JobObjectGuard.cs#L120-L228) | Attaches spawned child processes (`TryAttachProcess(Process)`) so no child CLI binary survives parent termination. |
| **Mother Suite Manifest Registration** | `<PROGRAMS_ROOT>\Mother\manifests\<appId>.manifest.json` | [`InstallerEngineCore.RegisterSuiteManifestIfPresent`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngineCore.cs#L260-L275) | Copies `app\suite.manifest.json` into `Mother\manifests\` during install/upgrade (or `Sandbox\data\Mother\manifests\` when `Sandbox.IsContainedSandbox == true`). |

---

## 5. Logging, Heartbeats `<= 15s`, Crash Bundles & System Doctor

### 5.1 15-Second Progress Heartbeats ([`HeartbeatLogger`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HeartbeatLogger.cs#L26-L124) & [`emit_heartbeat`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/__init__.py#L40-L47))
- **Hard Ceiling:** `HeartbeatLogger.MaxHeartbeatIntervalSeconds = 15`. Any `heartbeatInterval` passed to the constructor exceeding 15 seconds is automatically clamped down to `15s`.
- **Log Format:** `[HEARTBEAT] <OperationName>: <CompletedUnits>/<TotalUnits> (<PercentComplete:F1>%) | <ElapsedSeconds:F1>s | <CurrentItem>`
- **Terminal Behavior:** When `_enableInPlaceAnsi` is true (default when `!Console.IsOutputRedirected`), writes `\r\x1b[K<formatted>` in-place without scrolling the terminal buffer, emitting a final newline on `Complete()`.

### 5.2 Zero-PII JSON Crash Bundles ([`CrashBundleService`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/CrashBundleService.cs#L70-L197))
- **Location:** `<LogDirectory>\crashes\crash_<appId>_<yyyyMMdd_HHmmss_fff>.json`
- **Triggers:** `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` (which also marks `e.SetObserved()` after writing the dump).
- **Captured Telemetry:** `schemaVersion` (`1`), `appId`, `version`, `crashedAtUtc`, `pid`, `osDescription`, `frameworkDescription`, `workingSetBytes`, `exceptionType`, `exceptionMessage`, `stackTrace`, `innerException`, `recentLogs` (last 100 lines), and `activeJobs`.

---

## 6. Backup, Restore & Schema/SDK Migration

### 6.1 Online Atomic SQLite Hot Backups ([`SqliteWalPool.VacuumIntoAsync`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SqliteWalPool.cs#L80-L114))
1. Stages backup to `<destinationDbPath>.<guid>.tmp` using SQLite's online `VACUUM INTO '<escapedTemp>';` command while the WAL database remains live.
2. Atomically renames `<destinationDbPath>.<guid>.tmp` to `<destinationDbPath>` (`File.Move(..., overwrite: true)`).
3. Cleans up any leftover `.tmp` file inside `finally` if interrupted.

### 6.2 Settings `.bak` & 20-Snapshot Restore
- **Automatic Self-Healing:** On startup, `GranularSettingsManager.LoadOrSelfHeal` automatically restores `<file>.bak` if `<file>` is corrupt.
- **Manual Snapshot Rollback:** To roll back to any of the last 20 historical snapshots in `data\config_history\`, copy the chosen `settings_<timestamp>_<id>.json` file over `data\settings.json` (or pass its content to `SaveAtomically`).

### 6.3 Cross-Workspace SDK Migration Rules ([Standard 17 §1.6](file:///<PROJECTS_ROOT>/_DevBible/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md))
- Every change to `Foundation` must be **100% backward-compatible** (additive properties with defaults, optional trailing parameters).
- Never delete or overwrite existing version folders under `<PROJECTS_ROOT>\SharedLibs\Foundation\v<OldVersion>\`.

---

## 7. Build, Package, Sign, Deploy & Rollback Runbook

### 7.1 Build & Run Unit Tests (`0` Warnings Required)
```powershell
cd <PROJECTS_ROOT>\Foundation\Dev
dotnet build Foundation.sln -c Release
dotnet test Foundation.sln -c Release --no-build --logger "console;verbosity=normal"
```

### 7.2 Automated SDK Publish & Sister-App Consumer Sync (`sync_foundation_sdk.py`)
Building `Foundation.UI.csproj` in `Release` mode automatically invokes the MSBuild target `SyncFoundationWebAndSharedLibs`, which runs [`Dev\tools\sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py#L1-L199) to mirror `Web\**` into `WebAssets\Web\` and publish the full SDK snapshot (`.dll`, `.xml`, `WebAssets\`, `foundation_core\`, `tools\`, and `sdk-manifest.json`) to `<PROJECTS_ROOT>\SharedLibs\Foundation\v0.3.0\`, `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\`, and `<PROJECTS_ROOT>\Foundation\Builds\v0.3.0\app\`.

You can also run `sync_foundation_sdk.py` directly from any terminal or sister-app build script:
```powershell
# 1. Publish Foundation v0.3.0 (Foundation.UI v0.2.0) to SharedLibs\v0.3.0, SharedLibs\Latest, and Builds\v0.3.0\app:
python "<PROJECTS_ROOT>\Foundation\Dev\tools\sync_foundation_sdk.py" `
  --publish --version 0.3.0 --ui-version 0.2.0 --configuration Release

# 2. Pull the latest SDK into any Sister App on build (auto-detects flat Dev\lib\ vs Dev\lib\Foundation\):
python "<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\tools\sync_foundation_sdk.py" `
  --sync-consumer "<PROJECTS_ROOT>\ShadowLibrary" `
  --web-dir "Dev\ShadowLibrary.Web\src\lib\foundation"
```

### 7.3 Rollback Procedure for Consuming Sister Apps
If a sister app experiences a regression after syncing a newer `Foundation` build (e.g. `v0.3.0`):
1. Run `python "<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\tools\sync_foundation_sdk.py" --sync-consumer "<PROJECTS_ROOT>\<SisterApp>" --source-version v0.2.1` to restore the previous known-good assemblies and `WebAssets\`.
2. Revert `"sdkVersion": "0.2.1"` in `<SisterApp>\Dev\app\suite.manifest.json`.
3. Rebuild and verify the sister app in `<SisterApp>\Sandbox\`.

---

## 8. Sandbox Verification Recipe (`--test-mode`)

To verify `Foundation` in strict isolation inside `<PROJECTS_ROOT>\Foundation\Sandbox\`:

```powershell
# 1. Execute the full xUnit verification suite (verifies SandboxEnvironment, PidSafeLockfile,
#    SqliteWalPool, HybridModuleLoader, CrashBundleService, HeartbeatLogger, GranularSettingsManager,
#    DpapiSecretStore, InstallerEngineCore, WindowsShortcutWriter, InstallerWizardForm, SafeNetworkTransfer, ToolResolver)
cd <PROJECTS_ROOT>\Foundation\Sandbox
dotnet test "..\Dev\tests\Foundation.Tests\Foundation.Tests.csproj" -c Release --logger "console;verbosity=detailed"

# 2. Verify Python 3.12+ foundation_core companion module in Sandbox\data\
python -c "
import sys
sys.path.insert(0, r'<PROJECTS_ROOT>\Foundation\Dev\src')
import foundation_core as fc
sandbox_root = r'<PROJECTS_ROOT>\Foundation\Sandbox\data'
p = fc.assert_path_contained(sandbox_root + r'\test.db', sandbox_root)
conn = fc.open_wal_sqlite(p)
mode = conn.execute('PRAGMA journal_mode;').fetchone()[0]
conn.close()
hb = fc.emit_heartbeat('SandboxCheck', 1, 1, 'done')
assert mode.lower() == 'wal', f'Expected wal, got {mode}'
print('\nPython foundation_core v' + fc.SDK_VERSION + ' verified: WAL=' + mode)
"
```

---

## 9. Common Operational Incident Playbooks

### Playbook 1: `IOException: Lockfile '...' is actively held by live process`
- **Symptom:** A scheduled or manual job aborts immediately reporting an active `.hklock` holder.
- **Diagnosis:** Open the `.hklock` file in a text editor to inspect `appId`, `pid`, `processStartTimeUtc`, `operation`, and `expiresAtUtc`. Check `Get-Process -Id <pid>` in PowerShell.
- **Remediation:**
  - If the process is genuinely running a heavy job on that drive, allow it to finish (or stop it gracefully via the app's UI / `POST /api/v1/jobs/stop-all`).
  - If the process has exited, simply re-run the operation—[`PidSafeLockfile.TryAcquire`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PidSafeLockfile.cs#L85-L134) will automatically detect that the PID is dead (or its start time changed) and delete the orphan lockfile.

### Playbook 2: `InvalidDataException: SHA-256 verification failed for '...'`
- **Symptom:** [`SafeNetworkTransfer.DownloadFileAsync`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SafeNetworkTransfer.cs#L145-L160) throws `InvalidDataException` after completing a download.
- **Diagnosis:** Either the upstream mirror returned a corrupted payload (or an HTML error page with HTTP 200), or an older `.partial_<jobId>` staging file contained mismatched bytes from a changed upstream file.
- **Remediation:** `VerifyExpectedHash` automatically deletes the bad `.partial_<jobId>` staging file on mismatch. Verify the expected `SHA-256` digest against the upstream manifest and re-run the transfer.

### Playbook 3: `InvalidDataException: Payload archive exceeds maximum 2 GB bounded extraction limit`
- **Symptom:** [`InstallerEngineCore.InstallOrUpgradeFromZipAsync`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngineCore.cs#L161-L165) aborts during setup extraction.
- **Diagnosis:** The embedded ZIP payload expands to `> 2,147,483,648` bytes (`MaxExpandedPayloadBytes`), triggering the Zip-bomb safety guard.
- **Remediation:** Audit the build packaging script to ensure large runtime data vaults, `.pdb` symbols, or test fixtures were not accidentally bundled inside the application code ZIP.

### Playbook 4: `VerifyInstallation` Returns `false` (`Missing expected installed file`)
- **Symptom:** Post-install verification (`InstallerEngineCore.VerifyInstallation`) fails during `--test-mode` audit.
- **Diagnosis:** Compare `app\install-manifest.json` (`installedRelativeFiles[]`) against the extracted directory tree in `InstallDirectory`. Check if an antivirus scanner quarantined an `.exe` or `.dll` during extraction.
- **Remediation:** Re-run the installer with `--repair` or perform an in-place upgrade (`InstallOrUpgradeFromZipAsync`), which re-extracts all `app\` and `scripts\` files while leaving `data\` and `logs\` intact.
