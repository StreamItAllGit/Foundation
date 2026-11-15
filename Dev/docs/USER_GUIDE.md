# Foundation (`v0.4.0`) — Consumer User Guide & Integration Recipes

> **Goal:** Copy-paste integration recipes for consuming `Foundation` across C#, React/TypeScript, Vanilla Web, and Python projects without re-reading SDK source files.
> **Full API Dictionary:** [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md)

---

## 1. Syncing Foundation into a Consumer Project

Consumer repositories (`Housekeeping`, `ShadowLibrary`, `reStation`, `KingsFieldRecomp`, `MediaSync`) reference published binaries in `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\` (or vendored copies under `<Consumer>\Dev\lib\` synced via [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py)):

```powershell
# Verify hub health and pull Latest SDK DLLs, @foundation/ui, and foundation_core into a consumer repo
python <PROJECTS_ROOT>\Foundation\Dev\tools\sync_foundation_sdk.py --sync-consumer <PROJECTS_ROOT>\<ProjectName> --source-version Latest
```

> **Rule Reminder:** Consumer development sessions treat `<PROJECTS_ROOT>\Foundation\` and `<PROJECTS_ROOT>\SharedLibs\Foundation\` as **strictly read-only**. Log upstream SDK gaps in [`Dev\docs\reports\`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/).

---

## 2. Core Startup Wiring (`Foundation.Core`)

```csharp
using Foundation.Core;

var sandbox = new SandboxEnvironment("myapp", args);
CrashBundleService.RegisterGlobalHandlers(sandbox.LogDirectory, sandbox.AppId, "0.1.0");

var settingsMgr = new GranularSettingsManager(sandbox.DataDirectory);
var outcome = settingsMgr.LoadOrSelfHeal("settings.json", () => new MySettings());

var secrets = new DpapiSecretStore(sandbox.DataDirectory);
var dbPool = new SqliteWalPool(Path.Combine(sandbox.DataDirectory, "myapp.db")); // WAL, busy_timeout=15000, cache_size=-65536
using var lockHandle = PidSafeLockfile.Acquire(
    Path.Combine(sandbox.DataDirectory, "myapp.lock"),
    sandbox.AppId,
    "startup");
```

- Pass `--test-mode` during sandbox testing; `SandboxEnvironment` enforces fail-closed path containment (`AssertPathContained`) and keeps all writes inside `Sandbox\`.

---

## 3. Drop-In Installer Integration (`Foundation.Installer v0.4.0`)

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
    AllowAutoStart: false, // Omit or pass true for background daemons; false hides Startup checkbox & clamps --autostart
    StudioName: "StreamItAll",
    SuiteName: "MySuite");

return StandardInstallerHost.Run(descriptor, args, typeof(Program).Assembly);
```

- Automatically resolves `%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\` by default, serves the Dark WinForms Setup Wizard (`InstallerWizardForm`, with optional `InstallerProfileOption` `Lightweight`/`Full` and `SecondaryPathPicker`), or runs `--silent` / `--test-mode` / `--sandbox-root` / `--visible-test` (3s auto-closing foreground showcase) / `--profile` / `--reengine-dir` / `--suite-dir` / `--migrate-legacy` / `--verify` / `--uninstall` / `--purge-data` from CLI with exit codes `0` / `1` / `2`.

### 3.2 Tier 2 — Custom / Low-Level Host (`InstallerThemeOptions` + `InstallerEngineCore`)
If building a custom host UI, configure `InstallerThemeOptions` (also supporting `AllowAutoStart = false`) and drive `InstallerEngineCore` directly (writes `FoundationInstallManifest` instead of `InstallManifest`). See [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md).

---

## 4. Clean-Root Launcher, Server, Retro & Sidecars

### 4.1 Clean-Root Launcher (`Foundation.Launcher`)
```csharp
using Foundation.Launcher;

// Placed in <PROGRAMS_ROOT>\MyApp\MyApp.exe; forwards CLI console I/O or spawns app\MyApp.exe
return RootLauncherHost.Run(@"app\MyApp.exe", "My App", args);
```

### 4.2 Kestrel Tiered Auth & Sister-App Manifest (`Foundation.Server`)
```csharp
using Foundation.Server;

byte[] hmacKey = TieredAuthEngine.LoadOrCreateSigningKey(sandbox.DataDirectory);
app.Use(async (ctx, next) =>
{
    var eval = TieredAuthEngine.EvaluateRequest(ctx, authSnapshot, hmacKey);
    if (await FoundationTieredAuthMiddleware.WriteUnauthorizedIfBlockedAsync(
        ctx, ctx.Request.Path.Value ?? "/", eval.IsAuthorized, eval.RequiresInitialPasswordSetup, "My App"))
        return;
    await next();
});

var apiV1 = app.MapGroup("/api/v1");
SisterAppStandardEndpoints.MapSuiteManifestEndpoints(app, apiV1, () => mySuiteManifest);
```

### 4.3 Retro Catalog, Single-Pass Hashing & `.hash` / `.hklock` Sidecars (`Foundation.Retro` + `Foundation.Sidecars`)
```csharp
using Foundation.Retro;
using Foundation.Sidecars;

string? platformId = ConsoleSystemCatalog.ResolvePlatformId("PC Engine CD", ".chd"); // "PCEngineCD"
var meta = DatTitleNormalizer.ParseFilename("Game Name (USA) (Rev 1).sfc");
var hashes = await SinglePassStreamHasher.HashFileSinglePassAsync(romPath, ConsoleSystemCatalog.BuildDefaultHeaderSkipRules());
var sidecar = HashSidecarService.ParseHashSidecarLines(File.ReadLines(HashSidecarService.GetHashSidecarPath(romPath)), Path.GetFileName(romPath));
var (acquired, lockPath, reason) = HkLockSentinel.TryAcquireDirectoryHkLock(dir, "myapp", honorHkLocks: true);
```

---

## 5. React `@foundation/ui` (`v0.4.0`) & Vanilla Web Integration

```tsx
// 7 components + terminalDrawerCore available directly from the barrel:
import {
  GlobalTerminalDrawer,
  UniversalDataTable,
  SchemaSettingsRenderer,
  ModalDialog,
  StyledTooltip,
} from "@foundation/ui";

// ⚠️ IMPORTANT: These 3 components are NOT re-exported in index.ts — use direct submodule imports:
import { CoexistenceWarningBanner } from "@foundation/ui/components/CoexistenceWarningBanner";
import { HybridPathPicker } from "@foundation/ui/components/HybridPathPicker";
import { SidebarNavRail } from "@foundation/ui/components/SidebarNavRail";
```

For Vanilla JS / FastAPI / Razor hosts, include `foundation-tokens.css`, `foundation-terminal.css`, and `foundation-terminal.js` from `Dev\src\Foundation.UI\WebAssets\` and initialize `window.FoundationTerminalDrawer`.

---

## 6. Python `foundation_core` (`v0.4.0`) Quickstart

```python
from foundation_core import (
    resolve_sandbox_paths,
    GranularSettingsStore,
    HeartbeatTracker,
    CrashDumper,
    PidSafeLock,
    SqliteWalHelper,
)

paths = resolve_sandbox_paths("my_py_app")
CrashDumper.register_global_handlers(paths.log_dir, "my_py_app", "0.1.0")
store = GranularSettingsStore(paths.data_dir)
loaded = store.load_or_self_heal("settings.json", lambda: {"enabled": True})
db = SqliteWalHelper(paths.data_dir / "app.db")  # busy_timeout = 15000 ms, WAL, cache_size = -65536
```
