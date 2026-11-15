# Foundation (`v0.4.0`)

**Shared SDK & Zero-Duplication Infrastructure Core** (`Foundation.Core v0.4.0`, `Foundation.Retro v0.4.0`, `Foundation.Sidecars v0.4.0`, `Foundation.Server v0.4.0`, `Foundation.Launcher v0.4.0`, `Foundation.UI v0.4.0` + `@foundation/ui v0.4.0`, `Foundation.Installer v0.4.0`, Python `foundation_core v0.4.0`, and `Dev\tools\*.py`).

---

## 1. Agent Quick-Routing Matrix ("Find What You Need Without Digging")

| Task / Question | Go Directly Here | What You Get in 1 Read |
|---|---|---|
| Looking up a C# class, method, React prop, Python helper, CLI flag, or JSON schema | [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md) | 1-Stop API Dictionary + `@foundation/ui` barrel vs submodule import warning |
| Copy-paste integration recipes (Tier 1 Installer, Web UI, Python, C# Core) | [`Dev\docs\USER_GUIDE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/USER_GUIDE.md) | Minimal working quickstart snippets for all 8 subprojects |
| Locating a file by path/LOC (`<= 400 LOC`), assembly graph, SQLite pragmas, or ADRs | [`Dev\docs\ARCHITECTURE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/ARCHITECTURE.md) | Complete module registry, `33` test inventory, `ADR-001`..`008` |
| Building, running `Foundation.Tests`, publishing `SharedLibs`, or calling `Dev\tools\*.py` | [`Dev\docs\OPERATIONS.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/OPERATIONS.md) | Exact build/sync/verify commands & installer exit codes |
| Checking implemented capabilities vs remaining upstream backlog | [`FEATURES.md`](file:///<PROJECTS_ROOT>/Foundation/FEATURES.md) & [`ROADMAP.md`](file:///<PROJECTS_ROOT>/Foundation/ROADMAP.md) | Verified module inventory + milestone roadmap |

> **Workspace vs. Pinned Binary Hub:** Consumer projects (`Housekeeping`, `ShadowLibrary`, `reStation`, `KingsFieldRecomp`, `MediaSync`) treat both `<PROJECTS_ROOT>\Foundation\` and `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\` (`52` published files via [`sync_foundation_sdk.py`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/sync_foundation_sdk.py)) as **strictly read-only**. Log consumer SDK gaps in [`Dev\docs\reports\`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/reports/).

---

## 2. Subproject Inventory (`v0.4.0`)

- **C# Core & Runtime (`Dev\src\Foundation.Core\` — 18 files):** [`SandboxEnvironment`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs) (`VisibleTest` / `--visible-test`, `ResolveSiblingSandboxRoot`), [`HighResFramePacer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HighResFramePacer.cs), [`AudioRingBuffer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/AudioRingBuffer.cs), [`WavCaptureWriter`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/WavCaptureWriter.cs), [`RgbaPngWriter`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/RgbaPngWriter.cs), [`PixelOverlayCanvas`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PixelOverlayCanvas.cs), [`CliArgumentHelper` / `BoundedTailStream` / `CappedSharedLogSink`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/CliAndProcessPrimitives.cs), [`GranularSettingsManager`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/GranularSettingsManager.cs), [`DpapiSecretStore`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/DpapiSecretStore.cs), [`SqliteWalPool`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SqliteWalPool.cs), [`PidSafeLockfile`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/PidSafeLockfile.cs), [`HeartbeatLogger`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HeartbeatLogger.cs), [`CrashBundleService`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/CrashBundleService.cs), [`DualJobCancellationCoordinator` / `JobObjectGuard`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/JobObjectGuard.cs), [`SafeNetworkTransfer`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/SafeNetworkTransfer.cs) (ADS `JobId` sanitization, HTTP `416` recovery, `ResponseHeadersTimeout`), [`ToolResolver`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/ToolResolver.cs), [`HybridModuleLoader` / `IFoundationModule`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Core/HybridModuleLoader.cs).
- **Retro & Sidecar Engines (`Foundation.Retro v0.4.0` [9 files] + `Foundation.Sidecars v0.4.0`):** 41-platform catalog (`IsVerifiedReferenceHash`), No-Intro/Redump/RA/CMP DAT parser, filename normalizer, headerless stripper + 20-magic sniffer, in-memory IPS/BPS/UPS patcher, BYO `.iso`/`.bin`+`.cue`/`.chd`/`.7z`/`.zip` [`MediaContainerExtractor`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/MediaContainerExtractor.cs), static recomp [`RecompContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompContracts.cs) + [`RecompTierAndBackendContracts`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/RecompTierAndBackendContracts.cs) (AOT-primary `<dataDir>\cache\`, JIT/IR bring-up, `.decomp` manifest), [`SaveStateFixtureRunner`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/Foundation.Retro/SaveStateFixtureRunner.cs), single-pass `ArrayPool<byte>` stream hasher, ZIP/7z `-slt` + LZMA/Deflate64 `7z.exe` fallback + nested `::` inspection + TorrentZip builder, `.hash` v1 sidecars, and `.hklock` / `.coexistence.json` v2 coordination.
- **Server, Launcher, UI & Installer (`Foundation.Server v0.4.0`, `Launcher v0.4.0`, `UI v0.4.0` [`SdkVersion = "0.4.0"`], `Installer v0.4.0` [15 files]):** Tiered Kestrel localhost/Tailscale/PBKDF2/HMAC auth, `/api/v1/suite/manifest`, `RootLauncherHost` (`AttachConsole(-1)`, `--wait`/`--open`/`--visible-test` blocking, relative arg rebase, Ctrl+C forwarding), WebView2 host + STA dialogs + tray badges, single-source `Web\` React components + Vanilla terminal drawer, and Tier 1 `StandardInstallerHost` (`%LOCALAPPDATA%\Programs\<Studio>\<Suite?>\<App>\`, `Lightweight`/`Full` profiles, `SecondaryPathPicker`, opt-in `--migrate-legacy`, `AllowAutoStart`, `FileLockRetryHelper`, sibling `Sandbox\` containment, `--visible-test` showcase, bounded retry self-delete trampoline).
- **Python & Universal Tooling:** Zero-dependency Python `foundation_core` (`v0.4.0` in [`Dev\src\foundation_core\`](file:///<PROJECTS_ROOT>/Foundation/Dev/src/foundation_core/)) and 4 universal scripts in [`Dev\tools\`](file:///<PROJECTS_ROOT>/Foundation/Dev/tools/).

---

## 3. Quick Start & Build Verification

```powershell
# Run full unit test suite (33 [Fact] tests across 6 files)
dotnet test <PROJECTS_ROOT>\Foundation\Dev\Foundation.sln -c Release /warnaserror

# Publish compiled DLLs, @foundation/ui, and foundation_core into <PROJECTS_ROOT>\SharedLibs\Foundation\Latest
python <PROJECTS_ROOT>\Foundation\Dev\tools\sync_foundation_sdk.py --publish --version 0.4.0 --ui-version 0.4.0 --configuration Release

# Sync pinned SDK into a consumer project
python <PROJECTS_ROOT>\Foundation\Dev\tools\sync_foundation_sdk.py --sync-consumer <PROJECTS_ROOT>\<Project>
```

> ⚠️ **Post-Build Side Effect Note:** Building `Foundation.UI.csproj` with `-p:PublishFoundationSdk=true` updates `WebAssets\` and `<PROJECTS_ROOT>\SharedLibs\Foundation\Latest\`.

---

## 4. Governance & Lifecycle Status

- **Lifecycle State:** [`active.state`](file:///<PROJECTS_ROOT>/Foundation/active.state) (`v0.4.0`)
- **Project Anchor & Roadmap:** [`AGENTS.md`](file:///<PROJECTS_ROOT>/Foundation/AGENTS.md) · [`FEATURES.md`](file:///<PROJECTS_ROOT>/Foundation/FEATURES.md) · [`ROADMAP.md`](file:///<PROJECTS_ROOT>/Foundation/ROADMAP.md) · [`CHANGELOG.md`](file:///<PROJECTS_ROOT>/Foundation/CHANGELOG.md)
- **Dev Bible Compliance:** Strictly adheres to [`00_CORE_INVARIANTS_AND_GLOSSARY.md`](file:///<PROJECTS_ROOT>/_DevBible/00_CORE_INVARIANTS_AND_GLOSSARY.md) (`<= 400 LOC` cap across `Dev\` sources and docs).

---

## 5. License & Legal

- **License:** Distributed under the [MIT License](file:///<PROJECTS_ROOT>/Foundation/LICENSE).
- **Clean-Room & Zero-PII Guarantee:** Contains zero copyrighted ROMs, BIOS dumps, proprietary keys, or workstation-specific credentials/paths.

---

## 6. 📘 Manuals

- [`Dev\docs\INTERFACES.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/INTERFACES.md) — **1-Stop Agent API Dictionary** (all C# classes/signatures, `@foundation/ui` props + submodule import rules, Vanilla JS/CSS, `foundation_core`, `Dev\tools\*.py` CLIs, JSON schemas)
- [`Dev\docs\USER_GUIDE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/USER_GUIDE.md) — Consumer Integration Recipes & Tier 1 `StandardInstallerHost` Quickstart
- [`Dev\docs\ARCHITECTURE.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/ARCHITECTURE.md) — System Topology, Runtime Pipelines, Complete Module Registry, SQLite Pragmas, `33` Tests, and ADRs
- [`Dev\docs\OPERATIONS.md`](file:///<PROJECTS_ROOT>/Foundation/Dev/docs/OPERATIONS.md) — Build, Hub Sync, Installer & `Dev\tools\*.py` CLI Flags, Exit Codes, and Troubleshooting
