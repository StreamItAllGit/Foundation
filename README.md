# Foundation

- **Active Version:** `v0.3.0` (Introducing `Foundation.UI.dll v0.2.0` & `@foundation/ui v0.2.0` Canonical ShadowLibrary Strict Standard Docked Terminal Drawer across React 19 + Vanilla ES Module `foundation-terminal.js` + `foundation-terminal.css`, `sync_foundation_sdk.py` Auto-Sync Pipeline, `Foundation.Launcher.dll v0.1.1`, and `v0.1.0` of `Foundation.Retro.dll`, `Foundation.Sidecars.dll`, `Foundation.Server.dll`, and `foundation_core` — Full SemVer `MAJOR.MINOR.PATCH`)
- **Lifecycle State:** `active` ([`active.state`](file:///<PROJECTS_ROOT>/Foundation/active.state))
- **Tech Stack:** .NET 8 (C# 12 Class Libraries, ASP.NET Core & WinForms) / React 19 + TypeScript (`@foundation/ui v0.2.0`) + Vanilla ES Module (`foundation-terminal.js`) / Python 3.12+ (`foundation_core`)
- **Canonical Shared Output Path:** [`<PROJECTS_ROOT>\SharedLibs\Foundation\v0.3.0\`](file:///<PROJECTS_ROOT>/SharedLibs/Foundation/v0.3.0/) & [`<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\`](file:///<PROJECTS_ROOT>/SharedLibs/Foundation/Latest/)

---

## 1. What It Is & Core Goal
**Foundation** is the shared, customizable SDK and framework library underneath our entire ecosystem of sister apps (`Mother`, `Housekeeping`, `ShadowLibrary`, `Redroom`, `Greenroom`, `StreamEngine`, `uStation`), standalone tools, and games.

Instead of copy-pasting thousands of lines of identical installer wizards, sandbox guards, crash dumpers, SQLite WAL helpers, retro DAT parsers, single-pass stream hashers, `.hash` sidecar serializers, Kestrel tiered auth middlewares, and React 19 / Vanilla JS UI shells across every repository, `Foundation` compiles them once into versioned `.dll`, `.whl`, and TS/JS/CSS packages stored in `<PROJECTS_ROOT>\SharedLibs\Foundation\v<Version>\` (and mirrored to `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\`). Individual projects pin the exact version they need inside their own `Dev\lib\` folder (or pull the latest snapshot on build via `sync_foundation_sdk.py --sync-consumer`)—eliminating boilerplate while keeping every project 100% self-contained.

---

## 2. Quickstart & Cross-Workspace Update Commands

### Build & Auto-Publish Versioned Packages to `SharedLibs\Foundation\v0.3.0` & `Latest`
```powershell
cd <PROJECTS_ROOT>\Foundation\Dev
dotnet build Foundation.sln -c Release
# Or invoke the publisher / consumer sync script directly:
python <PROJECTS_ROOT>\SharedLibs\Foundation\Latest\tools\sync_foundation_sdk.py --sync-consumer <PROJECTS_ROOT>\<SisterApp>
```

### Isolated Sandbox Verification (`Sandbox\`)
```powershell
cd <PROJECTS_ROOT>\Foundation\Sandbox
dotnet test ..\Dev\tests\Foundation.Tests\Foundation.Tests.csproj -c Release --logger "console;verbosity=normal"
```

| Package / Artifact | Version | Target Stack | Purpose & Customization |
| :--- | :---: | :--- | :--- |
| **`Foundation.Core.dll`** | `v0.3.0` | .NET 8 / C# 12 | `SandboxEnvironment`, `CrashBundleService`, `SqliteWalPool` (`64 MB` page cache), `PidSafeLockfile`, `ToolResolver`, `HeartbeatLogger`, `JobObjectGuard`, `SafeNetworkTransfer`, `GranularSettingsManager`, `DpapiSecretStore`, `IFoundationModule` Hybrid Loader, `CliSchema`, `BoundedTailStream`, and `CappedSharedLogSink`. |
| **`Foundation.Retro.dll`** | `v0.1.0` | .NET 8 / C# 12 | `ConsoleSystemCatalog` (64 platforms, 50+ BIOS MD5/SHA-1/Size rules, header-skip rules, provenance tiers), `DatTitleNormalizer`, `NoIntroRedumpDatParser` (Logiqx/No-Intro/Redump/MAME/RA XML + ClrMamePro + SFV/MD5), `HeaderlessRomStripper` & `MagicBytePlatformDetector`, and `SoftPatchEngine` (in-memory IPS/BPS1/UPS1). |
| **`Foundation.Sidecars.dll`** | `v0.1.0` | .NET 8 / C# 12 | `SinglePassStreamHasher` (`ArrayPool` single-pass CRC32/MD5/SHA-1/SHA-256 + headerless ROM stream hasher), `SevenZipAndZipInspector` (`.zip` central directory, `7z l -slt`, TorrentZip), `HashSidecarService` (Standard 17 `.hash` v1 + wrapper-path normalizer + `COMPOSITE_SHA1` + archive CRC32 index validator), and `HkLockSentinel` / `SisterAppCoexistenceClient`. |
| **`Foundation.Server.dll`** | `v0.1.0` | .NET 8 ASP.NET Core | `TieredAuthEngine` & `FoundationTieredAuthMiddleware` (7-step Kestrel auth ladder: API Token, Tailscale Serve, Loopback, PBKDF2-SHA256, HMAC session cookie, Tailscale `100.64.0.0/10`) and `SisterAppStandardEndpoints` (`/suite.manifest.json`, `/api/v1/suite/manifest`). |
| **`Foundation.Launcher.dll`** | `v0.1.1` | .NET 8 Windows | `RootLauncherHost` (`AttachConsole(-1)` root `.exe` shim with `ResolveTargetExecutable` clean-root `app\<exe>` auto-resolution & fail-closed self-spawn guard). |
| **`Foundation.Installer.dll`** | `v0.2.0` | .NET 8 WinForms | Customizable `650×434` $\rightarrow$ `650×544` Dark Setup, Upgrade, Repair & Uninstall Wizard (`InstallerWizardForm`, `InstallerEngineCore`), fast `<1.5s` `NoCompression` `CreatePreUpgradeBackupAsync`, and `WindowsShortcutWriter`. |
| **`Foundation.UI.dll`, `@foundation/ui` & Vanilla WebAssets** | `v0.2.0` | WinForms / React 19 / Vanilla ESM | `Foundation.UI.Desktop` (`StaNativeDialogBridgeBase`, `DisposableWebViewHostForm`, `TrayIconBadgeHelper`) + `@foundation/ui v0.2.0` (10 shared React 19 components including the Canonical ShadowLibrary Strict Standard `GlobalTerminalDrawer`, `TerminalToolbarAndFilters`, and `terminalDrawerCore.ts`) + self-contained Vanilla ES Module [`foundation-terminal.js`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.js) & [`foundation-terminal.css`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/WebAssets/foundation-terminal.css). |
| **`foundation_core` & `Dev\tools\`** | `v0.3.0` | Python 3.12+ | Multi-module Python companion SDK (`settings.py`, `logging.py`, `crash_dumper.py`, `locks.py`, `db.py`, `cli.py`) + [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py) auto-sync publisher/consumer pipeline & universal verification/packaging/Notion helpers (`verify_sandbox_universal.py`, `build_release_universal.py`, `notion_task_helper.py`). |

---

## 3. Architecture At a Glance
- **Application Archetype:** Versioned Multi-Language Shared SDK (`Standard 08 §8`, `Standard 17 §1.6`)
- **Core Entry Points:**
  - [`Foundation.Core`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/)
  - [`Foundation.Retro`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/)
  - [`Foundation.Sidecars`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Sidecars/)
  - [`Foundation.Server`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Server/)
  - [`Foundation.Launcher`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Launcher/)
  - [`Foundation.Installer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Installer/)
  - [`Foundation.UI` & `@foundation/ui`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.UI/)
  - [`foundation_core` (Python)](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/)
- **Backward-Compatibility Invariant:** All cross-workspace updates to `Foundation` must be **strictly additive and backward-compatible** (optional parameters with defaults, new properties, zero breaking signature changes) with full side-by-side version retention in `<PROJECTS_ROOT>\SharedLibs\Foundation\v<Version>\`.

---

## 4. Project Navigation, Root Quartet & 4-Doc Manual Shelf
- **Root Documentation Quartet:**
  - **Feature Inventory & Code Map:** [`FEATURES.md`](file:///<PROJECTS_ROOT>/Foundation/FEATURES.md)
  - **Milestones & Strategic Horizon:** [`ROADMAP.md`](file:///<PROJECTS_ROOT>/Foundation/ROADMAP.md)
  - **Version History Ledger:** [`CHANGELOG.md`](file:///<PROJECTS_ROOT>/Foundation/CHANGELOG.md)
- **4-Document Manual Shelf (`Dev\docs\` — Standard 16 §3.5):**
  - **📐 Architecture & Engineering:** [`Dev\docs\ARCHITECTURE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/ARCHITECTURE.md)
  - **📘 User & Integration Guide:** [`Dev\docs\USER_GUIDE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/USER_GUIDE.md)
  - **🛠️ Operations Runbook:** [`Dev\docs\OPERATIONS.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/OPERATIONS.md)
  - **🔌 Interfaces & API Reference:** [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md)
- **Blueprints & Diagnostic Reports:**
  - **Implementation Plans (`@pland`):** [`Dev\docs\plans\`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/plans/)
  - **Diagnostic Reports (`@inspecter`):** [`Dev\docs\reports\`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/)
