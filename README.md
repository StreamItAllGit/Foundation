# Foundation (`v0.5.2` — Beta)

[![Beta Release](https://img.shields.io/github/v/release/StreamItAllGit/Foundation?include_prereleases&label=Beta%20Release&color=f59e0b)](https://github.com/StreamItAllGit/Foundation/releases)
[![CI Build & Tests](https://github.com/StreamItAllGit/Foundation/actions/workflows/ci.yml/badge.svg)](https://github.com/StreamItAllGit/Foundation/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/github/license/StreamItAllGit/Foundation?color=22c55e)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Python 3.12+](https://img.shields.io/badge/Python-3.12%2B-3776AB)
![React / TypeScript](https://img.shields.io/badge/React%20%2F%20TypeScript-19-61DAFB)

> **Release Status:** **Beta (`v0.5.2` — Pre-1.0)**. Foundation and all sibling projects default to Beta until explicitly promoted out of Beta at `v1.0.0`.

**Modular `.NET 8`, React/TypeScript, and Python SDK** for Windows desktop applications (`Foundation.Core`, `Foundation.Retro`, `Foundation.Sidecars`, `Foundation.Server`, `Foundation.Launcher`, `Foundation.UI` + `@foundation/ui`, `Foundation.Installer`, `Foundation.Automation`, Python `foundation_core`, and cross-platform/sandbox automation scripts).

> **What's New in `v0.5.2`:** Self-healing `LastInstallBaseDir` registry memory and non-doubling versioned umbrella subpaths in `Foundation.Installer`, canonical `suiteId` manifest identity (preserving legacy `suiteName` fallback), extensible zero-dependency `RootLauncherOptions` with built-in suite flags in `Foundation.Launcher`, strict 7-file `--allow-portable-zip` release validation in `build_release_universal.py`, and focus-free managed mouse dispatch in `Foundation.Automation`.

---

## 1. Documentation & Navigation Map

| What You Need | Destination | Summary |
|---|---|---|
| C# APIs, React props, Python helpers, CLI flags & JSON schemas | [Dev/docs/INTERFACES.md](Dev/docs/INTERFACES.md) | 1-stop API dictionary & `@foundation/ui` import guide |
| Copy-paste integration recipes | [Dev/docs/USER_GUIDE.md](Dev/docs/USER_GUIDE.md) | Quickstart recipes for installers, UI automation, Web UI, Python, Core & 4-drive sandboxes |
| Architecture, file registry, SQLite pragmas & ADRs | [Dev/docs/ARCHITECTURE.md](Dev/docs/ARCHITECTURE.md) | Dependency graph, runtime pipelines, SLOC inventory & ADR notes |
| Build, test, SDK publish & sandbox CLI commands | [Dev/docs/OPERATIONS.md](Dev/docs/OPERATIONS.md) | Exact build/sync/release commands, exit codes & operational runbooks |
| Features, roadmap & version history | [FEATURES.md](FEATURES.md) · [ROADMAP.md](ROADMAP.md) · [CHANGELOG.md](CHANGELOG.md) | Module inventory, milestone horizon & Keep a Changelog ledger |
| Security policy & contributing | [SECURITY.md](SECURITY.md) · [CONTRIBUTING.md](CONTRIBUTING.md) | Security invariants, vulnerability disclosure & PR workflow |

---

## 2. Subproject Architecture Overview (`v0.5.2` — Beta)

- **C# Core & Runtime (`Dev/src/Foundation.Core/` — `25` files, `net8.0`):**
  - Environment, sandboxing & registry: [`AppEnvironment`](Dev/src/Foundation.Core/AppEnvironment.cs) (`--data-dir`, `--config`, `--log-dir`, `--port`, `--dry-run`, `UiPresentationMode`), [`KnownFolderPaths`](Dev/src/Foundation.Core/KnownFolderPaths.cs), [`SandboxEnvironmentSeam`](Dev/src/Foundation.Core/SandboxEnvironmentSeam.cs) (`ExitCode = 3` containment guards), [`JsonHivePlatformRegistry`](Dev/src/Foundation.Core/JsonHivePlatformRegistry.cs) (`IPlatformRegistry` atomic `.tmp -> .bak` hive + `WindowsPlatformRegistry`), [`SandboxDriveController`](Dev/src/Foundation.Core/SandboxDriveController.cs).
  - Resiliency, media & primitives: [`HighResFramePacer`](Dev/src/Foundation.Core/HighResFramePacer.cs), [`AudioRingBuffer`](Dev/src/Foundation.Core/AudioRingBuffer.cs), [`WavCaptureWriter`](Dev/src/Foundation.Core/WavCaptureWriter.cs), [`RgbaPngWriter`](Dev/src/Foundation.Core/RgbaPngWriter.cs), [`PixelOverlayCanvas`](Dev/src/Foundation.Core/PixelOverlayCanvas.cs), [`QrCodeEncoder`](Dev/src/Foundation.Core/QrCodeEncoder.cs) (zero-dependency ISO/IEC 18004 V1–V10 ECC-M QR matrix & SVG renderer), [`GranularSettingsManager`](Dev/src/Foundation.Core/GranularSettingsManager.cs), [`DpapiSecretStore`](Dev/src/Foundation.Core/DpapiSecretStore.cs), [`SqliteWalPool`](Dev/src/Foundation.Core/SqliteWalPool.cs), [`PidSafeLockfile`](Dev/src/Foundation.Core/PidSafeLockfile.cs), [`HeartbeatLogger`](Dev/src/Foundation.Core/HeartbeatLogger.cs), [`CrashBundleService`](Dev/src/Foundation.Core/CrashBundleService.cs), [`JobObjectGuard`](Dev/src/Foundation.Core/JobObjectGuard.cs), [`SafeNetworkTransfer`](Dev/src/Foundation.Core/SafeNetworkTransfer.cs), [`ToolResolver`](Dev/src/Foundation.Core/ToolResolver.cs), [`HybridModuleLoader`](Dev/src/Foundation.Core/HybridModuleLoader.cs).
- **Retro & Sidecar Libraries (`Dev/src/Foundation.Retro/` [`8` files] + `Dev/src/Foundation.Sidecars/` [`5` files], `net8.0`):**
  - 41-platform catalog (`ConsoleSystemCatalog`), No-Intro/Redump/RA/CMP streaming DAT parser, headerless ROM stripper + binary magic sniffer, IPS/BPS/UPS soft patcher, [`MediaContainerExtractor`](Dev/src/Foundation.Retro/MediaContainerExtractor.cs) (`.iso`, `.bin`+`.cue`, `.chd`, `.7z`, `.zip`), static recompilation contracts ([`RecompContracts`](Dev/src/Foundation.Retro/RecompContracts.cs), [`RecompTierAndBackendContracts`](Dev/src/Foundation.Retro/RecompTierAndBackendContracts.cs)), single-pass multi-hasher, ZIP/7z `-slt` inspector + TorrentZip builder, `.hash` v1 sidecars, and `.hklock` / `.coexistence.json` v2 coordination.
- **Server, Launcher, UI, Installer & Automation (`net8.0` / `net8.0-windows`):**
  - **Web & Auth Server (`Foundation.Server`, `3` files):** Tiered localhost/Tailscale/PBKDF2/HMAC authentication and `/api/v1/suite/manifest` endpoints.
  - **Clean-Root Launcher (`Foundation.Launcher`, `2` files):** [`RootLauncherHost`](Dev/src/Foundation.Launcher/RootLauncherHost.cs) + [`RootLauncherOptions`](Dev/src/Foundation.Launcher/RootLauncherOptions.cs) (zero-dependency `AttachConsole(-1)` shim, extensible sync/value/path flags including suite diagnostics/automation switches, non-swallowing optional flags, relative arg rebasing, Ctrl+C forwarding).
  - **Desktop & Web UI (`Foundation.UI` + `@foundation/ui`):** `AppEnvironment`-aware [`DisposableWebViewHostForm`](Dev/src/Foundation.UI/Desktop/DisposableWebViewHostForm.cs), STA dialogs, tray badges, React 19 UI components, and Vanilla ES terminal drawer.
  - **Declarative & 3-Mode Installer (`Foundation.Installer`, `18` files):** Tier 1 [`StandardInstallerHost`](Dev/src/Foundation.Installer/StandardInstallerHost.cs), declarative [`InstallerManifestStore`](Dev/src/Foundation.Installer/InstallerManifestStore.cs) (`installer.manifest.json` `schemaVersion: 2`, canonical `suiteId` with `suiteName` legacy fallback, `single-line` / `multi-line`, `lightweight` / `full` / `custom`, 2-line tooltips, theme tokens, `logoPath`/`iconPath`), version-leaf-safe umbrella subpaths, and self-healing `HKCU\Software\StreamItAll\LastInstallBaseDir` fresh-install memory (preserving existing upgrade directories and clean user roots; guarded under `APP_ISOLATED_TEST=1` & reparse checks).
  - **Focus-Free UI Automation (`Foundation.Automation`, `4` files):** Opt-in `127.0.0.1` loopback server ([`AutomationLoopbackServer`](Dev/src/Foundation.Automation/AutomationLoopbackServer.cs)) with Bearer `401` + `Host`/`Origin` `403` enforcement, focus-free managed mouse dispatch (`Win32VirtualInputDriver`, `0` `SendInput` or `SetFocus()` steals) & window-scoped keyboard/tree inspection, and PNG window/framebuffer snapshots (`IRenderSurfaceSnapshotProvider` + `RgbaPngWriter`).
- **Python SDK & Dev Tooling ([`Dev/src/foundation_core/`](Dev/src/foundation_core/) + [`Dev/tools/`](Dev/tools/)):**
  - Pure-stdlib Python `foundation_core` (`0.5.2`) with 1:1 C# 4-drive sandbox (`sandbox_seam.py`) and atomic JSON pseudo-registry (`pseudo_registry.py`) parity, plus universal sandbox orchestration, SDK packaging, and 6/7-file (`--allow-portable-zip`) release validation scripts.

---

## 3. Quickstart & Verification

### Prerequisites
- **.NET 8 SDK** (`net8.0` cross-platform; `net8.0-windows` on Windows for WinForms/Installer/Automation)
- **Python 3.12+** (`unittest` works with stdlib only; `pytest` optional)

```powershell
# 1. Clone repository and run isolated .NET test suite (zero warnings required)
git clone https://github.com/StreamItAllGit/Foundation.git
cd Foundation
$env:FOUNDATION_SUPPRESS_MODALS = "1"
dotnet test Dev/Foundation.sln -c Release -p:TreatWarningsAsErrors=true

# 2. Run Python foundation_core & tools test suite
python -m unittest discover -s Dev/tests/foundation_core -v

# 3. Publish compiled SDK snapshot (DLLs, @foundation/ui, foundation_core, tools)
python Dev/tools/sync_foundation_sdk.py --publish --version 0.5.2 --ui-version 0.5.2 --configuration Release

# 4. Sync pinned SDK into a downstream consumer directory
python Dev/tools/sync_foundation_sdk.py --sync-consumer path/to/ConsumerProject

# 5. Initialize 4-drive sandbox and run 12-point Stage 2 verification
python Dev/tools/sandbox_ctl.py init
python Dev/tools/run_sandbox_e2e.py --app-id myapp --version 0.1.0 --installer path/to/Setup.exe
```

> **Note on `PublishFoundationSdk`:** Building `Foundation.UI.csproj` with `-p:PublishFoundationSdk=true` updates `WebAssets/` and publishes to the local `SharedLibs/Foundation/Latest/` snapshot directory.

---

## 4. Releases, Governance & License

- **GitHub Beta Releases:** [github.com/StreamItAllGit/Foundation/releases](https://github.com/StreamItAllGit/Foundation/releases) (`v0.5.2` Beta SDK ZIP, release notes & SHA-256 checksums)
- **Lifecycle State:** [active.state](active.state) (`v0.5.2` — Beta, Public Repository)
- **License:** Distributed under the [MIT License](LICENSE).
- **Security & Contributing:** See [SECURITY.md](SECURITY.md) and [CONTRIBUTING.md](CONTRIBUTING.md).
- **Clean-Room & Zero-PII Guarantee:** Contains zero copyrighted ROMs, BIOS dumps, proprietary keys, or workstation-specific credentials/paths.

---

## 5. Technical Manuals (`Dev/docs/`)

- [Dev/docs/INTERFACES.md](Dev/docs/INTERFACES.md) — **API Dictionary** (C# classes/signatures including `QrCodeEncoder`, `Foundation.Automation`, `InstallerManifestStore`, `@foundation/ui` props, Vanilla JS/CSS, `foundation_core`, `Dev/tools/*.py` CLIs & JSON schemas)
- [Dev/docs/USER_GUIDE.md](Dev/docs/USER_GUIDE.md) — Consumer Integration Recipes, Declarative Markup Installer, `Foundation.Automation` Loopback & Stage 2 Sandbox Quickstart
- [Dev/docs/ARCHITECTURE.md](Dev/docs/ARCHITECTURE.md) — System Topology, Runtime Pipelines, Complete Module Registry, SQLite Pragmas & ADR Notes
- [Dev/docs/OPERATIONS.md](Dev/docs/OPERATIONS.md) — Build, Hub Sync, 4-Drive Sandbox CLIs, Installer Flags & Exit Codes
