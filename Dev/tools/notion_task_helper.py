# @module: Foundation.Tools.notion_task_helper
# @purpose: Shared helper for formatting Notion Unified Task Backbone payloads and 4-doc Manual Shelf sync manifests (Standard 16 §5).
# @depends-on: json, pathlib, time
# @exports: build_task_backbone_payload, build_manual_shelf_manifest

from __future__ import annotations

import json
import time
from pathlib import Path
from typing import Any

TASK_BACKBONE_DATA_SOURCE_ID = "fbbc2263-d731-43c8-bb99-fc8710db5856"
PROJECTS_HUB_PAGE_ID = "40eb1b97-5a45-81e0-bf30-c6bc5a8ec7c3"


def build_task_backbone_payload(
    task_title: str,
    project_name: str,
    target_version: str,
    status: str = "To Do",
    priority: str = "High",
) -> dict[str, Any]:
    """Builds a standardized Notion Unified Task Backbone properties dictionary."""
    return {
        "dataSourceId": TASK_BACKBONE_DATA_SOURCE_ID,
        "properties": {
            "Task": f"[{target_version}] {task_title}",
            "Project": project_name,
            "Status": status,
            "Priority": priority,
            "UpdatedAtUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        },
    }


def build_manual_shelf_manifest(project_root: str | Path) -> dict[str, Any]:
    """Inspects the 4-doc Manual Shelf in Dev\\docs\\ and returns line/byte metrics for Notion sync."""
    root = Path(project_root).resolve()
    docs_dir = root / "Dev" / "docs"
    shelf_files = ("ARCHITECTURE.md", "USER_GUIDE.md", "OPERATIONS.md", "INTERFACES.md")
    metrics: dict[str, Any] = {}
    for name in shelf_files:
        p = docs_dir / name
        if p.exists():
            text = p.read_text(encoding="utf-8", errors="replace")
            metrics[name] = {
                "exists": True,
                "lines": len(text.splitlines()),
                "bytes": p.stat().st_size,
            }
        else:
            metrics[name] = {"exists": False, "lines": 0, "bytes": 0}
    return {"projectRoot": str(root), "manualShelf": metrics}


if __name__ == "__main__":
    print(json.dumps({"taskBackboneId": TASK_BACKBONE_DATA_SOURCE_ID, "projectsHubId": PROJECTS_HUB_PAGE_ID}, indent=2))
