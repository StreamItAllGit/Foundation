# Foundation (`v0.4.0`) — Operations, Build & CLI Runbook

> **Audience:** Engineers maintaining `Foundation` itself or running `Dev\tools\*.py` / `StandardInstallerHost` CLIs.
> **Full Symbol Reference:** [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md)

---

## 1. Build & Verification (`Foundation.sln`)

| Command | Purpose | Notes |
|---|---|---|
| `dotnet test <PROJECTS_ROOT>\Foundation\Dev\Foundation.sln -c Release /warnaserror` | Compiles all assemblies + runs all `33` `[Fact]` tests across `6` files | Zero-warning build (`PublishFoundationSdk` defaults to `false`) |
| `dotnet test <PROJECTS_ROOT>\Foundation\Dev\Foundation.sln -c Release --no-build` | Runs `33` tests against already-built binaries without re-triggering MSBuild side effects | Requires prior `-c Release` build |

### ⚠️ MSBuild Post-Build Side Effect Note (`Foundation.UI.csproj`)
Building `Foundation.UI.csproj` with `-p:PublishFoundationSdk=true` copies `Web\` into `WebAssets\` and invokes `sync_foundation_sdk.py` toward `<PROJECTS_ROOT>\SharedLibs\Foundation\`. Normal `dotnet build` / `dotnet test` keeps `PublishFoundationSdk=false` to avoid unintended hub mutations.

---

## 2. Universal Dev Tooling CLIs (`Dev\tools\`)

### 2.1 `sync_foundation_sdk.py` (`Dev\tools\sync_foundation_sdk.py`)
| Flag | Default / Type | Description |
|---|---|---|
| `--publish` | bool | Copies compiled Release binaries, `@foundation/ui` (`0.4.0`), and `foundation_core` (`0.4.0`) to `<PROJECTS_ROOT>\SharedLibs\Foundation\v<version>`, `Latest`, and `Builds\v<version>\app`, regenerating `sdk-manifest.json` (`52` files) |
| `--sync-consumer` | `<ProjectRoot>` | Copies pinned SDK DLLs, `WebAssets`, and `foundation_core` into a consumer project's `Dev\lib\` tree |
| `--source-version` | `Latest` | Source SDK version folder inside `SharedLibs\Foundation\` when syncing a consumer (`Latest` or `v0.4.0`) |
| `--web-dir` | Path (optional) | Optional consumer frontend directory target for `WebAssets` |
| `--version` | `0.4.0` | Foundation SDK SemVer |
| `--ui-version` | `0.4.0` | `Foundation.UI` / `@foundation/ui` SemVer |
| `--configuration` | `Release` | Build configuration (`Release`/`Debug`) |

### 2.2 `build_release_universal.py` (`Dev\tools\build_release_universal.py`)
| Argument / Flag | Required? | Description |
|---|---|---|
| `release_dir` | Positional (Yes) | Path to `Releases\v<Version>` or `Releases\Latest` to validate strictly as a 6-file bundle |
| `--regen-sums` | Optional | Regenerates `SHA256SUMS.txt` before validating the 6-file release bundle |

### 2.3 `verify_sandbox_universal.py` (`Dev\tools\verify_sandbox_universal.py`)
| Flag | Default / Required? | Description |
|---|---|---|
| `--base-url` | `http://127.0.0.1:5179` | Sandbox server base URL to probe |
| `--endpoint` | Optional (repeatable) | Endpoint path(s) to probe (defaults: `/api/v1/health`, `/api/v1/system/dependencies`, `/suite.manifest.json`) |
| `--output-json` | Optional Path | Writes structured HTTP status summary JSON |

---

## 3. `StandardInstallerHost` CLI Reference (`Foundation.Installer v0.4.0`)

When interactive GUI wizard mode is bypassed (`--silent`, `--headless`, `--test-mode`, `--visible-test`, `--dry-run`, `--verify`, or `--install-dir`), [`StandardInstallerHost`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/StandardInstallerHost.cs) accepts:

| Switch / Alias | Argument | Behavior |
|---|---|---|
| `--install-dir <dir>` | Path | Target install directory (defaults to `%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\`, or auto-contains inside sibling `<SandboxRoot>\installed` / `%TEMP%` under `--test-mode` while failing closed on host escapes) |
| `--sandbox-root <dir>` | Path | Explicit sibling sandbox root for `--test-mode` containment |
| `--profile <id>` | String | Selects installer profile (`Lightweight` / `Full`) when `AvailableProfiles` are configured |
| `--reengine-dir <dir>` · `--suite-dir <dir>` | Path | Sets secondary path picker target when `SecondaryPathPicker` is configured |
| `--migrate-legacy` | Flag | Opts in to migrating a detected legacy flat `<PROGRAMS_ROOT>\<App>` installation |
| `--payload <path>` | Path | External `.payload.zip` override instead of embedded assembly resource |
| `--data-dir <dir>` · `--config <path>` · `--log-dir <dir>` | Path | Standard 02 containment & pre-seed override (`InstallerConfigOverride`) |
| `--silent` / `--quiet` | Flag | Suppresses banner & GUI wizard (`non-interactive`) |
| `--headless` · `--test-mode` · `--dry-run` | Flag | Headless execution, sandboxed containment, or no-op dry run |
| `--visible-test` | Flag | Launches a `~3000 ms` foreground WinForms dry-preview showcase (`VISIBLE TEST` badge, zero prompts, exits `0`) |
| `--verify` | Flag | Verifies payload SHA-256 & zip readability without installing |
| `--uninstall` · `--purge-data` | Flag | Clean uninstall (`--purge-data` removes `data\` and `logs\`) |
| `--no-shortcuts` · `--autostart` | Flag | Skips `.lnk` shortcut creation or registers Windows Run autostart (clamped with warning when `AllowAutoStart = false`) |
| `--launch` · `--no-launch` / `--no-restart` | Flag | Controls post-install process launch |
| `--version` / `-v` · `--help` / `-h` / `/?` / `help` | Flag | Prints `descriptor.Version` or CLI usage and exits immediately |

### Installer Exit Codes
- **`0`**: Success (`--version`, `--help`, valid `--verify`, successful install/uninstall).
- **`1`**: Operational/runtime failure (payload missing/corrupt, file lock error, failed verify).
- **`2`**: CLI argument parsing failure (unknown option or missing option value).

---

## 4. Runtime Diagnostics & Troubleshooting

### 4.1 Verified SQLite WAL Defaults (`v0.3.0`)
`SqliteWalPool` (`Foundation.Core`) and `SqliteWalHelper` (`foundation_core`) enforce:
- `Cache = SqliteCacheMode.Default` (never `Shared` cache, preventing `SQLITE_LOCKED` Error 6 across concurrent readers/writers)
- `PRAGMA journal_mode = WAL;`
- `PRAGMA busy_timeout = 15000;` (`15` seconds)
- `PRAGMA synchronous = NORMAL;`
- `PRAGMA foreign_keys = ON;`
- `PRAGMA temp_store = MEMORY;`
- `PRAGMA cache_size = -65536;` (`64 MB` per connection)

### 4.2 Locks, Transfers & Crash Telemetry
- **Crash Bundles:** Unhandled C# / Python exceptions emit `crash_<appId>_<timestamp>.json` (`schemaVersion: 1`) under `<logDir>\crashes\`.
- **Stale `.hklock` Recovery:** `HkLockSentinel` verifies `ProcessId` + `ProcessStartTimeUtc` before clearing dead locks (`90`-minute `StaleLockMaxAge` threshold).
- **Safe Downloads (`SafeNetworkTransfer`):** Stages into `<dest>.partial_<jobId>` (with `JobId` sanitized against ADS colons), requires `1.2x expectedSize + 512 MB` free disk space, recovers HTTP `416`, fails fast on `400/401/403/404/410`, and retries up to `3` times with `10s` connect / `30s` `ResponseHeadersTimeout` / `30s` read-stall timeouts.
