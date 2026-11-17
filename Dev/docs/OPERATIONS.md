# Foundation (`v0.5.0`) — Operations, Build & CLI Runbook

> **Audience:** Engineers maintaining `Foundation` itself or running `Dev\tools\*.py`, `sandbox_ctl.py`, `run_sandbox_e2e.py`, `Foundation.Automation`, or `StandardInstallerHost` CLIs.
> **Full Symbol Reference:** [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md)

---

## 1. Build & Verification (`Foundation.sln` + `foundation_core`)

| Command | Purpose | Notes |
|---|---|---|
| `dotnet test <PROJECTS_ROOT>\Foundation\Dev\Foundation.sln -c Release -p:TreatWarningsAsErrors=true` | Compiles all assemblies + runs all C# xUnit tests | Zero-warning build (`PublishFoundationSdk` defaults to `false`) |
| `python -m unittest discover -s Dev\tests\foundation_core -v` | Runs pure-stdlib Python `foundation_core` & tools tests | Zero external dependencies |
| `pytest Dev\tests\foundation_core -q` | Runs the same Python suite via Pytest | Uses isolated `TemporaryDirectory()` fixtures |

### ⚠️ MSBuild Post-Build Side Effect Note (`Foundation.UI.csproj`)
Building `Foundation.UI.csproj` with `-p:PublishFoundationSdk=true` copies `Web\` into `WebAssets\` and invokes `sync_foundation_sdk.py --version 0.5.0 --ui-version 0.5.0 --publish`. Keep `PublishFoundationSdk=false` during normal builds and read-only `/audit` sweeps.

---

## 2. SDK Publisher & Release Validator (`Dev\tools\`)

### 2.1 `sync_foundation_sdk.py` (`Dev\tools\sync_foundation_sdk.py`, `<= 260` raw lines)
| Flag | Default / Type | Description |
|---|---|---|
| `--publish` | bool | Publishes Release binaries, `@foundation/ui` (`0.5.0`), `foundation_core` (`0.5.0`), and `Dev\tools\` to `SharedLibs\Foundation\v<version>`, `Latest`, and `Builds\v<version>\app` (`app\LICENSE`, zero `AGENTS.md`), plus strict 6-file `Releases\Latest\` (without loose `LICENSE`) unless `FOUNDATION_SKIP_RELEASE_ZIP` is set |
| `--sync-consumer` | `<ProjectRoot>` | Copies pinned SDK DLLs, `WebAssets`, and `foundation_core` into consumer `Dev\lib\` and removes only retired legacy test globs (`tools/verify_sandbox_universal.py`, `Foundation.Sandbox.*`, `*VisibleTest*`, `*InstallerSandboxContext*`) so `sandbox_seam.py`, `sandbox_ctl.py`, and `run_sandbox_e2e.py` survive |
| `--source-version` | `Latest` | Source folder inside `SharedLibs\Foundation\` (`Latest` or `v0.5.0`) |
| `--web-dir` | Path (optional) | Optional consumer frontend directory target for `WebAssets` |
| `--version` / `--ui-version` | `0.5.0` | Foundation SDK / `@foundation/ui` SemVer |
| `--configuration` | `Release` | Build configuration (`Release`/`Debug`) |

### 2.2 `build_release_universal.py` (`Dev\tools\build_release_universal.py`)
| Argument / Flag | Required? | Description |
|---|---|---|
| `release_dir` | Positional (Yes) | Path to `Releases\v<Version>` or `Releases\Latest` to validate strictly as a 6-file bundle |
| `--regen-sums` | Optional | Regenerates `SHA256SUMS.txt` before validating the 6-file bundle |
| `--allow-sdk-zip` | Optional | Validates strictly 6 files (`Foundation-v<Version>.zip`, `README.md`, `FEATURES.md`, `RELEASE_NOTES.md`, `CHANGELOG.md`, `SHA256SUMS.txt` — loose `LICENSE` fails validation) |

---

## 3. 4-Drive Sandbox Orchestrator & Stage 2 E2E Runner

### 3.1 `sandbox_ctl.py` (`Dev\tools\sandbox_ctl.py`)
Resolves `SANDBOX_C_DRIVE`, `SANDBOX_D_DRIVE`, `SANDBOX_REMOVABLE_DRIVE`, `SANDBOX_DISC_DRIVE`, and `FOUNDATION_SANDBOX_ROOT` in order **`CLI -> process env -> Dev\.env -> %USERPROFILE%\.gemini\.secrets\.env`**, parsing `subst` output dynamically so already-mounted drives succeed idempotently and inferring `FOUNDATION_SANDBOX_ROOT` from `Path(subst_target).parent` if unset. Exits `3` on any host path or drive collision.

| Subcommand | Flags | Behavior |
|---|---|---|
| `init` | `--no-subst`, `--force-hive` | Creates `C`/`D`/`E`/`registry`/`locks`/`snapshots`/`runs`/`logs`, mounts `subst` drives, writes Standard 40 `sandbox.manifest.json` (no `manifestType`) & `registry.baseline.json` -> `registry.hive.json` |
| `mount` | `--no-subst` | Idempotently mounts `SANDBOX_C_DRIVE`, `SANDBOX_D_DRIVE`, `SANDBOX_REMOVABLE_DRIVE` |
| `unmount-removable` / `remount-removable` | — | Runs `subst <Removable> /D` or remounts `<SandboxRoot>\E` for eject/retry tests |
| `mount-disc` / `unmount-disc` | `--iso <path>` | Mounts/dismounts an ISO via PowerShell `Mount-DiskImage`/`Dismount-DiskImage` or verifies pre-mounted `SANDBOX_DISC_DRIVE` |
| `status` | `--json` | Prints resolved paths, manifest/hive presence, and active mount states |
| `snapshot` / `diff-host` | `--name`, `--output`, `--baseline`, `--fail-on-diff` | Captures read-only host Desktop/Start Menu/Programs/`winreg` snapshot and diffs host state (`--fail-on-diff` exits `3` if `hostDiffCount > 0`) |
| `reset` | `--keep-runs`, `--no-subst` | Clears sandbox state (keeping `runs\` if `--keep-runs`) and restores `registry.hive.json` from `registry.baseline.json` |

Global flags accepted before or after subcommands: `--sandbox-root`, `--c-drive`, `--d-drive`, `--removable-drive`, `--disc-drive`, `--profile-dir`, `--env-file`.

### 3.2 `run_sandbox_e2e.py` (`Dev\tools\run_sandbox_e2e.py`)
Executes the 12-point `Standard 02 §4` matrix (`S2-01`..`S2-12`) with scrubbed child environments (`FOUNDATION_SANDBOX_ROOT`, `SANDBOX_*_DRIVE`, sandboxed `USERPROFILE`/`LOCALAPPDATA`/`APPDATA`/`TEMP`/`TMP`, `FOUNDATION_SUPPRESS_MODALS=1`, `APP_ISOLATED_TEST` stripped) and emits a 25-key Standard 18 receipt JSON under `<SandboxRoot>\runs\<appId>-v<Ver>-<ts>.json`.

| Flag | Purpose |
|---|---|
| `--app-id`, `--version`, `--installer` | Target app slug, SemVer, and setup `.exe`/script |
| `--payload`, `--installer-args`, `--upgrade-installer`, `--upgrade-payload`, `--uninstaller` | Optional payload/upgrade/uninstaller overrides |
| `--install-dir`, `--exe-rel`, `--probe-arg` / `--app-probe-args` | Target install dir inside sandbox (`<SandboxDDrive>\Programs\<appId>` default) and CLI boot probe |
| `--missing-file-arg`, `--missing-file-hook`, `--missing-file-expected-exit` | Missing-file standby/error-recovery check (`S2-06`) |
| `--disc-iso`, `--disc-verify-hook` | Virtual optical ISO verification (`S2-08`) |
| `--receipt-out`, `--timeout-seconds`, `--no-subst` | Receipt path override, child timeout (`120s`), and non-subst test flag |

---

## 4. `StandardInstallerHost` (`v0.5.0`) & `Foundation.Automation` CLI Reference

| Switch / Alias | Argument | Behavior |
|---|---|---|
| `--install-dir <dir>` / `--target-dir <dir>` | Path | Target install directory (defaults via `KnownFolderPaths` to sandboxed or host `%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\`) |
| `--profile <id>` · `--reengine-dir <dir>` · `--suite-dir <dir>` · `--migrate-legacy` | String / Path / Flag | Profile selection, secondary picker target, and opt-in legacy flat migration (`<SandboxDDrive>\Programs` under Stage 2) |
| `--payload <path>` · `--data-dir <dir>` · `--config <path>` · `--log-dir <dir>` | Path | External `.payload.zip` and Standard 02 isolation overrides |
| `--silent` / `--quiet` · `--dry-run` · `--verify` · `--uninstall` · `--purge-data` | Flag | Non-interactive setup, simulation, payload verify, or uninstall |
| `--no-shortcuts` · `--autostart` · `--launch` · `--no-launch` / `--no-restart` | Flag | Shortcut, Startup (clamped when `AllowAutoStart = false`), and launch flags |
| `--version` / `-v` · `--help` / `-h` / `/?` / `help` | Flag | Prints version or usage and exits |

### Installer Exit Codes
- **`0`**: Success (`--version`, `--help`, valid `--verify`, successful install/uninstall).
- **`1`**: Operational/runtime failure (payload missing/corrupt, file lock error, failed verify).
- **`2`**: CLI argument parsing failure (unknown option or missing value).
- **`3`**: Production/Sandbox Containment Violation (`SandboxEnvironmentSeam.IsContainmentViolation` — host OS/user/host-`Programs` escape aborted fail-closed).

- **`--automation-port <port>` & `--automation-token <token>` (`Foundation.Automation`):** Opt-in `127.0.0.1` loopback HTTP/JSON control surface (`AutomationCliHelper.TryCreateFromArgs`); defaults to disabled unless `--automation-port` is supplied. Falls back to `FOUNDATION_AUTOMATION_TOKEN` or a CSPRNG 32-hex bearer token.

---

## 5. Runtime Diagnostics (`SQLite WAL`, `Locks`, `Transfers`)
- **`SqliteWalPool` (`.NET`) & `open_wal_sqlite` (`Python`):** `SqliteCacheMode.Default`, `WAL`, `busy_timeout = 15000`, `synchronous = NORMAL`, `foreign_keys = ON`, `temp_store = MEMORY`, `cache_size = -65536` (`64 MB`).
- **Locks & Crash Bundles:** `JsonHivePlatformRegistry` locks `<SandboxRoot>\locks\registry.hive.lock` via named Mutex + `PidSafeLockfile` (`PidSafeLockEnvelope`); unhandled crashes emit `logs\crashes\crash_<appId>_<ts>.json`.
- **Safe Downloads (`SafeNetworkTransfer`):** `.partial_<jobId>`, `1.2x + 512 MB` reserve, HTTP `416` recovery, `10s` connect / `30s` `ResponseHeadersTimeout` / `30s` read-stall timeouts.

---

## 6. Security Policy, Pre-Public History Scrub & Public-Repo Human-Review Push Gate

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
