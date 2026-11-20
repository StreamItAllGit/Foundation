# Foundation (`v0.5.3`) — Operations, Build & CLI Runbook

> **Audience:** Engineers maintaining `Foundation` itself or running `Dev\tools\*`, `sandbox_ctl.py`, `run_sandbox_e2e.py`, `ExeboxStreamHelpers.ps1` / `exebox_stream.py`, `Foundation.Automation`, or `StandardInstallerHost` CLIs.
> **Full Symbol Reference:** [Dev/docs/INTERFACES.md](INTERFACES.md) · **Integration Recipes:** [Dev/docs/USER_GUIDE.md](USER_GUIDE.md)

---

## 1. Build & Verification (`Foundation.sln` + `foundation_core`)

Open `Dev\Foundation.code-workspace` in VS Code / Antigravity for preconfigured `Dev\` + Project Root folders, `dotnet.defaultSolution`, build/test/scaffold-audit tasks, and recommended extensions.

| Command | Purpose | Notes |
|---|---|---|
| `dotnet test <PROJECTS_ROOT>\Foundation\Dev\Foundation.sln -c Release -p:TreatWarningsAsErrors=true` | Compiles all assemblies + runs all C# xUnit tests (`55/55`) | Zero-warning build (`PublishFoundationSdk` defaults to `false`) |
| `python -m unittest discover -s Dev\tests\foundation_core -v` | Runs pure-stdlib Python `foundation_core` & tools tests (`16/16`) | Zero external dependencies |
| `pytest Dev\tests\foundation_core -q` | Runs the same Python suite via Pytest | Uses isolated `TemporaryDirectory()` fixtures |

### ⚠️ MSBuild Post-Build Side Effect Note (`Foundation.UI.csproj`)
Building `Foundation.UI.csproj` with `-p:PublishFoundationSdk=true` copies `Web\` into `WebAssets\` and invokes `sync_foundation_sdk.py --version 0.5.3 --ui-version 0.5.3 --publish`. Keep `PublishFoundationSdk=false` during normal builds and read-only `/audit` sweeps.

---

## 2. SDK Publisher & Release Validator (`Dev\tools\`)

### 2.1 `sync_foundation_sdk.py` (`Dev\tools\sync_foundation_sdk.py`, `<= 260` raw lines)
| Flag | Default / Type | Description |
|---|---|---|
| `--publish` | bool | Publishes `59` Release SDK files (`Foundation.*.dll`, `@foundation/ui` `0.5.3`, `foundation_core` `0.5.3`, `Dev\tools\` incl. `ExeboxStreamHelpers.ps1` & `exebox_stream.py`) to `SharedLibs\Foundation\v<version>`, `Latest`, and `Builds\v<version>\app` (`app\LICENSE`, zero `AGENTS.md`), plus hardlink-safe 6-file `Releases\Latest\` (without loose `LICENSE`, delegating checksums to `generate_sha256sums`) unless `FOUNDATION_SKIP_RELEASE_ZIP` is set |
| `--sync-consumer` | `<ProjectRoot>` | Copies pinned SDK DLLs, `WebAssets`, and `foundation_core` into consumer `Dev\lib\` and removes only retired legacy test globs (`tools/verify_sandbox_universal.py`, `Foundation.Sandbox.*`, `*VisibleTest*`, `*InstallerSandboxContext*`) so `sandbox_seam.py`, `sandbox_ctl.py`, `run_sandbox_e2e.py`, `ExeboxStreamHelpers.ps1`, and `exebox_stream.py` survive |
| `--source-version` | `Latest` | Source folder inside `SharedLibs\Foundation\` (`Latest` or `v0.5.3`) |
| `--web-dir` | Path (optional) | Optional consumer frontend directory target for `WebAssets` |
| `--version` / `--ui-version` | `0.5.3` | Foundation SDK / `@foundation/ui` SemVer |
| `--configuration` | `Release` | Build configuration (`Release`/`Debug`) |

### 2.2 `build_release_universal.py` (`Dev\tools\build_release_universal.py`)
Writes `SHA256SUMS.txt` (lowercase SHA-256 hex) atomically via `_atomic_write_text` (`.tmp -> os.replace`) and unlinks destination `.zip`s before writing in `stage_sdk_release_zip()` so NTFS-hardlinked prior releases are never mutated in place.

| Argument / Flag | Required? | Description |
|---|---|---|
| `release_dir` | Positional (Yes) | Path to `Releases\v<Version>` or `Releases\Latest` to validate via `validate_release_bundle` (`validate_six_file_release_bundle`) as a strict 6-, 7-, or 8-file bundle |
| `--regen-sums` | Optional | Atomically regenerates lowercase `SHA256SUMS.txt` before validating the bundle |
| `--stage-sdk-zip <sdk_dir>` | Optional | Stages `Foundation-v<version>.zip` from `<sdk_dir>` into `release_dir` (requiring `--version <X.Y.Z>`) and atomically updates `SHA256SUMS.txt` |
| `--version <X.Y.Z>` | With `--stage-sdk-zip` | Version tag for staged `Foundation-v<version>.zip` |
| `--allow-sdk-zip` | Optional | Validates strictly 6 files (`Foundation-v<Version>.zip`, `README.md`, `FEATURES.md`, `RELEASE_NOTES.md`, `CHANGELOG.md`, `SHA256SUMS.txt` — loose `LICENSE` fails validation; mutually exclusive with `--allow-portable-zip`) |
| `--allow-portable-zip` | Optional | Validates strictly 7 files for app+portable releases (`*-Setup-v*.exe`, `*-Portable.zip`, `README.md`, `FEATURES.md`, `RELEASE_NOTES.md`, `CHANGELOG.md`, `SHA256SUMS.txt` — rejects loose subdirectories/extras, empty/corrupt archives, or any `*Setup*.exe` / `-Offline-Setup-` inside `*-Portable.zip`; auto-enabled when `*-Portable.zip` is present without `--allow-sdk-zip` or `--allow-dual-installer`) |
| `--allow-dual-installer` | Optional | Validates strictly 8 files for Dual Online+Offline+Portable releases (`*-Setup-v*.exe`, `*-Offline-Setup-v*.exe`, `*-Portable.zip`, `README.md`, `FEATURES.md`, `RELEASE_NOTES.md`, `CHANGELOG.md`, `SHA256SUMS.txt`, enforcing `_inspect_portable_zip` leak checks; auto-enabled when `*-Offline-Setup-v*.exe` is present) |

---

## 3. 4-Drive Sandbox Orchestrator, Stage 2 Runner & Live GUI Streaming

### 3.1 `sandbox_ctl.py` (`Dev\tools\sandbox_ctl.py`)
Resolves `SANDBOX_C_DRIVE`, `SANDBOX_D_DRIVE`, `SANDBOX_REMOVABLE_DRIVE`, `SANDBOX_DISC_DRIVE`, and `FOUNDATION_SANDBOX_ROOT` in order **`CLI -> process env -> Dev\.env -> %USERPROFILE%\.gemini\.secrets\.env`**, parsing `subst` output dynamically so already-mounted drives succeed idempotently and inferring `FOUNDATION_SANDBOX_ROOT` from `Path(subst_target).parent` if unset. Exits `3` on any host path or drive collision.

| Subcommand | Flags | Behavior |
|---|---|---|
| `init` | `--no-subst`, `--force-hive` | Creates `C`/`D`/`E`/`registry`/`locks`/`snapshots`/`runs`/`logs`, mounts `subst` drives, writes `schemaVersion: 2` `sandbox.manifest.json` (no `manifestType`) & `registry.baseline.json` -> `registry.hive.json` |
| `mount` | `--no-subst` | Idempotently mounts `SANDBOX_C_DRIVE`, `SANDBOX_D_DRIVE`, `SANDBOX_REMOVABLE_DRIVE` |
| `unmount-removable` / `remount-removable` | — | Runs `subst <Removable> /D` or remounts `<SandboxRoot>\E` for eject/retry tests |
| `mount-disc` / `unmount-disc` | `--iso <path>` | Mounts/dismounts an ISO via PowerShell `Mount-DiskImage`/`Dismount-DiskImage` or verifies pre-mounted `SANDBOX_DISC_DRIVE` |
| `status` | `--json` | Prints resolved paths, manifest/hive presence, and active mount states |
| `snapshot` / `diff-host` | `--name`, `--output`, `--baseline`, `--fail-on-diff` | Captures read-only host Desktop/Start Menu/Programs/`winreg` snapshot and diffs host state (`--fail-on-diff` exits `3` if `hostDiffCount > 0`) |
| `reset` | `--keep-runs`, `--no-subst` | Clears sandbox state (keeping `runs\` if `--keep-runs`) and restores `registry.hive.json` from `registry.baseline.json` |

Global flags accepted before or after subcommands: `--sandbox-root`, `--c-drive`, `--d-drive`, `--removable-drive`, `--disc-drive`, `--profile-dir`, `--env-file`.

### 3.2 `run_sandbox_e2e.py` (`Dev\tools\run_sandbox_e2e.py`)
Executes the 12-point Stage 2 sandbox matrix (`S2-01`..`S2-12`) with scrubbed child environments (`FOUNDATION_SANDBOX_ROOT`, `SANDBOX_*_DRIVE`, sandboxed `USERPROFILE`/`LOCALAPPDATA`/`APPDATA`/`TEMP`/`TMP`, `FOUNDATION_SUPPRESS_MODALS=1`, `APP_ISOLATED_TEST` stripped, and case-insensitive OS keys `SystemDrive`, `ProgramData`, `ProgramFiles`, `ProgramFiles(x86)`, `CommonProgramFiles`, `CommonProgramFiles(x86)`, `ALLUSERSPROFILE`, `PUBLIC` preserved via `_env_lookup_ci`) and emits a 25-key JSON receipt under `<SandboxRoot>\runs\<appId>-v<Ver>-<ts>.json`.

| Flag | Purpose |
|---|---|
| `--app-id`, `--version`, `--installer` | Target app slug, SemVer, and setup `.exe`/script |
| `--payload`, `--offline-bundle`, `--installer-args`, `--upgrade-installer`, `--upgrade-payload`, `--uninstaller` | Optional payload, `--offline-bundle` (forwarded in `S2-02`/`S2-09`), upgrade, and uninstaller overrides |
| `--install-dir`, `--exe-rel`, `--probe-arg` / `--app-probe-args` | Target install dir inside sandbox (`<SandboxDDrive>\Programs\<appId>` default) and CLI boot probe |
| `--missing-file-arg`, `--missing-file-hook`, `--missing-file-expected-exit` | Missing-file standby/error-recovery check (`S2-06`) |
| `--disc-iso`, `--disc-verify-hook` | Virtual optical ISO verification (`S2-08`) |
| `--receipt-out`, `--timeout-seconds`, `--no-subst` | Receipt path override, child timeout (`120s`), and non-subst test flag |

### 3.3 Live GUI Streaming Helpers (`ExeboxStreamHelpers.ps1` & `exebox_stream.py`)
Shared desktop preflight and `exebox-stream` UDP `5004` streaming wrappers for Stage 2 GUI runs:
- PowerShell (`Dev\tools\ExeboxStreamHelpers.ps1`): `Get-ExeboxDesktopName` (`GetThreadDesktop` truthfulness), `Test-ExeboxVlcViewerActive`, `Initialize-ExeboxTestViewerPreflight` (never launches `Test Viewer.lnk` inside `WinSta0\exebox-*`), `Start-ExeboxLiveVlcStream`, `Stop-ExeboxLiveVlcStream`.
- Python (`Dev\tools\exebox_stream.py`): `get_thread_desktop_name`, `is_vlc_viewer_active`, `start_exebox_live_stream`, `stop_exebox_live_stream`.

---

## 4. `StandardInstallerHost` (`v0.5.3`), `RootLauncherOptions` & `Foundation.Automation` CLI Reference

| Switch / Alias | Argument | Behavior |
|---|---|---|
| `--install-dir <dir>` / `--target-dir <dir>` | Path | Target install directory (defaults via `KnownFolderPaths` to sandboxed or host `%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\`; fails closed with `ProductMismatch:` if owned by another product) |
| `--profile <id>` · `--offline-bundle <zipOrDir>` · `--reengine-dir <dir>` · `--suite-dir <dir>` · `--migrate-legacy` | String / Path / Flag | Profile selection, local `--offline-bundle` asset verification for Online Setup, secondary picker target, and opt-in legacy flat migration (`<SandboxDDrive>\Programs` under Stage 2) |
| `--payload <path>` · `--data-dir <dir>` · `--config <path>` · `--log-dir <dir>` | Path | External `.payload.zip` and runtime isolation overrides |
| `--silent` / `--quiet` · `--dry-run` · `--verify` · `--uninstall` · `--purge-data` | Flag | Non-interactive setup, simulation, payload/bundle verify, or ownership-checked wildcard-free uninstall |
| `--no-shortcuts` · `--autostart` · `--launch` · `--no-launch` / `--no-restart` | Flag | Shortcut, Startup (clamped when `AllowAutoStart = false`), and launch flags |
| `--version` / `-v` · `--help` / `-h` / `/?` / `help` | Flag | Prints version or usage and exits |

### Installer Exit Codes
- **`0`**: Success (`--version`, `--help`, valid `--verify`, successful install/uninstall).
- **`1`**: Operational/runtime failure (payload missing/corrupt, foreign `ProductMismatch:` directory ownership refusal, online 404/hash/SSRF failure, file lock error, failed verify).
- **`2`**: CLI argument parsing failure (unknown option or missing value).
- **`3`**: Production/Sandbox Containment Violation (`SandboxEnvironmentSeam.IsContainmentViolation` — host OS/user/host-`Programs` escape aborted fail-closed).

- **`--automation-port <port>` & `--automation-token <token>` (`Foundation.Automation`):** Opt-in `127.0.0.1` loopback HTTP/JSON control surface (`AutomationCliHelper.TryCreateFromArgs` for WinForms or `AutomationCliHelper.TryCreateForSurface` for WinForms-free `IAutomationSurfaceTarget` targets); defaults to disabled unless `--automation-port` is supplied. Exposes `/v1/{health,tree,pointer,keyboard,snapshot}` plus in-memory `GET /v1/snapshot.png` (`image/png`).
- **`RootLauncherHost` + `RootLauncherOptions` (`Foundation.Launcher`):** Built-in sync flags include `--diagnose`, `--doctor`, `--verify`, `--verify-module` (plus `--wait`, `--open`, `--check-updates`, `--register-assoc`, `--dry-run`, `--version`, `--help`); built-in path/value flags include `--programs-dir`, `--save-dir`, `--preload-media`, `--backend`, `--automation-port`, `--automation-token`; optional-value switches (`--suite-tab`, `--window`, `--browser`) consume an adjacent non-flag value without swallowing trailing sync flags.

---

## 5. Runtime Diagnostics (`SQLite WAL`, `Locks`, `Backups`, `Frame Pacer`, `Transfers`)
- **`SqliteWalPool` (`.NET`) & `open_wal_sqlite` (`Python`):** `SqliteCacheMode.Default`, `WAL`, `busy_timeout = 15000`, `synchronous = NORMAL`, `foreign_keys = ON`, `temp_store = MEMORY`, `cache_size = -65536` (`64 MB`).
- **Locks, Rotating Backups & Crash Bundles:** `JsonHivePlatformRegistry` locks `<SandboxRoot>\locks\registry.hive.lock` via named Mutex + `PidSafeLockfile`; `RollingFileBackupStore` writes `.tmp -> Flush(true) -> File.Move` + `.bak` snapshots (`DefaultMaxBackups = 10`, monotonic sequence collision guard, `AssertPathContained` restore); unhandled crashes emit `logs\crashes\crash_<appId>_<ts>.json`.
- **VRR Late-Latch Timing (`HighResFramePacer`):** `BeginLateLatchFrame()` + `WaitBeforePresent()` / `WaitBeforePresentAsync()` with clamped `LateLatchMarginMs` and rolling `LastLateLatchWaitMs` / `AverageLateLatchWaitMs` telemetry.
- **Safe Downloads (`SafeNetworkTransfer`):** `.partial_<jobId>`, `1.2x + 512 MB` reserve, HTTP `416` recovery, `10s` connect / `30s` `ResponseHeadersTimeout` / `30s` read-stall timeouts, plus `DownloadVerifiedAsync` (`https://`, `AllowAutoRedirect = false`, `MaxRedirects = 5`, DNS IP `ConnectCallback` SSRF guard, TLS 1.2/1.3, `ExpectedSizeBytes`).

---

## 6. CI Signing, Public Docs Workflow (`pages.yml`) & Pre-Public Checklist

- **Release & SignPath Workflow ([`.github/workflows/release.yml`](../../.github/workflows/release.yml)):** Runs `dotnet test`, Python `unittest`, `dotnet build`, stages `SharedLibs\Foundation` into `RUNNER_TEMP`, runs `sync_foundation_sdk.py --publish` and `build_release_universal.py Releases/Latest --stage-sdk-zip ... --version ... --allow-sdk-zip`, submits the unsigned SDK ZIP to SignPath when `SIGNPATH_PROJECT_SLUG` is configured, and uploads signed assets to tagged GitHub Releases (`--prerelease` on `v0.*` / prerelease tags).
- **Documentation Site Workflow ([`.github/workflows/pages.yml`](../../.github/workflows/pages.yml)):** Visibility-gated (`if: github.event.repository.visibility == 'public'`) static HTML generator converting root Markdown docs and `Dev/docs/*.md` (excluding `plans/`, `reports/`, `tests/`, `archive/`, and `AGENTS.md`) plus `.nojekyll`, `404.html`, `robots.txt`, and `sitemap.xml` to [https://streamitallgit.github.io/Foundation/](https://streamitallgit.github.io/Foundation/).

> [!CAUTION]
> Never execute destructive history rewrites (`git-filter-repo`, `git push --force`) or flip GitHub repository visibility (`gh repo edit --visibility public`) without explicit human confirmation from Stream.

### Pre-Public Human-Gated `git-filter-repo` Checklist
1. **Verify working tree cleanliness (`0` PII / secret / workstation-path leaks):**
   ```powershell
   python "%USERPROFILE%\.gemini\config\skills\audit\scripts\audit_scanner.py" "<PROJECTS_ROOT>\Foundation\Dev" --format markdown
   git -C "<PROJECTS_ROOT>\Foundation" grep -iE "(D:\\Projects|C:\\Users|ghp_|aiza|sk-|discord\.com/api/webhooks)"
   ```
2. **Optional commit-history audit & scrub (human-only, on a verified clone + `.7z` backup):**
   ```powershell
   # Inspect historical commits for any pre-v0.4.0 workstation paths or Notion IDs before publishing
   git log --all -S "C:\\Users\\" --oneline
   # If historical paths must be scrubbed before flipping public:
   # pip install git-filter-repo
   # git filter-repo --replace-text expressions.txt --force
   # git push origin --force --all && git push origin --force --tags
   ```
3. **Flip visibility after Stream review sign-off & enforce public-repo push gate:**
   - Set `"isPublicRepo": true` and `"requireHumanReviewBeforePush": true` in `active.state` once flipped (`gh repo edit <owner>/Foundation --visibility public --accept-visibility-change-consequences`).
   - Once a repository is public (`isPublicRepo: true` / `isPrivate == false`), **every** subsequent `git push` or `gh release create` requires explicit human approval via `ask_question` first.
