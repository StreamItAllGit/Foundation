# Project Context: Foundation (`<PROJECTS_ROOT>\Foundation\`)

- **Role:** Shared SDK (`v0.5.6`: `Core`, `Retro`, `Sidecars`, `Server`, `Launcher`, `UI`, `Installer`, `Automation`, `@foundation/ui`, `foundation_core`).
- **Agent Fast-Path:** `README.md` (Routing Matrix) -> `Dev\docs\INTERFACES.md` (1-Stop API Dictionary) -> `Dev\docs\ARCHITECTURE.md` (File Registry).
- **Workspace & Hub:** [`Dev/`](Dev/) | Read-Only Hub: `<PROJECTS_ROOT>\SharedLibs\Foundation\`

## 1. Quartet & State
- [`README.md`](README.md) | [`FEATURES.md`](FEATURES.md) | [`ROADMAP.md`](ROADMAP.md) | [`CHANGELOG.md`](CHANGELOG.md) | [`active.state`](active.state)

## 2. Manuals & Active Docs (`Dev\docs\`)
- **Manual Shelf:** [INTERFACES.md](Dev/docs/INTERFACES.md) | [ARCHITECTURE.md](Dev/docs/ARCHITECTURE.md) | [USER_GUIDE.md](Dev/docs/USER_GUIDE.md) | [OPERATIONS.md](Dev/docs/OPERATIONS.md)
- **Plans & Reports:** Local-only gitignored: `Dev/docs/plans/`, `Dev/docs/reports/`

## 3. Governance & Notion ("The Me Hub")
- **Dev Bible:** `00_GLOSSARY.md` | `_DevBible\README.md` (read-only external reference)
- **Notion:** `<NOTION_PROJECT_PAGE_ID>` (`Project = "Foundation"`, `<NOTION_TASK_LIST_COLLECTION>` — local IDs in gitignored `Dev/data/notion_hub.local.json`)
- **Commit `/post` Hook:** Every commit & release pushes `/post` via `active.state` -> `discordPost` (`DISCORD_WEBHOOK_FOUNDATION`), tagging `@StreamDoesDev`.
