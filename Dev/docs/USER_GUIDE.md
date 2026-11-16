# Foundation (`v0.4.2`) — Consumer User Guide & Integration Recipes

> **Goal:** Copy-paste integration recipes for consuming `Foundation` across C#, React/TypeScript, Vanilla Web, Python, and Stage 2 external 4-drive sandboxes without re-reading SDK source files.
> **Full API Dictionary:** [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md)

---

## 1. Syncing Foundation into a Consumer Project

Consumer repositories (`Housekeeping`, `ShadowLibrary`, `reStation`, `KingsFieldRecomp`, `MediaSync`) reference published binaries in `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\` (or vendored copies under `<Consumer>\Dev\lib\` synced via [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py)):

```powershell
# Verify hub health, pull Latest SDK DLLs/@foundation/ui/foundation_core, and purge retired legacy test artifacts
python <PROJECTS_ROOT>\Foundation\Dev\tools\sync_foundation_sdk.py --sync-consumer <PROJECTS_ROOT>\<ProjectName> --source-version Latest
```

> **Rule Reminder:** Consumer development sessions treat `<PROJECTS_ROOT>\Foundation\` and `<PROJECTS_ROOT>\SharedLibs\Foundation\` as **strictly read-only**. Log upstream SDK gaps in [`Dev\docs\reports\`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/).

---

## 2. Core Startup & 4-Drive Sandbox Wiring (`Foundation.Core`)

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
var dbPool = new SqliteWalPool(Path.Combine(env.DataDirectory, "myapp.db"));
using var lockHandle = PidSafeLockfile.Acquire(Path.Combine(env.DataDirectory, "myapp.lock"), env.AppId, "startup");
```

- Pass `--data-dir`, `--config`, `--log-dir`, or `--port` to override runtime paths; `AppEnvironment.Sandbox` exposes `IsSandboxActive` (process env only; `APP_ISOLATED_TEST=1` always disables Stage 2 sandbox mode so Stage 1 unit tests stay strictly isolated).
- Call `SandboxEnvironmentSeam.AssertSandboxPathIfActive(candidatePath, env.Sandbox)` before filesystem writes when supporting Stage 2 verification; host escapes throw `InvalidOperationException` with `Data["ExitCode"] = 3`.

---

## 3. Drop-In Installer Integration (`Foundation.Installer v0.4.2`)

### 3.1 Tier 1 — Zero-Boilerplate `StandardInstallerHost` (Recommended)
Add a ~15-line `Dev\Installer\Program.cs` referencing `Foundation.Installer.dll` and embedding `payload.zip` as `<ProductName>.Installer.Embedded.payload.zip`:

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
    SuiteName: "MySuite");

return StandardInstallerHost.Run(descriptor, args, typeof(Program).Assembly);
```

- **3-Mode Isolation Precedence:**
  1. **Host Live:** `FOUNDATION_SANDBOX_ROOT` unset and no `--data-dir` / `APP_ISOLATED_TEST=1` -> normal `%LOCALAPPDATA%\Programs\...` + host registry + `.lnk` shortcuts.
  2. **Stage 1 Isolated (`APP_ISOLATED_TEST=1` or host `--data-dir`):** `ShouldSuppressHostMutations == true` -> writes suppressed on host registry/shortcuts; canonical `Programs\` rejected.
  3. **Stage 2 Sandbox (`FOUNDATION_SANDBOX_ROOT` active & `APP_ISOLATED_TEST != 1`):** `IsSandboxActive == true`, `ShouldSuppressHostMutations == false`, `ShouldSuppressLiveHostMutations == true` -> installs into `<SandboxDDrive>\Programs\...`, writes `.lnk` into `<SandboxCDrive>\Users\Sandbox\...` (sanitizing `:` -> ` -` via `WindowsShortcutWriter.SanitizeShortcutFileName`), persists uninstall metadata to `registry.hive.json`, and aborts any host escape with **exit code `3`**.
- **UI & Modals:** `InstallerWizardForm` clamps `_lblTitle` and `_lblSub` with `AutoEllipsis = true` cleanly left of `_lblModeBadge` at DPI-safe heights while preserving `650x460` / `650x570` `ClientSize`, and suppresses modal prompts when `FOUNDATION_SUPPRESS_MODALS=1`.

### 3.2 Tier 2 — Custom / Low-Level Host (`InstallerThemeOptions` + `InstallerEngineCore`)
Configure `InstallerThemeOptions` (`AllowAutoStart`, `AllowLegacyMigration`, `AvailableProfiles`, `SecondaryPathPicker`) and construct `InstallerEngineCore(theme, env)`. Under `IsSandboxActive`, `DetectLegacyFlatInstall()` automatically scans `<SandboxDDrive>\Programs\`.

---

## 4. Clean-Root Launcher, Server, Retro & Sidecars

- **`Foundation.Launcher`:** `return RootLauncherHost.Run(@"app\MyApp.exe", "My App", args);`
- **`Foundation.Server` (`net8.0`):** `TieredAuthEngine.LoadOrCreateSigningKey(env.DataDirectory)`, `FoundationTieredAuthMiddleware`, and `SisterAppStandardEndpoints.MapSuiteManifestEndpoints(...)`.
- **`Foundation.Retro` + `Foundation.Sidecars`:** `ConsoleSystemCatalog.ResolvePlatformId`, `DatTitleNormalizer.ParseFilename`, `SinglePassStreamHasher.HashFileSinglePassAsync`, `HashSidecarService`, `HkLockSentinel.TryAcquireDirectoryHkLock`.

---

## 5. React `@foundation/ui` (`v0.4.2`) & Vanilla Web Integration

```tsx
import { GlobalTerminalDrawer, UniversalDataTable, SchemaSettingsRenderer, ModalDialog, StyledTooltip } from "@foundation/ui";
// Direct submodule imports (not in barrel):
import { CoexistenceWarningBanner } from "@foundation/ui/components/CoexistenceWarningBanner";
import { HybridPathPicker } from "@foundation/ui/components/HybridPathPicker";
import { SidebarNavRail } from "@foundation/ui/components/SidebarNavRail";
```

For Vanilla JS / FastAPI / Razor hosts, include `foundation-tokens.css`, `foundation-terminal.css`, and `foundation-terminal.js` from `Dev\src\Foundation.UI\WebAssets\`.

---

## 6. Python `foundation_core` (`v0.4.2`) Quickstart

```python
from foundation_core import (
    CrashDumper,
    JsonHivePlatformRegistry,
    PidSentinelLock,
    SqliteSchemaMigrationRunner,
    ThreeFileSettingsStore,
    assert_sandbox_path_if_active,
    open_wal_sqlite,
    parse_standard_cli_flags,
    resolve_known_folders,
    resolve_sandbox_environment,
)

cli = parse_standard_cli_flags(app_name="my_py_app", app_version="0.4.2")
sandbox = resolve_sandbox_environment()
folders = resolve_known_folders(sandbox)
reg = JsonHivePlatformRegistry.from_sandbox(sandbox) if sandbox.is_sandbox_active else None
```

---

## 7. Stage 2 External 4-Drive Sandbox E2E (`sandbox_ctl.py` + `run_sandbox_e2e.py`)

Configure `SANDBOX_C_DRIVE`, `SANDBOX_D_DRIVE`, `SANDBOX_REMOVABLE_DRIVE`, `SANDBOX_DISC_DRIVE` (and optional `FOUNDATION_SANDBOX_ROOT`) in `%USERPROFILE%\.gemini\.secrets\.env` or gitignored `Dev\.env` (copy `Dev\.env.example`), then run:

```powershell
python <PROJECTS_ROOT>\Foundation\Dev\tools\sandbox_ctl.py init
python <PROJECTS_ROOT>\Foundation\Dev\tools\run_sandbox_e2e.py `
  --app-id myapp --version 0.1.0 --installer Releases\Latest\MyApp-Setup-v0.1.0.exe `
  --probe-arg --version
```

The runner executes the 12-point `Standard 02 §4` matrix (`S2-01`..`S2-12`) with scrubbed child environments (`FOUNDATION_SUPPRESS_MODALS=1`), verifies `hostDiffCount == 0`, and emits a Standard 18 JSON receipt under `<SandboxRoot>\runs\`.
