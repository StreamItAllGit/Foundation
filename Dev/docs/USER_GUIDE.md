# Foundation — End-User & Developer Integration Guide (`USER_GUIDE.md`)

> **Active Baseline Version:** `v0.3.0` (Introducing `Foundation.UI v0.2.0` / `@foundation/ui v0.2.0` Canonical ShadowLibrary Strict Standard Docked Terminal Drawer across React 19 + Vanilla ES Module `foundation-terminal.js` + `foundation-terminal.css`, `sync_foundation_sdk.py` Auto-Sync Pipeline, `Foundation.Launcher.dll v0.1.1`, and `v0.1.0` of `Foundation.Retro.dll`, `Foundation.Sidecars.dll`, `Foundation.Server.dll`, and `foundation_core`) | **Full SemVer (`MAJOR.MINOR.PATCH`)** | **Owner:** `@tessa` ("The Manual Maker") | **Verified By:** `@dean` ([Standard 16 §3.5](file:///<PROJECTS_ROOT>/_DevBible/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md))

Welcome to the **`Foundation`** User & Integration Guide. This manual explains what `Foundation` does in plain English, how sister applications and standalone utilities consume and customize its shared building blocks, and how to use the standardized user-facing surfaces it powers—from the **650×434 Deep Slate Dark Setup Wizard** to the **Collapsible Left Sidebar (`Ctrl+B`)** and **Canonical ShadowLibrary-Standard Docked Resizable Terminal (`Ctrl+\``)**.

---

## 1. What It Is & Who It's For

**`Foundation`** is the shared, customizable software development kit (SDK) and UI/installer framework underneath our entire desktop and server ecosystem—including **Mother** (`:5100`), **Housekeeping** (`:5178`), **ShadowLibrary** (`:5179`), **Redroom** (`:5180`), **Greenroom** (`:8420`), **StreamEngine** (`:8430`), **uStation** (`:8460`), standalone tools, and games.

### Why `Foundation` Exists
Before `Foundation`, every application had to maintain its own copy of:
- The dark-mode Windows setup and uninstall wizard
- Sandbox path-safety guards (`--test-mode`, `--data-dir`)
- Crash bundle dumpers (`logs\crashes\`) and 15-second progress heartbeats
- SQLite `WAL` database pools and hot backups
- PID-safe directory lockfiles (`.hklock`) and 6-pillar resumable downloads
- The collapsible left sidebar and docked resizable terminal drawer

`Foundation` compiles those proven components **once** into version-stamped packages stored in [`<PROJECTS_ROOT>\SharedLibs\Foundation\v0.3.0\`](file:///<PROJECTS_ROOT>/SharedLibs/Foundation/v0.3.0/) and mirrored to [`<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\`](file:///<PROJECTS_ROOT>/SharedLibs/Foundation/Latest/). Every project either pins a specific version inside its own `Dev\lib\` directory or runs `sync_foundation_sdk.py --sync-consumer <ProjectRoot>` on build to pull the latest SDK snapshot—giving you identical quality, keyboard shortcuts, and crash resilience across every app while letting each app customize its brand colors, icons, and feature toggles in under 30 lines of code.

---

## 2. Installation / SDK Consumption & Upgrade

Because `Foundation` is a shared library and SDK rather than a standalone background app, you consume it by syncing or pinning its compiled binaries, React 19 `@foundation/ui` components, Vanilla ES Modules, and Python `foundation_core` modules from `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\` (or `v<Version>\`) into your project's `Dev\lib\` folder.

### 2.1 Automated Consumer Sync on Build (`sync_foundation_sdk.py --sync-consumer`)
Any sister app (`ShadowLibrary`, `Housekeeping`, `uStation`, `Mother`, `Greenroom`, `Redroom`, `StreamEngine`) can pull the complete latest SDK (`.dll`, `.xml`, `WebAssets\` containing `@foundation/ui v0.2.0` + `foundation-terminal.js` + `foundation-terminal.css`, `foundation_core\`, and `sdk-manifest.json`) in a single command from its `build.bat` or MSBuild target using [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py#L138-L195):
```powershell
# Sync Dev\lib\ (and optionally copy WebAssets into a frontend static/components directory):
python "<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\tools\sync_foundation_sdk.py" `
  --sync-consumer "<PROJECTS_ROOT>\<YourProject>" `
  --web-dir "Dev\<YourProject>.Web\public\foundation"
```
To pin a specific historical release instead of `Latest`, pass `--source-version v0.3.0`.

### 2.2 Consuming `Foundation` in a C# / .NET 8 Project
1. Run `sync_foundation_sdk.py --sync-consumer "<PROJECTS_ROOT>\<YourProject>"` (which populates `Dev\lib\`).
2. Add assembly references in your `.csproj`:
   ```xml
   <ItemGroup>
     <Reference Include="Foundation.Core">
       <HintPath>..\lib\Foundation.Core.dll</HintPath>
     </Reference>
     <Reference Include="Foundation.Retro">
       <HintPath>..\lib\Foundation.Retro.dll</HintPath>
     </Reference>
     <Reference Include="Foundation.Sidecars">
       <HintPath>..\lib\Foundation.Sidecars.dll</HintPath>
     </Reference>
     <Reference Include="Foundation.Server">
       <HintPath>..\lib\Foundation.Server.dll</HintPath>
     </Reference>
     <Reference Include="Foundation.Launcher">
       <HintPath>..\lib\Foundation.Launcher.dll</HintPath>
     </Reference>
     <Reference Include="Foundation.Installer">
       <HintPath>..\lib\Foundation.Installer.dll</HintPath>
     </Reference>
     <Reference Include="Foundation.UI">
       <HintPath>..\lib\Foundation.UI.dll</HintPath>
     </Reference>
   </ItemGroup>
   ```
3. Record `"sdkVersion": "0.3.0"` inside your application's `app\suite.manifest.json`.

### 2.3 Consuming `GlobalTerminalDrawer` in a React 19 Application (`@foundation/ui v0.2.0`)
React 19 + TypeScript frontends (`ShadowLibrary`, `Housekeeping`, `Mother`) import [`GlobalTerminalDrawer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Web/components/GlobalTerminalDrawer.tsx#L57-L291), [`TerminalToolbarAndFilters`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Web/components/TerminalToolbarAndFilters.tsx#L64-L263), and pure log/height helpers from `@foundation/ui` ([`Web/index.ts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Web/index.ts#L1-L52)) alongside [`foundation-terminal.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.css#L1-L112):
```tsx
import React, { useState } from 'react';
import {
  GlobalTerminalDrawer,
  coalesceTerminalLogBatch,
  type TerminalActiveJobDto,
  type TerminalLogLine,
} from '@foundation/ui';
import '@foundation/ui/WebAssets/foundation-terminal.css';

export function AppShell() {
  const [logs, setLogs] = useState<TerminalLogLine[]>([]);
  const [activeJob, setActiveJob] = useState<TerminalActiveJobDto | null>(null);

  return (
    <div className="app-shell">
      <div className="app-body-column">
        <main className="app-main-pane">{/* Primary Page Content */}</main>
        <GlobalTerminalDrawer
          jobStatus={activeJob}
          logs={logs}
          connected={true}
          storagePrefix="sl-terminal"
          onClearTerminal={() => setLogs([])}
          onCancelCurrentJob={() => fetch('/api/v1/jobs/cancel-current', { method: 'POST' })}
          onCancelAllJobs={() => fetch('/api/v1/jobs/cancel-all', { method: 'POST' })}
        />
      </div>
    </div>
  );
}
```

### 2.4 Consuming `mountFoundationTerminalDrawer` in Vanilla JS / FastAPI / Kestrel Apps (`foundation-terminal.js`)
Server-rendered or Vanilla JS frontends (`Redroom`, `Greenroom`, `StreamEngine`, `uStation`) include [`foundation-terminal.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.css#L1-L112) and import [`mountFoundationTerminalDrawer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.js#L96-L297) from `foundation-terminal.js` (or access `window.FoundationTerminalDrawer`):
```html
<link rel="stylesheet" href="/static/foundation/foundation-terminal.css" />
<div id="terminal-mount"></div>

<script type="module">
  import { mountFoundationTerminalDrawer } from '/static/foundation/foundation-terminal.js';

  const terminal = mountFoundationTerminalDrawer({
    container: '#terminal-mount',
    storagePrefix: 'sl-terminal',
    connected: true,
    verbosity: 'Verbose',
    historyLimit: 250,
    onClearTerminal: async () => { await fetch('/api/v1/logs/clear', { method: 'POST' }); },
    onCancelCurrentJob: async () => { await fetch('/api/v1/jobs/cancel-current', { method: 'POST' }); },
    onCancelAllJobs: async () => { await fetch('/api/v1/jobs/cancel-all', { method: 'POST' }); },
  });

  // Push real-time log batches & job status updates from SSE / WebSocket / SignalR:
  terminal.appendLogs([
    { seq: 1, timestampUtc: new Date().toISOString(), level: 'JOB', message: 'Started vault verification' },
    { seq: 2, timestampUtc: new Date().toISOString(), level: 'HEARTBEAT', message: '[HEARTBEAT] Verify: 42/100 (42.0%)' }
  ]);
  terminal.setJobStatus({ isRunning: true, jobName: 'Vault Verify', progressPercent: 42, queuedCount: 2 });
</script>
```

### 2.5 Consuming `foundation_core` in a Python 3.12+ Project
For Python sister applications (`Greenroom`, `Redroom`, `StreamEngine`), install or reference [`foundation_core`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/__init__.py#L1-L47) (`settings.py`, `logging.py`, `crash_dumper.py`, `locks.py`, `db.py`, `cli.py`) from `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\foundation_core\` (or `<YourProject>\Dev\lib\foundation_core\` after running `--sync-consumer`):
```powershell
pip install --no-deps -e "<PROJECTS_ROOT>\Foundation\Dev\src\foundation_core"
```

### 2.6 Side-by-Side Upgrades & Zero-Breakage Rollback
- **Upgrading to a Newer `Foundation` Version:** Run `python sync_foundation_sdk.py --sync-consumer <YourProject>`, update `"sdkVersion": "0.3.0"`, and run your test suite.
- **Instant Rollback:** Because older versions in `<PROJECTS_ROOT>\SharedLibs\Foundation\` (`v0.1.0`, `v0.1.1`, `v0.1.2`, `v0.2.0`, `v0.2.1`, `v0.3.0`) are **never** overwritten or deleted, rolling back a consuming app takes a single `--source-version v0.2.1` sync command.

---

## 3. First-Run Setup / Integration Quickstart

### 3.1 Bootstrapping a Safe Application Host in 15 Lines of C#
Using [`SandboxEnvironment`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L26-L128), [`CrashBundleService`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/CrashBundleService.cs#L70-L110), and [`GranularSettingsManager`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/GranularSettingsManager.cs#L28-L122), any application gets full Dev Bible CLI flag parsing, fail-closed `--test-mode` sandbox containment, crash dumps, and self-healing JSON settings out of the box:

```csharp
using Foundation.Core;

var sandbox = new SandboxEnvironment(appId: "shadowlibrary", args: args);
CrashBundleService.RegisterGlobalHandlers(
    logDir: sandbox.LogDirectory,
    appId: sandbox.AppId,
    version: "1.0.0-rc.14");

var settingsMgr = new GranularSettingsManager(sandbox.DataDirectory);
SettingsLoadOutcome<AppSettings> outcome = settingsMgr.LoadOrSelfHeal(
    "settings.json",
    () => new AppSettings());
```

### 3.2 Launching a Custom-Branded Dark Setup Wizard in 20 Lines of C#
Using [`InstallerThemeOptions`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerThemeOptions.cs#L25-L68), [`InstallerEngineCore`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngineCore.cs#L46-L62), and [`InstallerWizardForm`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerWizardForm.cs#L20-L187), any app can spin up the `650×434` Deep Slate Dark Setup/Upgrade/Uninstall Wizard with its own brand accent color:

```csharp
using System.Windows.Forms;
using Foundation.Core;
using Foundation.Installer;

var sandbox = new SandboxEnvironment("shadowlibrary", args);
var theme = new InstallerThemeOptions
{
    AppId = "shadowlibrary",
    DisplayName = "ShadowLibrary",
    Version = "1.0.0-rc.14",
    Subtitle = "Redump & No-Intro ROM Vault Archiver",
    BrandAccentHex = "#6366F1", // Shadow Indigo
    DefaultInstallDir = @"<PROGRAMS_ROOT>\ShadowLibrary"
};

var engine = new InstallerEngineCore(theme, sandbox);
using var wizard = new InstallerWizardForm(engine, isUninstallMode: args.Contains("--uninstall"));
Application.Run(wizard);
return wizard.ExitCode;
```

---

## 4. Dashboard / Component & SDK Surface Tour

`Foundation` provides four primary user- and developer-facing component surfaces across the ecosystem:

### 4.1 Surface 1: Single-Surface Dark WinForms Setup & Uninstall Wizard ([`InstallerWizardForm`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerWizardForm.cs#L20-L266))
Every installer built on `Foundation.Installer.dll` presents a unified, zero-clutter single-window dark UI (`#0E0E10` backdrop, `#18181B` cards, Windows 11 DWM immersive dark titlebar):

| Wizard Control / Region | Dimensions / Location | Plain-English Behavior |
| :--- | :--- | :--- |
| **Header Banner & Mode Pill** | Top `650×76` panel (`_headerPanel`) | Displays `<DisplayName> — Setup Wizard`, version/subtitle, and an automatic mode pill: **`CLEAN INSTALL`** (Brand Accent), **`UPGRADE DETECTED`** (`#10B981` Emerald when an existing install is found), or **`UNINSTALL MODE`** (`#EF4444` Crimson). |
| **Destination Folder Row** | `y = 90..141` (`_txtInstallDir` + `Change...`) | Defaults to `<PROGRAMS_ROOT>\<AppName>` (or `Sandbox\data\installed-app` in `--test-mode`). Typing or browsing immediately re-evaluates whether the target folder is a clean install or an in-place upgrade. |
| **Options Card** | `606×128` card (`_optionsPanel`) | Checkboxes for: (1) *Create Desktop and Start Menu shortcuts*, (2) *Start `<AppName>` automatically with Windows*, (3) *Launch `<AppName>` when setup finishes*, and (in Uninstall Mode only) a red warning checkbox: *Delete all user `data\`, `logs\`, and databases (destructive)*. |
| **Progress Bar & Details Toggle** | `y = 290..340` (`_progressBar` + `Show Details ▾`) | Shows real-time extraction status and percentage (`0–100%`). Clicking **`Show Details ▾`** expands the window height from **`650×434`** to **`650×544`** to reveal the timestamped `Consolas` extraction log (`_txtLog`). |
| **Action Footer** | Bottom `650×64` bar (`Cancel` + `Install`/`Upgrade`/`Uninstall`) | Primary CTA button automatically switches label to **`Finish`** (`#10B981` Emerald) once installation or upgrade completes. |

### 4.2 Surface 2: Collapsible Left Sidebar & Canonical ShadowLibrary-Standard Docked Terminal (`Foundation.UI v0.2.0`)
Defined in [`GlobalTerminalDrawer.tsx`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Web/components/GlobalTerminalDrawer.tsx#L1-L292), [`TerminalToolbarAndFilters.tsx`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Web/components/TerminalToolbarAndFilters.tsx#L1-L263), [`terminalDrawerCore.ts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/Web/components/terminalDrawerCore.ts#L1-L163), [`foundation-terminal.js`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.js#L1-L306), and [`foundation-terminal.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.css#L1-L112):
- **Full-Height Left Sidebar Rail (`.sidebar-nav-rail`):** Spans `100vh` on the left edge (`220px` expanded, `56px` icon-only collapsed when pressing **`Ctrl+B`**). Nothing ever overlays the sidebar rail.
- **Right-Hand Body Column (`.app-body-column`):** Holds the scrollable main content viewport (`.app-main-pane`, minimum reserve height `165px` desktop / `120px` mobile landscape) above the **Docked Resizable Terminal Drawer (`.sl-terminal-docked.fn-terminal-docked`)**.
- **6px Pointer-Capture Sash & `S` / `M` / `L` Presets (`Ctrl+\``):** Toggled via **`Ctrl+\``**. Dragging `.fn-terminal-resize-handle` (`role="separator"`, keyboard `ArrowUp`/`ArrowDown`/`PageUp`/`PageDown`/`Home`/`End`, double-click reset to `M`) or clicking **`S` (`25%`) / `M` (`40%`) / `L` (`58%`)** resizes the terminal between `168px` (`140px` on mobile landscape) and `min(600px, innerHeight - 165px)`.
- **Collapsible Filters Toolbar (`⚙ Filters` on Desktop / `⚙ Flt` on Mobile):** Filters by `Verbosity` (`Quiet`, `Normal`, `Verbose`, `Trace`), `History` cap (`100`, `250`, `500`, `1000` lines), real-time search query, `Wrap lines`, and `Auto-scroll` (`ResizeObserver` bottom latch that automatically disengages when scrolling up `> 35px`).
- **In-Place Progress Coalescing & Split Job Cancellation:** Consecutive `[HEARTBEAT]`, `[SQLite Commit]`, `Overall: ... Speed:`, `Hashing ... MB)...`, and `(N/M)` progress lines replace the previous progress line in place without flooding history, and active jobs expose split **`⏹ Stop Current Job`** vs **`⏹ Stop All Jobs (N)`** confirmation modals.
- **2-Line Styled Tooltips (`.foundation-tooltip` / `StyledTooltip`):** Appears after `250ms` hover (`max-width: 320px`), displaying a bold Title Case header (`+ [Hotkey]`) on Line 1 and a concise Sentence-case explanation on Line 2.

### 4.3 Surface 3: Declarative Module Settings & Universal Data Tables
- **[`SchemaSettingDescriptor`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/IFoundationModule.cs#L20-L31):** Each feature module declares its own settings (`Key`, `Label`, `InputType`, `DefaultValue`, `TooltipTitle`, `TooltipBody`, `MinValue`, `MaxValue`, `AllowedOptions`) so the UI renders settings forms automatically without manual HTML edits.
- **[`TableColumnSchema`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/ShellBrandingOptions.cs#L14-L20):** Binds Standard 18 `camelCase` JSON keys to `Title Case` column headers, `tabular-nums` alignment (`IsNumericTabular`), sorting (`IsSortable`), and status badge coloring.

### 4.4 Surface 4: Resilience, Storage & Concurrency Primitives (`Foundation.Core`)
- **[`SqliteWalPool`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SqliteWalPool.cs#L18-L115):** Opens SQLite connections with `WAL` mode enabled and performs online atomic `VACUUM INTO` backups.
- **[`PidSafeLockfile`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PidSafeLockfile.cs#L52-L266):** Creates `.hklock` files that verify both OS PID and process start time (`processStartTimeUtc`), automatically breaking stale locks from crashed processes.
- **[`SafeNetworkTransfer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SafeNetworkTransfer.cs#L50-L255):** Downloads files with `1.2x + 512 MB` free-space preflight, HTTP `Range` resume on `.partial_<jobId>`, token-bucket bandwidth throttling, and dual `SHA-256`/`SHA-1` verification.

---

## 5. Everyday Workflows — Fleet Supervision, Job Arbitration, Shared UI/Backend Primitives

### Workflow 1: Writing & Saving Settings with 20-Snapshot Rollback and Encrypted Secrets
Never store Discord webhooks or API tokens inside plaintext `settings.json`. Use [`GranularSettingsManager`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/GranularSettingsManager.cs#L28-L202) for settings and [`DpapiSecretStore`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/DpapiSecretStore.cs#L19-L93) for secrets:
```csharp
var settingsMgr = new GranularSettingsManager(sandbox.DataDirectory, maxHistorySnapshots: 20);
settingsMgr.SaveAtomically("settings.json", currentSettings); // Creates .bak + snapshot in data\config_history\

// List up to 20 historical snapshots for Ctrl+S rollback UI:
IReadOnlyList<string> snapshots = settingsMgr.GetHistorySnapshots("settings.json");

// Store sensitive Discord webhook encrypted at rest in data\secrets.dpapi:
var secrets = new DpapiSecretStore(sandbox.DataDirectory);
secrets.SetSecret("discord_webhook_url", "https://discord.com/api/webhooks/...");
```

### Workflow 2: Coordinating Shared Drive Access Between Sister Apps via `PidSafeLockfile`
Before a job in `Housekeeping`, `ShadowLibrary`, `Redroom`, `StreamEngine`, or `uStation` mutates a shared vault directory, acquire a PID-safe `.hklock`:
```csharp
string lockPath = Path.Combine(vaultDirectory, $".{sandbox.AppId}_{Environment.ProcessId}.hklock");
if (!PidSafeLockfile.TryAcquire(lockPath, sandbox.AppId, "chd-conversion", out PidSafeLockfile? lockHandle, out PidSafeLockEnvelope? holder))
{
    Console.WriteLine($"Deferring: drive locked by {holder?.AppId} (PID {holder?.Pid}, op: {holder?.Operation})");
    return;
}
using (lockHandle)
{
    // Perform archive/media operation; call lockHandle.RefreshHeartbeat() on long loops
}
```

### Workflow 3: Implementing `Stop Current Job` vs `Stop All Jobs` + Child Tool Containment
Use [`DualJobCancellationCoordinator`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JobObjectGuard.cs#L18-L114) and [`JobObjectGuard`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JobObjectGuard.cs#L120-L228) so child CLI tools never become zombie processes:
```csharp
using var cancelCoord = new DualJobCancellationCoordinator();
using var jobGuard = new JobObjectGuard();

CancellationToken jobToken = cancelCoord.BeginJobScope("job-42", hostShutdownToken);
using Process child = Process.Start(psi)!;
jobGuard.TryAttachProcess(child); // Kernel kills child automatically if parent crashes

// When user clicks "Stop Current Job" in UI:
cancelCoord.StopCurrentJob(); // Cancels job-42; next queued job still proceeds

// When user clicks "🛑 Stop All":
cancelCoord.StopAllJobs(); // Cancels job-42 AND sets IsAllJobsStopRequested = true
```

---

## 6. Notifications, Tray/Daemon Controls & Scheduling

While `Foundation` does not run its own tray icon or background scheduler, it provides the exact flags, heartbeats, and manifest hooks that allow sister apps to plug into **Mother (`:5100`)**:

1. **4 UI Presentation Modes & `--managed-by-mother` ([`SandboxEnvironment.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L64-L92)):**
   - Passing `--managed-by-mother` (or `MANAGED_BY_MOTHER=1`), `--headless`, or `--tray-only` sets `PresentationMode = UiPresentationMode.HeadlessOrTrayOnly`, signaling the child sister app to suppress its own standalone system tray icon because Mother's master tray sentinel is supervising it.
   - Passing `--suite-tab` sets `UiPresentationMode.SuiteTab`; `--window` sets `UiPresentationMode.DesktopWindow`; `--browser` sets `UiPresentationMode.LocalBrowser`.
2. **Automatic Mother Suite Manifest Registration ([`InstallerEngineCore.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/InstallerEngineCore.cs#L260-L275)):**
   - Whenever an installer powered by `Foundation.Installer.dll` extracts a payload containing `app\suite.manifest.json`, `RegisterSuiteManifestIfPresent` automatically copies it to `<PROGRAMS_ROOT>\Mother\manifests\<appId>.manifest.json` so Mother discovers the app and its `schedules[]` immediately.
3. **15-Second Progress Heartbeats ([`HeartbeatLogger.cs`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HeartbeatLogger.cs#L26-L124) & [`emit_heartbeat`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/__init__.py#L40-L47)):**
   - Long-running scans and transfers call `ReportProgress(completed, total, currentItem)`, which emits `[HEARTBEAT] <Op>: <cur>/<total> (<pct>%) | <elapsed>s | <item>` at least once every 15 seconds and updates interactive consoles in-place with `\r\x1b[K`.

---

## 7. Troubleshooting & FAQ

| Question / Symptom | Root Cause | Solution / Remediation |
| :--- | :--- | :--- |
| **`InvalidOperationException: Fail-Closed Sandbox Violation: Path '...' escapes allowed root '...'`** | Code running in `--test-mode` (or calling `AssertSandboxPathContained`) attempted to read or write a path outside `Sandbox\data\`. | Ensure all test fixtures, mock programs, and output paths pass through `sandbox.DataDirectory` rather than hardcoded `<PROGRAMS_ROOT>\...` or `%USERPROFILE%\..\...` paths. |
| **`IOException: Lockfile '...' is actively held by live process '...'`** | Another sister app is currently running an exclusive job on that directory with a live PID and unexpired lease. | Wait for the active job to finish, or call `/api/v1/scheduler/defer` if orchestrating scheduled tasks. If the process crashed, `PidSafeLockfile` will automatically break the orphan lock as soon as the PID disappears. |
| **Settings changes reverted after an unexpected power outage** | `data\settings.json` was left corrupted or half-written during a sudden host power loss. | Working as designed: [`GranularSettingsManager.LoadOrSelfHeal`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/GranularSettingsManager.cs#L60-L122) detected the corrupt JSON and automatically restored the last valid `.bak` copy (`HealedFromBackup == true`). Check `SettingsLoadOutcome.DiagnosticWarning` in your logs. |
| **`DpapiSecretStore.GetSecret(...)` returns `null` after copying `data\secrets.dpapi` to another PC** | `DpapiSecretStore` encrypts secrets with `DataProtectionScope.CurrentUser`, which is cryptographically bound to your Windows user account on that machine. | Re-enter the Discord webhook or API token once in the app's Settings UI on the new machine so `SetSecret` re-encrypts `data\secrets.dpapi` under the new machine's DPAPI key. |
| **External tool (`chdman`, `rclone`) shows `MissingFallback`** | [`ToolResolver.ResolveTool`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/ToolResolver.cs#L35-L162) checked explicit settings override, `app\tools\`, and `PATH`, and did not find the executable. | Place the binary inside `<AppRoot>\app\tools\` or configure its full path in `data\settings.json`. |

---

## 8. Plain-Language Glossary

| Term | Plain-English Definition |
| :--- | :--- |
| **Full SemVer (`MAJOR.MINOR.PATCH`)** | Suffix-free 3-number version format (`v0.1.0`, `v0.1.1`, `v0.2.0`) used by `Foundation` and `Mother` so sister apps can check version compatibility at a glance without parsing `-rc.<n>` suffixes. |
| **Pinned SDK (`Dev\lib\`)** | A local copy of a specific `Foundation` release stored directly inside an application's `Dev\lib\` folder so that app always builds reproducibly offline. |
| **`IFoundationModule` (Hybrid Slice)** | The shared C# plug-in interface implemented by both lightweight in-app feature folders (`ModuleType.FolderSlice`) and external `.dll` plugins (`ModuleType.DllModule`). |
| **`PidSafeLockfile` (`.hklock`)** | A JSON lockfile placed in a shared folder during disk-heavy jobs that records both the process ID (`pid`) and exact process start timestamp (`processStartTimeUtc`), preventing deadlocks if a process crashes. |
| **DPAPI (`secrets.dpapi`)** | Windows Data Protection API encryption that locks sensitive tokens (like Discord webhooks) to your Windows user login so they never sit in plaintext inside `settings.json`. |
| **SQLite `WAL` Mode** | Write-Ahead Logging mode in SQLite (configured automatically by `SqliteWalPool`) that allows the UI to read databases instantly while background jobs are writing. |
| **`VACUUM INTO`** | An online SQLite backup command wrapped by `SqliteWalPool.VacuumIntoAsync` that creates a clean, defragmented copy of a live database without stopping the app. |
| **Windows Job Object (`JobObjectGuard`)** | An OS kernel handle (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) that guarantees external helper tools (`7z.exe`, `ffmpeg.exe`) are closed automatically if the parent app closes or crashes. |
